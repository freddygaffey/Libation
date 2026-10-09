using LibationMobile.Services;
using Speechwarp.Voice;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>
/// Text read aloud as it plays, by speechwarp's <see cref="SpokenText"/>: rendered a sentence at a time a few minutes
/// of listening ahead, at the voice's own pace (the player does the speeding up). Its timeline is the text's
/// characters at a fixed pace, so a place is the same character whichever voice reads, and the voice can change
/// mid-sentence-stream without losing it.
/// </summary>
public sealed class SpokenTextSource : IVoicedSource
{
	/// <summary>Seconds of the voice's own audio kept ready: enough for a minute's listening at 5x.</summary>
	private const double LOOKAHEAD = 300;

	private readonly string text;
	private readonly double pace;
	private readonly Lock locker = new();
	private SpokenText spoken;
	private float[] scratch = [];
	/// <summary>Frames given to the player since the last seek, counted from the frame that seek returned.</summary>
	private long frame;
	/// <summary>Which character was being read at which frame, to place any frame on the timeline.</summary>
	private readonly List<(long Frame, int Character)> marks = [];

	public int SampleRate { get; }
	public int Channels => 1;
	public TimeSpan Duration { get; }
	public bool IsWaiting { get; private set; }
	public string VoiceId { get; private set; }

	public SpokenTextSource(string text, string voiceId, double secondsPerCharacter)
	{
		this.text = text;
		pace = secondsPerCharacter;
		Duration = TimeSpan.FromSeconds(text.Length * pace);
		VoiceId = voiceId;
		spoken = Create(voiceId);
		// The player plays at the first voice's rate; another voice's audio is converted to it.
		SampleRate = spoken.SampleRate;
		marks.Add((0, 0));
	}

	private SpokenText Create(string voiceId)
	{
		var voice = SpeechVoice.Find(voiceId) ?? throw new InvalidOperationException("That voice is no longer on this phone.");
		return new SpokenText(text, voice) { Speed = 1, Lookahead = LOOKAHEAD };
	}

	public int Read(Span<float> samples)
	{
		lock (locker)
		{
			int frames;
			if (spoken.SampleRate == SampleRate)
				frames = spoken.Read(samples);
			else
			{
				// Another voice, at another rate: read what makes up these frames at its rate, and convert.
				var wanted = (int)Math.Ceiling(samples.Length * (double)spoken.SampleRate / SampleRate);
				if (scratch.Length < wanted)
					scratch = new float[wanted];
				var read = spoken.Read(scratch.AsSpan(0, wanted));
				frames = (int)((long)read * SampleRate / spoken.SampleRate);
				for (var i = 0; i < frames; i++)
				{
					var x = (double)i * spoken.SampleRate / SampleRate;
					var j = (int)x;
					var f = (float)(x - j);
					samples[i] = j + 1 < read ? scratch[j] * (1 - f) + scratch[j + 1] * f : scratch[Math.Max(0, read - 1)];
				}
			}
			frame += frames;
			marks.Add((frame, spoken.CharacterPosition));
			// Only the last few minutes are needed: the player asks about what it is about to play.
			if (marks.Count > 4000)
				marks.RemoveRange(0, 2000);
			IsWaiting = frames < samples.Length && !spoken.IsFinished;
			return frames;
		}
	}

	public long Seek(TimeSpan position)
	{
		lock (locker)
		{
			var character = (int)Math.Clamp(position.TotalSeconds / pace, 0, text.Length);
			spoken.Seek(character);
			// It goes to the start of that sentence.
			var start = spoken.CharacterPosition;
			frame = (long)(start * pace * SampleRate);
			marks.Clear();
			marks.Add((frame, start));
			IsWaiting = false;
			return frame;
		}
	}

	public TimeSpan TimeAt(long at)
	{
		lock (locker)
		{
			// The last mark at or before the frame, and the next: between them, characters went by evenly.
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

	public void SetVoice(string voiceId)
	{
		// Splitting a whole book into sentences takes a moment: done away from the screen, then swapped in.
		_ = Task.Run(() =>
		{
			var next = Create(voiceId);
			SpokenText old;
			lock (locker)
			{
				next.Seek(spoken.CharacterPosition);
				old = spoken;
				spoken = next;
				VoiceId = voiceId;
				marks.Add((frame, next.CharacterPosition));
			}
			old.Dispose();
		});
	}

	public void Dispose()
	{
		lock (locker)
			spoken.Dispose();
	}
}
