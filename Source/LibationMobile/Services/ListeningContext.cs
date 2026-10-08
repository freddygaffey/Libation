using System;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>What the listener was doing over a stretch of time: shares of each activity, and steps.</summary>
/// <param name="Summary">Such as "walking 62% · stationary 38%".</param>
public record ActivityContext(string Summary, int? Steps);

/// <summary>
/// The phone's own record of what the listener was doing, asked for after the fact, so no sensor runs to collect it.
/// On iOS, the motion coprocessor classifies activity and counts steps all the time and keeps days of history.
/// </summary>
public interface IListeningContext
{
	Task<ActivityContext?> ActivityAsync(DateTimeOffset from, DateTimeOffset to);
}

public static class ListeningContext
{
	/// <summary>Set by the platform head. Null where there is none.</summary>
	public static IListeningContext? Platform { get; set; }
}
