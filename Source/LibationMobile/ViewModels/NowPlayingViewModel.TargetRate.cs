using CommunityToolkit.Mvvm.Input;
using System;

namespace LibationMobile.ViewModels;

/// <summary>
/// Speed by syllable rate: pick how many syllables a second to hear, and the speed follows the narrator. A slow narrator
/// is sped up more, a fast one less, so every book and chapter is heard at about the same rate.
/// </summary>
public partial class NowPlayingViewModel
{
	/// <summary>How often the speed is checked against the target. The rate is measured over the last minute, so it moves slowly.</summary>
	private static readonly TimeSpan TargetRateInterval = TimeSpan.FromSeconds(5);

	/// <summary>Smaller changes than this are left alone, so the speed does not wobble with every sentence.</summary>
	private const double TargetRateDeadband = 0.15;

	private DateTime nextTargetCheck;

	public bool IsTargetRateOn => settings.UseTargetSyllableRate;
	public string TargetRateText => $"{settings.TargetSyllableRate:0.0} syllables a second";

	[RelayCommand]
	private void ToggleTargetRate()
	{
		settings.UseTargetSyllableRate = !settings.UseTargetSyllableRate;
		// Start from what is heard now, so turning it on does not jump the speed.
		if (settings.UseTargetSyllableRate && HeardRate() is double heard)
			settings.TargetSyllableRate = heard;
		nextTargetCheck = DateTime.MinValue;
		LogEvent("mode", detail: settings.UseTargetSyllableRate ? $"target rate {settings.TargetSyllableRate:0.0}" : "target rate off");
		OnTargetRateChanged();
	}

	/// <summary>"+" or "-": a tenth of a syllable a second.</summary>
	[RelayCommand]
	private void NudgeTargetRate(string direction)
	{
		settings.TargetSyllableRate += direction == "-" ? -0.1 : 0.1;
		nextTargetCheck = DateTime.MinValue;
		OnTargetRateChanged();
	}

	private void OnTargetRateChanged()
	{
		OnPropertyChanged(nameof(IsTargetRateOn));
		OnPropertyChanged(nameof(TargetRateText));
	}

	private double? HeardRate() => player.SourceSyllablesPerSecond is double rate && rate > 0 ? rate * Speed : null;

	/// <summary>The listener moved the speed by hand: take the rate now heard as the new target.</summary>
	private void RetargetRate()
	{
		if (!settings.UseTargetSyllableRate || speedSource == "rate" || HeardRate() is not double heard)
			return;
		settings.TargetSyllableRate = heard;
		nextTargetCheck = DateTime.UtcNow + TargetRateInterval;
		OnPropertyChanged(nameof(TargetRateText));
	}

	/// <summary>Each tick: every few seconds, move the speed toward the one that gives the target rate.</summary>
	private void TargetRateTick()
	{
		var now = DateTime.UtcNow;
		if (!settings.UseTargetSyllableRate || !IsPlaying || IsTraining || IsTrialRunning || now < nextTargetCheck)
			return;
		nextTargetCheck = now + TargetRateInterval;
		if (player.SourceSyllablesPerSecond is not double rate || rate < 1)
			return;
		var wanted = Math.Clamp(settings.TargetSyllableRate / rate, MIN_SPEED, MAX_SPEED);
		if (Math.Abs(wanted - Speed) < TargetRateDeadband)
			return;
		speedSource = "rate";
		ApplySpeed(wanted, save: true);
		speedSource = "you";
	}
}
