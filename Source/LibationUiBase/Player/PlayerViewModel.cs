using AudioPlayer;
using DataLayer;
using LibationFileManager;
using Mpeg4Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationUiBase.Player;

/// <summary>Plays one downloaded audiobook, remembering where it was paused.</summary>
/// <remarks>
/// Create and use on the UI thread. Timer ticks and end-of-book notifications are posted to the UI thread,
/// so all state changes, and disposal of the player, happen on that one thread.
/// </remarks>
public class PlayerViewModel : ReactiveObject, IDisposable
{
	public static IReadOnlyList<double> SpeedPresets { get; } = [1, 1.5, 2, 3, 4, 5, 7, 10];
	public static double MinSpeed => AudioFilePlayer.MIN_SPEED;
	public static double MaxSpeed => AudioFilePlayer.MAX_SPEED;

	private static readonly TimeSpan SkipInterval = TimeSpan.FromSeconds(30);
	/// <summary>Going to the previous chapter within this far into a chapter restarts it instead.</summary>
	private static readonly TimeSpan RestartChapterThreshold = TimeSpan.FromSeconds(3);
	/// <summary>Resuming within this far of the end starts the book over.</summary>
	private static readonly TimeSpan FinishedThreshold = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

	public LibraryBook LibraryBook { get; }
	public string Title => LibraryBook.Book.TitleWithSubtitle;
	public string Authors => LibraryBook.Book.AuthorNames;
	public TimeSpan Duration => player.Duration;
	public double DurationSeconds => Duration.TotalSeconds;
	public IReadOnlyList<Chapter> Chapters { get; }
	public bool HasChapters => Chapters.Count > 0;

	public bool IsPlaying { get => _isPlaying; private set => RaiseAndSetIfChanged(ref _isPlaying, value); }
	public TimeSpan Position { get => _position; private set => RaiseAndSetIfChanged(ref _position, value); }
	public string ElapsedText => FormatTime(Position);
	/// <summary>Time left at the current speed.</summary>
	public string RemainingText => "-" + FormatTime((Duration - Position) / Speed);

	/// <summary>Playback position in seconds, for binding to a slider. Setting it seeks.</summary>
	public double PositionSeconds
	{
		get => Position.TotalSeconds;
		set
		{
			// Ignore the echo of a position update; only a user drag moves this far from the true position.
			if (Math.Abs(value - Position.TotalSeconds) > UpdateInterval.TotalSeconds * MaxSpeed)
				Seek(TimeSpan.FromSeconds(value));
		}
	}

	public double Speed
	{
		get => player.Speed;
		set
		{
			player.Speed = (float)value;
			RaisePropertyChanged(nameof(Speed));
			RaisePropertyChanged(nameof(SpeedText));
			RaisePropertyChanged(nameof(RemainingText));
		}
	}

	public string SpeedText => $"{Speed:0.##}x";

	public double Volume
	{
		get => player.Volume;
		set
		{
			player.Volume = (float)value;
			RaisePropertyChanged(nameof(Volume));
		}
	}

	public Chapter? CurrentChapter
	{
		get => _currentChapter;
		set
		{
			// Two-way binding from a chapter list: selecting a different chapter jumps to it.
			if (value is not null && value != ChapterAt(Position))
				Seek(value.StartOffset);
		}
	}

	private readonly AudioFilePlayer player;
	private readonly Timer updateTimer;
	private DateTime lastSaved = DateTime.MinValue;
	private bool _isPlaying;
	private TimeSpan _position;
	private Chapter? _currentChapter;
	private bool disposed;

	private PlayerViewModel(LibraryBook libraryBook, AudioFilePlayer player, IReadOnlyList<Chapter> chapters)
	{
		LibraryBook = libraryBook;
		this.player = player;
		Chapters = chapters;

		player.Speed = Configuration.Instance.PlayerSpeed;
		player.PlaybackEnded += Player_PlaybackEnded;

		if (PlaybackPositions.Get(libraryBook.Book.AudibleProductId) is TimeSpan saved && saved < Duration - FinishedThreshold)
			player.Seek(saved);

		UpdatePosition();
		updateTimer = new Timer(_ => BeginInvoke(UpdatePosition), null, UpdateInterval, UpdateInterval);
	}

