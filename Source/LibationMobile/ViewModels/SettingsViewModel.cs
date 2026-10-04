using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;

namespace LibationMobile.ViewModels;

/// <summary>The settings page. Every change is saved as it is made.</summary>
public partial class SettingsViewModel(MobileSettings settings, Action changed) : ObservableObject
{
	public int SkipSeconds => settings.SkipSeconds;
	public int ClipSeconds => settings.ClipSeconds;

	public bool SyncPosition
	{
		get => settings.SyncPosition;
		set
		{
			settings.SyncPosition = value;
			OnPropertyChanged();
			changed();
		}
	}

	public bool ScrubByChapter
	{
		get => settings.ScrubByChapter;
		set
		{
			settings.ScrubByChapter = value;
			OnPropertyChanged();
			changed();
		}
	}

	public bool HighQualityDownloads
	{
		get => settings.HighQualityDownloads;
		set
		{
			settings.HighQualityDownloads = value;
			OnPropertyChanged();
		}
	}

	/// <summary>How unevenly speech is sped up, as a percentage for a slider.</summary>
	public double Nonlinearity
	{
		get => Math.Round(settings.Nonlinearity * 100);
		set
		{
			// The slider snaps to its own steps. Sending the value back to it mid-drag made it catch.
			settings.Nonlinearity = (float)(value / 100);
			OnPropertyChanged(nameof(NonlinearityText));
		}
	}

	public bool IsNonlinearMethod => settings.UseNonlinearSpeed && NonlinearAvailable;
	public bool IsClassicMethod => !IsNonlinearMethod;

	[RelayCommand]
	private void SetSpeedMethod(string method)
	{
		settings.UseNonlinearSpeed = method == "nonlinear";
		OnPropertyChanged(string.Empty);
	}

	public string NonlinearityText => Nonlinearity <= 0 ? "Even" : $"{Nonlinearity:0}%";
	public bool NonlinearAvailable => AudioBackend.NonlinearAvailable;

	[RelayCommand]
	private void SetNonlinearity(string percent)
	{
		Nonlinearity = double.Parse(percent);
		OnPropertyChanged(nameof(Nonlinearity));
	}

	#region Speed listening

	public bool Training
	{
		get => settings.Training;
		set
		{
			settings.Training = value;
			OnPropertyChanged();
			changed();
		}
	}

	public bool TrainingClimb
	{
		get => settings.TrainingClimb;
		set
		{
			settings.TrainingClimb = value;
			OnPropertyChanged();
		}
	}

	public bool ShowSyllableRate
	{
		get => settings.ShowSyllableRate;
		set
		{
			settings.ShowSyllableRate = value;
			OnPropertyChanged();
		}
	}

	// Sliders: each setter saves and updates only its text. Sending the value back to a slider mid-drag made it catch.

	public double TrainingStartSpeed
	{
		get => Math.Round(settings.TrainingStartSpeed, 1);
		set { settings.TrainingStartSpeed = (float)value; OnPropertyChanged(nameof(TrainingStartText)); }
	}
	public string TrainingStartText => $"{TrainingStartSpeed:0.0}×";

	public double TrainingStep
	{
		get => Math.Round(settings.TrainingStep, 2);
		set { settings.TrainingStep = (float)value; OnPropertyChanged(nameof(TrainingStepText)); }
	}
	public string TrainingStepText => $"+{TrainingStep:0.0#}×";

	public double TrainingMinutes
	{
		get => Math.Round(settings.TrainingMinutes, 1);
		set { settings.TrainingMinutes = (float)value; OnPropertyChanged(nameof(TrainingMinutesText)); }
	}
	public string TrainingMinutesText => TrainingMinutes < 1 ? $"{TrainingMinutes * 60:0} s" : $"{TrainingMinutes:0.#} min";

	public double TrainingCeiling
	{
		get => Math.Round(settings.TrainingCeiling, 1);
		set { settings.TrainingCeiling = (float)value; OnPropertyChanged(nameof(TrainingCeilingText)); }
	}
	public string TrainingCeilingText => $"{TrainingCeiling:0.0}×";

	public double TrainingRestartMinutes
	{
		get => Math.Round(settings.TrainingRestartMinutes);
		set { settings.TrainingRestartMinutes = (float)value; OnPropertyChanged(nameof(TrainingRestartText)); }
	}
	public string TrainingRestartText => TrainingRestartMinutes >= 60 ? $"{TrainingRestartMinutes / 60:0.#} h" : $"{TrainingRestartMinutes:0} min";

	#endregion

	#region High speeds (speechwarp's options)

	/// <summary>Pause cap in milliseconds; 0 is off.</summary>
	public double PauseCapMs
	{
		get => Math.Round(settings.PauseCap * 1000);
		set { settings.PauseCap = (float)(value / 1000); OnPropertyChanged(nameof(PauseCapText)); OnPropertyChanged(nameof(UsesSpeedySpeedUp)); }
	}
	public string PauseCapText => PauseCapMs <= 0 ? "Off" : $"{PauseCapMs:0} ms";

	public bool KeepSpeed
	{
		get => settings.KeepSpeed;
		set { settings.KeepSpeed = value; OnPropertyChanged(); }
	}

