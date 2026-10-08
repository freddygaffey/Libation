using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibationMobile.ViewModels;

/// <summary>The settings page. Every change is saved as it is made.</summary>
public partial class SettingsViewModel(MobileSettings settings, Action changed, Action<float>? profileApplied = null) : ObservableObject
{
	#region Pages

	/// <summary>Which group is open: "" for the front page, else "speed", "training", "playback", "siri", "timesaved", "sync" or "account".</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRootPage), nameof(IsSpeedPage), nameof(IsTrainingPage), nameof(IsPlaybackPage), nameof(IsSiriPage),
		nameof(IsTimeSavedPage), nameof(IsSyncPage), nameof(IsAccountPage), nameof(PageTitle))]
	private string page = "";

	public bool IsRootPage => Page == "";
	public bool IsSpeedPage => Page == "speed";
	public bool IsTrainingPage => Page == "training";
	public bool IsPlaybackPage => Page == "playback";
	public bool IsSiriPage => Page == "siri";
	public bool IsTimeSavedPage => Page == "timesaved";
	public bool IsSyncPage => Page == "sync";
	public bool IsAccountPage => Page == "account";

	public string PageTitle => Page switch
	{
		"speed" => "Speed",
		"training" => "Training",
		"playback" => "Playback",
		"siri" => "Siri and voice",
		"timesaved" => "Time saved",
		"sync" => "Sync and downloads",
		"account" => "Account",
		_ => "Settings",
	};

	[RelayCommand]
	private void OpenPage(string name)
	{
		Page = name ?? "";
		// The front page's summaries may have changed in the group just closed.
		OnPropertyChanged(string.Empty);
	}

	/// <summary>Back from a group to the front page. False on the front page, so Back leaves settings.</summary>
	public bool Back()
	{
		if (IsRootPage)
			return false;
		OpenPage("");
		return true;
	}

	// One line under each group on the front page: what it is set to now.
	public string SpeedSummary
	{
		get
		{
			var method = settings.UseNonlinearSpeed && NonlinearAvailable ? "Speedy" : "Original";
			if (settings.ActiveProfile is { } profile)
				return $"{profile} · {method} · scales with speed";
			var options = new List<string> { "Custom", method };
			if (settings.PauseCap > 0) options.Add($"pauses {settings.PauseCap * 1000:0} ms");
			if (settings.SpeedFloor > 0 && settings.UseNonlinearSpeed) options.Add($"floor {settings.SpeedFloor:P0}");
			if (settings.RhythmGap > 0) options.Add("rhythm");
			return string.Join(" · ", options);
		}
	}

	public string TrainingSummary => settings.Training
		? $"On · from {(settings.TrainingFromBelow ? $"{settings.TrainingStartBelow:0.0#}× below the book's speed" : $"{settings.TrainingStartSpeed:0.0}×")}, +{settings.TrainingStep:0.0#}× every {(settings.TrainingMinutes < 1 ? $"{settings.TrainingMinutes * 60:0} s" : $"{settings.TrainingMinutes:0.#} min")}"
		: "Off";

	public string PlaybackSummary => $"Skips {settings.SkipSeconds} s · clips {settings.ClipSeconds} s · bar shows the {(settings.ScrubByChapter ? "chapter" : "book")}";
	public string SiriSummary => "\"Hey Siri, Speed 7.3\", \"Speed play a book\" and more";
	public string TimeSavedSummary => TimeSavedText;
	public string SyncSummary => $"{(settings.SyncPosition ? "Positions synced with Audible" : "Not syncing")} · {(settings.HighQualityDownloads ? "high quality" : "standard")} downloads";
	public string AccountSummary => $"Audible {AudibleAccount.DisplayNameOf(settings.RegionName)}";

	#endregion

	#region Speed profiles

	public IReadOnlyList<ProfileChoice> Profiles => settings.Profiles.Select(p => new ProfileChoice(p, p.Name == settings.ActiveProfile)).ToList();
	public string ActiveProfileText => settings.ActiveProfile is { } name
		? $"{name} in use. Its pause and floor rules follow the speed playing, through training and Siri too."
		: "Custom: the Advanced settings below, as set. Tap a profile to use its rules instead.";
	public bool HasActiveProfile => settings.ActiveProfile is not null;

	[RelayCommand]
	private void ApplyProfile(ProfileChoice choice)
	{
		settings.ApplyProfile(choice.Profile);
		profileApplied?.Invoke(choice.Profile.Speed);
		OnPropertyChanged(string.Empty);
		changed();
	}

	/// <summary>Keep the settings below, and the speed now, as the chosen profile.</summary>
	[RelayCommand]
	private void SaveProfile()
	{
		if (settings.ActiveProfile is not { } name)
			return;
		settings.SaveProfile(name, currentSpeed?.Invoke() ?? settings.Speed);
		OnPropertyChanged(string.Empty);
	}

	[RelayCommand]
	private void ResetProfile()
	{
		if (settings.ActiveProfile is not { } name)
			return;
		settings.ResetProfile(name);
		ApplyProfile(new ProfileChoice(settings.Profiles.First(p => p.Name == name), true));
	}

	/// <summary>The speed of the book playing, to save in a profile. Set by the main view model.</summary>
	public Func<float>? currentSpeed { get; set; }

	#endregion

	/// <summary>The list of Siri commands, under the (i) button.</summary>
	[ObservableProperty]
	private bool isSiriHelpOpen;

	[RelayCommand]
	private void ToggleSiriHelp() => IsSiriHelpOpen = !IsSiriHelpOpen;

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

	public bool TrainingFromBelow
	{
		get => settings.TrainingFromBelow;
		set { settings.TrainingFromBelow = value; OnPropertyChanged(); OnPropertyChanged(nameof(TrainingFromSpeed)); changed(); }
	}

	public bool TrainingFromSpeed => !TrainingFromBelow;

	[RelayCommand]
	private void SetTrainingFrom(string mode) => TrainingFromBelow = mode == "below";

	public double TrainingStartBelow
	{
		get => Math.Round(settings.TrainingStartBelow, 2);
		set { settings.TrainingStartBelow = (float)value; OnPropertyChanged(nameof(TrainingStartBelowText)); }
	}
	public string TrainingStartBelowText => $"{TrainingStartBelow:0.0#}× below";

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
		set { GoCustom(); settings.PauseCap = (float)(value / 1000); OnPropertyChanged(nameof(PauseCapText)); OnPropertyChanged(nameof(UsesSpeedySpeedUp)); }
	}
	public string PauseCapText => PauseCapMs <= 0 ? "Off" : $"{PauseCapMs:0} ms";

	public bool KeepSpeed
	{
		get => settings.KeepSpeed;
		set { GoCustom(); settings.KeepSpeed = value; OnPropertyChanged(); }
	}

	/// <summary>Speed floor as a percentage of the speed; 0 is off.</summary>
	public double SpeedFloorPercent
	{
		get => Math.Round(settings.SpeedFloor * 100);
		set { GoCustom(); settings.SpeedFloor = (float)(value / 100); OnPropertyChanged(nameof(SpeedFloorText)); }
	}
	public string SpeedFloorText => SpeedFloorPercent <= 0 ? "Off" : $"{SpeedFloorPercent:0}% of the speed";

	/// <summary>Rhythm gap in milliseconds; 0 is off.</summary>
	public double RhythmGapMs
	{
		get => Math.Round(settings.RhythmGap * 1000);
		set { GoCustom(); settings.RhythmGap = (float)(value / 1000); OnPropertyChanged(nameof(RhythmGapText)); OnPropertyChanged(nameof(RhythmOn)); }
	}
	public string RhythmGapText => RhythmGapMs <= 0 ? "Off" : $"{RhythmGapMs:0} ms";
	public bool RhythmOn => RhythmGapMs > 0;

	public double RhythmRate
	{
		get => Math.Round(settings.RhythmRate, 1);
		set { GoCustom(); settings.RhythmRate = (float)value; OnPropertyChanged(nameof(RhythmRateText)); }
	}
	public string RhythmRateText => $"{RhythmRate:0.#} a second";

	/// <summary>The floor only applies to Speedy; the others work with either method.</summary>
	public bool UsesSpeedySpeedUp => IsNonlinearMethod;

	/// <summary>A hand on an Advanced setting means custom settings: the profile's rules stop applying.</summary>
	private void GoCustom()
	{
		if (settings.ActiveProfile is null)
			return;
		settings.UseCustom();
		OnPropertyChanged(nameof(Profiles));
		OnPropertyChanged(nameof(ActiveProfileText));
		OnPropertyChanged(nameof(HasActiveProfile));
		changed();
	}

	[RelayCommand]
	private void ResetHighSpeed()
	{
		GoCustom();
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
		GoCustom();
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

/// <summary>A profile as a choice in the settings and the player.</summary>
public record ProfileChoice(SpeedProfile Profile, bool IsActive)
{
	public string Name => Profile.Name;
	public string Summary => Describe(Profile);
	public string Purpose => Profile.Purpose;

	/// <summary>"4.5×, Speedy · pauses you hear 25 ms from 3× · hardest sounds at least 50% of the speed, blending in from 3× to 5×".</summary>
	public static string Describe(SpeedProfile p)
	{
		var parts = new List<string> { $"{p.Speed:0.0}×, {(p.UseNonlinear ? (p.Nonlinearity >= 1 ? "Speedy" : $"Speedy {p.Nonlinearity:P0}") : "Original")}" };
		if (p.HeardPause > 0)
			parts.Add($"pauses you hear {p.HeardPause * 1000:0} ms{(p.PauseFrom > 1 ? $" from {p.PauseFrom:0.#}×" : "")}");
		if (p.Floor > 0)
			parts.Add(p.FloorFull > p.FloorFrom ? $"hardest sounds at least {p.Floor:P0} of the speed, blending in from {p.FloorFrom:0.#}× to {p.FloorFull:0.#}×" : $"hardest sounds at least {p.Floor:P0} of the speed");
		if (p.RhythmGap > 0)
			parts.Add($"rhythm {p.RhythmGap * 1000:0} ms × {p.RhythmRate:0.#}");
		return string.Join(" · ", parts);
	}
}
