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
	private float appliedPauseCap, appliedFloor, appliedGap, appliedRate = -1;
	private bool appliedKeepSpeed = true;

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
		// Picked up here, on the thread that uses the stream, because the settings are changed from the UI.
		// Playing for the original method (see SwitchingTimeStretcher), it speeds up evenly.
		var nonlinearity = AudioBackend.UseNonlinear ? AudioBackend.Nonlinearity : 0f;
		if (nonlinearity != appliedNonlinearity)
			stream.Nonlinear = appliedNonlinearity = nonlinearity;
		if (AudioBackend.PauseCap != appliedPauseCap)
			stream.PauseCap = appliedPauseCap = AudioBackend.PauseCap;
		if (AudioBackend.KeepSpeed != appliedKeepSpeed)
			stream.KeepSpeed = appliedKeepSpeed = AudioBackend.KeepSpeed;
		if (AudioBackend.SpeedFloor != appliedFloor)
			stream.SpeedFloor = appliedFloor = AudioBackend.SpeedFloor;
		if (AudioBackend.RhythmGap != appliedGap)
			stream.RhythmGap = appliedGap = AudioBackend.RhythmGap;
		if (AudioBackend.RhythmRate != appliedRate)
			stream.RhythmRate = appliedRate = AudioBackend.RhythmRate;

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
