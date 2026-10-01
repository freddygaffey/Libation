using AudioPlayer;
using System;

namespace LibationMobile.Services;

/// <summary>How each platform decodes and plays audio. Set by the platform head at startup.</summary>
public static class AudioBackend
{
	/// <summary>Opens a downloaded book for decoding. Defaults to SoundFlow's FFmpeg decoder.</summary>
	public static Func<string, IPcmSource> OpenSource { get; set; } = path => new FFmpegPcmSource(path);

	/// <summary>Creates the sound output. Null uses <see cref="AudioFilePlayer"/>'s default, SoundFlow.</summary>
	public static AudioOutputFactory? CreateOutput { get; set; }

	private static volatile float nonlinearity = 1f;
	/// <summary>
	/// How unevenly speech is sped up, 0 to 1: 1 hurries vowels and pauses and spares consonants, 0 speeds
	/// everything up evenly. Takes effect during playback.
	/// </summary>
	public static float Nonlinearity
	{
		get => nonlinearity;
		set => nonlinearity = Math.Clamp(value, 0f, 1f);
	}

	private static volatile bool useNonlinear = true;
	/// <summary>
	/// Which speed-up method plays: speechwarp (true), or the original even one built into the player (false).
	/// Takes effect during playback.
	/// </summary>
	public static bool UseNonlinear
	{
		get => useNonlinear;
		set => useNonlinear = value;
	}

	/// <summary>False if the speechwarp library could not be loaded on this device, so speed-up is always even.</summary>
	public static bool NonlinearAvailable { get; private set; } = true;

	/// <summary>The speed changer: both methods with the chosen one playing, or only the original one where speechwarp is missing.</summary>
	public static ITimeStretcher CreateStretcher(int sampleRate, int channels)
	{
		if (NonlinearAvailable)
		{
			try
			{
				return new SwitchingTimeStretcher(new SpeechwarpTimeStretcher(sampleRate, channels), new SonicTimeStretcher(sampleRate, channels));
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				Console.WriteLine($"speechwarp is not available, using even speed-up: {ex.Message}");
				NonlinearAvailable = false;
			}
		}
		return new SonicTimeStretcher(sampleRate, channels);
	}
}
