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
		// A profile's rules give the options for the speed playing now, so they follow training, Siri and the buttons.
		var profile = AudioBackend.Scaling;
		var speed = stream.Speed;
		var pauseCap = profile?.PauseCapAt(speed) ?? AudioBackend.PauseCap;
		var keepSpeed = profile?.KeepSpeed ?? AudioBackend.KeepSpeed;
		var floor = profile?.FloorAt(speed) ?? AudioBackend.SpeedFloor;
		var gap = profile?.RhythmGap ?? AudioBackend.RhythmGap;
		var rate = profile?.RhythmRate ?? AudioBackend.RhythmRate;
		if (pauseCap != appliedPauseCap)
			stream.PauseCap = appliedPauseCap = pauseCap;
		if (keepSpeed != appliedKeepSpeed)
			stream.KeepSpeed = appliedKeepSpeed = keepSpeed;
		if (floor != appliedFloor)
			stream.SpeedFloor = appliedFloor = floor;
		if (gap != appliedGap)
			stream.RhythmGap = appliedGap = gap;
		if (rate != appliedRate)
			stream.RhythmRate = appliedRate = rate;

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
