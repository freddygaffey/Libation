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
