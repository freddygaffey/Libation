using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>Books voiced on the phone from documents: made from Downloads, listed there, and played like any other.</summary>
public partial class MainViewModel
{
	private VoicedLibrary voiced = null!;
	private readonly Dictionary<string, VoicedRowViewModel> voicedRows = [];

	public VoiceViewModel Voice { get; private set; } = null!;

	private void StartVoiced(string dataDirectory)
	{
		voiced = new VoicedLibrary(dataDirectory);
		// Documents fetched or picked wait in the temporary folder. Not Documents/Inbox: iOS keeps that for files other
		// apps hand over, and an app may not write there.
		Voice = new VoiceViewModel(voiced, SharedDocuments.Folder);
		// Made: straight to the player, reading.
		Voice.Made += book => _ = OpenVoiced(new VoicedRowViewModel(book, voiced));
		Voice.BooksChanged += () =>
		{
			// A book added or removed changes the list; a new voice only changes its row.
			if (voiced.Books.Select(b => b.Id).ToHashSet().SetEquals(voicedRows.Keys))
				foreach (var row in voicedRows.Values)
					row.Refresh();
			else
				RefreshDownloads();
		};
	}

	/// <summary>A document shared with the app: the voice sheet, with it read and ready to make.</summary>
	private async void OpenSharedDocument(string path)
	{
		CurrentPage = Page.Downloads;
		await Voice.OpenDocumentAsync(path);
	}

	/// <summary>The voiced books, newest first, keeping each row so it can be updated in place.</summary>
	private IReadOnlyList<VoicedRowViewModel> VoicedRows()
	{
		var books = voiced.Books;
		foreach (var gone in voicedRows.Keys.Except(books.Select(b => b.Id)).ToList())
			voicedRows.Remove(gone);
		return books.Select(b => voicedRows.TryGetValue(b.Id, out var row) ? row : voicedRows[b.Id] = new VoicedRowViewModel(b, voiced)).ToList();
	}

	[RelayCommand]
	private async Task OpenVoiced(VoicedRowViewModel row)
	{
		if (NowPlaying?.Book.Id != row.Book.Id && !await LoadVoicedAsync(row.Book, showNowPlaying: true))
			return;
		CurrentPage = Page.NowPlaying;
		if (!NowPlaying!.IsPlaying)
			NowPlaying.PlayPause();
	}

	[RelayCommand]
	private void RemoveVoiced(VoicedRowViewModel row)
	{
		if (NowPlaying?.Book.Id == row.Book.Id)
		{
			NowPlaying.Dispose();
			NowPlaying = null;
		}
		voiced.Delete(row.Book);
	}

	/// <summary>Open a voiced book in the player. Nothing goes to Audible: it is not an Audible book.</summary>
	private async Task<bool> LoadVoicedAsync(VoicedBook book, bool showNowPlaying)
	{
		Error = null;
		NowPlaying?.Dispose();
		NowPlaying = null;
		try
		{
			if (BookVoice.Platform is not { } voice)
				throw new NotSupportedException("This device has no voices to read with.");
			NowPlaying = await NowPlayingViewModel.OpenAsync(voiced.ToLocalBook(book, voice), settings, annotations: null, localAnnotations, listeningLog);
			NowPlaying.VoiceChanged += choice => voiced.SetVoice(book, choice);
			if (showNowPlaying)
				CurrentPage = Page.NowPlaying;
			return true;
		}
		catch (Exception ex)
		{
			Error = $"{book.Title} could not be opened: {ex.Message}";
			return false;
		}
	}
}
