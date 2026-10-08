using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;

namespace LibationMobile.ViewModels;

/// <summary>
/// The listener's own research (Services/Experiments.cs): "how well did you follow?" after a session, and blind trials
/// that play two values of one setting in turn and ask which was easier.
/// </summary>
public partial class NowPlayingViewModel
{
	/// <summary>Where ratings and trials are kept. Set by the main view model.</summary>
	public Experiments? Experiments { get; set; }

	/// <summary>Sessions shorter than this are not asked about.</summary>
	private static readonly TimeSpan RatedSessionLength = TimeSpan.FromMinutes(5);

	/// <summary>Each part of a trial: long enough to settle into, short enough to compare.</summary>
	private static readonly TimeSpan TrialPartLength = TimeSpan.FromMinutes(2);

	/// <summary>What is playing now, for the record: the speed, method and options in force, and syllables a second.</summary>
	private ListeningSettings CurrentListening(double? syllablesPerSecond = null)
	{
		var speed = Speed;
		var profile = AudioBackend.Scaling;
		var cap = profile?.PauseCapAt(speed) ?? AudioBackend.PauseCap;
		return new ListeningSettings(speed, AudioBackend.UseNonlinear && AudioBackend.NonlinearAvailable, AudioBackend.Nonlinearity,
			cap > 0 ? cap / speed : 0, profile?.FloorAt(speed) ?? AudioBackend.SpeedFloor, profile?.RhythmGap ?? AudioBackend.RhythmGap,
			settings.ActiveProfile, syllablesPerSecond ?? (player.SourceSyllablesPerSecond is double rate ? rate * speed : null));
	}

