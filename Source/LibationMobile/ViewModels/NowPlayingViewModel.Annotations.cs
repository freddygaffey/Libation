using AudibleApi.Common;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.ViewModels;

/// <summary>A bookmark or clip in the list: saved on this device, or found on Audible's annotation server.</summary>
public class AnnotationRowViewModel
{
	/// <summary>Set for one saved on this device.</summary>
	public LocalAnnotation? Local { get; }
	/// <summary>Set for one that exists only on Audible's server, e.g. made in another app.</summary>
	public IRecord? Server { get; }

	public TimeSpan Start { get; }
	public TimeSpan? End { get; }
	public bool IsClip => End is not null;
	public string Title { get; }
	/// <summary>Where it is, and for a clip how long.</summary>
	public string DetailText { get; }

	public AnnotationRowViewModel(LocalAnnotation local, string? chapterTitle)
		: this(local.Start, local.End, local.Title, chapterTitle) => Local = local;

	public AnnotationRowViewModel(IRecord server, string? chapterTitle)
		: this(server.Start, (server as Clip)?.End, (server as Clip)?.Title, chapterTitle) => Server = server;

	private AnnotationRowViewModel(TimeSpan start, TimeSpan? end, string? title, string? chapterTitle)
	{
		Start = start;
		End = end;
		Title = !string.IsNullOrWhiteSpace(title) ? title : end is null ? "Bookmark" : "Clip";
		var where = NowPlayingViewModel.FormatTime(start) + (chapterTitle is null ? "" : $" in {chapterTitle}");
		DetailText = end is TimeSpan clipEnd ? $"{where}, {(clipEnd - start).TotalSeconds:0}s" : where;
	}
}

// Position sync with Audible, and bookmarks and clips. See AudibleAnnotations for why the two differ.
public partial class NowPlayingViewModel
{
	private static readonly TimeSpan ClipLength = TimeSpan.FromSeconds(30);
	/// <summary>How often the position goes to Audible while playing. It is also sent on pause and on leaving the book.</summary>
	private static readonly TimeSpan RemoteSaveInterval = TimeSpan.FromMinutes(1);
	/// <summary>A position from another device only wins if it is newer than ours by more than clock jitter, and actually elsewhere.</summary>
	private static readonly TimeSpan SyncTimeMargin = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan SyncMinimumDistance = TimeSpan.FromSeconds(10);
	/// <summary>Two annotations this close together, of the same kind, are the same one seen in two places.</summary>
	private static readonly TimeSpan SameAnnotationTolerance = TimeSpan.FromSeconds(1);
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
	private LocalAnnotations? localAnnotations;
	private IReadOnlyList<IRecord> serverAnnotations = [];
	private DispatcherTimer? statusTimer;
	private DateTime lastRemoteSave = DateTime.UtcNow;
	private TimeSpan positionBeforeSync;
	private TimeSpan? clipEnd;