	/// <summary>Speed floor as a percentage of the speed; 0 is off.</summary>
	public double SpeedFloorPercent
	{
		get => Math.Round(settings.SpeedFloor * 100);
		set { settings.SpeedFloor = (float)(value / 100); OnPropertyChanged(nameof(SpeedFloorText)); }
	}
	public string SpeedFloorText => SpeedFloorPercent <= 0 ? "Off" : $"{SpeedFloorPercent:0}% of the speed";

	/// <summary>Rhythm gap in milliseconds; 0 is off.</summary>
	public double RhythmGapMs
	{
		get => Math.Round(settings.RhythmGap * 1000);
		set { settings.RhythmGap = (float)(value / 1000); OnPropertyChanged(nameof(RhythmGapText)); OnPropertyChanged(nameof(RhythmOn)); }
	}
	public string RhythmGapText => RhythmGapMs <= 0 ? "Off" : $"{RhythmGapMs:0} ms";
	public bool RhythmOn => RhythmGapMs > 0;

	public double RhythmRate
	{
		get => Math.Round(settings.RhythmRate, 1);
		set { settings.RhythmRate = (float)value; OnPropertyChanged(nameof(RhythmRateText)); }
	}
	public string RhythmRateText => $"{RhythmRate:0.#} a second";

	/// <summary>The floor only applies to Speedy; the others work with either method.</summary>
	public bool UsesSpeedySpeedUp => IsNonlinearMethod;

	[RelayCommand]
	private void ResetHighSpeed()
	{
		settings.PauseCap = 0;
		settings.KeepSpeed = true;
		settings.SpeedFloor = 0;
		settings.RhythmGap = 0;
		settings.RhythmRate = 6;
		OnPropertyChanged(string.Empty);
	}

	/// <summary>The settings the library's own evaluation used at 5x to 8x, as a starting point.</summary>
	[RelayCommand]
	private void SuggestHighSpeed()
	{
		settings.PauseCap = 0.06f;
		settings.KeepSpeed = true;
		settings.SpeedFloor = 0.5f;
		OnPropertyChanged(string.Empty);
	}

	#endregion

	#region Time saved

	/// <summary>The speed to compare against, for a slider.</summary>
	public double BaselineSpeed
	{
		get => Math.Round(settings.BaselineSpeed, 2);
		set
		{
			settings.BaselineSpeed = (float)value;
			RefreshTimeSaved();
		}
	}

	public string BaselineText => $"{BaselineSpeed:0.##}×";

	private TimeSpan Saved
	{
		get
		{
			var saved = settings.BookTimeHeard / settings.BaselineSpeed - settings.TimeSpentListening;
			return saved > TimeSpan.Zero ? saved : TimeSpan.Zero;
		}
	}

	public string TimeSavedText => settings.ListeningCountedSince is null ? "Nothing yet" : FormatLong(Saved);
	public string TimeSavedDetail => settings.ListeningCountedSince is DateTimeOffset since
		? $"{FormatLong(settings.BookTimeHeard)} of books heard in {FormatLong(settings.TimeSpentListening)}, an average of {AverageSpeed:0.0}×. Counted since {since.ToLocalTime():d MMM yyyy}."
		: "Listen to a book and the time you save over your usual speed adds up here.";

	private double AverageSpeed => settings.TimeSpentListening > TimeSpan.Zero ? settings.BookTimeHeard / settings.TimeSpentListening : 1;

	/// <summary>Called when the settings page opens, to show figures from listening since it was last open.</summary>
	public void RefreshTimeSaved()
	{
		// The player has its own training switch.
		OnPropertyChanged(nameof(Training));
		OnPropertyChanged(nameof(BaselineText));
		OnPropertyChanged(nameof(TimeSavedText));
		OnPropertyChanged(nameof(TimeSavedDetail));
	}

	[RelayCommand]
	private void ResetTimeSaved()
	{
		settings.ResetListening();
		RefreshTimeSaved();
	}

	/// <summary>Weeks, days, hours, minutes and seconds, leaving out leading units that are zero.</summary>
	private static string FormatLong(TimeSpan time)
	{
		var seconds = (long)time.TotalSeconds;
		(string Unit, long Size)[] units = [("w", 604800), ("d", 86400), ("h", 3600), ("m", 60), ("s", 1)];
		var parts = new System.Collections.Generic.List<string>();
		foreach (var (unit, size) in units)
		{
			if (seconds >= size || parts.Count > 0 || size == 1)
				parts.Add($"{seconds / size}{unit}");
			seconds %= size;
		}
		return string.Join(" ", parts);
	}

	#endregion

	public bool IsSkip10 => SkipSeconds == 10;
	public bool IsSkip15 => SkipSeconds == 15;
	public bool IsSkip30 => SkipSeconds == 30;
	public bool IsSkip60 => SkipSeconds == 60;
	public bool IsClip15 => ClipSeconds == 15;
	public bool IsClip30 => ClipSeconds == 30;
	public bool IsClip45 => ClipSeconds == 45;

	[RelayCommand]
	private void SetSkip(string seconds)
	{
		settings.SkipSeconds = int.Parse(seconds);
		OnPropertyChanged(string.Empty);
		changed();
	}

	[RelayCommand]
	private void SetClip(string seconds)
	{
		settings.ClipSeconds = int.Parse(seconds);
		OnPropertyChanged(string.Empty);
	}
}
