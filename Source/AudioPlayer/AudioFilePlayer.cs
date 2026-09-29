using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;
using System;
using System.Threading;

namespace AudioPlayer;

/// <summary>
/// Plays an <see cref="IPcmSource"/> on the default output device, with pitch-preserving speed control.
/// </summary>
/// <remarks>
/// Decoding and time stretching run on the audio device's thread. <see cref="PlaybackEnded"/> is raised on a
/// thread pool thread; UI subscribers must marshal to their own thread.
/// </remarks>
public sealed class AudioFilePlayer : IDisposable
{
	public const float MIN_SPEED = 0.5f;
	public const float MAX_SPEED = 10f;
	// Samples decoded per pull from the source. Small enough to keep the audio callback short.
	private const int DECODE_CHUNK_FRAMES = 2048;

	public event EventHandler? PlaybackEnded;

	public TimeSpan Duration => source.Duration;
	public bool IsPlaying => device.IsRunning;

	/// <summary>Playback speed, clamped to [<see cref="MIN_SPEED"/>, <see cref="MAX_SPEED"/>].</summary>
	public float Speed
	{
		get => sonic.Speed;
		set
		{
			lock (locker)
				sonic.Speed = Math.Clamp(value, MIN_SPEED, MAX_SPEED);
		}
	}

	/// <summary>Output gain, where 1 is unchanged.</summary>
	public float Volume
	{
		get => sonic.Volume;
		set
		{
			lock (locker)
				sonic.Volume = Math.Max(0f, value);
		}
	}

	/// <summary>Position in the source that is currently being heard.</summary>
	public TimeSpan Position
	{
		get
		{
			lock (locker)
			{
				// Frames handed to Sonic but not yet heard: its pending input, plus its pending output scaled back to source time.
				var bufferedFrames = sonic.InputFramesPending + (long)(sonic.FramesAvailable * sonic.Speed);
				return TimeSpan.FromSeconds(Math.Max(0, sourceFrame - bufferedFrames) / (double)source.SampleRate);
			}
		}
	}

	private readonly Lock locker = new();
	private readonly IPcmSource source;
	private readonly Sonic sonic;
	private readonly MiniAudioEngine engine;
	private readonly AudioPlaybackDevice device;
	private readonly StretchComponent component;
	private readonly float[] decodeBuffer;
	/// <summary>Index of the next source frame to be handed to <see cref="sonic"/>.</summary>
	private long sourceFrame;
	private bool sourceEnded;
	private bool sonicFlushed;
	private bool endRaised;

	/// <param name="source">The audio to play. Ownership passes to the player, which disposes it.</param>
	public AudioFilePlayer(IPcmSource source)
	{
		ArgumentNullException.ThrowIfNull(source, nameof(source));

		this.source = source;
		sonic = new Sonic(source.SampleRate, source.Channels);
		decodeBuffer = new float[DECODE_CHUNK_FRAMES * source.Channels];

		var format = new AudioFormat
		{
			Format = SampleFormat.F32,
			Channels = source.Channels,
			Layout = AudioFormat.GetLayoutFromChannels(source.Channels),
			SampleRate = source.SampleRate
		};

		engine = new MiniAudioEngine();
		try
		{
			device = engine.InitializePlaybackDevice(null, format);
			component = new StretchComponent(engine, format, this);
			device.MasterMixer.AddComponent(component);
		}
		catch
		{
			engine.Dispose();
			throw;
		}
	}

	public void Play()
	{
		lock (locker)
		{
			if (endRaised)
				SeekInternal(TimeSpan.Zero);
		}
		device.Start();
	}

	public void Pause() => device.Stop();

	public void Seek(TimeSpan position)
	{
		lock (locker)
			SeekInternal(position);
	}

	private void SeekInternal(TimeSpan position)
	{
		sourceFrame = source.Seek(position);
		sonic.Clear();
		sourceEnded = false;
		sonicFlushed = false;
		endRaised = false;
	}

	/// <summary>Fill <paramref name="buffer"/> with stretched audio. Called on the audio device's thread.</summary>
	private void FillBuffer(Span<float> buffer)
	{
		lock (locker)
		{
			var written = 0;
			while (written < buffer.Length)
			{
				var read = sonic.Read(buffer[written..]);
				written += read;
				if (read > 0)
					continue;

				if (!sourceEnded)
				{
					var decoded = source.Read(decodeBuffer);
					if (decoded > 0)
					{
						sonic.Write(decodeBuffer.AsSpan(0, decoded));
						sourceFrame += decoded / source.Channels;
					}
					else
						sourceEnded = true;
				}
				else if (!sonicFlushed)
				{
					sonic.Flush();
					sonicFlushed = true;
				}
				else
				{
					if (!endRaised)
					{
						endRaised = true;
						ThreadPool.QueueUserWorkItem(_ => PlaybackEnded?.Invoke(this, EventArgs.Empty));
					}
					// The rest of the buffer stays silent.
					break;
				}
			}
		}
	}

	public void Dispose()
	{
		device.Stop();
		device.MasterMixer.RemoveComponent(component);
		component.Dispose();
		device.Dispose();
		engine.Dispose();
		source.Dispose();
	}

	private sealed class StretchComponent : SoundComponent
	{
		private readonly AudioFilePlayer player;

		public StretchComponent(AudioEngine engine, AudioFormat format, AudioFilePlayer player) : base(engine, format)
		{
			this.player = player;
			Name = nameof(AudioFilePlayer);
		}

		protected override void GenerateAudio(Span<float> buffer, int channels) => player.FillBuffer(buffer);
	}
}
