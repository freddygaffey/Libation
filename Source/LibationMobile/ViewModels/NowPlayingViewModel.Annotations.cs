using AudibleApi.Common;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>A bookmark or clip in the list.</summary>
public class AnnotationRowViewModel(IRecord record, string? chapterTitle)
{
	public IRecord Record { get; } = record;
	public bool IsClip => Record is Clip;
	public TimeSpan Start => Record.Start;
	public TimeSpan? End => (Record as Clip)?.End;

	public string Title => Record is Clip clip
		? string.IsNullOrWhiteSpace(clip.Title) ? "Clip" : clip.Title
		: "Bookmark";

	/// <summary>Where it is, and for a clip how long.</summary>
	public string DetailText
	{
		get
		{
			var where = NowPlayingViewModel.FormatTime(Start) + (chapterTitle is null ? "" : $" in {chapterTitle}");
			return End is TimeSpan end ? $"{where}, {(end - Start).TotalSeconds:0}s" : where;
		}
	}
}

// Position sync, bookmarks and clips, all kept in the user's Audible account.
public partial class NowPlayingViewModel
{
	private static readonly TimeSpan ClipLength = TimeSpan.FromSeconds(30);
	/// <summary>How often the position goes to Audible while playing. It is also sent on pause and on leaving the book.</summary>
	private static readonly TimeSpan RemoteSaveInterval = TimeSpan.FromMinutes(1);
	/// <summary>A position from another device only wins if it is newer than ours by more than clock jitter, and actually elsewhere.</summary>
	private static readonly TimeSpan SyncTimeMargin = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan SyncMinimumDistance = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(6);

	public ObservableCollection<AnnotationRowViewModel> Annotations { get; } = new();
	public bool HasAnnotations => Annotations.Count > 0;

	[ObservableProperty]
	private bool isAnnotationListOpen;

	/// <summary>A short note about something that just happened, shown for a few seconds.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatus))]
	private string? statusMessage;

	public bool HasStatus => StatusMessage is not null;

	/// <summary>Set after the position jumped to the one from another device, until the note about it goes away.</summary>
	[ObservableProperty]
	private bool canUndoSync;

	private AudibleAnnotations? annotations;
	private DispatcherTimer? statusTimer;
	private DateTime lastRemoteSave = DateTime.UtcNow;
	private TimeSpan positionBeforeSync;
	private TimeSpan? clipEnd;

