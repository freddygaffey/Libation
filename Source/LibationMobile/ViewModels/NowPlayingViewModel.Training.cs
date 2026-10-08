using CommunityToolkit.Mvvm.ComponentModel;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibationMobile.ViewModels;

/// <summary>
/// Training mode: each listening session starts below the book's speed and climbs to it a step at a time, so the ear
/// warms up. People adapt to fast speech within a few minutes, and the adaptation fades between sessions (see
/// speechwarp's docs/research-high-speed.md). Optionally it keeps climbing past the book's speed and keeps what it reaches.
/// </summary>
public partial class NowPlayingViewModel
{
	private DateTime lastTrainingTick = DateTime.MinValue;

	[ObservableProperty]
	private string trainingText = "";

	public bool IsTraining => planner is not null;

	/// <summary>The profiles, for the picker in the speed box.</summary>
	public IReadOnlyList<ProfileChoice> Profiles => settings.Profiles.Select(p => new ProfileChoice(p, p.Name == settings.ActiveProfile)).ToList();
	public string ProfileText => settings.ActiveProfile ?? "Custom";
	public bool IsCustomProfile => settings.ActiveProfile is null;

	/// <summary>Where the warm-up starts, as set in Settings, Training, and from the player's training sheet.</summary>
	public string TrainingStartText => settings.TrainingFromBelow
		? $"{settings.TrainingStartBelow:0.0#}× below the book's speed"
		: $"At {settings.TrainingStartSpeed:0.0}×";

	public bool IsStartBelow => settings.TrainingFromBelow;
	public bool IsStartAtSpeed => !settings.TrainingFromBelow;

	/// <summary>"below": a gap under the book's speed; "speed": a set speed.</summary>
	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void SetTrainingStartMode(string mode)
	{
		settings.TrainingFromBelow = mode == "below";
		OnTrainingStartChanged();
	}

	/// <summary>"+" or "-": the gap by 0.25×, or the set speed by 0.1×, whichever is in use.</summary>
	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void NudgeTrainingStart(string direction)
	{
		var up = direction == "+";
		if (settings.TrainingFromBelow)
			settings.TrainingStartBelow += up ? 0.25f : -0.25f;
		else
			settings.TrainingStartSpeed = (float)Math.Round(settings.TrainingStartSpeed + (up ? 0.1f : -0.1f), 1);
		OnTrainingStartChanged();
	}

	private void OnTrainingStartChanged()
	{
		OnPropertyChanged(nameof(TrainingStartText));
		OnPropertyChanged(nameof(IsStartBelow));
		OnPropertyChanged(nameof(IsStartAtSpeed));
	}

	public bool IsSpeedy => settings.UseNonlinearSpeed && AudioBackend.NonlinearAvailable;
	public bool IsOriginal => !IsSpeedy;

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void SetMethod(string method)
	{
		settings.UseNonlinearSpeed = method == "speedy";
		OnPropertyChanged(nameof(IsSpeedy));
		OnPropertyChanged(nameof(IsOriginal));
	}

	/// <summary>Refresh the mode panel's figures, which Settings may have changed.</summary>
	private void RefreshMode()
	{
		OnPropertyChanged(nameof(Profiles));
		OnPropertyChanged(nameof(ProfileText));
		OnPropertyChanged(nameof(IsCustomProfile));
		OnPropertyChanged(nameof(TrainingStartText));
		OnPropertyChanged(nameof(IsSpeedy));
		OnPropertyChanged(nameof(IsOriginal));
	}

	/// <summary>Switch profile from the player: its settings, and its speed for this book.</summary>
	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void ChooseProfile(ProfileChoice choice)
	{
		settings.ApplyProfile(choice.Profile);
		LogEvent("profile", detail: choice.Profile.Name);
		speedSource = "profile";
		Speed = choice.Profile.Speed;
		RefreshMode();
	}

	#region Modes: you choose, training, blind training

