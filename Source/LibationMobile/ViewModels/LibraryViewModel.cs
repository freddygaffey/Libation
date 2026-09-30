using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

public enum DownloadState
{
	NotDownloaded,
	Downloading,
	Downloaded
}

/// <summary>One title in the library list.</summary>
public partial class BookItemViewModel : ObservableObject
{
	public CatalogBook Book { get; }
	public string Title => Book.Title;
	public string Author => Book.Authors;

	private readonly Func<CatalogBook, Task<byte[]?>> loadCoverBytes;
	private Bitmap? cover;
	private bool coverRequested;

	/// <summary>
	/// Loaded the first time a row on screen asks for it, and decoded off the UI thread. A library can hold
	/// hundreds of books; decoding every cover up front froze the app.
	/// </summary>
	public Bitmap? Cover
	{
		get
		{
			if (!coverRequested)
			{
				coverRequested = true;
				_ = LoadCoverAsync();
			}
			return cover;
		}
	}

	private async Task LoadCoverAsync()
	{
		var bitmap = await Task.Run(async () => await loadCoverBytes(Book) is byte[] bytes ? NowPlayingViewModel.LoadCover(bytes, 160) : null);
		if (bitmap is null)
			return;
		cover = bitmap;
		OnPropertyChanged(nameof(Cover));
	}

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsNotDownloaded), nameof(IsDownloading), nameof(IsDownloaded))]
	private DownloadState state;

	/// <summary>Download progress, 0 to 1.</summary>
	[ObservableProperty]
	private double downloadProgress;

	/// <summary>Listening progress, 0 to 1.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasProgress))]
	private double progress;

	[ObservableProperty]
	private string statusText = "";

	public bool IsNotDownloaded => State == DownloadState.NotDownloaded;
	public bool IsDownloading => State == DownloadState.Downloading;
	public bool IsDownloaded => State == DownloadState.Downloaded;
	public bool HasProgress => Progress > 0;

	internal CancellationTokenSource? DownloadCancellation { get; set; }

	public BookItemViewModel(CatalogBook book, DownloadState state, Func<CatalogBook, Task<byte[]?>> loadCoverBytes)
	{
		Book = book;
		this.state = state;
		this.loadCoverBytes = loadCoverBytes;
	}

	/// <summary>How far through the book, and how long is left at the given speed.</summary>
	public void Refresh(TimeSpan? position, double speed)
	{
		var length = Book.Length;
		var at = position ?? TimeSpan.Zero;
		Progress = length > TimeSpan.Zero ? Math.Clamp(at / length, 0, 1) : 0;
		StatusText = State switch
		{
			DownloadState.Downloading => $"Downloading, {DownloadProgress:P0}",
			_ when at > TimeSpan.Zero => $"{FormatDuration((length - at) / speed)} left at {speed:0.0}×",
			_ => FormatDuration(length)
		};
	}

	private static string FormatDuration(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{time.Minutes}m" : $"{Math.Max(0, time.Seconds)}s";
}

/// <summary>The user's Audible library, with each title's download state.</summary>
public partial class LibraryViewModel : ObservableObject
{
	private readonly LibraryCatalog catalog;
	private readonly AudibleAccount account;
	private readonly BookDownloader downloader;
	private readonly MobileSettings settings;
	private List<BookItemViewModel> allBooks = [];

