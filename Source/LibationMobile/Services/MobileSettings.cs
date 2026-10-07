using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		public float Nonlinearity { get; set; } = 1f;
		public bool UseNonlinearSpeed { get; set; } = true;
		public string? LibrarySort { get; set; }
		public bool ScrubByChapter { get; set; } = true;
		public Dictionary<string, float> BookSpeeds { get; set; } = new();
		public double BookSecondsHeard { get; set; }
		public double SecondsSpentListening { get; set; }
		public DateTimeOffset? ListeningCountedSince { get; set; }
		public float BaselineSpeed { get; set; } = 1f;
		/// <summary>When each downloaded book's download was asked for, so a new download sorts as just listened to.</summary>
		public Dictionary<string, DateTimeOffset> DownloadTimes { get; set; } = new();
		public bool Training { get; set; }
		public float TrainingStartSpeed { get; set; } = 3f;
		public float TrainingCeiling { get; set; } = 10f;
		public float TrainingRestartMinutes { get; set; } = 10f;
		public float PauseCap { get; set; }
		public bool KeepSpeed { get; set; } = true;
		public float SpeedFloor { get; set; }
		public float RhythmGap { get; set; }
		public float RhythmRate { get; set; } = 6f;
		public float TrainingStep { get; set; } = 0.1f;
		public float TrainingMinutes { get; set; } = 2f;
		public bool TrainingClimb { get; set; }
		public bool ShowSyllableRate { get; set; } = true;
		public Dictionary<string, SpeedProfile> Profiles { get; set; } = new();
		public string? ActiveProfile { get; set; }
	}

	public MobileSettings(string path)
	{
		this.path = path;
		state = Load(path);
		AudioBackend.Nonlinearity = state.Nonlinearity;
		AudioBackend.UseNonlinear = state.UseNonlinearSpeed;
		AudioBackend.PauseCap = state.PauseCap;
		AudioBackend.KeepSpeed = state.KeepSpeed;
		AudioBackend.SpeedFloor = state.SpeedFloor;
		AudioBackend.RhythmGap = state.RhythmGap;
		AudioBackend.RhythmRate = state.RhythmRate;
	}

	/// <summary>Speed up with speechwarp (true) or with the original even method (false).</summary>
	public bool UseNonlinearSpeed
	{
		get { lock (locker) return state.UseNonlinearSpeed; }
		set
		{
			lock (locker) { state.UseNonlinearSpeed = value; Save(); }
			AudioBackend.UseNonlinear = value;
		}
	}

	/// <summary>How unevenly speech is sped up, 0 (evenly) to 1. See <see cref="AudioBackend.Nonlinearity"/>.</summary>
	public float Nonlinearity
	{
		get { lock (locker) return state.Nonlinearity; }
		set
		{
			lock (locker) { state.Nonlinearity = Math.Clamp(value, 0f, 1f); Save(); }
			AudioBackend.Nonlinearity = value;
		}
	}

	public float Speed
	{
		get { lock (locker) return state.Speed; }
		set { lock (locker) { state.Speed = value; Save(); } }
	}

	/// <summary>The speed this book was last played at. Null for a book not played here yet, which starts at <see cref="Speed"/>.</summary>
	public float? GetBookSpeed(string bookId)
	{
		lock (locker)
			return state.BookSpeeds.TryGetValue(bookId, out var speed) ? speed : null;
	}

	/// <summary>Remember this book's speed, and make it the speed new books start at.</summary>
	public void SetBookSpeed(string bookId, float speed)
	{
		lock (locker)
		{
			state.BookSpeeds[bookId] = speed;
			state.Speed = speed;
			Save();
		}
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

	/// <summary>Whether the player's progress bar covers the chapter playing now (true) or the whole book.</summary>
	public bool ScrubByChapter
	{
		get { lock (locker) return state.ScrubByChapter; }
		set { lock (locker) { state.ScrubByChapter = value; Save(); } }
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

	#region Time saved

	/// <summary>How much of books has been heard, in seconds of the book at normal speed.</summary>
	public TimeSpan BookTimeHeard { get { lock (locker) return TimeSpan.FromSeconds(state.BookSecondsHeard); } }

	/// <summary>How long that took to listen to.</summary>
	public TimeSpan TimeSpentListening { get { lock (locker) return TimeSpan.FromSeconds(state.SecondsSpentListening); } }

	/// <summary>When counting began. Null if nothing has been counted.</summary>
	public DateTimeOffset? ListeningCountedSince { get { lock (locker) return state.ListeningCountedSince; } }

	/// <summary>The speed time saved is measured against: what the listener would otherwise listen at.</summary>
	public float BaselineSpeed
	{
		get { lock (locker) return state.BaselineSpeed; }
		set { lock (locker) { state.BaselineSpeed = Math.Clamp(value, 1f, 3.5f); Save(); } }
	}

	public void AddListening(TimeSpan bookTime, TimeSpan timeSpent)
	{
		lock (locker)
		{
			state.ListeningCountedSince ??= DateTimeOffset.UtcNow;
			state.BookSecondsHeard += bookTime.TotalSeconds;
			state.SecondsSpentListening += timeSpent.TotalSeconds;
			Save();
		}
	}

	public void ResetListening()
	{
		lock (locker)
		{
			state.BookSecondsHeard = 0;
			state.SecondsSpentListening = 0;
			state.ListeningCountedSince = null;
			Save();
		}
	}

	#endregion

	#region Speed profiles

	/// <summary>The profiles, in order, with any the listener has changed: Casual, School, Hard and Max.</summary>
	public IReadOnlyList<SpeedProfile> Profiles
	{
		get
		{
			lock (locker)
				return SpeedProfile.Defaults.Select(d => state.Profiles.TryGetValue(d.Name, out var saved) ? saved with { Name = d.Name } : d).ToList();
		}
	}

	/// <summary>The profile chosen last. Null until one is chosen; changing a setting by hand does not clear it.</summary>
	public string? ActiveProfile
	{
		get { lock (locker) return state.ActiveProfile; }
	}

	/// <summary>Use a profile: its speed-up method and options, and its speed as the speed new books start at.</summary>
	public void ApplyProfile(SpeedProfile profile)
	{
		lock (locker)
		{
			state.ActiveProfile = profile.Name;
			state.Speed = profile.Speed;
			Save();
		}
		UseNonlinearSpeed = profile.UseNonlinear;
		Nonlinearity = profile.Nonlinearity;
		PauseCap = profile.PauseCap;
		KeepSpeed = profile.KeepSpeed;
		SpeedFloor = profile.SpeedFloor;
		RhythmGap = profile.RhythmGap;
		RhythmRate = profile.RhythmRate;
	}

	/// <summary>Keep the current settings, and this speed, as the named profile.</summary>
	public void SaveProfile(string name, float speed)
	{
		lock (locker)
		{
			state.Profiles[name] = new SpeedProfile(name, speed, state.UseNonlinearSpeed, state.Nonlinearity, state.PauseCap, state.KeepSpeed,
				state.SpeedFloor, state.RhythmGap, state.RhythmRate);
			state.ActiveProfile = name;
			Save();
		}
	}

	/// <summary>Put a profile back to how it came.</summary>
	public void ResetProfile(string name)
	{
		lock (locker)
		{
			if (state.Profiles.Remove(name))
				Save();
		}
	}

	#endregion

	#region Speed listening

	/// <summary>
	/// Training mode: each session starts <see cref="TrainingStartBelow"/> under the book's speed and rises by
	/// <see cref="TrainingStep"/> every <see cref="TrainingMinutes"/> of listening until it gets there. Listeners adapt to
	/// fast speech within minutes, and the adaptation fades between sessions; see speechwarp's docs/research-high-speed.md.
	/// </summary>
	public bool Training
	{
		get { lock (locker) return state.Training; }
		set { lock (locker) { state.Training = value; Save(); } }
	}

	/// <summary>Where the warm-up starts: a speed already comfortable without one. A book set at or below it has no warm-up.</summary>
	public float TrainingStartSpeed
	{
		get { lock (locker) return state.TrainingStartSpeed; }
		set { lock (locker) { state.TrainingStartSpeed = Math.Clamp(value, 1f, 10f); Save(); } }
	}

	/// <summary>The fastest that keep climbing goes.</summary>
	public float TrainingCeiling
	{
		get { lock (locker) return state.TrainingCeiling; }
		set { lock (locker) { state.TrainingCeiling = Math.Clamp(value, 1f, 10f); Save(); } }
	}

	/// <summary>A break longer than this starts the warm-up again; a shorter one carries on where it was.</summary>
	public float TrainingRestartMinutes
	{
		get { lock (locker) return state.TrainingRestartMinutes; }
		set { lock (locker) { state.TrainingRestartMinutes = Math.Clamp(value, 1f, 240f); Save(); } }
	}

	public float TrainingStep
	{
		get { lock (locker) return state.TrainingStep; }
		set { lock (locker) { state.TrainingStep = Math.Clamp(value, 0.05f, 1f); Save(); } }
	}

	public float TrainingMinutes
	{
		get { lock (locker) return state.TrainingMinutes; }
		set { lock (locker) { state.TrainingMinutes = Math.Clamp(value, 0.5f, 30f); Save(); } }
	}

	/// <summary>Keep rising past the book's speed, and keep the speed reached for next time, as Rightspeed did.</summary>
	public bool TrainingClimb
	{
		get { lock (locker) return state.TrainingClimb; }
		set { lock (locker) { state.TrainingClimb = value; Save(); } }
	}

	/// <summary>See <see cref="AudioBackend.PauseCap"/>.</summary>
	public float PauseCap
	{
		get { lock (locker) return state.PauseCap; }
		set { lock (locker) { state.PauseCap = value; Save(); } AudioBackend.PauseCap = value; }
	}

	/// <summary>See <see cref="AudioBackend.KeepSpeed"/>.</summary>
	public bool KeepSpeed
	{
		get { lock (locker) return state.KeepSpeed; }
		set { lock (locker) { state.KeepSpeed = value; Save(); } AudioBackend.KeepSpeed = value; }
	}

	/// <summary>See <see cref="AudioBackend.SpeedFloor"/>.</summary>
	public float SpeedFloor
	{
		get { lock (locker) return state.SpeedFloor; }
		set { lock (locker) { state.SpeedFloor = value; Save(); } AudioBackend.SpeedFloor = value; }
	}

	/// <summary>See <see cref="AudioBackend.RhythmGap"/>.</summary>
	public float RhythmGap
	{
		get { lock (locker) return state.RhythmGap; }
		set { lock (locker) { state.RhythmGap = value; Save(); } AudioBackend.RhythmGap = value; }
	}

	/// <summary>See <see cref="AudioBackend.RhythmRate"/>.</summary>
	public float RhythmRate
	{
		get { lock (locker) return state.RhythmRate; }
		set { lock (locker) { state.RhythmRate = value; Save(); } AudioBackend.RhythmRate = value; }
	}

	/// <summary>Show syllables a second under the speed in the player.</summary>
	public bool ShowSyllableRate
	{
		get { lock (locker) return state.ShowSyllableRate; }
		set { lock (locker) { state.ShowSyllableRate = value; Save(); } }
	}

	#endregion

	/// <summary>The order the library is listed in. Null is the newest purchase first.</summary>
	public string? LibrarySort
	{
		get { lock (locker) return state.LibrarySort; }
		set { lock (locker) { state.LibrarySort = value; Save(); } }
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

	/// <summary>When the book's download was asked for. Null if it is not downloaded, or was downloaded before this was recorded.</summary>
	public DateTimeOffset? GetDownloadTime(string bookId)
	{
		lock (locker)
			return state.DownloadTimes.TryGetValue(bookId, out var time) ? time : null;
	}

	/// <summary>Record a download asked for now, or forget it (null) when the download is removed or fails.</summary>
	public void SetDownloadTime(string bookId, DateTimeOffset? time)
	{
		lock (locker)
		{
			if (time is { } at)
				state.DownloadTimes[bookId] = at;
			else if (!state.DownloadTimes.Remove(bookId))
				return;
			Save();
		}
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
/// <summary>
/// A named set of speed settings to switch between, such as Casual for relaxed listening and Max for record attempts:
/// the speed books start at, the speed-up method, and the options for very high speeds.
/// </summary>
public record SpeedProfile(string Name, float Speed, bool UseNonlinear, float Nonlinearity, float PauseCap, bool KeepSpeed,
	float SpeedFloor, float RhythmGap, float RhythmRate)
{
	/// <summary>Starting points, each changeable with "Save current settings".</summary>
	public static readonly IReadOnlyList<SpeedProfile> Defaults =
	[
		// Relaxed: Speedy as designed, nothing else.
		new("Casual", 2f, true, 1f, 0f, true, 0f, 0f, 6f),
		// Study and textbooks: moderate speed, every part of the speech at least 60% of the speed, so nothing is rushed.
		new("School", 1.8f, true, 0.7f, 0f, true, 0.6f, 0f, 6f),
		// Fast and still comfortable: pauses shortened, the hardest sounds kept at half speed or more.
		new("Hard", 4f, true, 1f, 0.06f, true, 0.5f, 0f, 6f),
		// Record attempts: everything the research suggests at 6x and above, rhythm gaps included.
		new("Max", 7f, true, 1f, 0.04f, true, 0.5f, 0.04f, 6f),
	];
}

[JsonSerializable(typeof(MobileSettings.State))]
internal partial class MobileSettingsJsonContext : JsonSerializerContext
{
}
