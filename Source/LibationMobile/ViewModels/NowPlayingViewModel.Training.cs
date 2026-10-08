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
	/// <summary>The speed the climb is heading for, while one is under way.</summary>
	private double? trainingTarget;
	private TimeSpan untilNextStep;
	private DateTime lastTrainingTick = DateTime.MinValue;

	[ObservableProperty]
	private string trainingText = "";

	public bool IsTraining => trainingTarget is not null;

	/// <summary>The profiles, for the picker in the speed box.</summary>
	public IReadOnlyList<ProfileChoice> Profiles => settings.Profiles.Select(p => new ProfileChoice(p, p.Name == settings.ActiveProfile)).ToList();
	public string ProfileText => settings.ActiveProfile ?? "Custom";
	public bool IsCustomProfile => settings.ActiveProfile is null;

	/// <summary>The ramp's starting gap, adjustable from the player's mode panel.</summary>
	public string TrainingStartText => settings.TrainingFromBelow
		? $"Start {settings.TrainingStartBelow:0.0#}× below"
		: $"Start at {settings.TrainingStartSpeed:0.0}×";

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void TrainingStartLower() => NudgeTrainingStart(+0.25f);

	[CommunityToolkit.Mvvm.Input.RelayCommand]
	private void TrainingStartHigher() => NudgeTrainingStart(-0.25f);

	/// <summary>Further below (a gentler warm-up) or nearer the book's speed. Switches to starting below if set to a fixed speed.</summary>
	private void NudgeTrainingStart(float further)
	{
		if (!settings.TrainingFromBelow)
			settings.TrainingFromBelow = true;
		else
			settings.TrainingStartBelow += further;
		OnPropertyChanged(nameof(TrainingStartText));
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
		Speed = choice.Profile.Speed;
		RefreshMode();
	}

	/// <summary>
	/// The player's training switch, the same setting as in Settings. On starts a warm-up now (or at the next play);
	/// off part-way up goes straight to the book's speed.
	/// </summary>
	public bool TrainingEnabled
	{
		get => settings.Training;
		set
		{
			if (value == settings.Training)
				return;
			settings.Training = value;
			OnPropertyChanged();
			if (value)
			{
				lastTrainingTick = DateTime.MinValue;
				if (sessionStarted is not null)
					StartTraining();
			}
			else if (trainingTarget is double target)
			{
				if (Speed < target)
					ApplySpeed(target, save: false);
				StopTraining();
			}
		}
	}

	private void StartTraining()
	{
		// A short break carries on the same climb; a longer one starts the warm-up again.
		var resuming = DateTime.UtcNow - lastTrainingTick < TimeSpan.FromMinutes(settings.TrainingRestartMinutes);
		if (!settings.Training || resuming)
			return;
		var target = settings.GetBookSpeed(Book.Id) ?? Speed;
		var start = settings.TrainingFromBelow
			? Math.Max(1, target - settings.TrainingStartBelow)
			: Math.Min(target, settings.TrainingStartSpeed);
		if (start >= target && (!settings.TrainingClimb || target >= settings.TrainingCeiling))
			return;
		trainingTarget = target;
		untilNextStep = TimeSpan.FromMinutes(settings.TrainingMinutes);
		ApplySpeed(start, save: false);
		OnPropertyChanged(nameof(IsTraining));
		UpdateTrainingText();
	}

	/// <summary>
	/// The listener changed the speed during a warm-up: the climb carries on from the new speed, with a full step's
	/// time before the next rise. The book's own speed, where the climb is heading, is left as it was.
	/// </summary>
	private void AdjustTraining(double speed)
	{
		ApplySpeed(speed, save: false);
		untilNextStep = TimeSpan.FromMinutes(settings.TrainingMinutes);
		UpdateTrainingText();
	}

	/// <summary>Training was turned off, or the climb is over: leave the speed where it is.</summary>
	private void StopTraining()
	{
		if (trainingTarget is null)
			return;
		trainingTarget = null;
		OnPropertyChanged(nameof(IsTraining));
		UpdateTrainingText();
	}

	/// <summary>Called with each stretch of listening.</summary>
	private void TrainingTick(TimeSpan listened)
	{
		lastTrainingTick = DateTime.UtcNow;
		if (trainingTarget is not double target)
			return;
		if (!settings.Training)
		{
			StopTraining();
			return;
		}
		untilNextStep -= listened;
		if (untilNextStep <= TimeSpan.Zero)
		{
			untilNextStep = TimeSpan.FromMinutes(settings.TrainingMinutes);
			var top = settings.TrainingClimb ? Math.Max(target, settings.TrainingCeiling) : target;
			// Already there, or set past it by hand: the climb is done, at the listener's speed.
			if (Speed >= top - 0.001)
			{
				StopTraining();
				return;
			}
			var next = Speed + settings.TrainingStep;
			if (next >= top - 0.001)
			{
				ApplySpeed(top, save: top > target + 0.001);
				StopTraining();
				return;
			}
			// Past the book's speed, a climb is kept: next time starts from there.
			ApplySpeed(next, save: next > target + 0.001);
		}
		UpdateTrainingText();
	}

	private void UpdateTrainingText()
	{
		if (trainingTarget is not double target)
		{
			TrainingText = "";
			return;
		}
		// Where the climb really ends: past the book's speed to the ceiling when climbing.
		var top = settings.TrainingClimb ? Math.Max(target, settings.TrainingCeiling) : target;
		var wait = untilNextStep.TotalMinutes >= 1
			? $"{(int)untilNextStep.TotalMinutes} min {untilNextStep.Seconds} s"
			: $"{Math.Max(0, untilNextStep.Seconds)} s";
		TrainingText = $"{Speed:0.0}× now, climbing to {top:0.0}×. Next +{settings.TrainingStep:0.0#}× in {wait}.";
	}
}
