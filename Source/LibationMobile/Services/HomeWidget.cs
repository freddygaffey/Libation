using System;

namespace LibationMobile.Services;

/// <summary>What the home-screen widget shows: the book last played.</summary>
/// <param name="Remaining">Book time left, at 1x; likewise the other times.</param>
/// <param name="SkipSeconds">The skip buttons' length, for their labels.</param>
public record WidgetInfo(string BookId, string Title, string? Author, byte[]? Cover, TimeSpan Remaining, double Speed, bool IsPlaying,
	TimeSpan Duration, string? ChapterTitle = null, TimeSpan? ChapterRemaining = null, TimeSpan? ChapterDuration = null, double SkipSeconds = 30);

/// <summary>
/// The platform's home-screen widget. Its play and pause buttons arrive through <see cref="IMediaSession"/>,
/// like the lock screen's; its speed buttons arrive here.
/// </summary>
public interface IHomeWidget
{
	void Show(WidgetInfo info);

	/// <summary>The widget changed the speed of a book, given by its ID.</summary>
	event Action<string, double>? SpeedRequested;

	/// <summary>A speed the widget set for this book before the app had the book open. Reading it clears it.</summary>
	double? TakePendingSpeed(string bookId);

	/// <summary>A recent book was tapped on the widget: open it, by its ID.</summary>
	event Action<string>? OpenRequested;

	/// <summary>A book tapped before the app was ready to open it. Reading it clears it.</summary>
	string? TakePendingOpen();
}

public static class HomeWidget
{
	/// <summary>Set by the platform head at startup. Null where there is no widget.</summary>
	public static IHomeWidget? Platform { get; set; }
}
