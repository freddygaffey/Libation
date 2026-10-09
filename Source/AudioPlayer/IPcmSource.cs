using System;

namespace AudioPlayer;

/// <summary>A seekable source of decoded, interleaved 32-bit float PCM audio.</summary>
public interface IPcmSource : IDisposable
{
	int SampleRate { get; }
	int Channels { get; }
	TimeSpan Duration { get; }

	/// <summary>Read interleaved samples.</summary>
	/// <returns>The number of samples (not frames) read. Zero at end of stream.</returns>
	int Read(Span<float> samples);

	/// <summary>Seek to the frame nearest <paramref name="position"/>.</summary>
	/// <returns>The frame index decoding will resume from.</returns>
	long Seek(TimeSpan position);
}

/// <summary>
/// A source made as it plays, such as text being read aloud: it can run short while the next part is made, and its
/// frames map to the book's timeline through what is being read rather than one to one.
/// </summary>
public interface ILiveSource : IPcmSource
{
	/// <summary>The last read came up short because more is still being made, not because the end was reached.</summary>
	bool IsWaiting { get; }

	/// <summary>The place on the book's timeline of a frame, counted from the frame the last seek returned.</summary>
	TimeSpan TimeAt(long frame);
}
