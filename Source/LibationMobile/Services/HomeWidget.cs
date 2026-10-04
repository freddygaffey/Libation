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

	/// <summary>
	/// The widget opened the app with a link (see <see cref="HomeWidget.LINK_SCHEME"/>): "play" to play the last book, or
	/// "open/ID" for a recent one. Buttons that need the app on screen use links; iOS only runs an action that opens
	/// the app if the app itself declares it, which this app cannot.
	/// </summary>
	void OpenLink(Uri link);
}

public static class HomeWidget
{
	/// <summary>Set by the platform head at startup. Null where there is no widget.</summary>
	public static IHomeWidget? Platform { get; set; }

	/// <summary>The URL scheme of the widget's links, registered in the iOS Info.plist.</summary>
	public const string LINK_SCHEME = "libation-player";
}
