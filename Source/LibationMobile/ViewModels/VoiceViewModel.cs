using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>A voiced book in Downloads.</summary>
public partial class VoicedRowViewModel(VoicedBook book, VoicedLibrary library) : ObservableObject
{
	public VoicedBook Book { get; } = book;
	public string Title => Book.Title;
	public string StatusText => $"{Book.VoiceName} · {VoiceViewModel.Hours(Book.Length.TotalSeconds)}";

	private Avalonia.Media.Imaging.Bitmap? cover;
	public Avalonia.Media.Imaging.Bitmap? Cover => cover ??= NowPlayingViewModel.LoadCover(library.Cover(Book), 112);
	public bool HasNoCover => Cover is null;

	public void Refresh() => OnPropertyChanged(nameof(StatusText));
}

/// <summary>
/// Making a book from a document: a PDF, EPUB or text file picked from Files, fetched from an address or shared from
/// another app, read aloud as it plays by one of the phone's voices. The player speeds it up like any other book.
/// </summary>
public partial class VoiceViewModel : ObservableObject
{
	/// <summary>Words a minute at a voice's normal pace, for the length shown before it is read.</summary>
	private const int WORDS_PER_MINUTE = 160;

	private readonly VoicedLibrary library;
	private readonly string tempDirectory;

	/// <summary>Where documents wait to be read: fetched ones, and copies of picked ones.</summary>
	public string TempDirectory => tempDirectory;
	private string? sourcePath;
	private string source = "";

	/// <summary>Opens the platform's file picker and copies the chosen file somewhere readable. Set by the view.</summary>
	public Func<Task<string?>>? PickFile { get; set; }

	/// <summary>A voiced book was added or changed.</summary>
	public event Action? BooksChanged;

	/// <summary>A book was made: to be opened and played at once.</summary>
	public event Action<VoicedBook>? Made;

	public VoiceViewModel(VoicedLibrary library, string tempDirectory)
	{
		this.library = library;
		this.tempDirectory = tempDirectory;
		library.Changed += _ => Dispatcher.UIThread.Post(() => BooksChanged?.Invoke());
	}

	public bool CanVoice => BookVoice.Platform is not null;

	[ObservableProperty]
	private bool isOpen;

	[ObservableProperty]
	private string url = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasMessage))]
	private string? message;
	public bool HasMessage => !string.IsNullOrEmpty(Message);

	[ObservableProperty]
	private bool isBusy;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasContent), nameof(ContentText))]
	private DocumentContent? content;
	public bool HasContent => Content is not null;

	[ObservableProperty]
	private string title = "";

	[ObservableProperty]
	private string author = "";

	[ObservableProperty]
	private IReadOnlyList<VoiceChoice> voices = [];

	[ObservableProperty]
	private VoiceChoice? voice;

	public string ContentText => Content is { } c
		? $"{c.Chapters.Count} chapters · {c.Words:N0} words · about {Hours(c.Words * 60.0 / WORDS_PER_MINUTE)}"
		: "";

	public bool CanGetNeuralVoices => BookVoice.Platform is { HasNeuralVoices: false };

	[ObservableProperty]
	private string? neuralStatus;

	/// <summary>Download the HSC library's neural voices (Kokoro), then list them.</summary>
	[RelayCommand]
	private async Task GetNeuralVoices()
	{
		if (BookVoice.Platform is not { } platform)
			return;
		try
		{
			await platform.InstallNeuralVoicesAsync(new Progress<double>(p => NeuralStatus = $"Downloading the voices, {p:P0}"), CancellationToken.None);
			NeuralStatus = null;
			Voices = platform.Voices();
			Voice = Voices.FirstOrDefault();
		}
		catch (Exception ex)
		{
			NeuralStatus = $"The voices could not be downloaded: {ex.Message}";
		}
		OnPropertyChanged(nameof(CanGetNeuralVoices));
	}

	[RelayCommand]
	private void Open()
	{
		Reset();
		Voices = BookVoice.Platform?.Voices() ?? [];
		// The voice last used, or the best there is.
		Voice = Voices.FirstOrDefault(v => v.Id == library.Books.FirstOrDefault()?.VoiceId) ?? Voices.FirstOrDefault();
		IsOpen = true;
	}

	[RelayCommand]
	private void Close()
	{
		IsOpen = false;
		Reset();
	}

	private void Reset()
	{
		Content = null;
		Message = null;
		Url = "";
		Title = Author = "";
		sourcePath = null;
	}

	/// <summary>Open the sheet with a document already chosen, as one shared from another app.</summary>
	public async Task OpenDocumentAsync(string path)
	{
		if (!IsOpen)
			OpenCommand.Execute(null);
		try
		{
			await LoadAsync(path, Path.GetFileName(path));
		}
		catch (Exception ex)
		{
			Message = ex.Message;
		}
	}

	[RelayCommand]
	private async Task PickDocument()
	{
		if (PickFile is null)
			return;
		try
		{
			if (await PickFile() is { } path)
				await LoadAsync(path, Path.GetFileName(path));
		}
		catch (Exception ex)
		{
			Message = ex.Message;
		}
	}

	[RelayCommand]
	private async Task FetchUrl()
	{
		if (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
		{
			Message = "Enter a web address starting with https://";
			return;
		}
		IsBusy = true;
		Message = "Downloading…";
		try
		{
			var path = await VoicedLibrary.DownloadAsync(address, tempDirectory, CancellationToken.None);
			await LoadAsync(path, address.ToString());
		}
		catch (Exception ex)
		{
			Message = ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task LoadAsync(string path, string from)
	{
		IsBusy = true;
		Message = "Reading the text…";
		try
		{
			var read = await Task.Run(() => DocumentText.Read(path));
			sourcePath = path;
			source = from;
			Content = read;
			Title = read.Title ?? "";
			Author = read.Author ?? "";
			Message = null;
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private void Make()
	{
		if (Content is not { } read || Voice is not { } chosen)
		{
			Message = Voice is null ? "No voice is installed. Add one in Settings, Accessibility, Spoken Content, Voices." : null;
			return;
		}
		var book = library.Add(read, source, string.IsNullOrWhiteSpace(Title) ? read.Title ?? "Untitled" : Title.Trim(), Author, chosen);
		BookVoice.Platform?.VoiceAhead(library.TextPath(book), chosen.Id);
		if (sourcePath is not null && sourcePath.StartsWith(tempDirectory, StringComparison.Ordinal))
			File.Delete(sourcePath);
		Close();
		Made?.Invoke(book);
	}

	internal static string Hours(double seconds)
	{
		var time = TimeSpan.FromSeconds(seconds);
		return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{Math.Max(1, time.Minutes)}m";
	}
}
