using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>One book of a series on the details page: in the library, or only in the store.</summary>
public partial class SeriesRowViewModel : ObservableObject
{
	private static readonly HttpClient Http = new();

	public SeriesBook Book { get; }
	/// <summary>The listener's copy. Null for a book they do not have.</summary>
	public BookItemViewModel? Owned { get; }
	public bool IsOwned => Owned is not null;
	public bool IsNotOwned => Owned is null;
	/// <summary>The book whose details are showing.</summary>
	public bool IsCurrent { get; }

	public string Title => Book.Title;
	public string SequenceText => Book.Sequence is null ? "" : $"Book {Book.Sequence}";
	public string StatusText => IsCurrent ? "This book" : Owned is null ? "Not in your library" : Owned.IsDownloaded ? "In your library, downloaded" : "In your library";

	private Bitmap? cover;
	private bool coverRequested;
	public Bitmap? Cover
	{
		get
		{
			if (Owned is not null)
				return Owned.Cover;
			if (!coverRequested)
			{
				coverRequested = true;
				_ = LoadCoverAsync();
			}
			return cover;
		}
	}

	public SeriesRowViewModel(SeriesBook book, BookItemViewModel? owned, bool isCurrent)
	{
		Book = book;
		Owned = owned;
		IsCurrent = isCurrent;
		if (owned is not null)
			owned.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BookItemViewModel.Cover)) OnPropertyChanged(nameof(Cover)); };
	}

	private async Task LoadCoverAsync()
	{
		if (Book.CoverUrl is null)
			return;
		try
		{
			var bytes = await Http.GetByteArrayAsync(Book.CoverUrl);
			cover = await Task.Run(() => NowPlayingViewModel.LoadCover(bytes, 160));
			OnPropertyChanged(nameof(Cover));
		}
		catch (HttpRequestException)
		{
			// Offline: the row shows without a cover.
		}
	}
}

/// <summary>A series on the details page, with its books in order.</summary>
public class SeriesSectionViewModel(string name, IReadOnlyList<SeriesRowViewModel> rows)
{
	public string Name { get; } = name;
	public IReadOnlyList<SeriesRowViewModel> Rows { get; } = rows;
	public string CountText
	{
		get
		{
			var owned = Rows.Count(r => r.IsOwned);
			return owned == Rows.Count ? $"{Rows.Count} books, all in your library" : $"{Rows.Count} books, {owned} in your library";
		}
	}
}

/// <summary>More about one book: what it is about, and the rest of its series.</summary>
public partial class BookDetailsViewModel : ObservableObject
{
	public BookItemViewModel Item { get; }
	public string Title => Item.Title;
	public string? Subtitle => Item.Book.Subtitle;
	public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
	public string Author => Item.Author;
	public string NarratorText => string.IsNullOrWhiteSpace(Item.Book.Narrators) ? "" : $"Read by {Item.Book.Narrators}";
	public bool HasPdf => Item.Book.HasPdf;
	public string LengthText => $"{(int)Item.Book.Length.TotalHours}h {Item.Book.Length.Minutes}m";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasSummary))]
	private string? summary;
	public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

	[ObservableProperty]
	private IReadOnlyList<SeriesSectionViewModel> series = [];

	[ObservableProperty]
	private bool isLoading = true;

	[ObservableProperty]
	private string? message;

	public BookDetailsViewModel(BookItemViewModel item, AudibleSeries store, LibraryViewModel library)
	{
		Item = item;
		_ = LoadAsync(store, library);
	}

	private async Task LoadAsync(AudibleSeries store, LibraryViewModel library)
	{
		try
		{
			var memberships = Item.Book.Series ?? [];
			try
			{
				var info = await store.GetBookInfoAsync(Item.Book.Asin);
				Summary = info.Summary;
				// A library saved before series were kept has none; the store knows.
				if (memberships.Count == 0)
					memberships = info.Series;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Could not load details of {Item.Book.Asin}: {ex.Message}");
			}

			var sections = new List<SeriesSectionViewModel>();
			foreach (var membership in memberships)
			{
				var books = await store.GetSeriesBooksAsync(membership.Id);
				var rows = books
					.Select(b =>
					{
						// The listener may own a different edition of the same book.
						var owned = library.Find(b.Asin) ?? library.FindByTitle(b.Title, b.Authors);
						return new SeriesRowViewModel(b, owned, owned == Item);
					})
					// The store lists each edition of a book. Show one: the listener's own if they have it.
					.GroupBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
					.Select(editions => editions.FirstOrDefault(r => r.IsCurrent) ?? editions.FirstOrDefault(r => r.IsOwned) ?? editions.First())
					.ToList();
				if (rows.Count > 0)
					sections.Add(new SeriesSectionViewModel(membership.Name, rows));
			}
			Series = sections;
			if (memberships.Count == 0)
				Message = "This book is not part of a series.";
		}
		catch (Exception ex)
		{
			Message = $"The series could not be loaded: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}
}
