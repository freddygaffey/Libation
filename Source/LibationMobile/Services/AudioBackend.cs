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
}
