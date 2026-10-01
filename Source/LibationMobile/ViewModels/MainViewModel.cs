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
	NowPlaying,
	Settings,
	Details
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
	private readonly AudibleAnnotations annotations;
	private readonly LocalAnnotations localAnnotations;

	public LibraryViewModel Library { get; }
	public IReadOnlyList<string> RegionNames { get; } = AudibleAccount.Regions.Select(r => r.DisplayName).ToList();

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSignInPage), nameof(IsBrowserPage), nameof(IsLibraryPage), nameof(IsNowPlayingPage), nameof(IsSettingsPage), nameof(IsDetailsPage), nameof(ShowMiniPlayer))]
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
	public bool IsSettingsPage => CurrentPage == Page.Settings;
	public bool IsDetailsPage => CurrentPage == Page.Details;

	/// <summary>The book whose details page is open.</summary>
	[ObservableProperty]
	private BookDetailsViewModel? details;

	/// <summary>Opens a web address outside the app. Set by the view, which knows the window.</summary>
	public Func<Uri, Task>? OpenUri { get; set; }

	private readonly AudibleSeries store;

	[RelayCommand]
	private void ShowDetails(BookItemViewModel item)
	{
		Details = new BookDetailsViewModel(item, store, Library);
		CurrentPage = Page.Details;
	}

	/// <summary>A book in a series: show it if it is in the library, or open its store page if not.</summary>
	[RelayCommand]
	private async Task OpenSeriesBook(SeriesRowViewModel row)
	{
		if (row.Owned is { } owned)
		{
			if (!row.IsCurrent)
				ShowDetails(owned);
		}
		else if (OpenUri is not null)
			await OpenUri(store.StorePage(row.Book.Asin));
	}

	/// <summary>Open the PDF that comes with the book on the details page, where it can be read and saved.</summary>
	[RelayCommand]
	private async Task OpenPdf()
	{
		if (Details is not { } details || OpenUri is null)
			return;
		try
		{
			await OpenUri(await store.GetPdfLinkAsync(details.Item.Book.Asin));
		}
		catch (Exception ex)
		{
			details.Message = $"The PDF could not be opened: {ex.Message}";
		}
	}

	/// <summary>Play the book on the details page, or download it if it is not on the device.</summary>
	[RelayCommand]
	private async Task OpenDetailsBook()
	{
		if (Details is not null)
			await OpenBook(Details.Item);
	}
	public SettingsViewModel Settings { get; }
	public bool ShowMiniPlayer => NowPlaying is not null && CurrentPage == Page.Library;
	public string AccountText => $"Audible {AudibleAccount.DisplayNameOf(settings.RegionName)}";

	public MainViewModel(string dataDirectory)
	{
		settings = new MobileSettings(Path.Combine(dataDirectory, "settings.json"));
		account = new AudibleAccount(Path.Combine(dataDirectory, "audible-identity.json"), settings);
		annotations = new AudibleAnnotations(account, settings);
		store = new AudibleSeries(account);
		Settings = new SettingsViewModel(settings, () => NowPlaying?.SettingsChanged());
		localAnnotations = new LocalAnnotations(Path.Combine(dataDirectory, "annotations.json"));
		var catalog = new LibraryCatalog(dataDirectory);
		Library = new LibraryViewModel(catalog, account, new BookDownloader(catalog, settings), settings, annotations);
		// The refresh button refreshes everything Audible holds, including the loaded book's position and bookmarks.
		Library.SyncCommand.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(Library.SyncCommand.IsRunning) && !Library.SyncCommand.IsRunning)
				NowPlaying?.RefreshFromAudible();
		};
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

	private const string DEFAULT_ACCOUNT_RESOURCE = "DefaultAccount.json";

	/// <summary>A developer's account export bundled into this Debug build, for quick sign-in on emulators.</summary>
	public bool HasDefaultCredential { get; } = typeof(MainViewModel).Assembly.GetManifestResourceInfo(DEFAULT_ACCOUNT_RESOURCE) is not null;

	[RelayCommand]
	private void TypeDefaultCredential()
	{
		using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(DEFAULT_ACCOUNT_RESOURCE);
		if (stream is null)
			return;
		using var reader = new StreamReader(stream);
		ImportText = reader.ReadToEnd();
		ShowImport = true;
	}

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
			Console.WriteLine($"Account import failed: {ex}");
			Error = $"The account could not be imported: {ex.Message}";
		}
		finally
		{
			IsSigningIn = false;
		}
	}

	[RelayCommand]
	private void ShowSettings()
	{
		Settings.RefreshTimeSaved();
		CurrentPage = Page.Settings;
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
	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task OpenBook(BookItemViewModel item)
	{
		switch (item.State)
		{
			case DownloadState.NotDownloaded:
				// Start it and return, so this command is not busy (and every row disabled) for the whole download.
				_ = Library.DownloadCommand.ExecuteAsync(item);
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
	private void MarkFinished(BookItemViewModel item)
	{
		// The player would otherwise carry on from where it was and save over this.
		if (NowPlaying?.Book.Id == item.Book.Asin)
		{
			NowPlaying.Dispose();
			NowPlaying = null;
		}
		Library.MarkFinished(item, finished: true);
	}

	[RelayCommand]
	private void MarkNotStarted(BookItemViewModel item)
	{
		if (NowPlaying?.Book.Id == item.Book.Asin)
		{
			NowPlaying.Dispose();
			NowPlaying = null;
		}
		Library.MarkFinished(item, finished: false);
	}

	/// <summary>Open a downloaded book at its first second, whatever was listened to before.</summary>
	[RelayCommand]
	private async Task PlayFromStart(BookItemViewModel item)
	{
		if (item.State != DownloadState.Downloaded)
			return;
		MarkNotStarted(item);
		await OpenBook(item);
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
			case Page.NowPlaying when NowPlaying is { IsChapterListOpen: true }:
				NowPlaying.IsChapterListOpen = false;
				return true;
			case Page.NowPlaying when NowPlaying is { IsClipEditorOpen: true }:
				NowPlaying.CancelClipCommand.Execute(null);
				return true;
			case Page.NowPlaying when NowPlaying is { IsAnnotationListOpen: true }:
				NowPlaying.IsAnnotationListOpen = false;
				return true;
			case Page.NowPlaying:
				CurrentPage = Page.Library;
				Library.RefreshProgress();
				return true;
			case Page.Settings or Page.Details:
				CurrentPage = Page.Library;
				return true;
			default:
				return false;
		}
	}

	/// <summary>The app came back to the front: pick up anything that changed on other devices.</summary>
	public void OnAppResumed()
	{
		if (!account.IsSignedIn)
			return;
		NowPlaying?.RefreshFromAudible();
		_ = Library.RefreshPositionsAsync(force: false);
	}

	/// <summary>The app is leaving the front: save and report where the listener is.</summary>
	public void OnAppBackgrounded() => NowPlaying?.OnAppBackgrounded();

	/// <summary>Keep the playing book's library row in step with playback, not just with the last save.</summary>
	partial void OnNowPlayingChanged(NowPlayingViewModel? oldValue, NowPlayingViewModel? newValue)
	{
		Library.PlayingBookId = newValue?.Book.Id;
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
			NowPlaying = await NowPlayingViewModel.OpenAsync(await Library.ToLocalBookAsync(item), settings, annotations, localAnnotations);
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
			case "benchspeed":
				// How long this device takes to speed up 30s of 44.1 kHz stereo at 10x, which must be well under 3s to
				// play in real time. Noise in bursts, so the nonlinear analysis has something like speech to work on.
				var input = new float[4096];
				var random = new Random(1);
				var output = new float[8192];
				foreach (var amount in new[] { 1f, 0f, -1f })
				{
					// -1 stands for the original method.
					AudioBackend.UseNonlinear = amount >= 0;
					AudioBackend.Nonlinearity = Math.Max(0, amount);
					using var stretcher = AudioBackend.CreateStretcher(44100, 2);
					stretcher.Speed = 10f;
					var produced = 0L;
					var watch = System.Diagnostics.Stopwatch.StartNew();
					for (var frames = 0; frames < 30 * 44100; frames += input.Length / 2)
					{
						var loud = frames / 4410 % 3 != 0;
						for (var i = 0; i < input.Length; i++)
							input[i] = loud ? (float)(Math.Sin((frames + i / 2) * 0.02) * 0.4 + (random.NextDouble() - 0.5) * 0.2) : 0f;
						stretcher.Write(input);
						int read;
						while ((read = stretcher.Read(output)) > 0)
							produced += read / 2;
					}
					Console.WriteLine($"LIBATION_TEST benchspeed: {(amount < 0 ? "original" : $"speedy, nonlinearity {amount}")}: 30s at 10x took {watch.ElapsedMilliseconds} ms (budget 3000 ms), produced {produced / 44100.0:0.00}s");
				}
				AudioBackend.Nonlinearity = settings.Nonlinearity;
				AudioBackend.UseNonlinear = settings.UseNonlinearSpeed;
				break;
			case "open" when parts.Length > 1 && Library.Find(parts[1]) is { IsDownloaded: true } openBook:
				// Shows the player without playing: playback reports a position to Audible, which a look at the screen must not.
				await LoadAsync(openBook, showNowPlaying: true);
				break;
			case "settings":
				ShowSettings();
				break;
			case "details" when parts.Length > 1 && Library.Find(parts[1]) is { } detailsBook:
				ShowDetails(detailsBook);
				for (var i = 0; i < 60 && Details!.IsLoading; i++)
					await Task.Delay(500);
				Console.WriteLine($"LIBATION_TEST details: summary={Details!.Summary?.Length ?? 0} chars, message={Details.Message ?? "none"}");
				foreach (var section in Details.Series)
				{
					Console.WriteLine($"LIBATION_TEST details: series '{section.Name}', {section.CountText}");
					foreach (var row in section.Rows)
						Console.WriteLine($"LIBATION_TEST details:   {row.SequenceText} | {row.Title} | {row.StatusText}");
				}
				break;
			case "annotest" when parts.Length > 1 && Library.Find(parts[1]) is { IsDownloaded: true } book:
				await RunAnnotationTestAsync(book);
				break;
			case "importdefault":
				TypeDefaultCredential();
				await ImportAccountAsync(ImportText);
				Console.WriteLine($"LIBATION_TEST importdefault: error={Error ?? "none"} signedIn={account.IsSignedIn}");
				break;
			case "play" when parts.Length > 1 && Library.Find(parts[1]) is { } item:
				await OpenBookCommand.ExecuteAsync(item);
				if (parts.Length > 2 && NowPlaying is not null && double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var speed))
					NowPlaying.Speed = speed;
				break;
		}
	}

	/// <summary>
	/// End-to-end check of playback, of bookmarks and clips, and of reading and writing the Audible position,
	/// on one book. Leaves the book's Audible position as it found it and deletes what it created.
	/// </summary>
	private async Task RunAnnotationTestAsync(BookItemViewModel book)
	{
		static void Log(string message) => Console.WriteLine("LIBATION_TEST annotest: " + message);
		var asin = book.Book.Asin;
		var originalSpeed = settings.Speed;
		try
		{
			var before = await annotations.GetPositionAsync(asin);
			Log($"before: audible position={before?.Position.ToString() ?? "none"} updated={before?.Updated:u}");

			if (!await LoadAsync(book, showNowPlaying: true))
			{
				Log($"open failed: {Error}");
				return;
			}
			var np = NowPlaying!;
			Log($"opened: duration={np.Duration} chapters={np.Chapters.Count} startedAt={np.Position}");

			// Playback through this platform's audio backend, silent, sampled once a second.
			np.Volume = 0;
			np.Speed = 3;
			var testStart = TimeSpan.FromMinutes(10);
			np.Seek(testStart);
			np.PlayPause();
			for (var i = 1; i <= 5; i++)
			{
				await Task.Delay(1000);
				Log($"  t+{i}s at 3x: advanced {(np.Position - testStart).TotalSeconds:F1}s of book");
			}
			np.PlayPause();

			np.AddBookmarkCommand.Execute(null);
			Log($"add bookmark: status='{np.StatusMessage}' rows={np.Annotations.Count}");
			np.AddClipCommand.Execute(null);
			np.ClipEditor!.Title = "Test clip";
			np.ClipEditor.Note = "Made by the annotation test.";
			np.ClipEditor.SetLengthCommand.Execute("15");
			Log($"clip editor: {np.ClipEditor.StartText} to {np.ClipEditor.EndText}, {np.ClipEditor.RangeText}");
			np.SaveClipCommand.Execute(null);
			Log($"save clip: status='{np.StatusMessage}' rows={np.Annotations.Count}");
			foreach (var row in np.Annotations)
				Log($"  row: {row.Title} | {row.DetailText}");
			foreach (var row in np.Annotations.Where(r => r.Start >= testStart - TimeSpan.FromMinutes(1) && r.Start <= testStart + TimeSpan.FromMinutes(1)).ToList())
				await np.DeleteAnnotationCommand.ExecuteAsync(row);
			Log($"after delete: rows={np.Annotations.Count}");

			// Writing the position: send a marker, read it back, then put the original back.
			var marker = testStart + TimeSpan.FromSeconds(7);
			await annotations.SetPositionAsync(asin, marker);
			var during = await annotations.GetPositionAsync(asin);
			Log($"wrote {marker}, audible now says {during?.Position.ToString() ?? "none"}");
			if (before is not null)
			{
				await annotations.SetPositionAsync(asin, before.Position);
				np.Seek(before.Position);
			}
			var after = await annotations.GetPositionAsync(asin);
			Log($"after: audible position={after?.Position.ToString() ?? "none"}");
			np.Volume = 1;
			np.Speed = originalSpeed;
			Log("done");
		}
		catch (Exception ex)
		{
			Log($"FAILED: {ex}");
		}
	}
#endif
}
