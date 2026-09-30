using AudioPlayer;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using Mpeg4Lib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>One line in the chapter list.</summary>
public partial class ChapterRowViewModel(int number, Chapter chapter) : ObservableObject
{
	public Chapter Chapter { get; } = chapter;
	public int Number { get; } = number;
	public string Title => Chapter.Title;
	public string LengthText { get; } = NowPlayingViewModel.FormatTime(chapter.Duration);

	/// <summary>The chapter playing now.</summary>
	[ObservableProperty]
	private bool isCurrent;

	/// <summary>Before the current chapter.</summary>
	[ObservableProperty]
	private bool isPlayed;
}

/// <summary>The one book currently loaded for playback. Use on the UI thread.</summary>
public partial class NowPlayingViewModel : ObservableObject, IDisposable
{
	public static IReadOnlyList<double> SpeedStops { get; } = [1, 1.5, 2, 3, 5, 10];
	public const double MIN_SPEED = AudioFilePlayer.MIN_SPEED;
	public const double MAX_SPEED = AudioFilePlayer.MAX_SPEED;
	private const double SPEED_STEP = 0.1;

	private static readonly TimeSpan SkipInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan RestartChapterThreshold = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan FinishedThreshold = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

	public LocalBook Book { get; }
	public string Title => Book.Title;
	public string? Author => Book.Author;
	public Bitmap? Cover { get; }
	public IReadOnlyList<Chapter> Chapters { get; }
	public bool HasChapters => Chapters.Count > 1;
	public TimeSpan Duration => player.Duration;
	public double DurationSeconds => Duration.TotalSeconds;

