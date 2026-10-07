using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>One session in the log.</summary>
public class LogEntryViewModel(ListeningSession session)
{
	public string Title { get; } = session.Title;
	public string TimeText { get; } = $"{session.Started.ToLocalTime():t} to {session.Ended.ToLocalTime():t}";
	public string DetailText { get; } =
		$"{Describe(TimeSpan.FromSeconds(session.SpentSeconds))} at {session.Speed:0.0}×, heard {Describe(TimeSpan.FromSeconds(session.BookSeconds))} " +
		$"({NowPlayingViewModel.FormatTime(session.From)} to {NowPlayingViewModel.FormatTime(session.To)})";

	internal static string Describe(TimeSpan time)
		=> time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} min" : $"{time.Seconds} s";
}

/// <summary>A day of sessions, with what it added up to.</summary>
public class LogDayViewModel(DateTime day, IReadOnlyList<ListeningSession> sessions)
{
	public string Header { get; } = day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
	public string TotalText { get; } =
		$"{LogEntryViewModel.Describe(TimeSpan.FromSeconds(sessions.Sum(s => s.SpentSeconds)))} listening, {LogEntryViewModel.Describe(TimeSpan.FromSeconds(sessions.Sum(s => s.BookSeconds)))} of books";
	public IReadOnlyList<LogEntryViewModel> Entries { get; } = sessions.OrderByDescending(s => s.Started).Select(s => new LogEntryViewModel(s)).ToList();
}

/// <summary>One bar of the chart: listening in the Audible app and in Libation, as fractions of the tallest bar.</summary>
public record ChartBar(string Label, double AudibleHeight, double LibationHeight, string ValueText, bool ShowLabel);

/// <summary>A book finished, with when.</summary>
public record FinishedRow(string Title, string? Author, string DateText);

/// <summary>A figure at the top of the page: "312 h", "total listening".</summary>
public record StatTile(string Value, string Label);

