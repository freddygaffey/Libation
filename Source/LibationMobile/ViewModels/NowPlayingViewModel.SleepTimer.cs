using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;

namespace LibationMobile.ViewModels;

/// <summary>
/// The sleep timer: pause after a set time of listening, or at the end of the chapter. It counts only while playing,
/// fades over the last seconds, and a touch, a nod or a shake of the head in that time starts it again.
/// </summary>
public partial class NowPlayingViewModel
{
	private static readonly TimeSpan TimerFadeLength = TimeSpan.FromSeconds(10);

	/// <summary>Minutes offered; 0 is the end of the chapter.</summary>
	public static readonly int[] SleepTimerChoices = [5, 10, 15, 30, 45, 60, 90, 0];

	private TimeSpan? timerLength;
	private TimeSpan timerLeft;
	private bool timerToChapterEnd;
	/// <summary>For the end of the chapter: where in the book to pause.</summary>
	private TimeSpan timerStopAt;
	private DateTime? lastTimerTick;
	private DateTimeOffset? timerFadeStarted;
	private double volumeBeforeTimerFade = 1;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSleepTimerOn))]
	private string sleepTimerText = "Sleep";

	public bool IsSleepTimerOn => timerLength is not null || timerToChapterEnd;

	[RelayCommand]
	private void SetSleepTimer(string minutes)
	{
		StopTimerFade();
		if (minutes == "off")
		{
			timerLength = null;
			timerToChapterEnd = false;
			LogEvent("sleep-timer", detail: "off");
		}
		else if (minutes == "0")
		{
			timerLength = null;
			timerToChapterEnd = true;
			timerStopAt = ChapterEndAfter(Position);
			LogEvent("sleep-timer", detail: "end of chapter");
		}
		else if (int.TryParse(minutes, out var m))
		{
			timerToChapterEnd = false;
			timerLength = timerLeft = TimeSpan.FromMinutes(m);
			LogEvent("sleep-timer", m);
		}
		lastTimerTick = null;
		ShowTimer();
	}

	/// <summary>From Update, four times a second.</summary>
	private void SleepTimerTick()
	{
		if (!IsSleepTimerOn)
			return;
		var now = DateTime.UtcNow;
		if (IsPlaying && lastTimerTick is DateTime last && now - last < LongestCountedTick && timerLength is not null)
			timerLeft -= now - last;
		lastTimerTick = IsPlaying ? now : null;

		// Time to the end: of the timer, or of the chapter at the speed playing.
		TimeSpan? left = timerLength is not null ? timerLeft : (timerStopAt - Position) / Math.Max(Speed, 0.1);
		if (left is not TimeSpan remaining || !IsPlaying)
		{
			ShowTimer();
			return;
		}
		if (remaining <= TimeSpan.Zero)
		{
			StopTimerFade();
			if (player.IsPlaying)
				PlayPause();
			LogEvent("sleep-timer", detail: "paused");
			timerLength = null;
			timerToChapterEnd = false;
			ShowTimer();
			return;
		}
		if (remaining <= TimerFadeLength)
		{
			if (timerFadeStarted is null)
			{
				timerFadeStarted = DateTimeOffset.Now;
				volumeBeforeTimerFade = Volume;
			}
			// A nod or a shake of the head starts the timer again.
			var context = ListeningContext.Platform;
			if (context?.HeadGestured is DateTimeOffset nodded && nodded > timerFadeStarted)
			{
				RestartSleepTimer();
				return;
			}
			Volume = volumeBeforeTimerFade * Math.Max(0.05, remaining / TimerFadeLength);
			SleepFadeText = $"Sleep timer: pausing in {Math.Ceiling(remaining.TotalSeconds):0} s. "
				+ (context?.HeadLastMoved is not null ? "Nod, shake your head or tap to keep listening." : "Tap to keep listening.");
		}
		ShowTimer(remaining);
	}

	/// <summary>Start the timer again from its full length, as after a touch while it fades.</summary>
	private void RestartSleepTimer()
	{
		StopTimerFade();
		if (timerLength is TimeSpan length)
			timerLeft = length;
		else if (timerToChapterEnd)
			// Carry on to the end of the next chapter.
			timerStopAt = ChapterEndAfter(timerStopAt + TimeSpan.FromSeconds(1));
		LogEvent("sleep-timer", detail: "restarted");
	}

	private void StopTimerFade()
	{
		if (timerFadeStarted is null)
			return;
		timerFadeStarted = null;
		Volume = volumeBeforeTimerFade;
		SleepFadeText = "";
	}

	/// <summary>The end of the chapter playing at this point in the book; the end of the book without chapters.</summary>
	private TimeSpan ChapterEndAfter(TimeSpan at)
	{
		foreach (var chapter in Chapters)
			if (chapter.StartOffset + chapter.Duration > at)
				return chapter.StartOffset + chapter.Duration;
		return Duration;
	}

	private void ShowTimer(TimeSpan? remaining = null)
	{
		SleepTimerText = !IsSleepTimerOn ? "Sleep"
			: timerToChapterEnd && (remaining is null || !IsPlaying) ? "End of chapter"
			: $"{FormatTime(remaining ?? timerLeft)}";
		OnPropertyChanged(nameof(IsSleepTimerOn));
	}
}