	/// <summary>The books shown, after the All / Downloaded filter. Replaced as a whole when the filter changes.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsDownloadedEmpty))]
	private IReadOnlyList<BookItemViewModel> books = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowAll))]
	private bool showDownloadedOnly;

	[ObservableProperty]
	private bool isSyncing;

	[ObservableProperty]
	private string? message;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsLibraryEmpty), nameof(IsDownloadedEmpty))]
	private bool isLoaded;

	public bool ShowAll => !ShowDownloadedOnly;
	public bool IsLibraryEmpty => IsLoaded && !IsSyncing && allBooks.Count == 0;
	public bool IsDownloadedEmpty => IsLoaded && ShowDownloadedOnly && Books.Count == 0 && allBooks.Count > 0;

	public LibraryViewModel(LibraryCatalog catalog, AudibleAccount account, BookDownloader downloader, MobileSettings settings)
	{
		this.catalog = catalog;
		this.account = account;
		this.downloader = downloader;
		this.settings = settings;
	}

	partial void OnShowDownloadedOnlyChanged(bool value) => ApplyFilter();
	partial void OnIsSyncingChanged(bool value) => OnPropertyChanged(nameof(IsLibraryEmpty));

	[RelayCommand]
	private void ShowAllBooks() => ShowDownloadedOnly = false;

	[RelayCommand]
	private void ShowDownloadedBooks() => ShowDownloadedOnly = true;

	/// <summary>Show the cached library straight away; sync with Audible if there is none yet.</summary>
	public async Task LoadAsync()
	{
		SetBooks(await catalog.LoadAsync());
		IsLoaded = true;
		if (allBooks.Count == 0)
			await SyncAsync();
	}

	[RelayCommand]
	public async Task SyncAsync()
	{
		if (IsSyncing)
			return;
		IsSyncing = true;
		Message = null;
		try
		{
			var api = await account.GetApiAsync();
			SetBooks(await catalog.SyncAsync(api));
		}
		catch (Exception ex)
		{
			Message = $"Could not refresh your library: {ex.Message}";
		}
		finally
		{
			IsSyncing = false;
		}
	}

	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task Download(BookItemViewModel item)
	{
		if (item.State != DownloadState.NotDownloaded)
			return;

		using var cancellation = new CancellationTokenSource();
		item.DownloadCancellation = cancellation;
		item.DownloadProgress = 0;
		item.State = DownloadState.Downloading;
		RefreshItem(item);

		// Progress arrives on a worker thread; throttle to whole percents to keep the UI calm.
		var lastPercent = -1;
		var progress = new Progress<double>(p =>
		{
			var percent = (int)(p * 100);
			if (percent == lastPercent)
				return;
			lastPercent = percent;
			Dispatcher.UIThread.Post(() =>
			{
				item.DownloadProgress = p;
				RefreshItem(item);
			});
		});

		try
		{
			var api = await account.GetApiAsync();
			await Task.Run(() => downloader.DownloadAsync(api, item.Book, progress, cancellation.Token));
			item.State = DownloadState.Downloaded;
		}
		catch (OperationCanceledException)
		{
			item.State = DownloadState.NotDownloaded;
		}
		catch (Exception ex)
		{
			item.State = DownloadState.NotDownloaded;
			Message = $"{item.Title} could not be downloaded: {ex.Message}";
		}
		finally
		{
			item.DownloadCancellation = null;
			RefreshItem(item);
			ApplyFilter();
		}
	}

	[RelayCommand]
	private void CancelDownload(BookItemViewModel item) => item.DownloadCancellation?.Cancel();

	/// <summary>Free up space. The book stays in the library and can be downloaded again.</summary>
	public void RemoveDownload(BookItemViewModel item)
	{
		catalog.DeleteDownload(item.Book.Asin);
		item.State = DownloadState.NotDownloaded;
		RefreshItem(item);
		ApplyFilter();
	}

	public void RefreshProgress()
	{
		foreach (var item in allBooks)
			RefreshItem(item);
	}

	public BookItemViewModel? Find(string? asin) => allBooks.FirstOrDefault(b => b.Book.Asin == asin);

	public async Task<LocalBook> ToLocalBookAsync(BookItemViewModel item) => new(
		item.Book.Asin,
		catalog.BookPath(item.Book.Asin),
		item.Title,
		item.Author,
		item.Book.Narrators,
		item.Book.Length,
		await catalog.GetCoverAsync(item.Book));

	private void RefreshItem(BookItemViewModel item) => item.Refresh(settings.GetPosition(item.Book.Asin), settings.Speed);

	private void SetBooks(IReadOnlyList<CatalogBook> books)
	{
		// Keep the existing row for a book, so a download in progress survives a sync.
		var existing = allBooks.ToDictionary(b => b.Book.Asin);
		allBooks = books
			.Select(b => existing.TryGetValue(b.Asin, out var item) && item.Book == b
				? item
				: new BookItemViewModel(b, catalog.IsDownloaded(b.Asin) ? DownloadState.Downloaded : DownloadState.NotDownloaded, catalog.GetCoverAsync))
			.ToList();
		foreach (var item in allBooks)
			RefreshItem(item);
		ApplyFilter();
		OnPropertyChanged(nameof(IsLibraryEmpty));
	}

	private void ApplyFilter()
	{
		Books = ShowDownloadedOnly ? allBooks.Where(b => b.State != DownloadState.NotDownloaded).ToList() : allBooks;
	}
}
