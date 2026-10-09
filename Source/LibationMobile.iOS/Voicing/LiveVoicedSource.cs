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

	public LiveVoicedSource(string text, string voiceId, double secondsPerCharacter, Func<string, ISentenceVoice> makeVoice)
	{
		this.text = text;
		pace = secondsPerCharacter;
		this.makeVoice = makeVoice;
		Duration = TimeSpan.FromSeconds(text.Length * pace);
		sentences = SpokenText.Split(text);
		VoiceId = voiceId;
		voice = makeVoice(voiceId);
		// The player plays at the first voice's rate; another voice's audio is converted to it.
		SampleRate = voice.SampleRate;
		marks.Add((0, 0));
		worker = new Thread(Work) { IsBackground = true, Name = "Voicing", Priority = ThreadPriority.AboveNormal };
		worker.Start();
	}

	/// <summary>Speak the next sentence missing within the lookahead, again and again.</summary>
	private void Work()
	{
		while (!disposed)
		{
			int index = -1, forGeneration;
			ISentenceVoice speaker;
			lock (locker)
			{
				forGeneration = generation;
				speaker = voice;
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
				wake.WaitOne(500);
				continue;
			}
			float[] spoken;
			try
			{
				spoken = Convert(speaker.Speak(sentences[index].Text), speaker.SampleRate);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Sentence {index} not spoken: {ex.Message}");
				spoken = [];
			}
			lock (locker)
			{
				if (forGeneration == generation)
					ready[index] = spoken;
			}
		}
	}

	private float[] Convert(float[] samples, int rate)
	{
		if (rate == SampleRate || samples.Length == 0)
			return samples;
		var result = new float[(int)((long)samples.Length * SampleRate / rate)];
		for (var i = 0; i < result.Length; i++)
		{
			var x = (double)i * rate / SampleRate;
			var j = (int)x;
			var f = (float)(x - j);
			result[i] = j + 1 < samples.Length ? samples[j] * (1 - f) + samples[j + 1] * f : samples[^1];
		}
		return result;
	}

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
				VoiceId = voiceId;
				generation++;
				foreach (var later in ready.Keys.Where(k => k > sentence).ToList())
					ready.Remove(later);
			}
			wake.Set();
			// The worker may be mid-sentence in the old voice; let it finish before letting the old voice go.
			Thread.Sleep(2000);
			old.Dispose();
		});
	}

	public void Dispose()
	{
		disposed = true;
		wake.Set();
		lock (locker)
			voice.Dispose();
	}
}
