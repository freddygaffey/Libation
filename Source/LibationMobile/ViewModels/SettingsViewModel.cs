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

	public bool IsStartBelow05 => settings.TrainingStartBelow == 0.5f;
	public bool IsStartBelow1 => settings.TrainingStartBelow == 1f;
	public bool IsStartBelow15 => settings.TrainingStartBelow == 1.5f;
	public bool IsStartBelow2 => settings.TrainingStartBelow == 2f;
	public bool IsStep005 => settings.TrainingStep == 0.05f;
	public bool IsStep01 => settings.TrainingStep == 0.1f;
	public bool IsStep02 => settings.TrainingStep == 0.2f;
	public bool IsStep05 => settings.TrainingStep == 0.5f;
	public bool IsEvery1 => settings.TrainingMinutes == 1f;
	public bool IsEvery2 => settings.TrainingMinutes == 2f;
	public bool IsEvery5 => settings.TrainingMinutes == 5f;
	public bool IsEvery10 => settings.TrainingMinutes == 10f;

	[RelayCommand]
	private void SetTrainingStartBelow(string value)
	{
		settings.TrainingStartBelow = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
		OnPropertyChanged(string.Empty);
	}

	[RelayCommand]
	private void SetTrainingStep(string value)
	{
		settings.TrainingStep = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
		OnPropertyChanged(string.Empty);
	}

	[RelayCommand]
	private void SetTrainingMinutes(string value)
	{
		settings.TrainingMinutes = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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
