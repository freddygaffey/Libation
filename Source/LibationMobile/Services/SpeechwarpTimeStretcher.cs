using AudioPlayer;
using Speechwarp;
using System;

namespace LibationMobile.Services;

/// <summary>
/// Speed-up that hurries vowels and pauses and keeps consonants closer to their normal length, as a fast talker
/// does, so speech stays easier to follow at high speeds. Uses the speechwarp library (Google's Speedy).
/// </summary>
public sealed class SpeechwarpTimeStretcher : ITimeStretcher
{
	private readonly SpeechwarpStream stream;
	/// <summary>Frames written since the stream was created or last cleared, which is what its position counts from.</summary>
	private long framesWritten;
	private float appliedNonlinearity = -1;

	public SpeechwarpTimeStretcher(int sampleRate, int channels)
		=> stream = new SpeechwarpStream(sampleRate, channels);

	public float Speed
	{
		get => stream.Speed;
		set => stream.Speed = value;
	}

	public long BufferedSourceFrames => framesWritten - stream.Position;

	public void Write(ReadOnlySpan<float> samples)
	{
		// Picked up here, on the thread that uses the stream, because the setting is changed from the UI.
		var nonlinearity = AudioBackend.Nonlinearity;
		if (nonlinearity != appliedNonlinearity)
			stream.Nonlinear = appliedNonlinearity = nonlinearity;

		stream.Write(samples);
		framesWritten += samples.Length / stream.Channels;
	}

	public int Read(Span<float> samples) => stream.Read(samples) * stream.Channels;

	public void Flush() => stream.Flush();

	public void Clear()
	{
		stream.Reset();
		framesWritten = 0;
	}

	public void Dispose() => stream.Dispose();
}