	private void StartAnnotations(AudibleAnnotations? service, LocalAnnotations? local)
	{
		annotations = service;
		localAnnotations = local;
		Annotations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAnnotations));
		ShowAnnotationRows();
		_ = SyncPositionAsync();
		_ = LoadServerAnnotationsAsync();
	}

	#region Position

	/// <summary>Jump to where another device left off, if that is more recent than what this device has.</summary>
	private async Task SyncPositionAsync()
	{
		if (annotations is null)
			return;

		RemotePosition? remote;
		try
		{
			remote = await annotations.GetPositionAsync(Book.Id);
		}
		catch (Exception)
		{
			// Offline, or Audible is unreachable: carry on from the position saved on this device.
			return;
		}
		if (disposed || remote is null)
			return;

		// A position at the very start means "not started there", not "rewind to the beginning".
		if (remote.Position < SyncMinimumDistance || remote.Position >= Duration - FinishedThreshold)
			return;

		// A position saved here with no record of when (from before times were kept) must not lose to Audible's:
		// its age is unknown, and moving away from it loses the listener's place.
		var hasLocalPosition = settings.GetPosition(Book.Id) is TimeSpan local && local > TimeSpan.Zero;
		var localTime = settings.GetPositionTime(Book.Id);
		if (hasLocalPosition && localTime is null)
			return;

		var newer = remote.Updated > (localTime ?? DateTimeOffset.MinValue) + SyncTimeMargin;
		var elsewhere = (remote.Position - Position).Duration() > SyncMinimumDistance;
		if (!newer || !elsewhere)
			return;

		positionBeforeSync = Position;
		Seek(remote.Position);
		SavePosition();
		ShowStatus($"Moved to {FormatTime(remote.Position)}, where you left off on another device.", canUndoSync: true);
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
		if (annotations is not { } service)
			return;
		lastRemoteSave = DateTime.UtcNow;
		var position = player.Position;
		_ = Task.Run(async () =>
		{
			try
			{
				await service.SetPositionAsync(Book.Id, position);
			}
			catch (Exception)
			{
				// Offline: the position is still saved on this device.
			}
		});
	}

	#endregion

	#region Bookmarks and clips

	[RelayCommand]
	private void AddBookmark()
	{
		var at = Position;
		SaveAnnotation(at, null);
		ShowStatus($"Bookmark added at {FormatTime(at)}.");
	}

	/// <summary>Save what was just heard: the last 30 seconds up to now.</summary>
	[RelayCommand]
	private void AddClip()
	{
		var end = Position;
		var start = end - ClipLength < TimeSpan.Zero ? TimeSpan.Zero : end - ClipLength;
		if (end - start < TimeSpan.FromSeconds(1))
		{
			ShowStatus("Play a little first, then clip what you just heard.");
			return;
		}
		SaveAnnotation(start, end);
		ShowStatus($"Clipped the last {(end - start).TotalSeconds:0} seconds.");
	}

	private void SaveAnnotation(TimeSpan start, TimeSpan? end)
	{
		localAnnotations?.Add(Book.Id, start, end);
		ShowAnnotationRows();

		// Also offer it to Audible's annotation server, which keeps it only for some books.
		if (annotations is { } service)
			_ = Task.Run(async () =>
			{
				try
				{
					await service.TryAddToServerAsync(Book.Id, start, end);
				}
				catch (Exception)
				{
					// Saved on this device regardless.
				}
			});
	}

	[RelayCommand]
	private async Task DeleteAnnotation(AnnotationRowViewModel row)
	{
		if (row.Local is { } local)
			localAnnotations?.Remove(Book.Id, local.Id);

		// Remove the server's copy too, whether the row came from there or was mirrored there.
		var onServer = row.Server ?? serverAnnotations.FirstOrDefault(r => IsSame(r.Start, (r as Clip)?.End, row.Start, row.End));
		if (onServer is not null && annotations is { } service)
		{
			try
			{
				await service.DeleteFromServerAsync(Book.Id, onServer);
				serverAnnotations = serverAnnotations.Where(r => r != onServer).ToList();
			}
			catch (Exception)
			{
				if (row.Local is null)
					ShowStatus("That one is in your Audible account. Deleting it needs a connection.");
			}
		}
		ShowAnnotationRows();
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
		_ = LoadServerAnnotationsAsync();
	}

	[RelayCommand]
	private void HideAnnotations() => IsAnnotationListOpen = false;

	private async Task LoadServerAnnotationsAsync()
	{
		if (annotations is null)
			return;
		try
		{
			serverAnnotations = await annotations.GetServerAnnotationsAsync(Book.Id);
		}
		catch (Exception)
		{
			// Offline: show what is saved on this device.
			return;
		}
		if (!disposed)
			ShowAnnotationRows();
	}

	/// <summary>Everything saved on this device, plus anything on the server that is not already among it.</summary>
	private void ShowAnnotationRows()
	{
		string? chapterAt(TimeSpan start) => HasChapters ? Chapters.LastOrDefault(c => c.StartOffset <= start)?.Title : null;

		var local = localAnnotations?.Get(Book.Id) ?? [];
		// Creating a clip on the server also creates a bookmark at the same place; show only the clip.
		var serverClipStarts = serverAnnotations.OfType<Clip>().Select(c => c.Start).ToHashSet();
		var serverOnly = serverAnnotations
			.Where(r => r is Clip || !serverClipStarts.Contains(r.Start))
			.Where(r => !local.Any(l => IsSame(l.Start, l.End, r.Start, (r as Clip)?.End)));

		var rows = local.Select(l => new AnnotationRowViewModel(l, chapterAt(l.Start)))
			.Concat(serverOnly.Select(r => new AnnotationRowViewModel(r, chapterAt(r.Start))))
			.OrderBy(r => r.Start);

		Annotations.Clear();
		foreach (var row in rows)
			Annotations.Add(row);
	}

	private static bool IsSame(TimeSpan startA, TimeSpan? endA, TimeSpan startB, TimeSpan? endB)
		=> (endA is null) == (endB is null) && (startA - startB).Duration() < SameAnnotationTolerance;

	#endregion

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