	/// <summary>Open a downloaded book for playback.</summary>
	/// <exception cref="InvalidOperationException">The book's audio file is missing or split into several files.</exception>
	public static async Task<PlayerViewModel> CreateAsync(LibraryBook libraryBook)
	{
		ArgumentNullException.ThrowIfNull(libraryBook, nameof(libraryBook));

		var paths = AudibleFileStorage.Audio.GetPaths(libraryBook.Book.AudibleProductId);
		if (paths.Count == 0)
			throw new InvalidOperationException("The audio file for this book could not be found. Download it, or use 'Locate file...' if it was moved.");
		if (paths.Count > 1)
			throw new InvalidOperationException("This book was downloaded split into several files. The player only supports books downloaded as a single file.");

		string path = paths[0];
		var chapters = await AudioFileChapters.ReadAsync(path);
		var player = await Task.Run(() => new AudioFilePlayer(new FFmpegPcmSource(path)));
		return new PlayerViewModel(libraryBook, player, chapters);
	}

	public void PlayPause()
	{
		if (player.IsPlaying)
		{
			player.Pause();
			SavePosition();
		}
		else
			player.Play();

		UpdatePosition();
	}

	public void SkipBack() => Seek(Position - SkipInterval);
	public void SkipForward() => Seek(Position + SkipInterval);

	public void PreviousChapter()
	{
		var current = ChapterAt(Position);
		if (current is null)
			return;

		var previous = Chapters.LastOrDefault(c => c.StartOffset < current.StartOffset);
		if (Position - current.StartOffset > RestartChapterThreshold || previous is null)
			Seek(current.StartOffset);
		else
			Seek(previous.StartOffset);
	}

	public void NextChapter()
	{
		if (Chapters.FirstOrDefault(c => c.StartOffset > Position) is Chapter next)
			Seek(next.StartOffset);
	}

	public void Seek(TimeSpan position)
	{
		position = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
		player.Seek(position);
		UpdatePosition();
	}

	private Chapter? ChapterAt(TimeSpan position)
		=> Chapters.LastOrDefault(c => c.StartOffset <= position) ?? Chapters.FirstOrDefault();

	private void UpdatePosition()
	{
		if (disposed)
			return;

		IsPlaying = player.IsPlaying;
		Position = player.Position;
		RaisePropertyChanged(nameof(PositionSeconds));
		RaisePropertyChanged(nameof(ElapsedText));
		RaisePropertyChanged(nameof(RemainingText));

		var chapter = ChapterAt(Position);
		if (chapter != _currentChapter)
		{
			_currentChapter = chapter;
			RaisePropertyChanged(nameof(CurrentChapter));
		}

		if (IsPlaying && DateTime.UtcNow - lastSaved > SaveInterval)
			SavePosition();
	}

	private void SavePosition()
	{
		lastSaved = DateTime.UtcNow;
		PlaybackPositions.Set(LibraryBook.Book.AudibleProductId, player.Position);
	}

	private void Player_PlaybackEnded(object? sender, EventArgs e) => BeginInvoke(() =>
	{
		if (disposed)
			return;

		player.Pause();
		PlaybackPositions.Remove(LibraryBook.Book.AudibleProductId);
		UpdatePosition();
	});

	private static string FormatTime(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : time.ToString("m\\:ss");

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;

		updateTimer.Dispose();
		player.PlaybackEnded -= Player_PlaybackEnded;
		SavePosition();
		Configuration.Instance.PlayerSpeed = player.Speed;
		player.Dispose();
		GC.SuppressFinalize(this);
	}
}
