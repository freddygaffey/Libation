using CoreMotion;
using Foundation;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>
/// Core Motion's history: activity (stationary, walking, running, cycling, automotive) and steps over a stretch of time,
/// read after it, from what the motion coprocessor recorded anyway. Asks for Motion and Fitness permission once.
/// </summary>
public sealed class AppleListeningContext : IListeningContext
{
	private readonly CMMotionActivityManager activity = new();
	private readonly CMPedometer pedometer = new();

	public async Task<ActivityContext?> ActivityAsync(DateTimeOffset from, DateTimeOffset to)
	{
		if (!CMMotionActivityManager.IsActivityAvailable || to <= from)
			return null;
		try
		{
			var start = (NSDate)from.UtcDateTime;
			var end = (NSDate)to.UtcDateTime;
			var records = await activity.QueryActivityAsync(start, end, NSOperationQueue.MainQueue);
			var shares = Shares(records, from, to);
			int? steps = null;
			if (CMPedometer.IsStepCountingAvailable)
			{
				var data = await pedometer.QueryPedometerDataAsync(start, end);
				steps = data?.NumberOfSteps?.Int32Value;
			}
			var summary = string.Join(" · ", shares.Where(s => s.Value >= 0.05).OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value:P0}"));
			return new ActivityContext(summary.Length > 0 ? summary : "unknown", steps);
		}
		catch (Exception ex)
		{
			// Permission refused, or no history: the session is logged without it.
			Console.WriteLine($"Activity not read: {ex.Message}");
			return null;
		}
	}

	/// <summary>How much of the stretch each activity covered. Each record lasts until the next one starts.</summary>
	private static Dictionary<string, double> Shares(CMMotionActivity[]? records, DateTimeOffset from, DateTimeOffset to)
	{
		var shares = new Dictionary<string, double>();
		if (records is null || records.Length == 0)
			return shares;
		var total = (to - from).TotalSeconds;
		for (var i = 0; i < records.Length; i++)
		{
			var begins = Max((DateTimeOffset)(DateTime)records[i].StartDate, from);
			var ends = i + 1 < records.Length ? Min((DateTimeOffset)(DateTime)records[i + 1].StartDate, to) : to;
			if (ends <= begins)
				continue;
			var name = Name(records[i]);
			shares[name] = shares.GetValueOrDefault(name) + (ends - begins).TotalSeconds / total;
		}
		return shares;
	}

	private static string Name(CMMotionActivity a)
		=> a.Automotive ? "driving" : a.Cycling ? "cycling" : a.Running ? "running" : a.Walking ? "walking" : a.Stationary ? "still" : "unknown";

	private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
	private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
