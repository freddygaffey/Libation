using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

public enum Page
{
	Library,
	NowPlaying
}

/// <summary>App shell: which page is showing, and the book loaded for playback.</summary>
public partial class MainViewModel : ObservableObject
{
	private readonly MobileSettings settings;

	public LibraryViewModel Library { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsLibraryPage), nameof(IsNowPlayingPage), nameof(ShowMiniPlayer))]
	private Page currentPage = Page.Library;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowMiniPlayer))]
	private NowPlayingViewModel? nowPlaying;

	[ObservableProperty]
	private string? error;

	public bool IsLibraryPage => CurrentPage == Page.Library;
	public bool IsNowPlayingPage => CurrentPage == Page.NowPlaying;
	public bool ShowMiniPlayer => NowPlaying is not null && CurrentPage == Page.Library;

	/// <summary>Keep the playing book's library row in step with playback, not just with the last save.</summary>
	partial void OnNowPlayingChanged(NowPlayingViewModel? oldValue, NowPlayingViewModel? newValue)
	{
		if (oldValue is not null)
			oldValue.PropertyChanged -= NowPlaying_PropertyChanged;
		if (newValue is not null)
			newValue.PropertyChanged += NowPlaying_PropertyChanged;
	}

	private void NowPlaying_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (sender is NowPlayingViewModel np && e.PropertyName is nameof(NowPlayingViewModel.Position) or nameof(NowPlayingViewModel.Speed))
			Library.Find(np.Book.Id)?.Refresh(np.Position, np.Speed);
	}

	public MainViewModel(string dataDirectory)
	{
		settings = new MobileSettings(Path.Combine(dataDirectory, "settings.json"));
		Library = new LibraryViewModel(new BookLibrary(Path.Combine(dataDirectory, "Books")), settings);
	}

	public async Task InitializeAsync()
	{
		await Library.LoadAsync();

		// Put the last book back in the mini player, paused, so one tap resumes it.
		if (Library.Find(settings.LastBookId) is BookItemViewModel last)
			await LoadAsync(last, showNowPlaying: false);
	}

	[RelayCommand]
	private async Task OpenBook(BookItemViewModel item)
	{
		// Tapping a book in the library means "play it", whether or not it is already loaded.
		if (NowPlaying?.Book.Id != item.Book.Id && !await LoadAsync(item, showNowPlaying: true))
			return;
		CurrentPage = Page.NowPlaying;
		if (!NowPlaying!.IsPlaying)
			NowPlaying.PlayPause();
	}

	[RelayCommand]
	private void ShowNowPlaying()
	{
		if (NowPlaying is not null)
			CurrentPage = Page.NowPlaying;
	}

	[RelayCommand]
	private void GoToLibrary() => ShowLibrary();

	/// <summary>Leave Now Playing for the library. Playback continues.</summary>
	/// <returns>False if already on the library, so a back press can close the app instead.</returns>
	public bool ShowLibrary()
	{
		if (CurrentPage == Page.Library)
			return false;
		CurrentPage = Page.Library;
		Library.RefreshProgress();
		return true;
	}

	private async Task<bool> LoadAsync(BookItemViewModel item, bool showNowPlaying)
	{
		Error = null;
		NowPlaying?.Dispose();
		NowPlaying = null;
		try
		{
			NowPlaying = await NowPlayingViewModel.OpenAsync(item.Book, settings);
			if (showNowPlaying)
				CurrentPage = Page.NowPlaying;
			return true;
		}
		catch (Exception ex)
		{
			Error = $"{item.Title} could not be opened: {ex.Message}";
			return false;
		}
	}
}
