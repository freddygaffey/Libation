using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace LibationMobile.Services;

/// <summary>Everything that shapes what is heard, at a moment: to tie a rating or a trial to its settings.</summary>
/// <param name="HeardPause">The longest pause heard, in seconds of listening (the book's pause cap over the speed). 0 for none.</param>
/// <param name="Floor">The speed floor in force, as a fraction of the speed.</param>
/// <param name="SyllablesPerSecond">Syllables a second heard, when measured.</param>
public record ListeningSettings(double Speed, bool Speedy, double Nonlinearity, double HeardPause, double Floor, double RhythmGap,
	string? Profile, double? SyllablesPerSecond);

/// <summary>"How well did you follow?" after a session, 1 (lost) to 5 (every word).</summary>
public record FollowRating(DateTimeOffset At, string BookId, string Title, int Follow, ListeningSettings Settings);

/// <summary>
/// One blind trial: the same book heard with two values of one setting, in random order, each rated, then compared.
/// </summary>
/// <param name="Preferred">-1 the first part was easier, 1 the second, 0 the same.</param>
public record ExperimentTrial(DateTimeOffset At, string BookId, string Title, double Speed, string Parameter,
	double FirstValue, double SecondValue, int FirstFollow, int SecondFollow, int Preferred, double? SyllablesPerSecond);

/// <summary>One block of a training session: what plan and kind, the speed, syllables a second, and the rating if asked.</summary>
/// <param name="Blind">Played in blind training, with the speed hidden.</param>
public record TrainingBlockLog(DateTimeOffset At, string BookId, string Title, string Plan, string Kind, double Speed,
	double? SyllablesPerSecond, int? Rating, bool Blind, string? Profile);

/// <summary>
/// The state of things as a session starts or ends, for the trainer to learn what helps: time of day, rest since the
/// last session, how the session went (speeds, syllables a second, skips back) and the mode and settings in force.
/// </summary>
/// <param name="HoursSinceLastSession">Since the last session of any book ended; null for the first one.</param>
/// <param name="Minutes">Minutes listened in the session; 0 at its start.</param>
/// <param name="SkipsBack">Skips back in the session: a sign of something missed.</param>
/// <param name="Mode">"normal", "training" or "blind".</param>
/// <param name="Activity">What the listener was doing, from the phone's motion record, when known.</param>
public record SessionContext(int HourOfDay, string DayOfWeek, double? HoursSinceLastSession, double Minutes, double StartSpeed,
	double MinSpeed, double MaxSpeed, double? MeanSyllablesPerSecond, int SkipsBack, int SpeedChanges, string? Route, string Mode,
	string? Plan, int BlocksDone, double? BookProgress, ListeningSettings Settings, string? Activity = null);

/// <summary>
/// A question at the start or end of a session, 0 to 4: at the start how alert (0 falling asleep, 4 wide awake), at the
/// end how well it was followed (0 lost it, 4 every word). Kept even when unanswered, for the context.
/// </summary>
/// <param name="Moment">"start" or "end".</param>
/// <param name="Answer">0 to 4; null when skipped or not answered.</param>
/// <param name="AnsweredBy">"screen", "voice", or "none".</param>
public record SessionCheckIn(DateTimeOffset At, string BookId, string Title, string Moment, int? Answer, string AnsweredBy,
	SessionContext Context);

/// <summary>A setting a trial can vary, and the values worth comparing.</summary>
public record ExperimentParameter(string Key, string Name, double[] Values, Func<double, string> Describe);

