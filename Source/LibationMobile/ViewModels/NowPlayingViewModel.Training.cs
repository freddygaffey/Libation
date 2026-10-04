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
	/// <summary>A pause shorter than this carries on the same climb; a longer one starts the warm-up again.</summary>
	private static readonly TimeSpan TrainingRestartAfter = TimeSpan.FromMinutes(10);

	/// <summary>The speed the climb is heading for, while one is under way.</summary>
	private double? trainingTarget;
	private TimeSpan untilNextStep;
	private DateTime lastTrainingTick = DateTime.MinValue;

	[ObservableProperty]
	private string trainingText = "";

	public bool IsTraining => trainingTarget is not null;

	private void StartTraining()
	{
		var resuming = DateTime.UtcNow - lastTrainingTick < TrainingRestartAfter;
		if (!settings.Training || resuming)
			return;
		var target = settings.GetBookSpeed(Book.Id) ?? Speed;
		var start = Math.Max(1, target - settings.TrainingStartBelow);
		if (start >= target && !settings.TrainingClimb)
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
			if (next >= target - 0.001 && !settings.TrainingClimb)
			{
				ApplySpeed(target, save: false);
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
		var wait = untilNextStep.TotalMinutes >= 1 ? $"{(int)untilNextStep.TotalMinutes}:{untilNextStep.Seconds:00}" : $"{Math.Max(0, untilNextStep.Seconds)} s";
		TrainingText = Speed < target - 0.001
			? $"Training up to {target:0.0}×, +{settings.TrainingStep:0.##}× in {wait}"
			: $"Training: climbing, +{settings.TrainingStep:0.##}× in {wait}";
	}
}
