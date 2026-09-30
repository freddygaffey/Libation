using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace LibationMobile.Services;

/// <summary>Playback speed and each book's resume position, saved as JSON in the app's data folder.</summary>
public class MobileSettings
{
	private const float DEFAULT_SPEED = 1f;

	private readonly string path;
	private readonly Lock locker = new();
	private State state;

	internal class State
	{
		public float Speed { get; set; } = DEFAULT_SPEED;
		public Dictionary<string, TimeSpan> Positions { get; set; } = new();
		/// <summary>When each position was saved, to compare with the one Audible holds from other devices.</summary>
		public Dictionary<string, DateTimeOffset> PositionTimes { get; set; } = new();
		/// <summary>Audible's content reference for each downloaded book, needed to report a position.</summary>
		public Dictionary<string, string> ContentReferences { get; set; } = new();
		public string? LastBookId { get; set; }
		public string? RegionName { get; set; }
		public int SkipSeconds { get; set; } = 30;
		public int ClipSeconds { get; set; } = 30;
		public bool SyncPosition { get; set; } = true;
		public bool HighQualityDownloads { get; set; } = true;
	}

	public MobileSettings(string path)
	{
		this.path = path;
		state = Load(path);
	}

	public float Speed
	{
		get { lock (locker) return state.Speed; }
		set { lock (locker) { state.Speed = value; Save(); } }
	}

	/// <summary>The book that was last opened, to restore the mini player on launch.</summary>
	public string? LastBookId
	{
		get { lock (locker) return state.LastBookId; }
		set { lock (locker) { state.LastBookId = value; Save(); } }
	}

	/// <summary>The Audible marketplace the account signed in to. Null when signed out.</summary>
	public string? RegionName
	{
		get { lock (locker) return state.RegionName; }
		set { lock (locker) { state.RegionName = value; Save(); } }
	}

	/// <summary>How far the skip buttons move, in seconds.</summary>
	public int SkipSeconds
	{
		get { lock (locker) return state.SkipSeconds; }
		set { lock (locker) { state.SkipSeconds = value; Save(); } }
	}

	/// <summary>How long a new clip starts out, in seconds.</summary>
	public int ClipSeconds
	{
		get { lock (locker) return state.ClipSeconds; }
		set { lock (locker) { state.ClipSeconds = value; Save(); } }
	}

	/// <summary>Whether the listening position is sent to Audible and taken from it.</summary>
	public bool SyncPosition
	{
		get { lock (locker) return state.SyncPosition; }
		set { lock (locker) { state.SyncPosition = value; Save(); } }
	}

	/// <summary>Download the larger, better-sounding file. Applies to books downloaded from now on.</summary>
	public bool HighQualityDownloads
	{
		get { lock (locker) return state.HighQualityDownloads; }
		set { lock (locker) { state.HighQualityDownloads = value; Save(); } }
	}

	public TimeSpan? GetPosition(string bookId)
	{
		lock (locker)
			return state.Positions.TryGetValue(bookId, out var position) ? position : null;
	}

	/// <param name="savedAt">When the position was reached. Defaults to now; pass Audible's time for one taken from there.</param>
	public void SetPosition(string bookId, TimeSpan position, DateTimeOffset? savedAt = null)
	{
		lock (locker)
		{
			state.Positions[bookId] = position;
			state.PositionTimes[bookId] = savedAt ?? DateTimeOffset.UtcNow;
			Save();
		}
	}

	/// <summary>Take positions from Audible for books where they are newer than what is saved here.</summary>
	/// <returns>The number of books whose position changed.</returns>
	public int MergeRemotePositions(IReadOnlyDictionary<string, RemotePosition> remote, TimeSpan margin, Func<string, bool> skip)
	{
		var changed = 0;
		lock (locker)
		{
			foreach (var (bookId, position) in remote)
			{
				// A position at the very start means "not started there".
				if (skip(bookId) || position.Position < TimeSpan.FromSeconds(10))
					continue;
				var hasLocal = state.Positions.TryGetValue(bookId, out var local) && local > TimeSpan.Zero;
				var hasTime = state.PositionTimes.TryGetValue(bookId, out var localTime);
				// A local position of unknown age is never overridden.
				if (hasLocal && !hasTime)
					continue;
				if (position.Updated <= (hasTime ? localTime : DateTimeOffset.MinValue) + margin || (hasLocal && (position.Position - local).Duration() < TimeSpan.FromSeconds(10)))
					continue;
				state.Positions[bookId] = position.Position;
				state.PositionTimes[bookId] = position.Updated;
				changed++;
			}
			if (changed > 0)
				Save();
		}
		return changed;
	}

	/// <summary>Audible's content reference (ACR) for a book, from its download license. Null if not known yet.</summary>
	public string? GetContentReference(string bookId)
	{
		lock (locker)
			return state.ContentReferences.TryGetValue(bookId, out var acr) ? acr : null;
	}

	public void SetContentReference(string bookId, string acr)
	{
		lock (locker)
		{
			state.ContentReferences[bookId] = acr;
			Save();
		}
	}

	/// <summary>When the position was last saved on this device. Null if never, or saved before this was recorded.</summary>
	public DateTimeOffset? GetPositionTime(string bookId)
	{
		lock (locker)
			return state.PositionTimes.TryGetValue(bookId, out var time) ? time : null;
	}

	public void RemovePosition(string bookId)
	{
		lock (locker)
		{
			state.PositionTimes.Remove(bookId);
			if (state.Positions.Remove(bookId))
				Save();
		}
	}

	private static State Load(string path)
	{
		try
		{
			if (File.Exists(path))
				return JsonSerializer.Deserialize(File.ReadAllText(path), MobileSettingsJsonContext.Default.State) ?? new();
		}
		catch (JsonException)
		{
			// A corrupt settings file loses positions, not the app. It is rewritten on the next save.
		}
		return new();
	}

	private void Save()
	{
		var temp = path + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(state, MobileSettingsJsonContext.Default.State));
		File.Move(temp, path, overwrite: true);
	}
}

/// <summary>Source-generated serializer, so release builds can trim reflection metadata safely.</summary>
[JsonSerializable(typeof(MobileSettings.State))]
internal partial class MobileSettingsJsonContext : JsonSerializerContext
{
}
