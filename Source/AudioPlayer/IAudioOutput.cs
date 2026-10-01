using System;

namespace AudioPlayer;

/// <summary>Called on the audio thread to fill <paramref name="buffer"/> with interleaved samples. Unfilled samples must be left as silence.</summary>
public delegate void AudioRenderCallback(Span<float> buffer);

/// <summary>Creates an output that plays audio of the given format, pulling samples from <paramref name="render"/>.</summary>
public delegate IAudioOutput AudioOutputFactory(int sampleRate, int channels, AudioRenderCallback render);

/// <summary>A sound device that pulls audio from a render callback while running.</summary>
public interface IAudioOutput : IDisposable
{
	bool IsRunning { get; }
	void Start();
	void Stop();

	/// <summary>Frames already taken from the render callback and not yet heard.</summary>
	int BufferedFrames => 0;

	/// <summary>Drop audio taken from the render callback and not yet heard, because it is from before a seek.</summary>
	void Discard() { }
}