/// <summary>
/// Listening stats, after Audible's: totals, a day streak, books finished, a chart of the last 30 days or 12 months,
/// then each day's sessions. Audible's figures cover its own apps; this phone's log covers Libation, which does not
/// report to Audible, so the two are added and the chart shows each.
/// </summary>
public partial class ListeningLogViewModel : ObservableObject
{
	private readonly ListeningLog log;
	private readonly AudibleStats? audible;
	private readonly Func<string, (string Title, string? Author)?> findBook;
	/// <summary>The height of the chart, in pixels, that the tallest bar fills.</summary>
	public const double CHART_HEIGHT = 120;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsEmpty))]
	private IReadOnlyList<LogDayViewModel> days = [];

	public bool IsEmpty => Days.Count == 0;

	[ObservableProperty]
	private IReadOnlyList<StatTile> tiles = [];

	[ObservableProperty]
	private IReadOnlyList<ChartBar> bars = [];

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsByMonth))]
	private bool isByDay = true;

	public bool IsByMonth => !IsByDay;

	[ObservableProperty]
	private string chartTotalText = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasFinished))]
	private IReadOnlyList<FinishedRow> finished = [];

	public bool HasFinished => Finished.Count > 0;

	[ObservableProperty]
	private string? audibleStatus;

	[ObservableProperty]
	private bool isRefreshing;

	public ListeningLogViewModel(ListeningLog log, AudibleStats? audible = null, Func<string, (string Title, string? Author)?>? findBook = null)
	{
		this.log = log;
		this.audible = audible;
		this.findBook = findBook ?? (_ => null);
		Refresh();
	}

	/// <summary>Show what is on the phone at once, then fetch Audible's figures if they are over an hour old.</summary>
	public void Refresh()
	{
		Days = log.Sessions
			.GroupBy(s => s.Started.ToLocalTime().Date)
			.OrderByDescending(g => g.Key)
			.Select(g => new LogDayViewModel(g.Key, g.ToList()))
			.ToList();
		Build(audible?.Cached);
		if (audible?.Cached is not { } cached || DateTimeOffset.Now - cached.Fetched > TimeSpan.FromHours(1))
			_ = RefreshFromAudibleAsync();
	}

	[RelayCommand]
	private async Task RefreshFromAudibleAsync()
	{
		if (audible is null || IsRefreshing)
			return;
		IsRefreshing = true;
		try
		{
			Build(await audible.FetchAsync());
		}
		catch (Exception ex)
		{
			AudibleStatus = $"Could not get Audible's figures: {ex.Message}";
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	[RelayCommand]
	private void ShowByDay()
	{
		IsByDay = true;
		Build(audible?.Cached);
	}

	[RelayCommand]
	private void ShowByMonth()
	{
		IsByDay = false;
		Build(audible?.Cached);
	}

	private void Build(AudibleStatsSnapshot? stats)
	{
		var sessions = log.Sessions;
		var localByDay = sessions.GroupBy(s => s.Started.ToLocalTime().Date).ToDictionary(g => g.Key, g => g.Sum(s => s.SpentSeconds) / 3600);
		double AudibleDay(DateTime day) => stats?.DailyMs.GetValueOrDefault(day.ToString("yyyy-MM-dd")) / 3.6e6 ?? 0;
		double AudibleMonth(DateTime month) => stats?.MonthlyMs.GetValueOrDefault(month.ToString("yyyy-MM")) / 3.6e6 ?? 0;
		double LocalMonth(DateTime month) => localByDay.Where(d => d.Key.Year == month.Year && d.Key.Month == month.Month).Sum(d => d.Value);
		var today = DateTime.Today;

		// Tiles.
		var localTotal = sessions.Sum(s => s.SpentSeconds) / 3600;
		var total = (stats?.TotalMs ?? 0) / 3.6e6 + localTotal;
		var thisMonth = AudibleMonth(today) + LocalMonth(today);
		var streak = 0;
		for (var day = localByDay.ContainsKey(today) || AudibleDay(today) > 0 ? today : today.AddDays(-1);
			localByDay.GetValueOrDefault(day) + AudibleDay(day) >= 1.0 / 60; day = day.AddDays(-1))
			streak++;
		var finishedThisYear = (stats?.Finished ?? []).Count(f => f.Finished && f.At.ToLocalTime().Year == today.Year);
		Tiles =
		[
			new(Hours(total), stats is null ? "listened here" : "listened, all apps"),
			new(Hours(thisMonth), today.ToString("MMMM", CultureInfo.CurrentCulture)),
			new(streak == 1 ? "1 day" : $"{streak} days", "streak"),
			new(stats is null ? "–" : finishedThisYear.ToString(CultureInfo.CurrentCulture), $"finished in {today.Year}"),
		];

		// Chart: the last 30 days or 12 months, oldest first.
		var points = IsByDay
			? Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).Select(d => (Label: d.Day.ToString(CultureInfo.CurrentCulture), Audible: AudibleDay(d), Local: localByDay.GetValueOrDefault(d), Show: (29 - (today - d).Days) % 5 == 4))
			: Enumerable.Range(0, 12).Select(i => new DateTime(today.Year, today.Month, 1).AddMonths(i - 11)).Select(m => (Label: m.ToString("MMM", CultureInfo.CurrentCulture)[..1], Audible: AudibleMonth(m), Local: LocalMonth(m), Show: true));
		var list = points.ToList();
		var tallest = Math.Max(list.Max(p => p.Audible + p.Local), 1.0 / 60);
		Bars = list.Select(p => new ChartBar(p.Label, CHART_HEIGHT * p.Audible / tallest, CHART_HEIGHT * p.Local / tallest, Hours(p.Audible + p.Local), p.Show)).ToList();
		var sum = list.Sum(p => p.Audible + p.Local);
		ChartTotalText = IsByDay ? $"{Hours(sum)} in 30 days, {Hours(sum / 30)} a day" : $"{Hours(sum)} in 12 months, {Hours(sum / 12)} a month";

		// Recently finished, on Audible.
		Finished = (stats?.Finished ?? []).Where(f => f.Finished).Take(15)
			.Select(f => findBook(f.Asin) is { } book ? new FinishedRow(book.Title, book.Author, f.At.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)) : null)
			.OfType<FinishedRow>().ToList();
		AudibleStatus = stats is null ? "Audible's figures appear here once fetched." : $"Audible's figures as of {stats.Fetched.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}, {stats.Fetched.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture)}. They count the Audible app; Libation adds this phone.";
	}

	private static string Hours(double hours)
		=> hours >= 100 ? $"{hours:0} h" : hours >= 1 ? $"{(int)hours} h {(int)(hours % 1 * 60)} m" : $"{Math.Round(hours * 60)} m";

	[RelayCommand]
	private void ClearLog()
	{
		log.Clear();
		Refresh();
	}
}