	private void StartAnnotations(AudibleAnnotations? service)
	{
		annotations = service;
		Annotations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAnnotations));
		_ = LoadAnnotationsAsync(syncPosition: true);
	}

	private async Task LoadAnnotationsAsync(bool syncPosition)
	{
		if (annotations is null)
			return;

		BookAnnotations result;
		try
		{
			result = await annotations.GetAsync(Book.Id);
		}
		catch (Exception)
		{
			// Offline, or Audible is unreachable: carry on from the position saved on this device.
			return;
		}
		if (disposed)
			return;

		// Creating a clip also creates a bookmark at the same place; show only the clip.
		var clipStarts = result.Clips.Select(c => c.Start).ToHashSet();
		var rows = result.Clips.Cast<IRecord>()
			.Concat(result.Bookmarks.Where(b => !clipStarts.Contains(b.Start)))
			.OrderBy(r => r.Start)
			.Select(r => new AnnotationRowViewModel(r, HasChapters ? Chapters.LastOrDefault(c => c.StartOffset <= r.Start)?.Title : null));
		Annotations.Clear();
		foreach (var row in rows)
			Annotations.Add(row);

		if (syncPosition && result.LastHeard is LastHeard remote)
			SyncPositionFrom(remote);
	}

	/// <summary>Jump to where another device left off, if that is more recent than what this device has.</summary>
	private void SyncPositionFrom(LastHeard remote)
	{
		// A record at the very start means "not started there", not "rewind to the beginning".
		if (remote.Start < SyncMinimumDistance || remote.Start >= Duration - FinishedThreshold)
			return;

		// A position saved here with no record of when (from before times were kept) must not lose to Audible's:
		// its age is unknown, and moving away from it loses the listener's place.
		var hasLocalPosition = settings.GetPosition(Book.Id) is TimeSpan local && local > TimeSpan.Zero;
		var localTime = settings.GetPositionTime(Book.Id);
		if (hasLocalPosition && localTime is null)
			return;

		var newer = remote.Created > (localTime ?? DateTimeOffset.MinValue) + SyncTimeMargin;
		var elsewhere = (remote.Start - Position).Duration() > SyncMinimumDistance;
		if (!newer || !elsewhere)
			return;

		positionBeforeSync = Position;
		Seek(remote.Start);
		SavePosition();
		ShowStatus($"Moved to {FormatTime(remote.Start)}, where you left off on another device.", canUndoSync: true);
	}

	[RelayCommand]
	private void UndoSync()
	{
		if (!CanUndoSync)
			return;
		Seek(positionBeforeSync);
		SavePosition();
		ShowStatus($"Back at {FormatTime(positionBeforeSync)}.");
	}

	/// <summary>Send the position to Audible. Failures are ignored: the next one replaces it.</summary>
	private void PushPosition()
	{
		if (annotations is null)
			return;
		lastRemoteSave = DateTime.UtcNow;
		var position = player.Position;
		_ = Task.Run(async () =>
		{
			try
			{
				await annotations.SetLastHeardAsync(Book.Id, position);
			}
			catch (Exception)
			{
				// Offline: the position is still saved on this device.
			}
		});
	}

	[RelayCommand]
	private async Task AddBookmark()
	{
		var at = Position;
		if (await ChangeAnnotationsAsync(a => a.AddBookmarkAsync(Book.Id, at)))
			ShowStatus($"Bookmark added at {FormatTime(at)}.");
	}

	/// <summary>Save what was just heard: the last 30 seconds up to now.</summary>
	[RelayCommand]
	private async Task AddClip()
	{
		var end = Position;
		var start = end - ClipLength < TimeSpan.Zero ? TimeSpan.Zero : end - ClipLength;
		if (end - start < TimeSpan.FromSeconds(1))
		{
			ShowStatus("Play a little first, then clip what you just heard.");
			return;
		}
		if (await ChangeAnnotationsAsync(a => a.AddClipAsync(Book.Id, start, end, null)))
			ShowStatus($"Clipped the last {(end - start).TotalSeconds:0} seconds.");
	}

	[RelayCommand]
	private async Task DeleteAnnotation(AnnotationRowViewModel row)
	{
		await ChangeAnnotationsAsync(async a =>
		{
			if (!await a.DeleteAsync(Book.Id, row.Record))
				return false;
			// A clip's companion bookmark can only go once the clip has.
			if (row.Record is Clip clip)
				await a.DeleteAsync(Book.Id, new Bookmark(clip.Created, clip.Start, null, clip.LastModified));
			return true;
		});
	}

	/// <summary>Jump to a bookmark, or play a clip from its start and stop at its end.</summary>
	[RelayCommand]
	private void OpenAnnotation(AnnotationRowViewModel row)
	{
		Seek(row.Start);
		clipEnd = row.End;
		IsAnnotationListOpen = false;
		if (!player.IsPlaying)
			PlayPause();
	}

	[RelayCommand]
	private void ShowAnnotations()
	{
		IsAnnotationListOpen = true;
		_ = LoadAnnotationsAsync(syncPosition: false);
	}

	[RelayCommand]
	private void HideAnnotations() => IsAnnotationListOpen = false;

	private async Task<bool> ChangeAnnotationsAsync(Func<AudibleAnnotations, Task<bool>> change)
	{
		if (annotations is null)
			return false;
		try
		{
			if (!await change(annotations))
			{
				ShowStatus("Audible did not accept that. Try again.");
				return false;
			}
		}
		catch (Exception)
		{
			ShowStatus("That needs a connection to Audible.");
			return false;
		}
		await LoadAnnotationsAsync(syncPosition: false);
		return true;
	}

	/// <summary>Called on each tick: stop at the end of a clip being played, and sync the position now and then.</summary>
	private void UpdateAnnotations()
	{
		if (clipEnd is TimeSpan end && Position >= end)
		{
			clipEnd = null;
			if (player.IsPlaying)
				PlayPause();
		}
		if (IsPlaying && DateTime.UtcNow - lastRemoteSave > RemoteSaveInterval)
			PushPosition();
	}

	private void ShowStatus(string message, bool canUndoSync = false)
	{
		StatusMessage = message;
		CanUndoSync = canUndoSync;
		statusTimer?.Stop();
		statusTimer = new DispatcherTimer(StatusDuration, DispatcherPriority.Normal, (_, _) =>
		{
			statusTimer?.Stop();
			StatusMessage = null;
			CanUndoSync = false;
		});
		statusTimer.Start();
	}
}
