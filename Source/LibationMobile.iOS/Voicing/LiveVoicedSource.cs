using LibationMobile.Services;
using Speechwarp.Voice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LibationMobile.iOS.Voicing;

/// <summary>
/// Text read aloud as it plays, by any of the voices (Apple's, eSpeak's, Kokoro's): sentences are spoken on a thread
/// of their own a few minutes of the voice's audio ahead of what is heard, at the voice's own pace (the player does
/// the speeding up). The timeline is the text's characters at a fixed pace, so a place is the same character in any
/// voice, and the voice can change between sentences.
/// </summary>
public sealed class LiveVoicedSource : IVoicedSource
{
	/// <summary>Seconds of the voice's own audio kept ready ahead: a minute of listening at 5x.</summary>
	private const double LOOKAHEAD = 300;

	private readonly string text;
	private readonly double pace;
	private readonly IReadOnlyList<SpokenText.Sentence> sentences;
	private readonly Func<string, ISentenceVoice> makeVoice;
	private readonly Lock locker = new();
	private readonly AutoResetEvent wake = new(false);
	private readonly Thread worker;
	private readonly Dictionary<int, float[]> ready = [];
	private ISentenceVoice voice;
	/// <summary>Where a slow voice's sentences are kept; null for a fast one.</summary>
	private VoiceCache? cache;
	/// <summary>Sentences of blocks not yet complete, to be kept once they are.</summary>
	private readonly Dictionary<int, float[]?[]> building = [];
	/// <summary>Raised by a seek or a new voice: audio made before it is not used.</summary>
	private int generation;
	private int sentence;
	private int offset;
	private long frame;
	private readonly List<(long Frame, int Character)> marks = [];
	private volatile bool disposed;

	public int SampleRate { get; }
	public int Channels => 1;
	public TimeSpan Duration { get; }
	public bool IsWaiting { get; private set; }
	public string VoiceId { get; private set; }

	/// <summary>Where the text is on disk, for overnight voicing to read; null for none.</summary>
	private readonly string? textPath;

	public LiveVoicedSource(string text, string voiceId, double secondsPerCharacter, Func<string, ISentenceVoice> makeVoice, string? textPath = null)
	{
		this.textPath = textPath;
		this.text = text;
		pace = secondsPerCharacter;
		this.makeVoice = makeVoice;
		Duration = TimeSpan.FromSeconds(text.Length * pace);
		sentences = SpokenText.Split(text);
		VoiceId = voiceId;
		voice = makeVoice(voiceId);
		cache = voice.KeepsSpoken ? new VoiceCache(text, voiceId) : null;
		AskOvernight();
		// The player plays at the first voice's rate; another voice's audio is converted to it.
		SampleRate = voice.SampleRate;
		marks.Add((0, 0));
		worker = new Thread(Work) { IsBackground = true, Name = "Voicing", Priority = ThreadPriority.AboveNormal };
		worker.Start();
	}

	/// <summary>
	/// Speak the next sentence missing within the lookahead, again and again: from the disk where a slow voice has
	/// spoken it before. With nothing missing, a slow voice carries on further ahead to disk while the phone charges.
	/// </summary>
	private void Work()
	{
		while (!disposed)
		{
			int index = -1, forGeneration;
			ISentenceVoice speaker;
			VoiceCache? keep;
			lock (locker)
			{
				forGeneration = generation;
				speaker = voice;
				keep = cache;
				var ahead = 0.0;
				for (var i = sentence; i < sentences.Count && ahead < LOOKAHEAD; i++)
				{
					if (ready.TryGetValue(i, out var audio))
						ahead += audio.Length / (double)SampleRate;
					else
					{
						index = i;
						break;
					}
				}
			}
			if (index < 0)
			{
				if (keep is null || !Charging() || !SpeakAheadToDisk(speaker, keep, forGeneration))
					wake.WaitOne(500);
				continue;
			}

			var block = index / VoiceCache.BLOCK;
			if (keep?.Load(block, SampleRate) is { } kept)
			{
				lock (locker)
				{
					if (forGeneration == generation)
						for (var i = 0; i < kept.Length; i++)
							if (block * VoiceCache.BLOCK + i >= sentence)
								ready.TryAdd(block * VoiceCache.BLOCK + i, kept[i]);
				}
				continue;
			}

			var spoken = Speak(speaker, index);
			lock (locker)
			{
				if (forGeneration != generation)
					continue;
				ready[index] = spoken;
			}
			if (keep is not null)
				Build(keep, index, spoken);
		}
	}

	private float[] Speak(ISentenceVoice speaker, int index)
	{
		try
		{
			return Convert(speaker.Speak(sentences[index].Text), speaker.SampleRate);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Sentence {index} not spoken: {ex.Message}");
			return [];
		}
	}

	/// <summary>Add a sentence to its block, and keep the block once every sentence of it is spoken.</summary>
	private void Build(VoiceCache keep, int index, float[] spoken)
	{
		var block = index / VoiceCache.BLOCK;
		float[][]? complete = null;
		lock (locker)
		{
			if (!building.TryGetValue(block, out var parts))
				building[block] = parts = new float[]?[Math.Min(VoiceCache.BLOCK, sentences.Count - block * VoiceCache.BLOCK)];
			parts[index % VoiceCache.BLOCK] = spoken;
			if (parts.All(p => p is not null))
			{
				complete = parts!;
				building.Remove(block);
			}
			// Blocks left part-spoken by a seek are let go.
			foreach (var stale in building.Keys.Where(k => k < sentence / VoiceCache.BLOCK).ToList())
				building.Remove(stale);
		}
		if (complete is null)
			return;
		try
		{
			keep.Save(block, complete, SampleRate);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Voiced block {block} not kept: {ex.Message}");
		}
	}

