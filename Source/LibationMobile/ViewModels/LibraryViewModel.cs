using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibationMobile.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

public partial class BookItemViewModel : ObservableObject
{
	public LocalBook Book { get; }
	public string Title => Book.Title;
	public string? Author => Book.Author;
	public Bitmap? Cover { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasProgress))]
	private double progress;

	[ObservableProperty]
	private string statusText = "";

	public bool HasProgress => Progress > 0;

	public BookItemViewModel(LocalBook book)
	{
		Book = book;
		Cover = NowPlayingViewModel.LoadCover(book.Cover, 160);
	}

	/// <summary>How far through the book, and how long is left at the given speed.</summary>
	public void Refresh(TimeSpan? position, double speed)
	{
		var duration = Book.Duration;
		if (duration <= TimeSpan.Zero)
		{
			Progress = 0;
			StatusText = "";
			return;
		}

		var at = position ?? TimeSpan.Zero;
		Progress = at / duration;
		StatusText = at <= TimeSpan.Zero
			? $"{FormatDuration(duration)}, not started"
			: $"{FormatDuration((duration - at) / speed)} left at {speed:0.0}×";
	}

	private static string FormatDuration(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{time.Minutes}m" : $"{time.Seconds}s";
}

public partial class LibraryViewModel : ObservableObject
{
	private readonly BookLibrary library;
	private readonly MobileSettings settings;

	public ObservableCollection<BookItemViewModel> Books { get; } = new();

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsEmpty))]
	private bool isLoaded;

	[ObservableProperty]
	private string? message;

	public bool IsEmpty => IsLoaded && Books.Count == 0;

	public LibraryViewModel(BookLibrary library, MobileSettings settings)
	{
		this.library = library;
		this.settings = settings;
		Books.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
	}

	public async Task LoadAsync()
	{
		var books = await library.LoadAsync();
		Books.Clear();
		foreach (var book in books)
			Books.Add(new BookItemViewModel(book));
		RefreshProgress();
		IsLoaded = true;
	}

	public void RefreshProgress()
	{
		foreach (var item in Books)
			item.Refresh(settings.GetPosition(item.Book.Id), settings.Speed);
	}

	public async Task ImportAsync(Stream source, string fileName)
	{
		try
		{
			var book = await library.ImportAsync(source, fileName);
			await LoadAsync();
			Message = $"Added {book.Title}.";
		}
		catch (InvalidDataException ex)
		{
			Message = ex.Message;
		}
	}

	public BookItemViewModel? Find(string? bookId) => Books.FirstOrDefault(b => b.Book.Id == bookId);
}
