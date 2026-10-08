using Avalonia.Threading;
using LibationMobile.Services;
using Mpeg4Lib;
using System;

namespace LibationMobile.ViewModels;

// The system's media controls: lock screen, Control Centre, headphone buttons.
public partial class NowPlayingViewModel
{
	private IMediaSession? mediaSession;

	private void StartMediaSession()
	{
		StartHomeWidget();
		mediaSession = MediaSession.Platform;
		if (mediaSession is null)
			return;

		mediaSession.PlayRequested += OnPlayRequested;
		mediaSession.PauseRequested += OnPauseRequested;
		mediaSession.TogglePlayPauseRequested += OnToggleRequested;
		mediaSession.SkipForwardRequested += OnSkipForwardRequested;
		mediaSession.SkipBackRequested += OnSkipBackRequested;
		mediaSession.SeekRequested += OnSeekRequested;
		mediaSession.SpeedRequested += OnSpeedRequested;
		ShowMediaInfo();
		if (mediaSession.TakePendingPlay())
			OnPlayRequested();
	}

	private void StopMediaSession()
	{
		if (HomeWidget.Platform is { } widget)
			widget.SpeedRequested -= OnWidgetSpeedRequested;
		if (mediaSession is null)
			return;

		mediaSession.PlayRequested -= OnPlayRequested;
		mediaSession.PauseRequested -= OnPauseRequested;
		mediaSession.TogglePlayPauseRequested -= OnToggleRequested;
		mediaSession.SkipForwardRequested -= OnSkipForwardRequested;
		mediaSession.SkipBackRequested -= OnSkipBackRequested;
		mediaSession.SeekRequested -= OnSeekRequested;
		mediaSession.SpeedRequested -= OnSpeedRequested;
		mediaSession.Clear();
		mediaSession = null;
	}

	/// <summary>
	/// What the lock screen shows. Like the player's own bar, it covers the chapter playing unless that is turned
	/// off: dragging a whole book's bar by a hair jumps hours, and loses the listener's place.
	/// </summary>
	private void ShowMediaInfo()
	{
		if (mediaSession is null)
			return;
		shownChapter = ScrubsChapter ? CurrentChapter : null;
		mediaSession.Show(shownChapter is { } chapter
			? new MediaInfo(chapter.Title, Title, Book.Cover, chapter.Duration, Album: Title)
			: new MediaInfo(Title, Author, Book.Cover, Duration));
		UpdateMediaSession();
	}

	/// <summary>The chapter the lock screen covers, or null when it covers the whole book.</summary>
	private Chapter? shownChapter;

	private void UpdateMediaSession()
	{
		UpdateHomeWidget();
		if (mediaSession is null)
			return;
		// A new chapter, or the setting changed: the lock screen needs the new span before the new position.
		if ((ScrubsChapter ? CurrentChapter : null) != shownChapter)
		{
			ShowMediaInfo();
			return;
		}
		mediaSession.Update(player.Position - ScrubberStart, Speed, player.IsPlaying);
	}

	/// <summary>The home-screen widget: shows this book, and changes its speed.</summary>
	private void StartHomeWidget()
	{
		if (HomeWidget.Platform is not { } widget)
			return;
		// Changed on the widget while the book was not open.
		if (widget.TakePendingSpeed(Book.Id) is double speed)
			Speed = speed;
		widget.SpeedRequested += OnWidgetSpeedRequested;
		UpdateHomeWidget();
	}

	private void UpdateHomeWidget()
	{
		if (HomeWidget.Platform is not { } widget)
			return;
		var chapter = HasChapters ? CurrentChapter : null;
		widget.Show(new WidgetInfo(Book.Id, Title, Author, Book.Cover, Duration - player.Position, Speed, player.IsPlaying, Duration,
			chapter?.Title, chapter is null ? null : chapter.EndOffset - player.Position, chapter?.Duration, settings.SkipSeconds, IsBlindMode));
	}

	private void OnWidgetSpeedRequested(string bookId, double speed) => OnUi(() =>
	{
		speedSource = "widget or siri";
		if (bookId == Book.Id)
			Speed = speed;
		else
			settings.SetBookSpeed(bookId, (float)speed);
	});

	// The system raises these on its own thread; the player is only touched from the UI thread.
	private void OnUi(Action action) => Dispatcher.UIThread.Post(() =>
	{
		if (!disposed)
			action();
	});

	private void OnPlayRequested() => OnUi(() => { if (!player.IsPlaying) PlayPause(); });
	private void OnPauseRequested() => OnUi(() => { if (player.IsPlaying) PlayPause(); });
	private void OnToggleRequested() => OnUi(PlayPause);
	private void OnSkipForwardRequested() => OnUi(() => Seek(Position + SkipInterval));
	private void OnSkipBackRequested() => OnUi(() => Seek(Position - SkipInterval));
	// The lock screen's bar covers what was last shown: the chapter, or the book.
	/// <summary>Siri's own speed command, sent to whichever app is playing.</summary>
	private void OnSpeedRequested(double speed) => OnUi(() =>
	{
		speedSource = "siri";
		Speed = speed;
	});

	private void OnSeekRequested(TimeSpan position) => OnUi(() => Seek((shownChapter?.StartOffset ?? TimeSpan.Zero) + position));
}