	/// <summary>The next block past what is ready that is not on disk yet, spoken and kept. False when there is none.</summary>
	private bool SpeakAheadToDisk(ISentenceVoice speaker, VoiceCache keep, int forGeneration)
	{
		int first;
		lock (locker)
			first = sentence / VoiceCache.BLOCK;
		var blocks = (sentences.Count + VoiceCache.BLOCK - 1) / VoiceCache.BLOCK;
		var block = Enumerable.Range(first, Math.Max(0, blocks - first)).FirstOrDefault(b => !keep.Has(b) && !building.ContainsKey(b), -1);
		if (block < 0)
			return false;
		var parts = new List<float[]>();
		for (var i = block * VoiceCache.BLOCK; i < Math.Min(sentences.Count, (block + 1) * VoiceCache.BLOCK); i++)
		{
			// Back to the sentences being heard as soon as they are wanted.
			if (disposed || forGeneration != generation || NeedsSpeaking())
				return true;
			parts.Add(Speak(speaker, i));
		}
		try
		{
			keep.Save(block, parts, SampleRate);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Voiced block {block} not kept: {ex.Message}");
		}
		return true;
	}

	/// <summary>Whether a sentence within the lookahead is missing.</summary>
	private bool NeedsSpeaking()
	{
		lock (locker)
		{
			var ahead = 0.0;
			for (var i = sentence; i < sentences.Count && ahead < LOOKAHEAD / 2; i++)
			{
				if (!ready.TryGetValue(i, out var audio))
					return true;
				ahead += audio.Length / (double)SampleRate;
			}
			return false;
		}
	}

	private static bool Charging() => UIKit.UIDevice.CurrentDevice.BatteryState is UIKit.UIDeviceBatteryState.Charging or UIKit.UIDeviceBatteryState.Full;

	private float[] Convert(float[] samples, int rate) => rate == SampleRate ? samples : VoiceCache.Resample(samples, rate, SampleRate);

	public int Read(Span<float> samples)
	{
		lock (locker)
		{
			var written = 0;
			while (written < samples.Length && sentence < sentences.Count)
			{
				if (!ready.TryGetValue(sentence, out var audio))
					break;
				var take = Math.Min(samples.Length - written, audio.Length - offset);
				audio.AsSpan(offset, take).CopyTo(samples[written..]);
				written += take;
				offset += take;
				frame += take;
				var s = sentences[sentence];
				marks.Add((frame, s.Start + (audio.Length > 0 ? (int)((long)s.Length * offset / audio.Length) : s.Length)));
				if (offset >= audio.Length)
				{
					ready.Remove(sentence);
					sentence++;
					offset = 0;
					wake.Set();
				}
			}
			if (marks.Count > 4000)
				marks.RemoveRange(0, 2000);
			IsWaiting = written < samples.Length && sentence < sentences.Count;
			return written;
		}
	}

	public long Seek(TimeSpan position)
	{
		lock (locker)
		{
			var character = (int)Math.Clamp(position.TotalSeconds / pace, 0, text.Length);
			sentence = SentenceAt(character);
			offset = 0;
			var start = sentence < sentences.Count ? sentences[sentence].Start : text.Length;
			frame = (long)(start * pace * SampleRate);
			marks.Clear();
			marks.Add((frame, start));
			// What is ready from here on still serves; behind it does not.
			foreach (var behind in ready.Keys.Where(k => k < sentence).ToList())
				ready.Remove(behind);
			IsWaiting = false;
			wake.Set();
			return frame;
		}
	}

	private int SentenceAt(int character)
	{
		for (var i = sentences.Count - 1; i >= 0; i--)
			if (sentences[i].Start <= character)
				return i;
		return 0;
	}

	public TimeSpan TimeAt(long at)
	{
		lock (locker)
		{
			var i = marks.FindLastIndex(m => m.Frame <= at);
			if (i < 0)
				return TimeSpan.FromSeconds(marks[0].Character * pace);
			var (fromFrame, fromCharacter) = marks[i];
			var character = (double)fromCharacter;
			if (i + 1 < marks.Count && marks[i + 1].Frame > fromFrame)
				character += (marks[i + 1].Character - fromCharacter) * (double)(at - fromFrame) / (marks[i + 1].Frame - fromFrame);
			return TimeSpan.FromSeconds(character * pace);
		}
	}

	/// <summary>Another voice from the next sentence: what is ready in the old one finishes the sentence being heard.</summary>
	public void SetVoice(string voiceId)
	{
		ThreadPool.QueueUserWorkItem(_ =>
		{
			var next = makeVoice(voiceId);
			ISentenceVoice old;
			lock (locker)
			{
				old = voice;
				voice = next;
				cache = next.KeepsSpoken ? new VoiceCache(text, voiceId) : null;
				building.Clear();
				VoiceId = voiceId;
				generation++;
				AskOvernight();
				foreach (var later in ready.Keys.Where(k => k > sentence).ToList())
					ready.Remove(later);
			}
			wake.Set();
			// The worker may be mid-sentence in the old voice; let it finish before letting the old voice go.
			Thread.Sleep(2000);
			old.Dispose();
		});
	}

	/// <summary>A slow voice: ask for the rest of the book to be spoken while the phone charges, from the place being heard.</summary>
	private void AskOvernight()
	{
		if (cache is not null && textPath is { } path)
			OvernightVoicing.Ask(new OvernightVoicing.Job(path, VoiceId, sentence < sentences.Count ? sentences[sentence].Start : 0));
	}

	public void Dispose()
	{
		AskOvernight();
		disposed = true;
		wake.Set();
		lock (locker)
			voice.Dispose();
	}
}
