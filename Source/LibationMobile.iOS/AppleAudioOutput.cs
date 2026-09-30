using AudioPlayer;
using AudioToolbox;
using AVFoundation;
using System;
using System.Buffers;

namespace LibationMobile.iOS;

/// <summary>
/// Plays through AVAudioEngine. A source node pulls interleaved samples from the render callback and hands them
/// to the engine one channel at a time, which is the layout Apple's standard format uses.
/// </summary>
public sealed class AppleAudioOutput : IAudioOutput
{
	private readonly AVAudioEngine engine = new();
	private readonly AVAudioSourceNode sourceNode;
	private readonly AudioRenderCallback render;
	private readonly int channels;

	public bool IsRunning => engine.Running;

	public AppleAudioOutput(int sampleRate, int channels, AudioRenderCallback render)
	{
		this.render = render;
		this.channels = channels;

		// Playback category: keeps playing with the screen locked and ignores the silent switch, like any audiobook app.
		var session = AVAudioSession.SharedInstance();
		session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.AllowAirPlay | AVAudioSessionCategoryOptions.AllowBluetoothA2DP);
		session.SetMode(AVAudioSessionMode.SpokenAudio, out _);

		var format = new AVAudioFormat(sampleRate, (uint)channels);
		sourceNode = new AVAudioSourceNode(format, Render);
		engine.AttachNode(sourceNode);
		engine.Connect(sourceNode, engine.MainMixerNode, format);
		engine.Prepare();
	}

	private unsafe int Render(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
	{
		var count = (int)frameCount * channels;
		var interleaved = ArrayPool<float>.Shared.Rent(count);
		try
		{
			var span = interleaved.AsSpan(0, count);
			span.Clear();
			render(span);

			for (var c = 0; c < channels && c < outputData.Count; c++)
			{
				var channel = (float*)outputData[c].Data;
				for (var f = 0; f < frameCount; f++)
					channel[f] = span[f * channels + c];
			}
		}
		finally
		{
			ArrayPool<float>.Shared.Return(interleaved);
		}
		return 0;
	}

	public void Start()
	{
		AVAudioSession.SharedInstance().SetActive(true);
		engine.StartAndReturnError(out var error);
		if (error is not null)
			throw new InvalidOperationException($"Audio could not start: {error.LocalizedDescription}");
	}

	public void Stop() => engine.Pause();

	public void Dispose()
	{
		engine.Stop();
		engine.DetachNode(sourceNode);
		sourceNode.Dispose();
		engine.Dispose();
	}
}