	#region Rating after a session

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsAskingFollow))]
	private ListeningSettings? followToRate;

	public bool IsAskingFollow => FollowToRate is not null && TrialPart == 0;

	/// <summary>Called as a session ends: ask about it if it was long enough.</summary>
	private void OfferFollowRating(TimeSpan spent, double syllablesHeard)
	{
		if (Experiments is null || !settings.AskFollowRating || TrialPart != 0 || spent < RatedSessionLength)
			return;
		FollowToRate = CurrentListening(syllablesHeard > 0 ? syllablesHeard / spent.TotalSeconds : null);
	}

	[RelayCommand]
	private void RateFollow(string score)
	{
		if (FollowToRate is { } rated && int.TryParse(score, out var follow))
			Experiments?.Add(new FollowRating(DateTimeOffset.Now, Book.Id, Title, follow, rated));
		FollowToRate = null;
	}

	[RelayCommand]
	private void SkipFollow() => FollowToRate = null;

	#endregion

	#region Blind trials

	/// <summary>0 no trial; 1 or 2 the part playing; 3 asking about part 1; 4 asking about part 2; 5 showing what was heard.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsTrialRunning), nameof(IsTrialPlaying), nameof(IsAskingTrialFollow), nameof(IsAskingPreference),
		nameof(IsShowingTrialResult), nameof(IsAskingFollow), nameof(TrialQuestion))]
	private int trialPart;

	public bool IsTrialRunning => TrialPart != 0;
	public bool IsTrialPlaying => TrialPart is 1 or 2;
	public bool IsAskingTrialFollow => TrialPart is 3 || TrialPart == 4 && trialSecondFollow == 0;
	public bool IsAskingPreference => TrialPart == 4 && trialSecondFollow != 0;
	public bool IsShowingTrialResult => TrialPart == 5;
	public string TrialQuestion => TrialPart == 3 ? "How well did you follow part 1?" : "How well did you follow part 2?";

	[ObservableProperty]
	private string trialStatus = "";

	[ObservableProperty]
	private string trialResult = "";

	private ExperimentParameter? trialParameter;
	private double trialFirst, trialSecond;
	private int trialFirstFollow, trialSecondFollow;
	private TimeSpan trialListened;
	private double trialSyllables;
	private TimeSpan trialSpent;
	private (SpeedProfile? Scaling, bool UseNonlinear, float Nonlinearity) beforeTrial;

	/// <summary>Start a blind trial at the speed playing now: two parts, in random order, the setting and values not shown.</summary>
	[RelayCommand]
	private void StartTrial()
	{
		if (Experiments is null || IsTrialRunning)
			return;
		StopTraining();
		FollowToRate = null;
		var (parameter, first, second) = Experiments.NextTrial(Speed, AudioBackend.NonlinearAvailable);
		trialParameter = parameter;
		trialFirst = first;
		trialSecond = second;
		trialFirstFollow = trialSecondFollow = 0;
		trialSyllables = 0;
		trialSpent = TimeSpan.Zero;
		beforeTrial = (AudioBackend.Scaling, AudioBackend.UseNonlinear, AudioBackend.Nonlinearity);
		ApplyTrialValue(first);
		BeginTrialPart(1);
		if (!player.IsPlaying)
			PlayPause();
	}

	private void BeginTrialPart(int part)
	{
		TrialPart = part;
		trialListened = TimeSpan.Zero;
		UpdateTrialStatus();
	}

	/// <summary>Called with each stretch of listening, from CountListening.</summary>
	private void TrialTick(TimeSpan listened)
	{
		if (!IsTrialPlaying)
			return;
		trialListened += listened;
		trialSpent += listened;
		if (player.SourceSyllablesPerSecond is double rate)
			trialSyllables += rate * (listened * Speed).TotalSeconds;
		if (trialListened >= TrialPartLength)
		{
			if (player.IsPlaying)
				PlayPause();
			TrialPart = TrialPart == 1 ? 3 : 4;
		}
		UpdateTrialStatus();
	}

	private void UpdateTrialStatus()
	{
		var left = TrialPartLength - trialListened;
		TrialStatus = IsTrialPlaying
			? $"Blind trial, part {TrialPart} of 2 · {(int)Math.Max(0, left.TotalMinutes)}:{Math.Max(0, left.Seconds):00} left"
			: "Blind trial";
	}

	[RelayCommand]
	private void RateTrialPart(string score)
	{
		if (!int.TryParse(score, out var follow))
			return;
		if (TrialPart == 3)
		{
			trialFirstFollow = follow;
			ApplyTrialValue(trialSecond);
			BeginTrialPart(2);
			if (!player.IsPlaying)
				PlayPause();
		}
		else if (TrialPart == 4)
		{
			trialSecondFollow = follow;
			OnPropertyChanged(nameof(IsAskingTrialFollow));
			OnPropertyChanged(nameof(IsAskingPreference));
		}
	}

	/// <summary>"-1" part 1 was easier, "0" the same, "1" part 2.</summary>
	[RelayCommand]
	private void PreferTrialPart(string choice)
	{
		if (TrialPart != 4 || trialParameter is not { } parameter || !int.TryParse(choice, out var preferred))
			return;
		Experiments?.Add(new ExperimentTrial(DateTimeOffset.Now, Book.Id, Title, Speed, parameter.Key, trialFirst, trialSecond,
			trialFirstFollow, trialSecondFollow, preferred, trialSpent > TimeSpan.Zero && trialSyllables > 0 ? trialSyllables / trialSpent.TotalSeconds : null));
		RestoreAfterTrial();
		TrialResult = $"{parameter.Name}.\nPart 1: {parameter.Describe(trialFirst)}.\nPart 2: {parameter.Describe(trialSecond)}.";
		TrialPart = 5;
	}

	[RelayCommand]
	private void FinishTrial() => TrialPart = 0;

	[RelayCommand]
	private void CancelTrial()
	{
		if (!IsTrialRunning)
			return;
		RestoreAfterTrial();
		TrialPart = 0;
	}

	/// <summary>Play with one value of the setting under trial, on top of the settings in use.</summary>
	private void ApplyTrialValue(double value)
	{
		var speed = Speed;
		var current = beforeTrial.Scaling ?? new SpeedProfile("Trial", (float)speed, beforeTrial.UseNonlinear, beforeTrial.Nonlinearity,
			AudioBackend.PauseCap / (float)speed, 0, AudioBackend.SpeedFloor, 0, 0, AudioBackend.KeepSpeed, AudioBackend.RhythmGap, AudioBackend.RhythmRate);
		AudioBackend.UseNonlinear = beforeTrial.UseNonlinear;
		AudioBackend.Nonlinearity = beforeTrial.Nonlinearity;
		var v = (float)value;
		AudioBackend.Scaling = trialParameter?.Key switch
		{
			"heardPause" => current with { HeardPause = v, PauseFrom = 0 },
			"floor" => current with { Floor = v, FloorFrom = 0, FloorFull = 0 },
			"rhythm" => current with { RhythmGap = v },
			_ => current,
		};
		if (trialParameter?.Key == "method")
			AudioBackend.UseNonlinear = value >= 1;
		else if (trialParameter?.Key == "nonlinearity")
			AudioBackend.Nonlinearity = v;
	}

	private void RestoreAfterTrial()
	{
		AudioBackend.Scaling = beforeTrial.Scaling;
		AudioBackend.UseNonlinear = beforeTrial.UseNonlinear;
		AudioBackend.Nonlinearity = beforeTrial.Nonlinearity;
	}

	#endregion
}
