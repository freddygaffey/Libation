using System;
using System.Threading;

namespace AudioPlayer;

/// <summary>
/// Plays an <see cref="IPcmSource"/> with pitch-preserving speed control.
/// </summary>
/// <remarks>
/// Decoding and time stretching run on the audio output's thread. <see cref="PlaybackEnded"/> is raised on a
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
	public bool IsPlaying => output.IsRunning;

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
	private readonly IAudioOutput output;
	private readonly float[] decodeBuffer;
	/// <summary>Index of the next source frame to be handed to <see cref="sonic"/>.</summary>
	private long sourceFrame;
	private bool sourceEnded;
	private bool sonicFlushed;
	private bool endRaised;

	/// <param name="source">The audio to play. Ownership passes to the player, which disposes it.</param>
	/// <param name="outputFactory">Where to play it. Defaults to <see cref="SoundFlowAudioOutput"/>.</param>
	public AudioFilePlayer(IPcmSource source, AudioOutputFactory? outputFactory = null)
	{
		ArgumentNullException.ThrowIfNull(source, nameof(source));

		this.source = source;
		sonic = new Sonic(source.SampleRate, source.Channels);
		decodeBuffer = new float[DECODE_CHUNK_FRAMES * source.Channels];
		output = (outputFactory ?? ((rate, channels, render) => new SoundFlowAudioOutput(rate, channels, render)))
			(source.SampleRate, source.Channels, FillBuffer);
	}

	public void Play()
	{
		lock (locker)
		{
			if (endRaised)
				SeekInternal(TimeSpan.Zero);
		}
		output.Start();
	}

	public void Pause() => output.Stop();

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

	/// <summary>Fill <paramref name="buffer"/> with stretched audio. Called on the audio output's thread.</summary>
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
		output.Dispose();
		source.Dispose();
	}
}
