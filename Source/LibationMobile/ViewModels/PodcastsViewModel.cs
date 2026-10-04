using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>A podcast in search results or among the subscriptions.</summary>
public partial class ShowItemViewModel : ObservableObject
{
	public PodcastShow Show { get; private set; }
	public string Title => Show.Title;
	public string Author => Show.Author ?? "";
	public string? Description => Show.Description;

	[ObservableProperty]
	private bool isSubscribed;

	[ObservableProperty]
	private Bitmap? artwork;

	public ShowItemViewModel(PodcastShow show, bool subscribed, PodcastLibrary library)
	{
		Show = show;
		isSubscribed = subscribed;
		_ = LoadArtworkAsync(library);
	}

	public void Update(PodcastShow show)
	{
		Show = show;
		OnPropertyChanged(string.Empty);
	}

	private async Task LoadArtworkAsync(PodcastLibrary library)
	{
		var bitmap = await Task.Run(async () => await library.GetArtworkAsync(Show.Id, Show.ArtworkUrl) is byte[] bytes ? NowPlayingViewModel.LoadCover(bytes, 160) : null);
		if (bitmap is not null)
			Artwork = bitmap;
	}
}

/// <summary>One episode: when, how long, and whether it is downloaded, being downloaded, or listened to.</summary>
public partial class EpisodeItemViewModel : ObservableObject
{
	public PodcastEpisode Episode { get; }
	public string ShowTitle { get; }
	public string Title => Episode.Title;
	public string? Description => Episode.Description;
	public string DateText => Episode.Published is { } date ? date.ToLocalTime().ToString(date.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture) : "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsNotDownloaded), nameof(IsDownloading), nameof(IsDownloaded))]
	private DownloadState state;

	[ObservableProperty]
	private double downloadProgress;

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

	public EpisodeItemViewModel(PodcastEpisode episode, string showTitle, DownloadState state)
	{
		Episode = episode;
		ShowTitle = showTitle;
		this.state = state;
	}

	/// <summary>Like a book's row: progress, and time left at this speed.</summary>
	public void Refresh(TimeSpan? position, double speed)
	{
		var length = Episode.Duration ?? TimeSpan.Zero;
		var at = position ?? TimeSpan.Zero;
		Progress = length > TimeSpan.Zero ? Math.Clamp(at / length, 0, 1) : 0;
		var detail = State switch
		{
			DownloadState.Downloading => $"Downloading, {DownloadProgress:P0}",
			_ when length > TimeSpan.Zero && at >= length - TimeSpan.FromSeconds(30) => "Played",
			_ when at > TimeSpan.Zero && length > TimeSpan.Zero => $"{Format((length - at) / speed)} left at {speed:0.0}×",
			_ when length > TimeSpan.Zero => Format(length),
			_ => ""
		};
		StatusText = string.Join(" · ", new[] { DateText, detail }.Where(s => s.Length > 0));
	}

	private static string Format(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{Math.Max(1, (int)time.TotalMinutes)}m";
}

/// <summary>
/// The Podcasts tab: search Apple's directory, follow podcasts, and download and play their episodes. Needs no
/// account. Positions are kept on this phone only; Audible knows nothing of podcasts.
/// </summary>
public partial class PodcastsViewModel : ObservableObject
{
	private readonly PodcastLibrary library;
	private readonly MobileSettings settings;
	private CancellationTokenSource? searchCancellation;
	/// <summary>Every episode row made, by ID, so a download carries on showing wherever the episode appears.</summary>
	private readonly Dictionary<string, EpisodeItemViewModel> episodeRows = [];

	/// <summary>Raised to play an episode: the main view model opens it in the player.</summary>
	public event Action<EpisodeItemViewModel>? PlayRequested;

	/// <summary>An episode finished downloading or was removed: the Downloads tab lists them.</summary>
	public event Action? DownloadsChanged;

	public PodcastsViewModel(PodcastLibrary library, MobileSettings settings)
	{
		this.library = library;
		this.settings = settings;
		RefreshSubscriptions();
	}

	public PodcastLibrary Library => library;

