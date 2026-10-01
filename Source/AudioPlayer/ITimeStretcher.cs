using System;

namespace AudioPlayer;

/// <summary>Creates the speed changer for a stream of the given format.</summary>
public delegate ITimeStretcher TimeStretcherFactory(int sampleRate, int channels);

/// <summary>
/// Changes how fast audio plays without changing its pitch. Audio is written in at normal speed and read out
/// sped up or slowed down. Samples are interleaved floats; an implementation is used from one thread at a time.
/// </summary>
public interface ITimeStretcher : IDisposable
{
	float Speed { get; set; }

	/// <summary>Source frames written but not yet read out, to work out which part of the source is being heard.</summary>
	long BufferedSourceFrames { get; }

	void Write(ReadOnlySpan<float> samples);

	/// <returns>The number of samples written to <paramref name="samples"/>, which may be zero until more is written.</returns>
	int Read(Span<float> samples);

	/// <summary>Process everything written so far, at the end of the source.</summary>
	void Flush();

	/// <summary>Discard everything buffered, after a seek.</summary>
	void Clear();
}

/// <summary>The built-in speed changer: <see cref="Sonic"/>, which speeds everything up evenly.</summary>
public sealed class SonicTimeStretcher(int sampleRate, int channels) : ITimeStretcher
{
	private readonly Sonic sonic = new(sampleRate, channels);

	public float Speed
	{
		get => sonic.Speed;
		set => sonic.Speed = value;
	}

	// Its pending input, plus its pending output scaled back to source time.
	public long BufferedSourceFrames => sonic.InputFramesPending + (long)(sonic.FramesAvailable * sonic.Speed);

	public void Write(ReadOnlySpan<float> samples) => sonic.Write(samples);
	public int Read(Span<float> samples) => sonic.Read(samples);
	public void Flush() => sonic.Flush();
	public void Clear() => sonic.Clear();
	public void Dispose() { }
}
