using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace LibationMobile.Services;

/// <summary>One stretch of listening, from pressing play to pausing.</summary>
/// <param name="From">Where in the book it started.</param>
/// <param name="To">Where in the book it stopped.</param>
/// <param name="BookSeconds">How much of the book was heard, at normal speed.</param>
/// <param name="SpentSeconds">How long the listening took.</param>
public record ListeningSession(
	string BookId,
	string Title,
	DateTimeOffset Started,
	DateTimeOffset Ended,
	TimeSpan From,
	TimeSpan To,
	double BookSeconds,
	double SpentSeconds,
	float Speed);

/// <summary>Every listening session on this device, newest last, saved as JSON in the app's data folder.</summary>
public class ListeningLog
{
	private readonly string path;
	private readonly Lock locker = new();
	private readonly List<ListeningSession> sessions;

	public ListeningLog(string path)
	{
		this.path = path;
		sessions = Load(path);
	}

	public IReadOnlyList<ListeningSession> Sessions
	{
		get { lock (locker) return sessions.ToList(); }
	}

	public void Add(ListeningSession session)
	{
		lock (locker)
		{
			sessions.Add(session);
			var temp = path + ".tmp";
			File.WriteAllText(temp, JsonSerializer.Serialize(sessions, ListeningLogJsonContext.Default.ListListeningSession));
			File.Move(temp, path, overwrite: true);
		}
	}

	public void Clear()
	{
		lock (locker)
		{
			sessions.Clear();
			File.Delete(path);
		}
	}

	private static List<ListeningSession> Load(string path)
	{
		try
		{
			if (File.Exists(path))
				return JsonSerializer.Deserialize(File.ReadAllText(path), ListeningLogJsonContext.Default.ListListeningSession) ?? [];
		}
		catch (JsonException)
		{
			// A damaged log is started again rather than stopping the app.
		}
		return [];
	}
}

[JsonSerializable(typeof(List<ListeningSession>))]
internal partial class ListeningLogJsonContext : JsonSerializerContext
{
}
