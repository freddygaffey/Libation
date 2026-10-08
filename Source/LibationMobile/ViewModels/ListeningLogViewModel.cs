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
		$"({NowPlayingViewModel.FormatTime(session.From)} to {NowPlayingViewModel.FormatTime(session.To)})" +
		(session.Syllables is > 0 && session.SpentSeconds > 0 ? $", {session.Syllables / session.SpentSeconds:0} syllables a second" : "");

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

/// <summary>Listening in Audible's app and in Libation: real (wall-clock) time, book time heard, and time saved against 1x.</summary>
/// <param name="AudibleSpeed">The speed Audible's real time is multiplied by: <paramref name="Estimate"/>'s, or the default set.</param>
/// <param name="HasAudible">Audible's figures have been fetched.</param>
public record TimeBreakdown(TimeSpan AudibleReal, double AudibleSpeed, AudibleSpeedEstimate? Estimate,
	TimeSpan LibationReal, TimeSpan LibationBook, bool HasAudible)
{
	public TimeSpan AudibleBook => AudibleReal * AudibleSpeed;
	public TimeSpan AudibleSaved => AudibleBook - AudibleReal;
	public TimeSpan LibationSaved => LibationBook > LibationReal ? LibationBook - LibationReal : TimeSpan.Zero;
	public TimeSpan Real => AudibleReal + LibationReal;
	public TimeSpan Book => AudibleBook + LibationBook;
	public TimeSpan Saved => AudibleSaved + LibationSaved;
	public double LibationSpeed => LibationReal > TimeSpan.Zero ? LibationBook / LibationReal : 0;
	public double Speed => Real > TimeSpan.Zero ? Book / Real : 0;
}

/// <summary>One bar of the chart: listening in the Audible app and in Libation, as heights in pixels. Tap to see its figures.</summary>
/// <param name="Name">What it covers in full, as "Tue 7 Oct" or "March 2026", shown when tapped.</param>
public record ChartBar(int Index, string Label, string Name, double AudibleHeight, double LibationHeight, string ValueText, bool ShowLabel, bool IsSelected)
{
	public double Opacity => IsSelected ? 1 : 0.55;
}

/// <summary>A book finished, with when, and the book in the library for its cover.</summary>
public record FinishedRow(string Title, string? Author, string DateText, BookItemViewModel? Book);

/// <summary>One app's share: real time, book time, time saved and speed.</summary>
public record AppShare(string Name, string Real, string Book, string Saved, string Speed, string Note);

/// <summary>How well sessions were followed at a rate of syllables a second, as a bar.</summary>
public record FollowBar(string Label, double Height, string ValueText);

