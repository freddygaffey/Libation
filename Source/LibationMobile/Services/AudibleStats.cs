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

/// <summary>Audible's listening statistics, as last fetched. Times are milliseconds, keyed "2026-09-20" and "2026-09".</summary>
public record AudibleStatsSnapshot(
	DateTimeOffset Fetched,
	Dictionary<string, double> DailyMs,
	Dictionary<string, double> MonthlyMs,
	double TotalMs,
	List<FinishedEvent> Finished);

/// <summary>
/// Listening statistics from Audible: time per day for 30 days, per month for 12, the total, and books finished. Read
/// only. They count listening in Audible's own apps; Libation's listening is in <see cref="ListeningLog"/>, not here,
/// because Libation does not report listening time to Audible. Kept in audible-stats.json between fetches.
/// </summary>
public class AudibleStats(AudibleAccount account, string dataDirectory)
{
	private readonly string cacheFile = Path.Combine(dataDirectory, "audible-stats.json");
	private const int MAX_FINISHED_PAGES = 20;

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

	public async Task<AudibleStatsSnapshot> FetchAsync()
	{
		var api = await account.GetApiAsync();
		var today = DateTime.Today;
		var aggregates = await GetJsonAsync(api,
			"/1.0/stats/aggregates?response_groups=total_listening_stats&store=Audible" +
			$"&daily_listening_interval_duration=30&daily_listening_interval_start_date={today.AddDays(-29):yyyy-MM-dd}" +
			$"&monthly_listening_interval_duration=12&monthly_listening_interval_start_date={today.AddMonths(-11):yyyy-MM}");
		var root = aggregates.RootElement;

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

		var snapshot = new AudibleStatsSnapshot(
			DateTimeOffset.Now,
			Sums(root, "aggregated_daily_listening_stats"),
			Sums(root, "aggregated_monthly_listening_stats"),
			root.TryGetProperty("aggregated_total_listening_stats", out var total) && total.TryGetProperty("aggregated_sum", out var sum) ? sum.GetDouble() : 0,
			finished.Values.OrderByDescending(f => f.At).ToList());
		aggregates.Dispose();

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