/// <summary>
/// The listener's own research into very high speeds, where the published research stops at about 4x: ratings after
/// sessions and blind A/B trials, kept in experiments.json, and what they add up to.
/// </summary>
public class Experiments
{
	public static readonly IReadOnlyList<ExperimentParameter> Parameters =
	[
		new("heardPause", "Pauses you hear", [0, 0.015, 0.03], v => v <= 0 ? "pauses left as they are" : $"pauses cut to {v * 1000:0} ms heard"),
		new("floor", "Hardest sounds", [0, 0.5, 0.7], v => v <= 0 ? "no floor" : $"hardest sounds at least {v:P0} of the speed"),
		new("method", "Speed-up method", [0, 1], v => v >= 1 ? "Speedy" : "Original (even)"),
		new("nonlinearity", "Speedy strength", [0.5, 1], v => $"Speedy at {v:P0}"),
		new("rhythm", "Rhythm gaps", [0, 0.015], v => v <= 0 ? "no rhythm gaps" : $"rhythm gaps of {v * 1000:0} ms"),
	];

	/// <summary>Speeds are grouped by whole numbers for the results: 4 to 5, 5 to 6, and so on.</summary>
	public static int Band(double speed) => (int)Math.Floor(speed);

	private readonly string path;
	private readonly Lock locker = new();
	private Store store;

	internal class Store
	{
		public List<FollowRating> Ratings { get; set; } = [];
		public List<ExperimentTrial> Trials { get; set; } = [];
		public List<TrainingBlockLog> Blocks { get; set; } = [];
		public List<SessionCheckIn> CheckIns { get; set; } = [];
	}

	public Experiments(string dataDirectory)
	{
		path = Path.Combine(dataDirectory, "experiments.json");
		store = Load(path);
	}

	public IReadOnlyList<FollowRating> Ratings { get { lock (locker) return store.Ratings.ToList(); } }
	public IReadOnlyList<ExperimentTrial> Trials { get { lock (locker) return store.Trials.ToList(); } }

	public void Add(FollowRating rating)
	{
		lock (locker)
		{
			store.Ratings.Add(rating);
			Save();
		}
	}

	public IReadOnlyList<TrainingBlockLog> Blocks { get { lock (locker) return store.Blocks.ToList(); } }

	public void Add(TrainingBlockLog block)
	{
		lock (locker)
		{
			store.Blocks.Add(block);
			Save();
		}
	}

	public IReadOnlyList<SessionCheckIn> CheckIns { get { lock (locker) return store.CheckIns.ToList(); } }

	public void Add(SessionCheckIn checkIn)
	{
		lock (locker)
		{
			store.CheckIns.Add(checkIn);
			Save();
		}
	}

	public void Add(ExperimentTrial trial)
	{
		lock (locker)
		{
			store.Trials.Add(trial);
			Save();
		}
	}

	/// <summary>
	/// What to test next at this speed: the setting with the fewest trials in its speed band, and two of its values, the
	/// pair compared least, in random order. Method trials are skipped where Speedy is not available.
	/// </summary>
	public (ExperimentParameter Parameter, double First, double Second) NextTrial(double speed, bool speedyAvailable)
	{
		var band = Band(speed);
		var trials = Trials.Where(t => Band(t.Speed) == band).ToList();
		var candidates = Parameters.Where(p => speedyAvailable || p.Key is not ("method" or "nonlinearity")).ToList();
		var parameter = candidates.OrderBy(p => trials.Count(t => t.Parameter == p.Key)).ThenBy(_ => Random.Shared.Next()).First();
		var pairs = (from a in parameter.Values from b in parameter.Values where a < b select (a, b)).ToList();
		var (low, high) = pairs.OrderBy(pair => trials.Count(t => t.Parameter == parameter.Key
			&& Math.Min(t.FirstValue, t.SecondValue) == pair.a && Math.Max(t.FirstValue, t.SecondValue) == pair.b))
			.ThenBy(_ => Random.Shared.Next()).First();
		return Random.Shared.Next(2) == 0 ? (parameter, low, high) : (parameter, high, low);
	}

	/// <summary>How a value of a setting has done: comparisons won and lost, and the average follow rating with it.</summary>
	public record ValueResult(double Value, string Description, int Won, int Lost, int Tied, double? MeanFollow, int Heard);

	public record ParameterResult(ExperimentParameter Parameter, int Band, IReadOnlyList<ValueResult> Values, string? Verdict);

