using AVFoundation;
using Foundation;
using LibationMobile.Services;
using MediaPlayer;
using System;
using UIKit;

namespace LibationMobile.iOS;

/// <summary>
/// The lock screen and Control Centre player, and the play, pause and skip buttons on headphones and in cars.
/// Also pauses when a call or another app takes the audio, and when headphones are unplugged.
/// </summary>
public sealed class AppleMediaSession : IMediaSession
{
	private const double SKIP_SECONDS = 30;

	public event Action? PlayRequested;
	public event Action? PauseRequested;
	public event Action? TogglePlayPauseRequested;
	public event Action? SkipForwardRequested;
	public event Action? SkipBackRequested;
	public event Action<TimeSpan>? SeekRequested;

	private MPNowPlayingInfo? nowPlaying;
	private bool isPlaying;
	private bool wasPlayingWhenInterrupted;
	private bool pendingPlay;

	public AppleMediaSession()
	{
		var commands = MPRemoteCommandCenter.Shared;
		Handle(commands.PlayCommand, RequestPlay);
		Handle(commands.PauseCommand, RequestPause);
		Handle(commands.TogglePlayPauseCommand, () =>
		{
			// Before a book is loaded, toggling can only mean play.
			if (TogglePlayPauseRequested is null)
				RequestPlay();
			else
				TogglePlayPauseRequested.Invoke();
		});

		// Audiobook controls: 30-second skips rather than next and previous track.
		commands.SkipForwardCommand.PreferredIntervals = [SKIP_SECONDS];
		commands.SkipBackwardCommand.PreferredIntervals = [SKIP_SECONDS];
		Handle(commands.SkipForwardCommand, () => SkipForwardRequested?.Invoke());
		Handle(commands.SkipBackwardCommand, () => SkipBackRequested?.Invoke());
		commands.NextTrackCommand.Enabled = false;
		commands.PreviousTrackCommand.Enabled = false;

		commands.ChangePlaybackPositionCommand.Enabled = true;
		commands.ChangePlaybackPositionCommand.AddTarget(e =>
		{
			if (e is not MPChangePlaybackPositionCommandEvent change)
				return MPRemoteCommandHandlerStatus.CommandFailed;
			SeekRequested?.Invoke(TimeSpan.FromSeconds(change.PositionTime));
			return MPRemoteCommandHandlerStatus.Success;
		});

		AVAudioSession.Notifications.ObserveInterruption((_, e) =>
		{
			if (e.InterruptionType == AVAudioSessionInterruptionType.Began)
			{
				wasPlayingWhenInterrupted = isPlaying;
				PauseRequested?.Invoke();
			}
			// After a call or Siri: carry on if it was playing and iOS says resuming is expected.
			else if (wasPlayingWhenInterrupted && e.Option == AVAudioSessionInterruptionOptions.ShouldResume)
			{
				wasPlayingWhenInterrupted = false;
				PlayRequested?.Invoke();
			}
		});
		AVAudioSession.Notifications.ObserveRouteChange((_, e) =>
		{
			// Headphones unplugged or a speaker disconnected: do not carry on out loud.
			if (e.Reason == AVAudioSessionRouteChangeReason.OldDeviceUnavailable)
				PauseRequested?.Invoke();
		});
	}

	/// <summary>
	/// Play, or remember to once a book is loaded: when iOS has closed the app, pressing play on the lock screen
	/// relaunches it, and the press arrives before the last book has been opened.
	/// </summary>
	public void RequestPlay()
	{
		if (PlayRequested is null)
			pendingPlay = true;
		else
			PlayRequested.Invoke();
	}

	/// <summary>Pause, if anything is playing. Also from the home-screen widget.</summary>
	public void RequestPause()
	{
		pendingPlay = false;
		PauseRequested?.Invoke();
	}

	public bool TakePendingPlay()
	{
		var pending = pendingPlay;
		pendingPlay = false;
		return pending;
	}

	private static void Handle(MPRemoteCommand command, Action action)
	{
		command.Enabled = true;
		command.AddTarget(_ =>
		{
			action();
			return MPRemoteCommandHandlerStatus.Success;
		});
	}

	public void Show(MediaInfo info)
	{
		nowPlaying = new MPNowPlayingInfo
		{
			Title = info.Title,
			Artist = info.Subtitle,
			AlbumTitle = info.Album ?? info.Title,
			PlaybackDuration = info.Duration.TotalSeconds,
			MediaType = MPNowPlayingInfoMediaType.Audio
		};

		if (info.Cover is not null && UIImage.LoadFromData(NSData.FromArray(info.Cover)) is UIImage image)
			nowPlaying.Artwork = new MPMediaItemArtwork(image.Size, _ => image);

		MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = nowPlaying;
	}

	public void Update(TimeSpan position, double speed, bool isPlaying)
	{
		this.isPlaying = isPlaying;
		if (nowPlaying is null)
			return;

		// The system advances the shown time by itself at PlaybackRate, so it only needs telling when something changes.
		nowPlaying.ElapsedPlaybackTime = position.TotalSeconds;
		nowPlaying.PlaybackRate = isPlaying ? speed : 0;
		nowPlaying.DefaultPlaybackRate = speed;
		MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = nowPlaying;
	}

	public void Clear()
	{
		nowPlaying = null;
		MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo();
	}
}
