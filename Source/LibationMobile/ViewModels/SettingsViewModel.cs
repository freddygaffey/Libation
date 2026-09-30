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

	public bool HighQualityDownloads
	{
		get => settings.HighQualityDownloads;
		set
		{
			settings.HighQualityDownloads = value;
			OnPropertyChanged();
		}
	}

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