/// <summary>A setting's results in one speed band, as lines of text.</summary>
public record ResearchRow(string Title, IReadOnlyList<string> Lines, string? Verdict);

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
	private readonly Func<string, BookItemViewModel?> findBook;
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

	/// <summary>"day" (30 days), "month" (12 months) or "year" (every year).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsByDay), nameof(IsByMonth), nameof(IsByYear), nameof(CanPage))]
	private string chartScale = "day";

	public bool IsByDay => ChartScale == "day";
	public bool IsByMonth => ChartScale == "month";
	public bool IsByYear => ChartScale == "year";
	public bool CanPage => !IsByYear;

	/// <summary>The bar tapped; -1 for none, when the heading shows the whole range.</summary>
	private int selectedBar = -1;

	/// <summary>Over the chart, as Audible's: the range's total, or the tapped bar's.</summary>
	[ObservableProperty]
	private string periodLabel = "";

	[ObservableProperty]
	private string periodValue = "";

	[ObservableProperty]
	private string periodDetail = "";

	[RelayCommand]
	private void SelectBar(ChartBar bar)
	{
		selectedBar = selectedBar == bar.Index ? -1 : bar.Index;
		Build(audible?.Cached);
	}

	#region Time saved, at the top

	[ObservableProperty]
	private string savedHero = "";

	[ObservableProperty]
	private string savedHeroUnit = "";

	[ObservableProperty]
	private string savedHeroDetail = "";

	[ObservableProperty]
	private string realTotalText = "";

	[ObservableProperty]
	private string bookTotalText = "";

	/// <summary>Real time as a share of book time, 0 to 100: the rest of the bar is time saved.</summary>
	[ObservableProperty]
	private double realShare;

	[ObservableProperty]
	private IReadOnlyList<AppShare> apps = [];

	[ObservableProperty]
	private string howWorkedOut = "";

	/// <summary>Changes the speed assumed for Audible's app when its history cannot give one. Set by the main view model.</summary>
	public Action<double>? SetAudibleAppSpeed { get; set; }

	public string AudibleDefaultText => $"{AudibleAppSpeed?.Invoke() ?? 1:0.0}×";

	[RelayCommand]
	private void ChangeAudibleDefault(string step)
	{
		if (double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out var by) && AudibleAppSpeed is { } get)
			SetAudibleAppSpeed?.Invoke(Math.Round(get() + by, 1));
		OnPropertyChanged(nameof(AudibleDefaultText));
		Build(audible?.Cached);
	}

	#endregion

	/// <summary>How many pages back from now: 1 is the 30 days or 12 months before the latest.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanGoNewer))]
	private int pagesBack;

	public bool CanGoNewer => PagesBack > 0;

	[ObservableProperty]
	private string chartTitle = "";

	[ObservableProperty]
	private string chartTotalText = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasFinished))]
	private IReadOnlyList<FinishedRow> finished = [];

	public bool HasFinished => Finished.Count > 0;

	[ObservableProperty]
	private string? audibleStatus;

	/// <summary>The listener's own research, from ratings and blind trials. Set by the main view model.</summary>
	public Experiments? Experiments { get; set; }

	/// <summary>The event log, for the research export. Set by the main view model.</summary>
	public ListeningEvents? Events { get; set; }

	/// <summary>The profiles in use, for the research export. Set by the main view model.</summary>
	public Func<IReadOnlyList<SpeedProfile>>? Profiles { get; set; }

	/// <summary>Everything collected, as JSON: for analysis, or to paste into an AI.</summary>
	public string ResearchJson() => ResearchExport.ToJson(log, Experiments ?? new Experiments(System.IO.Path.GetTempPath()),
		Events ?? new ListeningEvents(System.IO.Path.GetTempPath()), audible?.Cached, Profiles?.Invoke() ?? []);

	public static string ResearchFileName => $"libation-research-{DateTime.Now:yyyy-MM-dd}.json";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasResearch))]
	private IReadOnlyList<FollowBar> followBars = [];

	[ObservableProperty]
	private IReadOnlyList<ResearchRow> research = [];

	[ObservableProperty]
	private string researchSummary = "";

	public bool HasResearch => FollowBars.Count > 0 || Research.Count > 0;

	private void BuildResearch()
	{
		if (Experiments is not { } experiments)
			return;
		var follow = experiments.FollowBySyllables();
		FollowBars = follow.Select(f => new FollowBar($"{f.From}", 60 * f.MeanFollow / 5, $"{f.MeanFollow:0.0} from {f.Count}")).ToList();
		Research = experiments.Results()
			.Select(r => new ResearchRow($"{r.Parameter.Name}, {r.Band}× to {r.Band + 1}×",
				r.Values.Select(v => $"{v.Description}: won {v.Won}, lost {v.Lost}, same {v.Tied}; followed {v.MeanFollow:0.0} on average ({v.Heard} heard)").ToList(),
				r.Verdict))
			.ToList();
		// The ceiling: the fastest band still followed well (4 or more on average).
		var ceiling = follow.Where(f => f.MeanFollow >= 4).Select(f => f.From + 2).DefaultIfEmpty().Max();
		var ratings = experiments.Ratings.Count;
		var trials = experiments.Trials.Count;
		ResearchSummary = ratings + trials == 0
			? "Rate sessions and run blind trials from the player's mode panel; what works for you builds up here."
			: $"{ratings} session ratings, {trials} blind trials." + (ceiling > 0 ? $" You follow well up to about {ceiling} syllables a second." : "");
	}

	[ObservableProperty]
	private bool isRefreshing;

	/// <summary>The speed set for Audible's app, to turn its real-time hours into book time. Set by the main view model.</summary>
	public Func<double>? AudibleAppSpeed { get; set; }

	/// <summary>A book's length in hours, from the library, for the estimate.</summary>
	public Func<string, double?>? BookHours { get; set; }

	/// <summary>
	/// Listening in Audible's app and in Libation: real time, book time, and so time saved against 1x. Audible's real time
	/// is its own figure; its book time is that times its speed, worked out from its history where possible, otherwise the
	/// default set. Libation's are measured.
	/// </summary>
	public TimeBreakdown Breakdown() => Breakdown(audible?.Cached);

	private TimeBreakdown Breakdown(AudibleStatsSnapshot? stats)
	{
		var estimate = stats is not null && BookHours is { } hours ? AudibleStats.EstimateSpeed(stats, hours) : null;
		var sessions = log.Sessions;
		return new TimeBreakdown(
			TimeSpan.FromMilliseconds(stats?.TotalMs ?? 0), estimate?.Speed ?? AudibleAppSpeed?.Invoke() ?? 1, estimate,
			TimeSpan.FromSeconds(sessions.Sum(s => s.SpentSeconds)), TimeSpan.FromSeconds(sessions.Sum(s => s.BookSeconds)),
			stats is not null);
	}

	public ListeningLogViewModel(ListeningLog log, AudibleStats? audible = null, Func<string, BookItemViewModel?>? findBook = null)
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
		BuildResearch();
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
	private void ShowByDay() => SetScale("day");

	[RelayCommand]
	private void ShowByMonth() => SetScale("month");

	[RelayCommand]
	private void ShowByYear() => SetScale("year");

	private void SetScale(string scale)
	{
		ChartScale = scale;
		PagesBack = 0;
		selectedBar = -1;
		Build(audible?.Cached);
	}

	[RelayCommand]
	private void Older()
	{
		PagesBack++;
		selectedBar = -1;
		Build(audible?.Cached);
	}

	[RelayCommand]
	private void Newer()
	{
		if (PagesBack == 0)
			return;
		PagesBack--;
		selectedBar = -1;
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
		var streak = 0;
		for (var day = localByDay.ContainsKey(today) || AudibleDay(today) > 0 ? today : today.AddDays(-1);
			localByDay.GetValueOrDefault(day) + AudibleDay(day) >= 1.0 / 60; day = day.AddDays(-1))
			streak++;
		var finishedThisYear = (stats?.Finished ?? []).Count(f => f.Finished && f.At.ToLocalTime().Year == today.Year);
		// Measured in Libation: book time against real time, and syllables heard against real time.
		var counted = sessions.Where(s => s.Syllables is > 0).ToList();
		var syllablesPerSecond = counted.Sum(s => s.SpentSeconds) is > 60 and var countedSpent ? counted.Sum(s => s.Syllables!.Value) / countedSpent : (double?)null;
		var breakdown = Breakdown(stats);
		SavedHero = breakdown.Saved.TotalHours >= 1 ? $"{breakdown.Saved.TotalHours:N0}" : $"{breakdown.Saved.TotalMinutes:N0}";
		SavedHeroUnit = breakdown.Saved.TotalHours >= 1 ? "hours saved" : "minutes saved";
		SavedHeroDetail = breakdown.Real > TimeSpan.Zero
			? $"{breakdown.Book.TotalHours:N0} hours of books in {breakdown.Real.TotalHours:N0} hours of listening, at {breakdown.Speed:0.00}× on average. "
				+ (breakdown.Saved.TotalDays >= 2 ? $"That is {breakdown.Saved.TotalDays:N0} days back." : "")
			: "Listen to a book and the time your speed saves adds up here.";
		RealTotalText = $"{Hours(breakdown.Real.TotalHours)} listening";
		BookTotalText = $"{Hours(breakdown.Book.TotalHours)} of books";
		RealShare = breakdown.Book > TimeSpan.Zero ? 100 * breakdown.Real / breakdown.Book : 100;
		Apps =
		[
			new("Audible app", stats is null ? "–" : Hours(breakdown.AudibleReal.TotalHours), stats is null ? "–" : Hours(breakdown.AudibleBook.TotalHours),
				stats is null ? "–" : Hours(breakdown.AudibleSaved.TotalHours), stats is null ? "–" : $"{breakdown.AudibleSpeed:0.00}×",
				stats is null ? "Not fetched yet" : breakdown.Estimate is null ? "Speed: your default" : "Speed: from your history"),
			new("Libation", Hours(breakdown.LibationReal.TotalHours), Hours(breakdown.LibationBook.TotalHours), Hours(breakdown.LibationSaved.TotalHours),
				breakdown.LibationReal > TimeSpan.Zero ? $"{breakdown.LibationSpeed:0.00}×" : "–", "Measured as you listen"),
		];
		HowWorkedOut = stats is null
			? "Audible's figures have not been fetched yet."
			: breakdown.Estimate is { } e
				? $"Audible counts listening in real time, and not the speed. Comparing the books you finished with the hours it counted, your best three-month stretch "
					+ $"(of {e.Stretches}) gives at least {e.Speed:0.00}×. Listening that finishes no book, such as a book left playing while asleep, only adds hours, "
					+ "so this is a floor. Libation measures its own."
				: "Your Audible history could not give a speed (no finished book found in your library), so the default below is used.";
		Tiles =
		[
			new(streak == 1 ? "1" : $"{streak}", streak == 1 ? "day streak" : "day streak"),
			new(stats is null ? "–" : finishedThisYear.ToString(CultureInfo.CurrentCulture), $"finished in {today.Year}"),
			new(syllablesPerSecond is double sps ? $"{sps:0}" : "–", "syllables a second"),
		];

		// Chart: 30 days or 12 months, oldest first, paged back through the history; or every year.
		double AudibleYear(int year) => stats?.MonthlyMs.Where(m => m.Key.StartsWith(year.ToString(CultureInfo.InvariantCulture))).Sum(m => m.Value) / 3.6e6 ?? 0;
		double LocalYear(int year) => localByDay.Where(d => d.Key.Year == year).Sum(d => d.Value);
		List<(string Label, string Name, double Audible, double Local, bool Show)> list;
		if (IsByDay)
		{
			var last = today.AddDays(-30 * PagesBack);
			list = Enumerable.Range(0, 30).Select(i => last.AddDays(i - 29))
				.Select(d => (d.Day.ToString(CultureInfo.CurrentCulture), d == today ? "Today" : d == today.AddDays(-1) ? "Yesterday" : d.ToString("ddd d MMM", CultureInfo.CurrentCulture),
					AudibleDay(d), localByDay.GetValueOrDefault(d), (29 - (last - d).Days) % 5 == 4)).ToList();
			ChartTitle = PagesBack == 0 ? "Last 30 days" : $"{last.AddDays(-29):d MMM} to {last:d MMM yyyy}";
		}
		else if (IsByMonth)
		{
			var lastMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-12 * PagesBack);
			list = Enumerable.Range(0, 12).Select(i => lastMonth.AddMonths(i - 11))
				.Select(m => (m.ToString("MMM", CultureInfo.CurrentCulture)[..1], m.ToString("MMMM yyyy", CultureInfo.CurrentCulture), AudibleMonth(m), LocalMonth(m), true)).ToList();
			ChartTitle = PagesBack == 0 ? "Last 12 months" : $"{lastMonth.AddMonths(-11):MMM yyyy} to {lastMonth:MMM yyyy}";
		}
		else
		{
			var years = (stats?.MonthlyMs.Keys.AsEnumerable() ?? []).Select(k => int.Parse(k[..4], CultureInfo.InvariantCulture))
				.Concat(localByDay.Keys.Select(d => d.Year)).Append(today.Year).ToList();
			var first = years.Min();
			list = Enumerable.Range(first, today.Year - first + 1)
				.Select(y => ($"'{y % 100:00}", y.ToString(CultureInfo.InvariantCulture), AudibleYear(y), LocalYear(y), true)).ToList();
			ChartTitle = $"Every year since {first}";
		}
		var tallest = Math.Max(list.Max(p => p.Audible + p.Local), 1.0 / 60);
		if (selectedBar >= list.Count)
			selectedBar = -1;
		Bars = list.Select((p, i) => new ChartBar(i, p.Label, p.Name, CHART_HEIGHT * p.Audible / tallest, CHART_HEIGHT * p.Local / tallest,
			Hours(p.Audible + p.Local), p.Show, selectedBar < 0 || selectedBar == i)).ToList();
		var sum = list.Sum(p => p.Audible + p.Local);
		ChartTotalText = IsByDay ? $"{Hours(sum / 30)} a day on average"
			: IsByMonth ? $"{Hours(sum / 12)} a month on average"
			: $"{Hours(sum / list.Count)} a year on average";
		if (selectedBar >= 0)
		{
			var bar = list[selectedBar];
			PeriodLabel = bar.Name;
			PeriodValue = Hours(bar.Audible + bar.Local);
			PeriodDetail = bar.Local > 0 && bar.Audible > 0 ? $"{Hours(bar.Audible)} in the Audible app, {Hours(bar.Local)} in Libation"
				: bar.Local > 0 ? "In Libation" : bar.Audible > 0 ? "In the Audible app" : "No listening";
		}
		else
		{
			PeriodLabel = ChartTitle;
			PeriodValue = Hours(sum);
			PeriodDetail = ChartTotalText;
		}

		// Recently finished, on Audible.
		Finished = (stats?.Finished ?? []).Where(f => f.Finished).Take(15)
			.Select(f => findBook(f.Asin) is { } book ? new FinishedRow(book.Title, book.Author, f.At.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), book) : null)
			.OfType<FinishedRow>().ToList();
		AudibleStatus = stats is null ? "Audible's figures appear here once fetched." : $"Audible's figures as of {stats.Fetched.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}, {stats.Fetched.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture)}.";
	}

	private static string Hours(double hours)
		=> hours >= 100 ? $"{hours:N0} h" : hours >= 1 ? $"{(int)hours} h {(int)(hours % 1 * 60)} m" : $"{Math.Round(hours * 60)} m";

	[RelayCommand]
	private void ClearLog()
	{
		log.Clear();
		Refresh();
	}
}