	/// <summary>Each setting's results in each speed band where it has been tried.</summary>
	public IReadOnlyList<ParameterResult> Results()
	{
		var results = new List<ParameterResult>();
		foreach (var group in Trials.GroupBy(t => (t.Parameter, Band: Band(t.Speed))).OrderBy(g => g.Key.Band))
		{
			var parameter = Parameters.FirstOrDefault(p => p.Key == group.Key.Parameter);
			if (parameter is null)
				continue;
			var values = parameter.Values.Select(v =>
			{
				int won = 0, lost = 0, tied = 0;
				var follows = new List<int>();
				foreach (var t in group)
				{
					var isFirst = t.FirstValue == v;
					var isSecond = t.SecondValue == v;
					if (!isFirst && !isSecond)
						continue;
					follows.Add(isFirst ? t.FirstFollow : t.SecondFollow);
					if (t.Preferred == 0) tied++;
					else if (t.Preferred == -1 == isFirst) won++;
					else lost++;
				}
				return new ValueResult(v, parameter.Describe(v), won, lost, tied, follows.Count > 0 ? follows.Average() : null, follows.Count);
			}).Where(v => v.Heard > 0).ToList();
			results.Add(new ParameterResult(parameter, group.Key.Band, values, Verdict(values)));
		}
		return results;
	}

	/// <summary>A value is recommended once it has been tried at least three times and leads on both preference and rating.</summary>
	private static string? Verdict(IReadOnlyList<ValueResult> values)
	{
		var tried = values.Where(v => v.Heard >= 3).ToList();
		if (tried.Count < 2)
			return null;
		var best = tried.OrderByDescending(v => v.Won - v.Lost).ThenByDescending(v => v.MeanFollow).First();
		var runnerUp = tried.Where(v => v != best).OrderByDescending(v => v.Won - v.Lost).ThenByDescending(v => v.MeanFollow).First();
		return best.Won - best.Lost > runnerUp.Won - runnerUp.Lost && best.MeanFollow >= runnerUp.MeanFollow
			? $"Best so far: {best.Description}."
			: "No clear winner yet.";
	}

	/// <summary>How well sessions were followed against syllables a second heard, in bands of 2: the listener's ceiling.</summary>
	public IReadOnlyList<(int From, double MeanFollow, int Count)> FollowBySyllables()
		=> Ratings.Where(r => r.Settings.SyllablesPerSecond is > 0)
			.Select(r => (Band: (int)(r.Settings.SyllablesPerSecond!.Value / 2) * 2, r.Follow))
			.Concat(Trials.Where(t => t.SyllablesPerSecond is > 0).SelectMany(t => new[] {
				(Band: (int)(t.SyllablesPerSecond!.Value / 2) * 2, Follow: t.FirstFollow),
				(Band: (int)(t.SyllablesPerSecond!.Value / 2) * 2, Follow: t.SecondFollow) }))
			.Concat(Blocks.Where(b => b.Rating is int && b.SyllablesPerSecond is > 0)
				.Select(b => (Band: (int)(b.SyllablesPerSecond!.Value / 2) * 2, Follow: b.Rating!.Value)))
			.GroupBy(x => x.Band)
			.OrderBy(g => g.Key)
			.Select(g => (g.Key, g.Average(x => (double)x.Follow), g.Count()))
			.ToList();

	private void Save()
	{
		var temp = path + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(store, ExperimentsJsonContext.Default.Store));
		File.Move(temp, path, overwrite: true);
	}

	private static Store Load(string path)
	{
		try
		{
			if (File.Exists(path))
				return JsonSerializer.Deserialize(File.ReadAllText(path), ExperimentsJsonContext.Default.Store) ?? new();
		}
		catch (JsonException)
		{
		}
		return new();
	}
}

[JsonSerializable(typeof(Experiments.Store))]
internal partial class ExperimentsJsonContext : JsonSerializerContext
{
}
