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

	public AppleMediaSession()
	{
		var commands = MPRemoteCommandCenter.Shared;
		Handle(commands.PlayCommand, () => PlayRequested?.Invoke());
		Handle(commands.PauseCommand, () => PauseRequested?.Invoke());
		Handle(commands.TogglePlayPauseCommand, () => TogglePlayPauseRequested?.Invoke());

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
				PauseRequested?.Invoke();
		});
		AVAudioSession.Notifications.ObserveRouteChange((_, e) =>
		{
			// Headphones unplugged or a speaker disconnected: do not carry on out loud.
			if (e.Reason == AVAudioSessionRouteChangeReason.OldDeviceUnavailable)
				PauseRequested?.Invoke();
		});
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
			Artist = info.Author,
			AlbumTitle = info.Title,
			PlaybackDuration = info.Duration.TotalSeconds,
			MediaType = MPNowPlayingInfoMediaType.Audio
		};

		if (info.Cover is not null && UIImage.LoadFromData(NSData.FromArray(info.Cover)) is UIImage image)
			nowPlaying.Artwork = new MPMediaItemArtwork(image.Size, _ => image);

		MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = nowPlaying;
	}

	public void Update(TimeSpan position, double speed, bool isPlaying)
	{
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
