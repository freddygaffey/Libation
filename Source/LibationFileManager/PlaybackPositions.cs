using Dinah.Core.IO;
using FileManager;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LibationFileManager;

/// <summary>
/// Where each audiobook was last paused in the built-in player, keyed by product id.
/// Kept out of Settings.json because the player saves positions every few seconds while playing,
/// and every Settings.json write is logged.
/// </summary>
public static class PlaybackPositions
{
	private const string FILENAME = "PlaybackPositions.json";

	private static LongPath jsonFile => Path.Combine(Configuration.Instance.LibationFiles.Location, FILENAME);

	private static readonly Lock locker = new();
	private static Dictionary<string, TimeSpan>? cache;

	public static TimeSpan? Get(string productId)
	{
		lock (locker)
			return Load().TryGetValue(productId, out var position) ? position : null;
	}

	public static void Set(string productId, TimeSpan position)
	{
		lock (locker)
		{
			var positions = Load();
			if (positions.TryGetValue(productId, out var existing) && existing == position)
				return;

			positions[productId] = position;
			Save(positions);
		}
	}

	public static void Remove(string productId)
	{
		lock (locker)
		{
			if (Load().Remove(productId))
				Save(cache!);
		}
	}

	private static Dictionary<string, TimeSpan> Load()
	{
		if (cache is not null)
			return cache;

		try
		{
			if (File.Exists(jsonFile))
				cache = JsonConvert.DeserializeObject<Dictionary<string, TimeSpan>>(File.ReadAllText(jsonFile));
		}
		catch (Exception ex)
		{
			// Losing resume positions is not worth failing playback over. The file is rewritten on the next save.
			Serilog.Log.Logger.Error(ex, "Error reading playback positions. Starting with none. {@DebugInfo}", new { jsonFile });
		}

		return cache ??= new();
	}

	private static void Save(Dictionary<string, TimeSpan> positions)
	{
		try
		{
			AtomicFileWriter.WriteAllText(jsonFile, JsonConvert.SerializeObject(positions, Formatting.Indented));
		}
		catch (Exception ex)
		{
			Serilog.Log.Logger.Error(ex, "Error saving playback positions. {@DebugInfo}", new { jsonFile });
		}
	}
}
