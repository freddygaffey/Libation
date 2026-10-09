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
		Voice = new VoiceViewModel(voiced, Path.Combine(Path.GetTempPath(), "voice"));
		Voice.BooksChanged += () =>
		{
			// A book added or removed changes the list; progress only changes its row.
			if (voiced.Books.Select(b => b.Id).ToHashSet().SetEquals(voicedRows.Keys))
				foreach (var row in voicedRows.Values)
					row.Refresh();
			else
				RefreshDownloads();
		};
	}

	/// <summary>The voiced books, newest first, keeping each row so its progress can be updated in place.</summary>
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
		if (!row.IsReady)
			return;
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

	[RelayCommand]
	private void RetryVoiced(VoicedRowViewModel row) => voiced.Retry(row.Book);

	/// <summary>Open a voiced book in the player. Nothing goes to Audible: it is not an Audible book.</summary>
	private async Task<bool> LoadVoicedAsync(VoicedBook book, bool showNowPlaying)
	{
		Error = null;
		NowPlaying?.Dispose();
		NowPlaying = null;
		try
		{
			NowPlaying = await NowPlayingViewModel.OpenAsync(voiced.ToLocalBook(book), settings, annotations: null, localAnnotations, listeningLog);
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