	#region Subscriptions and search

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNoSubscriptions))]
	private IReadOnlyList<ShowItemViewModel> subscriptions = [];

	public bool HasNoSubscriptions => Subscriptions.Count == 0 && !IsSearching;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSearching), nameof(HasNoSubscriptions))]
	private string? searchText;

	public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

	[ObservableProperty]
	private IReadOnlyList<ShowItemViewModel> searchResults = [];

	[ObservableProperty]
	private bool isBusy;

	[ObservableProperty]
	private string? message;

	partial void OnSearchTextChanged(string? value)
	{
		if (!IsSearching)
		{
			SearchResults = [];
			Message = null;
		}
	}

	[RelayCommand]
	private void ClearSearch() => SearchText = null;

	/// <summary>Search on Return, not every keystroke: each search goes to Apple.</summary>
	[RelayCommand]
	private async Task SearchAsync()
	{
		if (!IsSearching)
			return;
		searchCancellation?.Cancel();
		var cancellation = searchCancellation = new CancellationTokenSource();
		var term = SearchText!.Trim();
		IsBusy = true;
		Message = null;
		try
		{
			// A feed address is followed directly, for podcasts Apple does not list.
			var shows = Uri.TryCreate(term, UriKind.Absolute, out var feed) && feed.Scheme is "http" or "https"
				? [PodcastLibrary.FromFeedUrl(term)]
				: await PodcastLibrary.SearchAsync(term, cancellation.Token);
			if (cancellation.IsCancellationRequested)
				return;
			SearchResults = shows.Select(s => new ShowItemViewModel(s, library.IsSubscribed(s.Id), library)).ToList();
			if (shows.Count == 0)
				Message = $"No podcasts found for \"{term}\".";
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Message = $"Could not search: {ex.Message}";
		}
		finally
		{
			if (cancellation == searchCancellation)
				IsBusy = false;
		}
	}

	private void RefreshSubscriptions()
		=> Subscriptions = library.Shows.Select(s => new ShowItemViewModel(s, true, library)).ToList();

	[RelayCommand]
	private async Task ToggleSubscriptionAsync(ShowItemViewModel show)
	{
		if (show.IsSubscribed)
		{
			library.Unsubscribe(show.Show.Id);
			show.IsSubscribed = false;
			RefreshSubscriptions();
			if (OpenShow?.Show.Id == show.Show.Id)
				OpenShowIsSubscribed = false;
			return;
		}
		IsBusy = true;
		Message = null;
		try
		{
			show.Update(await library.SubscribeAsync(show.Show));
			show.IsSubscribed = true;
			RefreshSubscriptions();
			if (OpenShow?.Show.Id == show.Show.Id)
			{
				OpenShowIsSubscribed = true;
				ShowEpisodes(show);
			}
		}
		catch (Exception ex)
		{
			Message = $"Could not follow {show.Title}: {ex.Message}";
		}
		finally
		{
			IsBusy = false;
		}
	}

	/// <summary>Fetch every followed podcast's feed for new episodes.</summary>
	[RelayCommand]
	private async Task RefreshAllAsync()
	{
		IsBusy = true;
		Message = null;
		var added = 0;
		var failed = new List<string>();
		foreach (var show in library.Shows)
		{
			try
			{
				added += await library.RefreshAsync(show.Id);
			}
			catch (Exception)
			{
				failed.Add(show.Title);
			}
		}
		RefreshSubscriptions();
		if (OpenShow is { } open)
			ShowEpisodes(open);
		IsBusy = false;
		Message = failed.Count > 0 ? $"Could not refresh {string.Join(", ", failed)}." : added == 0 ? "No new episodes." : added == 1 ? "1 new episode." : $"{added} new episodes.";
	}

	#endregion

	#region A podcast's page

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsShowOpen))]
	private ShowItemViewModel? openShow;

	public bool IsShowOpen => OpenShow is not null;

	[ObservableProperty]
	private bool openShowIsSubscribed;

	[ObservableProperty]
	private IReadOnlyList<EpisodeItemViewModel> episodes = [];

	[ObservableProperty]
	private bool isLoadingEpisodes;

	/// <summary>Open a podcast's page: its episodes, fetched first if it is not followed.</summary>
	[RelayCommand]
	private async Task OpenAsync(ShowItemViewModel show)
	{
		OpenShow = show;
		OpenShowIsSubscribed = show.IsSubscribed;
		Episodes = [];
		if (show.IsSubscribed)
		{
			ShowEpisodes(show);
			return;
		}
		IsLoadingEpisodes = true;
		try
		{
			// Seen before following: fetch the feed without saving it.
			var (fromFeed, episodes) = await Task.Run(() => PodcastLibrary.PreviewAsync(show.Show));
			if (OpenShow != show)
				return;
			show.Update(fromFeed);
			Episodes = episodes.Select(e => Row(e, fromFeed.Title)).ToList();
		}
		catch (Exception ex)
		{
			Message = $"Could not load {show.Title}: {ex.Message}";
		}
		finally
		{
			IsLoadingEpisodes = false;
		}
	}

	[RelayCommand]
	private void CloseShow() => OpenShow = null;

	private void ShowEpisodes(ShowItemViewModel show)
		=> Episodes = library.Episodes(show.Show.Id).Select(e => Row(e, show.Title)).ToList();

	private EpisodeItemViewModel Row(PodcastEpisode episode, string showTitle)
	{
		if (!episodeRows.TryGetValue(episode.Id, out var row))
		{
			row = new EpisodeItemViewModel(episode, showTitle, library.IsDownloaded(episode) ? DownloadState.Downloaded : DownloadState.NotDownloaded);
			episodeRows[episode.Id] = row;
		}
		RefreshRow(row);
		return row;
	}

	public void RefreshRow(EpisodeItemViewModel row)
		=> row.Refresh(settings.GetPosition(row.Episode.Id), settings.GetBookSpeed(row.Episode.Id) ?? settings.Speed);

	/// <summary>After listening: progress on every row shown.</summary>
	public void RefreshProgress()
	{
		foreach (var row in episodeRows.Values)
			RefreshRow(row);
	}

	#endregion

	#region Downloads and playing

	/// <summary>Downloaded episodes, newest first, for the Downloads tab.</summary>
	public IReadOnlyList<EpisodeItemViewModel> DownloadedRows()
		=> library.DownloadedEpisodes
			.OrderByDescending(e => e.Published ?? DateTimeOffset.MinValue)
			.Select(e => Row(e, library.FindShow(e.ShowId)?.Title ?? e.ShowTitle ?? ""))
			.ToList();

	public EpisodeItemViewModel? FindDownloaded(string? episodeId)
		=> episodeId is not null && library.FindDownloaded(episodeId) is { } episode ? Row(episode, library.FindShow(episode.ShowId)?.Title ?? episode.ShowTitle ?? "") : null;

	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task DownloadAsync(EpisodeItemViewModel row)
	{
		if (row.State != DownloadState.NotDownloaded)
			return;
		using var cancellation = new CancellationTokenSource();
		row.DownloadCancellation = cancellation;
		row.DownloadProgress = 0;
		row.State = DownloadState.Downloading;
		RefreshRow(row);
		var lastPercent = -1;
		try
		{
			await Task.Run(() => library.DownloadAsync(row.Episode, p =>
			{
				var percent = (int)(p * 100);
				if (percent == lastPercent)
					return;
				lastPercent = percent;
				Dispatcher.UIThread.Post(() =>
				{
					row.DownloadProgress = p;
					RefreshRow(row);
				});
			}, cancellation.Token));
			row.State = DownloadState.Downloaded;
			DownloadsChanged?.Invoke();
		}
		catch (OperationCanceledException)
		{
			row.State = DownloadState.NotDownloaded;
		}
		catch (Exception ex)
		{
			row.State = DownloadState.NotDownloaded;
			Message = $"{row.Title} could not be downloaded: {ex.Message}";
		}
		finally
		{
			row.DownloadCancellation = null;
			RefreshRow(row);
		}
	}

	[RelayCommand]
	private void CancelDownload(EpisodeItemViewModel row) => row.DownloadCancellation?.Cancel();

	[RelayCommand]
	private void RemoveDownload(EpisodeItemViewModel row)
	{
		library.DeleteDownload(row.Episode);
		row.State = DownloadState.NotDownloaded;
		RefreshRow(row);
		DownloadsChanged?.Invoke();
	}

	/// <summary>Tapping an episode plays it if downloaded, and downloads it if not.</summary>
	[RelayCommand]
	private async Task PlayAsync(EpisodeItemViewModel row)
	{
		if (row.IsDownloaded)
			PlayRequested?.Invoke(row);
		else if (row.IsNotDownloaded)
			await DownloadAsync(row);
	}

	public async Task<LocalBook> ToLocalBookAsync(EpisodeItemViewModel row)
	{
		var show = library.FindShow(row.Episode.ShowId);
		var artwork = await library.GetArtworkAsync(row.Episode.ShowId, show?.ArtworkUrl);
		return new LocalBook(row.Episode.Id, library.EpisodePath(row.Episode), row.Title, row.ShowTitle, null, row.Episode.Duration ?? TimeSpan.Zero, artwork);
	}

	#endregion
}
