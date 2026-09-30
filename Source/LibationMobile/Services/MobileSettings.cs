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
		public string? LastBookId { get; set; }
		public string? RegionName { get; set; }
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

	public TimeSpan? GetPosition(string bookId)
	{
		lock (locker)
			return state.Positions.TryGetValue(bookId, out var position) ? position : null;
	}

	public void SetPosition(string bookId, TimeSpan position)
	{
		lock (locker)
		{
			state.Positions[bookId] = position;
			state.PositionTimes[bookId] = DateTimeOffset.UtcNow;
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
