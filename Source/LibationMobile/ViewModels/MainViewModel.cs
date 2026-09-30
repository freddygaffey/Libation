using AudibleApi;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

public enum Page
{
	SignIn,
	Browser,
	Library,
	NowPlaying
}

/// <summary>Amazon's sign-in page, shown in the app. Completed with the URL Amazon redirects to after sign-in, or null if cancelled.</summary>
public class LoginRequest(string url, CookieCollection? cookies)
{
	private readonly TaskCompletionSource<string?> completion = new();

	public string Url { get; } = url;
	public CookieCollection? Cookies { get; } = cookies;
	public string UserAgent => DeviceRegistrationProfile.Default.UserAgent;
	public Task<string?> Result => completion.Task;

	public void Complete(string? responseUrl) => completion.TrySetResult(responseUrl);
}

/// <summary>App shell: which page is showing, the Audible account, and the book loaded for playback.</summary>
public partial class MainViewModel : ObservableObject, ILoginChoiceEager
{
	private readonly MobileSettings settings;
	private readonly AudibleAccount account;

	public LibraryViewModel Library { get; }
	public IReadOnlyList<string> RegionNames { get; } = AudibleAccount.Regions.Select(r => r.DisplayName).ToList();

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSignInPage), nameof(IsBrowserPage), nameof(IsLibraryPage), nameof(IsNowPlayingPage), nameof(ShowMiniPlayer))]
	private Page currentPage = Page.Library;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowMiniPlayer))]
	private NowPlayingViewModel? nowPlaying;

	[ObservableProperty]
	private LoginRequest? login;

	[ObservableProperty]
	private int selectedRegionIndex;

	[ObservableProperty]
	private bool isSigningIn;

	[ObservableProperty]
	private string? error;

	public bool IsSignInPage => CurrentPage == Page.SignIn;
	public bool IsBrowserPage => CurrentPage == Page.Browser;
	public bool IsLibraryPage => CurrentPage == Page.Library;
	public bool IsNowPlayingPage => CurrentPage == Page.NowPlaying;
	public bool ShowMiniPlayer => NowPlaying is not null && CurrentPage == Page.Library;
	public string AccountText => $"Audible {AudibleAccount.DisplayNameOf(settings.RegionName)}";

	public MainViewModel(string dataDirectory)
	{
		settings = new MobileSettings(Path.Combine(dataDirectory, "settings.json"));
		account = new AudibleAccount(Path.Combine(dataDirectory, "audible-identity.json"), settings);
		var catalog = new LibraryCatalog(dataDirectory);
		Library = new LibraryViewModel(catalog, account, new BookDownloader(catalog), settings);
		SelectedRegionIndex = Math.Max(0, AudibleAccount.Regions.ToList().FindIndex(r => r.Name == "us"));
	}

	public async Task InitializeAsync()
	{
		if (!account.IsSignedIn)
		{
			CurrentPage = Page.SignIn;
			return;
		}

		await Library.LoadAsync();

		// Put the last book back in the mini player, paused, so one tap resumes it.
		if (Library.Find(settings.LastBookId) is { IsDownloaded: true } last)
			await LoadAsync(last, showNowPlaying: false);
	}

	#region Sign in

	[RelayCommand]
	private async Task SignIn()
	{
		Error = null;
		IsSigningIn = true;
		try
		{
			await account.SignInAsync(AudibleAccount.Regions[SelectedRegionIndex].Name, this);
			OnPropertyChanged(nameof(AccountText));
			CurrentPage = Page.Library;
			await Library.LoadAsync();
		}
		catch (OperationCanceledException)
		{
			CurrentPage = Page.SignIn;
		}
		catch (Exception ex)
		{
			CurrentPage = Page.SignIn;
			Error = $"Sign-in did not complete: {ex.Message}";
		}
		finally
		{
			IsSigningIn = false;
			Login = null;
		}
	}

	/// <summary>Called by AudibleApi with Amazon's sign-in page. Shows it in the app and waits for the user to finish.</summary>
	async Task<string?> ILoginChoiceEager.StartAsync(ChoiceIn choiceIn)
	{
		var request = new LoginRequest(choiceIn.LoginUrl, choiceIn.SignInCookies);
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			Login = request;
			CurrentPage = Page.Browser;
		});
		return await request.Result;
	}

	[RelayCommand]
	private void CancelSignIn() => Login?.Complete(null);

	[ObservableProperty]
	private bool showImport;

	/// <summary>An account export pasted from Libation desktop.</summary>
	[ObservableProperty]
	private string? importText;

	[RelayCommand]
	private void ToggleImport() => ShowImport = !ShowImport;

	[RelayCommand]
	private Task ImportAccount() => ImportAccountAsync(ImportText);

	public async Task ImportAccountAsync(string? exportJson)
	{
		if (string.IsNullOrWhiteSpace(exportJson))
		{
			Error = "Paste the account export from Libation on your computer first.";
			return;
		}

		Error = null;
		IsSigningIn = true;
		try
		{
			await account.ImportAsync(exportJson);
			ImportText = null;
			ShowImport = false;
			OnPropertyChanged(nameof(AccountText));
			CurrentPage = Page.Library;
			await Library.LoadAsync();
		}
		catch (InvalidDataException ex)
		{
			Error = ex.Message;
		}
		catch (Exception ex)
		{
			Error = $"The account could not be imported: {ex.Message}";
		}
		finally
		{
			IsSigningIn = false;
		}
	}

	[RelayCommand]
	private void SignOut()
	{
		NowPlaying?.Dispose();
		NowPlaying = null;
		account.SignOut();
		CurrentPage = Page.SignIn;
	}

	#endregion

	#region Library and playback

	/// <summary>Tapping a title: play it if downloaded, otherwise start downloading it.</summary>
	[RelayCommand]
	private async Task OpenBook(BookItemViewModel item)
	{
		switch (item.State)
		{
			case DownloadState.NotDownloaded:
				await Library.DownloadCommand.ExecuteAsync(item);
				return;
			case DownloadState.Downloading:
				return;
		}

		if (NowPlaying?.Book.Id != item.Book.Asin && !await LoadAsync(item, showNowPlaying: true))
			return;
		CurrentPage = Page.NowPlaying;
		if (!NowPlaying!.IsPlaying)
			NowPlaying.PlayPause();
	}

	[RelayCommand]
	private void RemoveDownload(BookItemViewModel item)
	{
		if (NowPlaying?.Book.Id == item.Book.Asin)
		{
			NowPlaying.Dispose();
			NowPlaying = null;
		}
		Library.RemoveDownload(item);
	}

	[RelayCommand]
	private void ShowNowPlaying()
	{
		if (NowPlaying is not null)
			CurrentPage = Page.NowPlaying;
	}

	[RelayCommand]
	private void GoToLibrary() => ShowLibrary();

	/// <summary>Leave Now Playing or the sign-in browser. Playback continues.</summary>
	/// <returns>False if there is nowhere to go back to, so a back press can close the app instead.</returns>
	public bool ShowLibrary()
	{
		switch (CurrentPage)
		{
			case Page.Browser:
				Login?.Complete(null);
				return true;
			case Page.NowPlaying:
				CurrentPage = Page.Library;
				Library.RefreshProgress();
				return true;
			default:
				return false;
		}
	}

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

	private async Task<bool> LoadAsync(BookItemViewModel item, bool showNowPlaying)
	{
		Error = null;
		NowPlaying?.Dispose();
		NowPlaying = null;
		try
		{
			NowPlaying = await NowPlayingViewModel.OpenAsync(await Library.ToLocalBookAsync(item), settings);
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

	#endregion

#if DEBUG
	/// <summary>Debug builds only: act on a launch setting, so emulator and simulator runs can be driven without taps.</summary>
	/// <param name="action">"signin", or "play:ASIN[:speed]".</param>
	internal async Task RunTestActionAsync(string action)
	{
		var parts = action.Split(':');
		switch (parts[0])
		{
			case "signin":
				await SignInCommand.ExecuteAsync(null);
				break;
			case "play" when parts.Length > 1 && Library.Find(parts[1]) is { } item:
				await OpenBookCommand.ExecuteAsync(item);
				if (parts.Length > 2 && NowPlaying is not null && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var speed))
					NowPlaying.Speed = speed;
				break;
		}
	}
#endif
}
