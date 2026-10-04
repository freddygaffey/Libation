using CommunityToolkit.Mvvm.ComponentModel;
using System;

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
		var start = Math.Min(target, settings.TrainingStartSpeed);
		if (start >= target && (!settings.TrainingClimb || target >= settings.TrainingCeiling))
			return;
		trainingTarget = target;
		untilNextStep = TimeSpan.FromMinutes(settings.TrainingMinutes);
		ApplySpeed(start, save: false);
		OnPropertyChanged(nameof(IsTraining));
		UpdateTrainingText();
	}

	/// <summary>The listener chose a speed, or training was turned off: leave the speed where it is.</summary>
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
			var next = Speed + settings.TrainingStep;
			var top = settings.TrainingClimb ? Math.Max(target, settings.TrainingCeiling) : target;
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
