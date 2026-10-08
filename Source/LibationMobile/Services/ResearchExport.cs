using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibationMobile.Services;

/// <summary>Everything collected for the listener's research, in one file.</summary>
public record ResearchFile(
	string About,
	DateTimeOffset Exported,
	IReadOnlyList<ListeningSession> Sessions,
	IReadOnlyList<FollowRating> Ratings,
	IReadOnlyList<ExperimentTrial> Trials,
	IReadOnlyList<TrainingBlockLog> Blocks,
	IReadOnlyList<ListeningEvent> Events,
	IReadOnlyDictionary<string, double>? AudibleDailyMinutes,
	IReadOnlyList<SpeedProfile> Profiles);

/// <summary>
/// The listener's research data as JSON for analysis or an AI: sessions, ratings, blind trials, training blocks, the
/// event log, Audible's daily listening and the profiles. Field meanings are in About.
/// </summary>
public static class ResearchExport
{
	private const string ABOUT =
		"Speed-listening research data from one listener, exported from Libation. Speeds are playback multiples (2 = twice as fast); " +
		"syllablesPerSecond is measured from the audio and is what was heard (book rate times speed). " +
		"sessions: each listening session (bookSeconds heard, spentSeconds of real time, syllables heard, marks of position every 5 minutes). " +
		"ratings: 'how well did you follow' 1 (lost) to 5 (every word) after a session, with the settings in force. " +
		"trials: blind A/B comparisons of one setting (parameter) at two values, each part rated 1-5; preferred -1 first part, 1 second, 0 same. " +
		"blocks: training session blocks (plan, kind: warm-up/push/recover/hold/step, and in blind training probe = untried speed, " +
		"placebo = last speed repeated unannounced), with the rating if asked. " +
		"events: play/pause (value = pause length before play, seconds), speed (value = new speed, detail = who: you/plan/siri/widget/profile), " +
		"skip (value = seconds, negative = back; a skip back soon after fast listening suggests something was missed), seek, chapter, mode, " +
		"profile, plan, route (where the sound went), activity (detail = motion mix such as 'walking 62% · still 38%', value = steps). " +
		"Position and speed at any time follow from the events. audibleDailyMinutes: listening per day in Audible's own apps, from Audible. " +
		"profiles: the speed profiles' rules (heardPause = longest pause heard in seconds; floor = least speed of any part, as a fraction).";

	private static readonly ResearchExportJsonContext Context = new(new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	});

	public static string ToJson(ListeningLog log, Experiments experiments, ListeningEvents events, AudibleStatsSnapshot? audible, IReadOnlyList<SpeedProfile> profiles)
		=> JsonSerializer.Serialize(new ResearchFile(ABOUT, DateTimeOffset.Now, log.Sessions, experiments.Ratings, experiments.Trials,
			experiments.Blocks, events.All(), audible?.DailyMs.ToDictionary(d => d.Key, d => Math.Round(d.Value / 60000, 1)), profiles),
			Context.ResearchFile);
}

[JsonSerializable(typeof(ResearchFile))]
internal partial class ResearchExportJsonContext : JsonSerializerContext
{
}