	public IReadOnlyList<ChapterRowViewModel> ChapterRows { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PositionSeconds), nameof(Progress), nameof(ElapsedText), nameof(RemainingText), nameof(ChapterRemainingText))]
	private TimeSpan position;

	/// <summary>The chapter list, shown over Now Playing.</summary>
	[ObservableProperty]
	private bool isChapterListOpen;

	/// <summary>The chapter playing now, or null for a book without chapters.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ChapterText))]
	private ChapterRowViewModel? currentChapterRow;

	[ObservableProperty]
	private bool isPlaying;

	/// <summary>Playback position in seconds, for a slider. Setting it seeks.</summary>
	public double PositionSeconds
	{
		get => Position.TotalSeconds;
		set
		{
			if (Math.Abs(value - Position.TotalSeconds) >= 1)
				Seek(TimeSpan.FromSeconds(value));
		}
	}

	public double Progress => Duration > TimeSpan.Zero ? Position / Duration : 0;
	public string ElapsedText => FormatTime(Position);
	public string RemainingText => $"{FormatTime((Duration - Position) / Speed)} left at {SpeedText}";

	/// <summary>Time to the end of this chapter at the current speed: how long until the next natural stopping point.</summary>
	public string ChapterRemainingText
		=> CurrentChapterRow?.Chapter is Chapter chapter
			? $"{FormatTime((chapter.EndOffset - Position) / Speed)} left in chapter"
			: "";

	public double Speed
	{
		get => player.Speed;
		set
		{
			var speed = Math.Round(Math.Clamp(value, MIN_SPEED, MAX_SPEED) / SPEED_STEP) * SPEED_STEP;
			if (Math.Abs(speed - player.Speed) < 0.001)
				return;
			player.Speed = (float)speed;
			settings.Speed = (float)speed;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SpeedText));
			OnPropertyChanged(nameof(RemainingText));
			OnPropertyChanged(nameof(ChapterRemainingText));
		}
	}

	public string SpeedText => $"{Speed:0.0}×";

	public string ChapterText
		=> HasChapters && CurrentChapterRow is { } row ? $"{row.Title}, {row.Number} of {Chapters.Count}" : "";

	private Chapter? CurrentChapter => CurrentChapterRow?.Chapter ?? Chapters.FirstOrDefault();

	private readonly AudioFilePlayer player;
	private readonly MobileSettings settings;
	private readonly DispatcherTimer timer;
	private DateTime lastSaved = DateTime.MinValue;
	private bool disposed;

	private NowPlayingViewModel(LocalBook book, AudioFilePlayer player, IReadOnlyList<Chapter> chapters, MobileSettings settings)
	{
		Book = book;
		this.player = player;
		this.settings = settings;
		Chapters = chapters;
		ChapterRows = chapters.Select((c, i) => new ChapterRowViewModel(i + 1, c)).ToList();
		Cover = LoadCover(book.Cover, 900);

		player.Speed = settings.Speed;
		player.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(OnPlaybackEnded);
		if (settings.GetPosition(book.Id) is TimeSpan saved && saved < Duration - FinishedThreshold)
			player.Seek(saved);

		timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Update());
		timer.Start();
		Update();
	}

	public static async Task<NowPlayingViewModel> OpenAsync(LocalBook book, MobileSettings settings)
	{
		var chapters = await AudioFileChapters.ReadAsync(book.Path);
		var player = await Task.Run(() => new AudioFilePlayer(AudioBackend.OpenSource(book.Path), AudioBackend.CreateOutput));
		settings.LastBookId = book.Id;
		return new NowPlayingViewModel(book, player, chapters, settings);
	}

	[RelayCommand]
	public void PlayPause()
	{
		if (player.IsPlaying)
		{
			player.Pause();
			SavePosition();
		}
		else
			player.Play();
		Update();
	}

	[RelayCommand]
	private void SkipBack() => Seek(Position - SkipInterval);

	[RelayCommand]
	private void SkipForward() => Seek(Position + SkipInterval);

	[RelayCommand]
	private void PreviousChapter()
	{
		if (CurrentChapter is not Chapter current)
			return;
		var previous = Chapters.LastOrDefault(c => c.StartOffset < current.StartOffset);
		Seek(Position - current.StartOffset > RestartChapterThreshold || previous is null ? current.StartOffset : previous.StartOffset);
	}

	[RelayCommand]
	private void NextChapter()
	{
		if (Chapters.FirstOrDefault(c => c.StartOffset > Position) is Chapter next)
			Seek(next.StartOffset);
	}

	[RelayCommand]
	private void ShowChapters()
	{
		if (HasChapters)
			IsChapterListOpen = true;
	}

	[RelayCommand]
	private void HideChapters() => IsChapterListOpen = false;

	[RelayCommand]
	private void JumpToChapter(ChapterRowViewModel row)
	{
		Seek(row.Chapter.StartOffset);
		IsChapterListOpen = false;
	}

	[RelayCommand]
	private void SlowerStep() => Speed -= SPEED_STEP;

	[RelayCommand]
	private void FasterStep() => Speed += SPEED_STEP;

	public void Seek(TimeSpan target)
	{
		target = target < TimeSpan.Zero ? TimeSpan.Zero : target > Duration ? Duration : target;
		player.Seek(target);
		Update();
	}

	private void Update()
	{
		if (disposed)
			return;
		IsPlaying = player.IsPlaying;
		Position = player.Position;
		UpdateCurrentChapter();
		if (IsPlaying && DateTime.UtcNow - lastSaved > SaveInterval)
			SavePosition();
	}

	private void UpdateCurrentChapter()
	{
		// Ticks four times a second, so only touch the rows when the chapter actually changes.
		var index = -1;
		for (var i = 0; i < ChapterRows.Count && ChapterRows[i].Chapter.StartOffset <= Position; i++)
			index = i;
		var current = index >= 0 ? ChapterRows[index] : ChapterRows.FirstOrDefault();
		if (current == CurrentChapterRow)
			return;

		CurrentChapterRow = current;
		for (var i = 0; i < ChapterRows.Count; i++)
		{
			ChapterRows[i].IsCurrent = ChapterRows[i] == current;
			ChapterRows[i].IsPlayed = i < index;
		}
	}

	private void SavePosition()
	{
		lastSaved = DateTime.UtcNow;
		settings.SetPosition(Book.Id, player.Position);
	}

	private void OnPlaybackEnded()
	{
		if (disposed)
			return;
		player.Pause();
		settings.RemovePosition(Book.Id);
		Update();
	}

	internal static Bitmap? LoadCover(byte[]? bytes, int width)
	{
		if (bytes is null)
			return null;
		try
		{
			using var stream = new MemoryStream(bytes);
			return Bitmap.DecodeToWidth(stream, width);
		}
		catch
		{
			return null;
		}
	}

	internal static string FormatTime(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : time.ToString("m\\:ss");

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		timer.Stop();
		SavePosition();
		player.Dispose();
		Cover?.Dispose();
		GC.SuppressFinalize(this);
	}
}
