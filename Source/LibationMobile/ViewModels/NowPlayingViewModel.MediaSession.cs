using Avalonia.Threading;
using LibationMobile.Services;
using System;

namespace LibationMobile.ViewModels;

// The system's media controls: lock screen, Control Centre, headphone buttons.
public partial class NowPlayingViewModel
{
	private IMediaSession? mediaSession;

	private void StartMediaSession()
	{
		mediaSession = MediaSession.Platform;
		if (mediaSession is null)
			return;

		mediaSession.PlayRequested += OnPlayRequested;
		mediaSession.PauseRequested += OnPauseRequested;
		mediaSession.TogglePlayPauseRequested += OnToggleRequested;
		mediaSession.SkipForwardRequested += OnSkipForwardRequested;
		mediaSession.SkipBackRequested += OnSkipBackRequested;
		mediaSession.SeekRequested += OnSeekRequested;
		mediaSession.Show(new MediaInfo(Title, Author, Book.Cover, Duration));
		UpdateMediaSession();
		if (mediaSession.TakePendingPlay())
			OnPlayRequested();
	}

	private void StopMediaSession()
	{
		if (mediaSession is null)
			return;

		mediaSession.PlayRequested -= OnPlayRequested;
		mediaSession.PauseRequested -= OnPauseRequested;
		mediaSession.TogglePlayPauseRequested -= OnToggleRequested;
		mediaSession.SkipForwardRequested -= OnSkipForwardRequested;
		mediaSession.SkipBackRequested -= OnSkipBackRequested;
		mediaSession.SeekRequested -= OnSeekRequested;
		mediaSession.Clear();
		mediaSession = null;
	}

	private void UpdateMediaSession() => mediaSession?.Update(player.Position, Speed, player.IsPlaying);

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
	private void OnSeekRequested(TimeSpan position) => OnUi(() => Seek(position));
}
