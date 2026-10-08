using AudibleApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A book marked finished, or unmarked, on Audible.</summary>
public record FinishedEvent(string Asin, DateTimeOffset At, bool Finished);

/// <summary>
/// Audible's listening statistics, as last fetched: every day and month there was listening, in milliseconds, keyed
/// "2026-09-20" and "2026-09". HasFullHistory once the whole past has been fetched.
/// </summary>
public record AudibleStatsSnapshot(
	DateTimeOffset Fetched,
	Dictionary<string, double> DailyMs,
	Dictionary<string, double> MonthlyMs,
	double TotalMs,
	List<FinishedEvent> Finished,
	bool HasFullHistory = false);

/// <summary>
/// Listening statistics from Audible: time per day for 30 days, per month for 12, the total, and books finished. Read
/// only. They count listening in Audible's own apps; Libation's listening is in <see cref="ListeningLog"/>, not here,
/// because Libation does not report listening time to Audible. Kept in audible-stats.json between fetches.
/// </summary>
public class AudibleStats(AudibleAccount account, string dataDirectory)
{
	private readonly string cacheFile = Path.Combine(dataDirectory, "audible-stats.json");
	private const int MAX_FINISHED_PAGES = 20;
	/// <summary>Audible launched in 1995, but its listening statistics start much later; this is early enough.</summary>
	private const int EARLIEST_YEAR = 2008;

	private static DateTime? FirstListened(Dictionary<string, double> monthly)
		=> monthly.Where(m => m.Value > 0).Select(m => DateTime.ParseExact(m.Key + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
			.OrderBy(d => d).Cast<DateTime?>().FirstOrDefault();

	public AudibleStatsSnapshot? Cached
	{
		get
		{
			try
			{
				return File.Exists(cacheFile) ? JsonSerializer.Deserialize(File.ReadAllText(cacheFile), AudibleStatsJsonContext.Default.AudibleStatsSnapshot) : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// Everything Audible keeps: every month and every day back to the first listening (2017 for some accounts). The
	/// first fetch walks back year by year, about a hundred requests; later ones only fetch the last two months and
	/// keep the rest from the cache, since the past does not change.
	/// </summary>
	public async Task<AudibleStatsSnapshot> FetchAsync()
	{
		var api = await account.GetApiAsync();
		var today = DateTime.Today;
		var cached = Cached;
		var daily = new Dictionary<string, double>(cached?.DailyMs ?? []);
		var monthly = new Dictionary<string, double>(cached?.MonthlyMs ?? []);
		var fullHistory = cached?.HasFullHistory == true;
		var firstMonth = fullHistory ? new DateTime(today.Year, today.Month, 1).AddMonths(-2) : new DateTime(EARLIEST_YEAR, 1, 1);

		// Months, twelve a request.
		double total = 0;
		for (var start = firstMonth; start <= today; start = start.AddMonths(12))
		{
			using var json = await GetJsonAsync(api, "/1.0/stats/aggregates?response_groups=total_listening_stats&store=Audible" +
				$"&monthly_listening_interval_duration=12&monthly_listening_interval_start_date={start:yyyy-MM}");
			foreach (var (key, value) in Sums(json.RootElement, "aggregated_monthly_listening_stats"))
				monthly[key] = value;
			if (json.RootElement.TryGetProperty("aggregated_total_listening_stats", out var t) && t.TryGetProperty("aggregated_sum", out var sum))
				total = sum.GetDouble();
		}

		// Days, thirty a request, only over months that had listening.
		var firstDay = fullHistory ? today.AddDays(-60) : FirstListened(monthly) ?? today.AddDays(-60);
		for (var start = firstDay; start <= today; start = start.AddDays(30))
		{
			var month = start.ToString("yyyy-MM");
			if (!fullHistory && monthly.GetValueOrDefault(month) <= 0 && monthly.GetValueOrDefault(start.AddDays(29).ToString("yyyy-MM")) <= 0)
				continue;
			using var json = await GetJsonAsync(api, "/1.0/stats/aggregates?response_groups=total_listening_stats&store=Audible" +
				$"&daily_listening_interval_duration=30&daily_listening_interval_start_date={start:yyyy-MM-dd}");
			foreach (var (key, value) in Sums(json.RootElement, "aggregated_daily_listening_stats"))
				daily[key] = value;
		}

		var finished = new Dictionary<string, FinishedEvent>();
		string? continuation = null;
		for (var page = 0; page < MAX_FINISHED_PAGES; page++)
		{
			var path = "/1.0/stats/status/finished?start_date=2000-01-01T00:00:00Z" +
				(continuation is null ? "" : "&continuation_token=" + Uri.EscapeDataString(continuation));
			using var json = await GetJsonAsync(api, path);
			if (json.RootElement.TryGetProperty("mark_as_finished_status_list", out var list))
			{
				foreach (var item in list.EnumerateArray())
				{
					if (item.TryGetProperty("asin", out var asin) && asin.GetString() is { } id
						&& item.TryGetProperty("event_timestamp", out var at) && DateTimeOffset.TryParse(at.GetString(), out var when))
					{
						var isFinished = item.TryGetProperty("is_marked_as_finished", out var f) && f.ValueKind == JsonValueKind.True;
						if (!finished.TryGetValue(id, out var known) || known.At < when)
							finished[id] = new FinishedEvent(id, when, isFinished);
					}
				}
			}
			var next = json.RootElement.TryGetProperty("continuation_token", out var token) ? token.GetString() : null;
			if (string.IsNullOrEmpty(next) || next == continuation)
				break;
			continuation = next;
		}

		var snapshot = new AudibleStatsSnapshot(DateTimeOffset.Now, daily, monthly, total, finished.Values.OrderByDescending(f => f.At).ToList(), HasFullHistory: true);

		var temp = cacheFile + ".tmp";
		await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, AudibleStatsJsonContext.Default.AudibleStatsSnapshot));
		File.Move(temp, cacheFile, overwrite: true);
		return snapshot;
	}

	private static Dictionary<string, double> Sums(JsonElement root, string name)
	{
		var sums = new Dictionary<string, double>();
		if (root.TryGetProperty(name, out var list))
			foreach (var item in list.EnumerateArray())
				if (item.TryGetProperty("interval_identifier", out var key) && key.GetString() is { } k && item.TryGetProperty("aggregated_sum", out var value))
					sums[k] = value.GetDouble();
		return sums;
	}

	private static async Task<JsonDocument> GetJsonAsync(Api api, string path)
	{
		using var response = await api.AdHocAuthenticatedGetAsync(path);
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
	}
}

[JsonSerializable(typeof(AudibleStatsSnapshot))]
internal partial class AudibleStatsJsonContext : JsonSerializerContext
{
}
