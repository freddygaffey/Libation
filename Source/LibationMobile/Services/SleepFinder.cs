using System;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>
/// Finds, after the fact, where in a book the listener fell asleep, from Apple Health's sleep record. A watch's app
/// (Garmin Connect, or an Apple Watch) writes the sleep there once the watch syncs, usually after waking, so this runs
/// when the app opens and when a book's history is shown, over the last few days of sessions.
/// </summary>
public static class SleepFinder
{
	private static readonly TimeSpan LookBack = TimeSpan.FromDays(3);

	/// <summary>
	/// Sleep starting within a session marks the place in it; a sleep found by auto-pause is replaced, as the watch knows
	/// better. Looks over the last few days, or with <paramref name="everything"/>, over every session logged, as on
	/// first connecting: Apple Health keeps all the sleep ever written to it.
	/// </summary>
	public static async Task<int> RefineAsync(ListeningLog log, IListeningContext context, bool everything = false)
	{
		var since = everything ? DateTimeOffset.MinValue : DateTimeOffset.Now - LookBack;
		var found = 0;
		foreach (var session in log.Sessions.Where(s => s.Ended >= since && s.AsleepSource != "health"))
		{
			var spans = await context.SleepAsync(session.Started, session.Ended);
			// Sleep begun before the session (listening in the night, after waking) says nothing about this one.
			if (spans.FirstOrDefault(s => s.From > session.Started && s.From < session.Ended) is not { } sleep)
				continue;
			log.Add(session with { AsleepAt = sleep.From, AsleepPosition = session.PositionAt(sleep.From), AsleepSource = "health" });
			found++;
		}
		return found;
	}

	/// <summary>
	/// Whether the listener is probably asleep, after <paramref name="idle"/> without a touch. With headphones that report
	/// head movement, the head must have been still as long; otherwise the phone must have lain still for most of it;
	/// with neither known, no touch for that long is taken as enough, since a fade-out comes first and a touch stops it.
	/// </summary>
	public static bool LooksAsleep(TimeSpan idle, TimeSpan threshold, TimeSpan? headStill, double? phoneStillShare)
	{
		if (idle < threshold)
			return false;
		if (headStill is TimeSpan head)
			return head >= threshold;
		return phoneStillShare is not double still || still >= 0.9;
	}

	/// <summary>From 9 p.m. to 7 a.m.</summary>
	public static bool IsNight(DateTime local) => local.Hour >= 21 || local.Hour < 7;
}
