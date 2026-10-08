using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>
/// Auto-pause when asleep: after a while with no touch, and no movement of the head (through headphones that report it)
/// or of the phone, the sound fades over half a minute; a touch stops the fade, otherwise it pauses and remembers where
/// the listener last touched it, to go back to. Apple Health's sleep, from a watch, refines the place afterwards
/// (Services/SleepFinder.cs).
/// </summary>
public partial class NowPlayingViewModel
{
	private static readonly TimeSpan SleepCheckInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan FadeLength = TimeSpan.FromSeconds(30);

	private DateTimeOffset lastTouch = DateTimeOffset.Now;
	private TimeSpan positionAtTouch;
	private DateTime nextSleepCheck;
	private bool checkingSleep;
	private bool watchingHead;
	private bool watchingForSleep;
	private DateTimeOffset? fadeStarted;
	private double volumeBeforeFade = 1;
	/// <summary>Set as auto-pause pauses, for the session it ends.</summary>
	private (DateTimeOffset At, TimeSpan Position)? fellAsleep;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsFadingForSleep))]
	private string sleepFadeText = "";

	public bool IsFadingForSleep => SleepFadeText.Length > 0;

	/// <summary>After an auto-pause: where to go back to.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasAsleepPlace))]
	private string asleepPlaceText = "";

	public bool HasAsleepPlace => AsleepPlaceText.Length > 0;
	private TimeSpan asleepPlace;

	/// <summary>Something the listener did, from LogEvent: not changes made by a plan, a trial or a sync.</summary>
	private void NoteTouch(string kind, string? detail)
	{
		var touched = kind switch
		{
			"play" or "pause" or "skip" or "mode" or "profile" or "plan" => true,
			"speed" => detail is "you" or "siri" or "widget",
			"seek" => detail is not "sync",
			_ => false,
		};
		if (!touched)
			return;
		lastTouch = DateTimeOffset.Now;
		positionAtTouch = Position;
		if (fadeStarted is not null && kind != "pause")
			StopFade();
		if (timerFadeStarted is not null && kind != "pause")
			RestartSleepTimer();
	}

	[RelayCommand]
	private void KeepListening()
	{
		lastTouch = DateTimeOffset.Now;
		positionAtTouch = Position;
		StopFade();
		if (timerFadeStarted is not null)
			RestartSleepTimer();
	}

	/// <summary>From Update, four times a second.</summary>
	private void SleepTick()
	{
		var watch = IsPlaying && settings.AutoPauseAsleep && (!settings.AutoPauseNightOnly || SleepFinder.IsNight(DateTime.Now));
		// The head is followed for auto-pause, and while a sleep timer runs, for a nod to keep it going.
		var followHead = watch || IsPlaying && IsSleepTimerOn;
		if (followHead != watchingHead)
		{
			watchingHead = followHead;
			ListeningContext.Platform?.WatchHead(followHead);
		}
		if (watch != watchingForSleep)
		{
			watchingForSleep = watch;
			if (watch)
			{
				lastTouch = DateTimeOffset.Now;
				positionAtTouch = Position;
			}
		}
		if (!watch)
		{
			if (fadeStarted is not null)
				StopFade();
			return;
		}
		if (fadeStarted is DateTimeOffset started)
		{
			Fade(DateTimeOffset.Now - started);
			return;
		}
		// The sleep timer is fading already.
		if (timerFadeStarted is not null)
			return;
		if (DateTime.UtcNow < nextSleepCheck || checkingSleep)
			return;
		nextSleepCheck = DateTime.UtcNow + SleepCheckInterval;
		var idle = DateTimeOffset.Now - lastTouch;
		var threshold = TimeSpan.FromMinutes(settings.AutoPauseMinutes);
		if (idle < threshold)
			return;
		checkingSleep = true;
		_ = CheckSleepAsync(idle, threshold);
	}

	private async Task CheckSleepAsync(TimeSpan idle, TimeSpan threshold)
	{
		try
		{
			var context = ListeningContext.Platform;
			TimeSpan? headStill = context?.HeadLastMoved is DateTimeOffset moved ? DateTimeOffset.Now - moved : null;
			var phoneStill = headStill is null && context is not null ? await context.StillShareAsync(DateTimeOffset.Now - threshold, DateTimeOffset.Now) : null;
			if (SleepFinder.LooksAsleep(DateTimeOffset.Now - lastTouch, threshold, headStill, phoneStill) && IsPlaying && fadeStarted is null)
			{
				volumeBeforeFade = Volume;
				fadeStarted = DateTimeOffset.Now;
				LogEvent("sleep-fade", idle.TotalMinutes, headStill is not null ? "head still" : phoneStill is not null ? "phone still" : "no touch");
			}
		}
		finally
		{
			checkingSleep = false;
		}
	}

	private void Fade(TimeSpan gone)
	{
		// A nod or a shake of the head, or turning it, counts as a touch.
		var context = ListeningContext.Platform;
		if (context?.HeadGestured is DateTimeOffset nodded && nodded > fadeStarted
			|| context?.HeadLastMoved is DateTimeOffset moved && moved > fadeStarted)
		{
			KeepListening();
			return;
		}
		var left = FadeLength - gone;
		if (left > TimeSpan.Zero)
		{
			Volume = volumeBeforeFade * Math.Max(0.05, left / FadeLength);
			SleepFadeText = $"You seem to be asleep. Pausing in {Math.Ceiling(left.TotalSeconds):0} s. "
				+ (context?.HeadLastMoved is not null ? "Nod, shake your head or tap to keep listening." : "Tap to keep listening.");
			return;
		}
		fellAsleep = (lastTouch, positionAtTouch);
		asleepPlace = positionAtTouch;
		AsleepPlaceText = $"Paused as you seemed to be asleep. You last touched it at {lastTouch.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}, at {FormatTime(positionAtTouch)}.";
		LogEvent("asleep", positionAtTouch.TotalSeconds, "auto-pause");
		StopFade();
		if (player.IsPlaying)
			PlayPause();
	}

	private void StopFade()
	{
		if (fadeStarted is null)
			return;
		fadeStarted = null;
		Volume = volumeBeforeFade;
		SleepFadeText = "";
	}

	[RelayCommand]
	private void GoBackToAsleepPlace()
	{
		seekSource = "history";
		Seek(asleepPlace);
		AsleepPlaceText = "";
	}

	[RelayCommand]
	private void DismissAsleepPlace() => AsleepPlaceText = "";

	/// <summary>For EndSession: the sleep found as the session ended, if any, once.</summary>
	private (DateTimeOffset At, TimeSpan Position)? TakeFellAsleep()
	{
		var asleep = fellAsleep;
		fellAsleep = null;
		return asleep;
	}
}
