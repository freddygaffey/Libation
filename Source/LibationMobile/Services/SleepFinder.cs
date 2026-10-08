using System;
using System.Collections.Generic;
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

	/// <summary>
	/// When the listener usually falls asleep and wakes, in minutes after midnight, from the last 30 days of sleep in
	/// Apple Health: the middle of each, with half an hour's margin either side. Null with fewer than five nights.
	/// iOS does not let apps read the Sleep schedule set in the Health app, so this is the nearest thing to it.
	/// </summary>
	public static async Task<(int From, int Until)?> UsualSleepAsync(IListeningContext context)
	{
		var spans = (await context.SleepAsync(DateTimeOffset.Now.AddDays(-30), DateTimeOffset.Now))
			.Where(s => s.To - s.From >= TimeSpan.FromHours(3)).ToList();
		if (spans.Count < 5)
			return null;
		// Times before noon count as after midnight, so 23:30 and 00:30 average to midnight, not to noon.
		static int Minute(DateTimeOffset t) { var l = t.ToLocalTime(); var m = l.Hour * 60 + l.Minute; return m < 720 ? m + 1440 : m; }
		static int Middle(List<int> values) { values.Sort(); return values[values.Count / 2]; }
		var asleep = Middle(spans.Select(s => Minute(s.From)).ToList()) - 30;
		var awake = Middle(spans.Select(s => Minute(s.To)).ToList()) + 30;
		static int Round(int m) => ((int)Math.Round(m / 30.0) * 30 % 1440 + 1440) % 1440;
		return (Round(asleep), Round(awake));
	}

	/// <summary>Whether a time of day falls between two others, in minutes after midnight; the span may cross midnight.</summary>
	public static bool IsWithin(DateTime local, int fromMinute, int untilMinute)
	{
		var now = local.Hour * 60 + local.Minute;
		return fromMinute <= untilMinute ? now >= fromMinute && now < untilMinute : now >= fromMinute || now < untilMinute;
	}

	/// <summary>A time of day in minutes after midnight, as "9 p.m." or "6:30 a.m." in the listener's own format.</summary>
	public static string TimeOfDay(int minute)
		=> DateTime.Today.AddMinutes(minute).ToString(minute % 60 == 0 ? "h tt" : "h:mm tt", System.Globalization.CultureInfo.CurrentCulture).ToLowerInvariant();
}
