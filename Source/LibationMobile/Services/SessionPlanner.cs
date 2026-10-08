using System;
using System.Collections.Generic;

namespace LibationMobile.Services;

/// <summary>A stretch of a training session at one speed.</summary>
/// <param name="Kind">"warm-up", "push", "recover", "hold", "step"; in blind training also "probe" (an untried speed) and
/// "placebo" (the last speed again, unannounced, to see how much ratings vary by chance).</param>
/// <param name="AskAfter">Ask how well it was followed when it ends.</param>
public record SessionBlock(string Kind, double Speed, TimeSpan Length, bool AskAfter);

/// <summary>The settings a plan works from, taken from the listener's settings when a session starts.</summary>
/// <param name="Target">The speed the session centres on: the book's speed.</param>
/// <param name="StartBelow">How far below the target the warm-up starts.</param>
/// <param name="Step">The ramp's step.</param>
/// <param name="StepLength">How long each ramp step lasts.</param>
/// <param name="Ceiling">The fastest any block may go.</param>
/// <param name="Climb">For the ramp: carry on past the target, a step at a time, up to the ceiling.</param>
public record PlanSettings(double Target, double StartBelow, double Step, TimeSpan StepLength, double Ceiling, bool Climb = false);

/// <summary>
/// Plans a training session as a run of blocks, one at a time, each from what came before and how it was rated. A
/// stand-in for speechwarp's trainer, which will choose plans and their numbers from the listener's data over weeks;
/// until then the numbers here are reasonable starting points, and every block is logged for it to learn from.
/// </summary>
/// <remarks>
/// Plans: "ramp" climbs from below to the target and holds; "intervals" warms up, then alternates above and below;
/// "pyramid" steps past the target and back; "tracking" moves with each rating, up after a good one, down after a poor
/// one. Blind sessions add probes and placebos.
/// </remarks>
public class SessionPlanner(string plan, PlanSettings settings, bool blind, Random? random = null)
{
	public const double PROBE_SHARE = 0.2;
	public const double PLACEBO_SHARE = 0.1;
	private static readonly TimeSpan BlockLength = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan WarmUpLength = TimeSpan.FromMinutes(1.25);
	private const int WARM_UP_STEPS = 4;

	private readonly Random random = random ?? Random.Shared;
	private readonly List<SessionBlock> done = [];
	/// <summary>Where tracking has got to: moves with ratings.</summary>
	private double level = settings.Target;

	public string Plan { get; } = plan;
	public bool Blind { get; } = blind;
	public IReadOnlyList<SessionBlock> Done => done;

	/// <summary>The next block, given the rating of the one just played (1-5), if it was asked.</summary>
	public SessionBlock Next(int? lastRating)
	{
		var previous = done.Count > 0 ? done[^1] : null;
		if (lastRating is int rating && Plan == "tracking" && previous is not null && previous.Kind is not ("probe" or "placebo"))
			level = Clamp(previous.Speed * (rating >= 4 ? 1.05 : rating <= 2 ? 0.93 : 1.0));

		var block = Warm() ?? Special(previous) ?? Plan switch
		{
			"intervals" => Interval(),
			"pyramid" => Pyramid(),
			"tracking" => new SessionBlock("hold", level, BlockLength, true),
			_ when settings.Climb => Climb(previous),
			_ => new SessionBlock("hold", settings.Target, TimeSpan.FromMinutes(5), done.Count % 2 == 1),
		};
		done.Add(block);
		return block;
	}

	/// <summary>The warm-up: from below the target up to it, a step at a time. Every plan starts with it.</summary>
	private SessionBlock? Warm()
	{
		var start = Math.Max(1, settings.Target - settings.StartBelow);
		// The ramp climbs at the listener's own pace; the other plans warm up in about four quick steps.
		var step = Plan == "ramp" ? settings.Step : Math.Max(settings.Step, (settings.Target - start) / WARM_UP_STEPS);
		var speed = start + step * done.Count;
		if (speed >= settings.Target - 1e-6)
			return null;
		var length = Plan == "ramp" ? settings.StepLength : WarmUpLength;
		return new SessionBlock("warm-up", Clamp(speed), length, false);
	}

	/// <summary>In blind sessions, after the warm-up: now and then an untried speed, or the last speed again.</summary>
	private SessionBlock? Special(SessionBlock? previous)
	{
		if (!Blind || previous is null || previous.Kind == "warm-up")
			return null;
		var roll = random.NextDouble();
		if (roll < PLACEBO_SHARE && previous.Kind != "placebo")
			return previous with { Kind = "placebo", AskAfter = true };
		if (roll < PLACEBO_SHARE + PROBE_SHARE)
			return new SessionBlock("probe", Clamp(level * (0.8 + 0.5 * random.NextDouble())), BlockLength, true);
		return null;
	}

	private int MainBlocks()
	{
		var count = 0;
		foreach (var b in done)
			if (b.Kind is "push" or "recover" or "step")
				count++;
		return count;
	}

	/// <summary>Past the target a step at a time, to the ceiling, then holding there.</summary>
	private SessionBlock Climb(SessionBlock? previous)
	{
		var from = previous?.Kind is "step" or "warm-up" ? previous.Speed : settings.Target - settings.Step;
		return new SessionBlock("step", Clamp(Math.Max(settings.Target, from + settings.Step)), settings.StepLength, false);
	}

	/// <summary>Above the target, then below, in turns.</summary>
	private SessionBlock Interval()
		=> MainBlocks() % 2 == 0
			? new SessionBlock("push", Clamp(settings.Target * 1.15), TimeSpan.FromMinutes(2.5), true)
			: new SessionBlock("recover", Clamp(settings.Target * 0.9), TimeSpan.FromMinutes(2.5), false);

	/// <summary>Up in 5% steps to 20% over the target, back down, then again.</summary>
	private SessionBlock Pyramid()
	{
		int[] steps = [5, 10, 15, 20, 15, 10, 5, 0];
		var percent = steps[MainBlocks() % steps.Length];
		return new SessionBlock("step", Clamp(settings.Target * (1 + percent / 100.0)), TimeSpan.FromMinutes(2), percent is 20 or 0);
	}

	private double Clamp(double speed) => Math.Clamp(Math.Round(speed * 10) / 10, 1, Math.Min(10, settings.Ceiling));
}