	/// <summary>"normal" (you set the speed), "training" (a session plan sets it, shown) or "blind" (it sets it, hidden).</summary>
	public string ListeningMode => !settings.Training ? "normal" : settings.BlindTraining ? "blind" : "training";
	public bool IsNormalMode => ListeningMode == "normal";
	public bool IsTrainingMode => ListeningMode == "training";
	public bool IsBlindMode => ListeningMode == "blind";
	public bool ShowsSpeed => !IsBlindMode;

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void SetListeningMode(string mode)
	{
		var wasTraining = IsTraining;
		LogEvent("mode", detail: mode);
		settings.Training = mode != "normal";
		settings.BlindTraining = mode == "blind";
		if (mode == "normal")
		{
			// Back to the speed the listener set for this book.
			if (wasTraining && settings.GetBookSpeed(Book.Id) is float own)
				ApplySpeed(own, save: false);
			StopTraining();
		}
		else
		{
			lastTrainingTick = DateTime.MinValue;
			StopTraining();
			if (sessionStarted is not null)
				StartTraining();
		}
		RefreshModeDisplay();
	}

	/// <summary>The player's training switch: training on (shown or blind, as last chosen), or off.</summary>
	public bool TrainingEnabled
	{
		get => settings.Training;
		set
		{
			if (value != settings.Training)
				SetListeningMode(value ? (settings.BlindTraining ? "blind" : "training") : "normal");
		}
	}

	public bool IsPlanRamp => settings.TrainingPlan == "ramp";
	public bool IsPlanIntervals => settings.TrainingPlan == "intervals";
	public bool IsPlanPyramid => settings.TrainingPlan == "pyramid";
	public bool IsPlanTracking => settings.TrainingPlan == "tracking";

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void SetPlan(string plan)
	{
		settings.TrainingPlan = plan;
		LogEvent("plan", detail: plan);
		if (IsTraining)
		{
			StopTraining();
			lastTrainingTick = DateTime.MinValue;
			if (sessionStarted is not null)
				StartTraining();
		}
		RefreshModeDisplay();
	}

	private void RefreshModeDisplay()
	{
		foreach (var name in new[] { nameof(ListeningMode), nameof(IsNormalMode), nameof(IsTrainingMode), nameof(IsBlindMode), nameof(ShowsSpeed), nameof(ShowsTargetRate), nameof(ShowsSpeedRail),
			nameof(TrainingEnabled), nameof(IsTraining), nameof(IsPlanRamp), nameof(IsPlanIntervals), nameof(IsPlanPyramid), nameof(IsPlanTracking),
			nameof(SpeedText), nameof(RemainingText), nameof(ChapterRemainingText), nameof(ScrubberRemainingText), nameof(ScrubberDetailText) })
			OnPropertyChanged(name);
		UpdateSyllableRate();
		UpdateTrainingText();
		UpdateMediaSession();
	}

	#endregion

	#region Session plans

	private SessionPlanner? planner;
	private SessionBlock? block;
	private int blockNumber;
	private TimeSpan blockLeft;
	private TimeSpan blockSpent;
	private double blockSyllables;
	private int? lastBlockRating;

	/// <summary>A block just ended that asks how well it was followed. The card goes after 20 seconds unanswered.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsAskingBlock))]
	private SessionBlock? blockToRate;

	private DateTime blockAskedAt;
	private (SessionBlock Block, double? Syllables)? blockToLog;
	private static readonly TimeSpan BlockQuestionLasts = TimeSpan.FromSeconds(20);

	public bool IsAskingBlock => BlockToRate is not null;

	private void StartTraining()
	{
		// A short break carries on the same session; a longer one starts the warm-up again.
		var resuming = DateTime.UtcNow - lastTrainingTick < TimeSpan.FromMinutes(settings.TrainingRestartMinutes);
		if (!settings.Training || resuming && planner is not null)
			return;
		var target = settings.GetBookSpeed(Book.Id) ?? Speed;
		var below = settings.TrainingFromBelow ? settings.TrainingStartBelow : Math.Max(0, target - settings.TrainingStartSpeed);
		planner = new SessionPlanner(settings.TrainingPlan,
			new PlanSettings(target, below, settings.TrainingStep, TimeSpan.FromMinutes(settings.TrainingMinutes),
				settings.TrainingClimb ? Math.Max(target, settings.TrainingCeiling) : 10, settings.TrainingClimb),
			settings.BlindTraining);
		blockNumber = 0;
		NextBlock();
		OnPropertyChanged(nameof(IsTraining));
	}

