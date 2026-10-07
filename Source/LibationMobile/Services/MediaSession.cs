using System;

namespace LibationMobile.Services;

/// <summary>What the system's media controls show for the book being played.</summary>
/// <param name="Title">The main line: the chapter, when the controls cover one chapter.</param>
/// <param name="Subtitle">The second line: the book's title, or its author.</param>
/// <param name="Duration">What the system's progress bar covers: the chapter, or the whole book.</param>
public record MediaInfo(string Title, string? Subtitle, byte[]? Cover, TimeSpan Duration, string? Album = null);

/// <summary>
/// The platform's own media controls: lock screen, Control Centre or notification, headphone buttons.
/// The player tells it what is playing; it tells the player what the listener pressed.
/// </summary>
public interface IMediaSession
{
	/// <summary>A book has been loaded.</summary>
	void Show(MediaInfo info);

	/// <summary>Playback started, stopped, jumped or changed speed. The position is within what <see cref="MediaInfo.Duration"/> covers.</summary>
	void Update(TimeSpan position, double speed, bool isPlaying);

	/// <summary>Nothing is loaded any more.</summary>
	void Clear();

	/// <summary>Whether play was pressed before a book was loaded to receive it. Reading it clears it.</summary>
	bool TakePendingPlay();

	event Action? PlayRequested;
	event Action? PauseRequested;
	event Action? TogglePlayPauseRequested;
	event Action? SkipForwardRequested;
	event Action? SkipBackRequested;
	event Action<TimeSpan>? SeekRequested;

	/// <summary>The system asked for a speed, such as Siri's "set playback speed to 2".</summary>
	event Action<double>? SpeedRequested;
}

public static class MediaSession
{
	/// <summary>Set by the platform head at startup. Null where there is no integration yet.</summary>
	public static IMediaSession? Platform { get; set; }
}
