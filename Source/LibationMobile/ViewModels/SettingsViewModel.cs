using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace LibationMobile.ViewModels;

/// <summary>The settings page. Every change is saved as it is made.</summary>
public partial class SettingsViewModel(MobileSettings settings, Action changed, Action<float>? profileApplied = null) : ObservableObject
{
	#region Pages

	/// <summary>Which group is open: "" for the front page, else "speed", "training", "playback", "sleep", "siri", "sync" or "account".</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRootPage), nameof(IsSpeedPage), nameof(IsTrainingPage), nameof(IsPlaybackPage), nameof(IsSiriPage),
		nameof(IsSleepPage), nameof(IsSyncPage), nameof(IsAccountPage), nameof(PageTitle))]
	private string page = "";

	public bool IsRootPage => Page == "";
	public bool IsSpeedPage => Page == "speed";
	public bool IsTrainingPage => Page == "training";
	public bool IsPlaybackPage => Page == "playback";
	public bool IsSiriPage => Page == "siri";
	public bool IsSleepPage => Page == "sleep";
	public bool IsSyncPage => Page == "sync";
	public bool IsAccountPage => Page == "account";

	public string PageTitle => Page switch
	{
		"speed" => "Speed",
		"training" => "Training",
		"playback" => "Playback",
		"siri" => "Siri and voice",
		"sleep" => "Sleep",
		"sync" => "Sync and downloads",
		"account" => "Account",
		_ => "Settings",
	};

	[RelayCommand]
	private void OpenPage(string name)
	{
		Page = name ?? "";
		if (Page == "sleep")
			RefreshPermissions();
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
	public string SleepSummary => settings.AutoPauseAsleep
		? $"Pauses when you fall asleep, after {settings.AutoPauseMinutes} min{(settings.AutoPauseNightOnly ? $", {SleepFinder.TimeOfDay(settings.AutoPauseFromMinute)} to {SleepFinder.TimeOfDay(settings.AutoPauseUntilMinute)}" : "")}"
		: "Sleep timer in the player · auto-pause off";
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

	public bool PlayNextInSeries
	{
		get => settings.PlayNextInSeries;
		set { settings.PlayNextInSeries = value; OnPropertyChanged(); }
	}

	public bool DownloadNextInSeries
	{
		get => settings.DownloadNextInSeries;
		set { settings.DownloadNextInSeries = value; OnPropertyChanged(); }
	}

	#region Auto-pause when asleep

	public bool AutoPauseAsleep
	{
		get => settings.AutoPauseAsleep;
		set
		{
			settings.AutoPauseAsleep = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(SleepSummary));
			// Ask for motion now, while the listener is looking, not the first night it is needed.
			if (value && ListeningContext.Platform is { } context)
				_ = AskMotionAsync(context);
		}
	}

	[ObservableProperty]
	private string motionStatus = "";

	private async Task AskMotionAsync(IListeningContext context)
	{
		var allowed = await context.RequestMotionAsync();
		MotionStatus = allowed ? "" : "Without Motion & Fitness, only the time since your last touch is used. Allow it below.";
		RefreshPermissions();
	}

	public bool UseHeadMovement
	{
		get => settings.UseHeadMovement;
		set
		{
			settings.UseHeadMovement = value;
			if (ListeningContext.Platform is { } context)
				context.UseHead = value;
			OnPropertyChanged();
		}
	}

	[ObservableProperty]
	private string motionPermissionText = "";

	[ObservableProperty]
	private string motionActionText = "";

	[ObservableProperty]
	private bool hasMotionAction;

	/// <summary>Motion &amp; Fitness as it stands, read again whenever the Sleep page opens.</summary>
	public void RefreshPermissions()
	{
		var state = ListeningContext.Platform?.MotionPermission ?? "unavailable";
		MotionPermissionText = state switch
		{
			"allowed" => "Allowed",
			"denied" => "Not allowed",
			"not asked" => "Not asked yet",
			_ => "Not on this device",
		};
		MotionActionText = state == "not asked" ? "Allow" : "Change";
		HasMotionAction = state != "unavailable";
	}

	/// <summary>Ask, if never asked; otherwise open the app's page in the iPhone's Settings, the only place it can change.</summary>
	[RelayCommand]
	private async Task MotionAction()
	{
		if (ListeningContext.Platform is not { } context)
			return;
		if (context.MotionPermission == "not asked")
		{
			await context.RequestMotionAsync();
			RefreshPermissions();
		}
		else
			context.OpenAppSettings();
	}

	/// <summary>The log to look back over once Apple Health is connected. Set by the main view model.</summary>
	public ListeningLog? ListeningLog { get; set; }

	public bool AutoPauseNightOnly
	{
		get => settings.AutoPauseNightOnly;
		set { settings.AutoPauseNightOnly = value; OnPropertyChanged(); OnPropertyChanged(nameof(SleepSummary)); }
	}

	public string AutoPauseHoursSource => settings.AutoPauseHoursChosen ? "Set by you."
		: settings.UseHealthSleep ? "From your usual sleep in Apple Health; change them to set your own."
		: "Starting hours. With Apple Health connected, they follow your usual sleep.";

	public string AutoPauseFromText => SleepFinder.TimeOfDay(settings.AutoPauseFromMinute);
	public string AutoPauseUntilText => SleepFinder.TimeOfDay(settings.AutoPauseUntilMinute);

	/// <summary>"from+", "from-", "until+" or "until-": by half an hour.</summary>
	[RelayCommand]
	private void NudgeAutoPauseHours(string which)
	{
		var by = which.EndsWith('+') ? 30 : -30;
		settings.AutoPauseHoursChosen = true;
		if (which.StartsWith("from"))
			settings.AutoPauseFromMinute += by;
		else
			settings.AutoPauseUntilMinute += by;
		OnPropertyChanged(nameof(AutoPauseFromText));
		OnPropertyChanged(nameof(AutoPauseUntilText));
		OnPropertyChanged(nameof(AutoPauseHoursSource));
		OnPropertyChanged(nameof(SleepSummary));
	}

	public bool IsAsleep10 => settings.AutoPauseMinutes == 10;
	public bool IsAsleep20 => settings.AutoPauseMinutes == 20;
	public bool IsAsleep30 => settings.AutoPauseMinutes == 30;
	public bool IsAsleep45 => settings.AutoPauseMinutes == 45;

	[RelayCommand]
	private void SetAsleepMinutes(string minutes)
	{
		if (int.TryParse(minutes, out var m))
			settings.AutoPauseMinutes = m;
		OnPropertyChanged(nameof(SleepSummary));
		foreach (var name in new[] { nameof(IsAsleep10), nameof(IsAsleep20), nameof(IsAsleep30), nameof(IsAsleep45) })
			OnPropertyChanged(name);
	}

	public bool UseHealthSleep => settings.UseHealthSleep;

	[ObservableProperty]
	private string healthStatus = "";

	/// <summary>Ask to read sleep from Apple Health, where Garmin Connect or an Apple Watch writes it.</summary>
	[RelayCommand]
	private async Task ConnectHealth()
	{
		if (ListeningContext.Platform is not { } context)
		{
			HealthStatus = "Apple Health is not available here.";
			return;
		}
		var ok = await context.ConnectHealthAsync();
		settings.UseHealthSleep = ok;
		OnPropertyChanged(nameof(UseHealthSleep));
		HealthStatus = ok
			? "Connected. Where you fell asleep shows in each book's history once your watch has synced."
			: "Could not connect to Apple Health. Check Settings › Health › Data Access & Devices › Speed.";
		// Apple Health keeps all past sleep: look over every session logged so far, once.
		if (ok && ListeningLog is { } log)
		{
			var found = await Task.Run(() => SleepFinder.RefineAsync(log, context, everything: true));
			if (found > 0)
				HealthStatus = $"Connected. Found where you fell asleep in {found} past {(found == 1 ? "session" : "sessions")}; see each book's history.";
		}
	}

	#endregion

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

	public bool AskFollowRating
	{
		get => settings.AskFollowRating;
		set { settings.AskFollowRating = value; OnPropertyChanged(); }
	}

	public bool AskAlertness
	{
		get => settings.AskAlertness;
		set { settings.AskAlertness = value; OnPropertyChanged(); }
	}

	/// <summary>Voice prompts need a platform that can listen; iOS for now.</summary>
	public bool CanUseVoice => VoicePrompt.Platform is not null;

	public bool VoicePrompts
	{
		get => settings.VoicePrompts;
		set
		{
			settings.VoicePrompts = value;
			OnPropertyChanged();
			// Ask for the microphone now, while the listener is looking, not the first time a question is asked.
			if (value && VoicePrompt.Platform is { } voice)
				_ = AskVoiceAccessAsync(voice);
			else
				VoiceStatus = "";
		}
	}

	[ObservableProperty]
	private string voiceStatus = "";

	private async System.Threading.Tasks.Task AskVoiceAccessAsync(IVoicePrompt voice)
	{
		var allowed = await voice.RequestAccessAsync();
		VoiceStatus = allowed ? "" : "Libation needs the microphone and speech recognition to hear your answer. Allow them in the iPhone's Settings, under Libation.";
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

	/// <summary>Real time, book time and time saved, in Audible's app and in Libation. Set by the main view model.</summary>
	public Func<TimeBreakdown>? TimeBreakdown { get; set; }

	/// <summary>Worked out when the page is refreshed, not for each figure shown.</summary>
	private TimeBreakdown? Breakdown => breakdown ??= TimeBreakdown?.Invoke();
	private TimeBreakdown? breakdown;

	public string TimeSavedText => Breakdown is { } b && b.Real > TimeSpan.Zero ? $"{b.Saved.TotalHours:N0} hours saved at {b.Speed:0.00}× on average" : "Nothing yet";

	/// <summary>Called when the settings page opens, to show figures from listening since it was last open.</summary>
	public void RefreshTimeSaved()
	{
		// The player has its own training switch.
		OnPropertyChanged(nameof(Training));
		breakdown = null;
		foreach (var name in new[] { nameof(TimeSavedText), nameof(TimeSavedSummary) })
			OnPropertyChanged(name);
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