	private void NextBlock()
	{
		if (planner is null)
			return;
		block = planner.Next(lastBlockRating);
		lastBlockRating = null;
		blockNumber++;
		blockLeft = block.Length;
		blockSpent = TimeSpan.Zero;
		blockSyllables = 0;
		speedSource = $"plan: {block.Kind}";
		ApplySpeed(block.Speed, save: false);
		UpdateTrainingText();
	}

	/// <summary>The listener changed the speed during training: it holds until the next block, which follows the plan.</summary>
	private void AdjustTraining(double speed) => ApplySpeed(speed, save: false);

	/// <summary>Training was turned off: leave the speed where it is.</summary>
	private void StopTraining()
	{
		FlushBlockLog();
		planner = null;
		block = null;
		BlockToRate = null;
		OnPropertyChanged(nameof(IsTraining));
		UpdateTrainingText();
	}

	/// <summary>Called with each stretch of listening.</summary>
	private void TrainingTick(TimeSpan listened)
	{
		lastTrainingTick = DateTime.UtcNow;
		if (BlockToRate is not null && DateTime.UtcNow - blockAskedAt > BlockQuestionLasts)
		{
			BlockToRate = null;
			FlushBlockLog();
		}
		if (block is null)
			return;
		if (!settings.Training)
		{
			StopTraining();
			return;
		}
		blockLeft -= listened;
		blockSpent += listened;
		if (player.SourceSyllablesPerSecond is double rate)
			blockSyllables += rate * (listened * Speed).TotalSeconds;
		if (blockLeft <= TimeSpan.Zero)
		{
			var syllables = blockSpent > TimeSpan.Zero && blockSyllables > 0 ? blockSyllables / blockSpent.TotalSeconds : (double?)null;
			LogActivity(DateTimeOffset.Now - blockSpent, DateTimeOffset.Now, $"block {blockNumber}");
			FlushBlockLog();
			blockToLog = (block, syllables);
			if (block.AskAfter)
			{
				BlockToRate = block;
				blockAskedAt = DateTime.UtcNow;
			}
			else
				FlushBlockLog();
			NextBlock();
			return;
		}
		UpdateTrainingText();
	}

	/// <summary>How well the block just played was followed: steers tracking, and goes in the log.</summary>
	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void RateBlock(string score)
	{
		if (int.TryParse(score, out var rating))
		{
			lastBlockRating = rating;
			FlushBlockLog(rating);
		}
		BlockToRate = null;
	}

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void SkipBlock()
	{
		BlockToRate = null;
		FlushBlockLog();
	}

	private void FlushBlockLog(int? rating = null)
	{
		if (blockToLog is not { } done || planner is null)
			return;
		blockToLog = null;
		Experiments?.Add(new TrainingBlockLog(DateTimeOffset.Now, Book.Id, Title, planner.Plan, done.Block.Kind, done.Block.Speed,
			done.Syllables, rating, planner.Blind, settings.ActiveProfile));
	}

	private void UpdateTrainingText()
	{
		if (block is null || planner is null)
		{
			TrainingText = "";
			return;
		}
		var left = $"{(int)Math.Max(0, blockLeft.TotalMinutes)}:{Math.Max(0, blockLeft.Seconds):00}";
		var plan = planner.Plan switch { "intervals" => "Intervals", "pyramid" => "Pyramid", "tracking" => "Tracking", _ => "Ramp" };
		// Blind: nothing that gives the speed away, not even the kind of block.
		TrainingText = planner.Blind
			? $"Blind training · block {blockNumber} · {left} left"
			: $"{plan} · {Kind(block.Kind)} · {Speed:0.0}× · {left} left";
	}

	private static string Kind(string kind) => kind switch
	{
		"warm-up" => "Warm-up",
		"push" => "Push",
		"recover" => "Recover",
		"step" => "Step",
		_ => "Hold",
	};

	#endregion
}
