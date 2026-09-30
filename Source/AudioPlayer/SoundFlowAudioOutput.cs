using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;
using System;

namespace AudioPlayer;

/// <summary>Plays on the default output device through SoundFlow's miniaudio backend (Windows, macOS, Linux, Android).</summary>
public sealed class SoundFlowAudioOutput : IAudioOutput
{
	private readonly MiniAudioEngine engine;
	private readonly AudioPlaybackDevice device;
	private readonly RenderComponent component;

	public bool IsRunning => device.IsRunning;

	public SoundFlowAudioOutput(int sampleRate, int channels, AudioRenderCallback render)
	{
		var format = new AudioFormat
		{
			Format = SampleFormat.F32,
			Channels = channels,
			Layout = AudioFormat.GetLayoutFromChannels(channels),
			SampleRate = sampleRate
		};

		engine = new MiniAudioEngine();
		try
		{
			device = engine.InitializePlaybackDevice(null, format);
			component = new RenderComponent(engine, format, render);
			device.MasterMixer.AddComponent(component);
		}
		catch
		{
			engine.Dispose();
			throw;
		}
	}

	public void Start() => device.Start();
	public void Stop() => device.Stop();

	public void Dispose()
	{
		device.Stop();
		device.MasterMixer.RemoveComponent(component);
		component.Dispose();
		device.Dispose();
		engine.Dispose();
	}

	private sealed class RenderComponent : SoundComponent
	{
		private readonly AudioRenderCallback render;

		public RenderComponent(AudioEngine engine, AudioFormat format, AudioRenderCallback render) : base(engine, format)
		{
			this.render = render;
			Name = nameof(AudioFilePlayer);
		}

		protected override void GenerateAudio(Span<float> buffer, int channels) => render(buffer);
	}
}
