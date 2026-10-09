using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>
/// Series: the whole of one downloaded from its details page, and the next book got ready and played when one ends.
/// </summary>
public partial class MainViewModel
{
	/// <summary>Download every book of a series the listener owns and is not on the phone, one at a time, in order.</summary>
	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task DownloadSeries(SeriesSectionViewModel section)
	{
		foreach (var item in section.ToDownload())
		{
			// Started from somewhere else meanwhile, or removed from the library by a refresh.
			if (!item.IsNotDownloaded || Library.Find(item.Book.Asin) is null)
				continue;
			await Library.DownloadCommand.ExecuteAsync(item);
		}
	}

	/// <summary>
	/// Download every book of a series the listener owns and is not on the phone, in order, from a book's menu in the
	/// library. Read from the library, so only books it knows to be in the series.
	/// </summary>
	[RelayCommand(AllowConcurrentExecutions = true)]
	private async Task DownloadSeriesOf(BookItemViewModel item)
	{
		foreach (var book in Library.SeriesOf(item).Where(b => b.IsNotDownloaded).ToList())
		{
			if (book.IsNotDownloaded)
				await Library.DownloadCommand.ExecuteAsync(book);
		}
	}

	/// <summary>
	/// A book has been opened: show what comes after it in its series. If the listener has asked for it, the next book is
	/// downloaded once this one is within an hour's listening of its end.
	/// </summary>
	private void QueueNextInSeries(NowPlayingViewModel player, BookItemViewModel item)
	{
		var next = Library.NextInSeries(item);
		player.UpNext = next is null ? null : Describe(next, item);
		player.BookEnded += () => _ = PlayNextInSeriesAsync(item);
		player.NearingEnd += () =>
		{
			if (settings.DownloadNextInSeries && Library.NextInSeries(item) is { IsNotDownloaded: true } coming)
				_ = Library.DownloadCommand.ExecuteAsync(coming);
		};
	}

	/// <summary>The book just ended: carry on with the next in its series if it is here, or say why not.</summary>
	private async Task PlayNextInSeriesAsync(BookItemViewModel ended)
	{
		if (!settings.PlayNextInSeries || NowPlaying is not { } np || np.Book.Id != ended.Book.Asin || Library.NextInSeries(ended) is not { } next)
			return;
		if (!next.IsDownloaded)
		{
			np.Announce(next.IsDownloading
				? $"{next.Title} is still downloading. It will be in Downloads when it is ready."
				: $"Next in the series: {next.Title}. Download it to carry on.");
			return;
		}
		var showing = CurrentPage == Page.NowPlaying;
		if (!await LoadAsync(next, showNowPlaying: showing))
			return;
		NowPlaying!.Announce($"Now playing the next in the series: {Describe(next, ended)}");
		NowPlaying.PlayPause();
	}

	/// <summary>"Book 2, The Dark Forest": with its number in the series the two books share, where it has one.</summary>
	private static string Describe(BookItemViewModel next, BookItemViewModel after)
	{
		var shared = after.Book.Series?.Select(s => s.Id).ToHashSet() ?? [];
		var sequence = next.Book.Series?.FirstOrDefault(s => shared.Contains(s.Id))?.Sequence;
		return sequence is null ? next.Title : $"Book {sequence}, {next.Title}";
	}
}
