using System;
using System.Collections.Generic;

namespace LibationMobile.Services;

/// <summary>What the home-screen widget shows: the book last played.</summary>
/// <param name="Remaining">Book time left, at 1x; likewise the other times.</param>
/// <param name="SkipSeconds">The skip buttons' length, for their labels.</param>
public record WidgetInfo(string BookId, string Title, string? Author, byte[]? Cover, TimeSpan Remaining, double Speed, bool IsPlaying,
	TimeSpan Duration, string? ChapterTitle = null, TimeSpan? ChapterRemaining = null, TimeSpan? ChapterDuration = null, double SkipSeconds = 30,
	bool SpeedHidden = false);

/// <summary>A downloaded book or podcast episode, as Siri lists it.</summary>
public record PlayableBook(string Id, string Title, string? Author);

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

	/// <summary>A book was asked for, by the widget or Siri: open it, by its ID, and play it if Play is true.</summary>
	event Action<string, bool>? OpenRequested;

	/// <summary>A book asked for before the app was ready to open it. Reading it clears it.</summary>
	(string Id, bool Play)? TakePendingOpen();

	/// <summary>The books that can be played, last listened first, for Siri to list and pick from by number or name.</summary>
	void ShowBooks(IReadOnlyList<PlayableBook> books);

	/// <summary>
	/// The widget opened the app with a link (see <see cref="HomeWidget.LINK_SCHEME"/>): "play" to play the last book, or
	/// "open/ID" for a recent one, or "play/ID" to open a book and play it. Buttons that need the app on screen use links; iOS only runs an action that opens
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
