using BackgroundTasks;
using Foundation;
using Speechwarp.Voice;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS.Voicing;

/// <summary>
/// A slow voice (Kokoro) reads the book being listened to ahead to disk while the phone charges, as iOS allows: a
/// background processing task, usually overnight. Picks up where it left off; what it speaks the player then uses.
/// </summary>
public static class OvernightVoicing
{
	public const string TASK_ID = "io.github.freddygaffey.libation.voicing";

	/// <summary>What to voice: written when a book is opened in a slow voice, read by the task.</summary>
	public record Job(string TextPath, string VoiceId, int FromCharacter);

	private static string JobPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache), "Voiced", "job.json");

	/// <summary>At launch, before it finishes: tell iOS what to run.</summary>
	public static void Register()
	{
		BGTaskScheduler.Shared.Register(TASK_ID, null, task => Run((BGProcessingTask)task));
	}

	/// <summary>Voice this book in this voice from here on, when the phone next charges.</summary>
	public static void Ask(Job job)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(JobPath)!);
			File.WriteAllText(JobPath, JsonSerializer.Serialize(job));
			Schedule();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Overnight voicing not asked for: {ex.Message}");
		}
	}

	private static void Schedule()
	{
		var request = new BGProcessingTaskRequest(TASK_ID) { RequiresExternalPower = true, RequiresNetworkConnectivity = false };
#pragma warning disable CA1422 // The replacement is iOS 27's; this works from 16.
		if (!BGTaskScheduler.Shared.Submit(request, out var error))
#pragma warning restore CA1422
			Console.WriteLine($"Overnight voicing not scheduled: {error?.LocalizedDescription}");
	}

	private static void Run(BGProcessingTask task)
	{
		using var stop = new CancellationTokenSource();
		task.ExpirationHandler = stop.Cancel;
		var finished = false;
		try
		{
			finished = VoiceAhead(stop.Token);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Overnight voicing stopped: {ex.Message}");
		}
		// Not done: again at the next chance.
		if (!finished)
			Schedule();
		task.SetTaskCompleted(finished);
	}

	/// <summary>Speak every block from the job's place to the end that is not on disk. True when the book is done.</summary>
	public static bool VoiceAhead(CancellationToken token, TimeSpan? limit = null)
	{
		if (!File.Exists(JobPath) || JsonSerializer.Deserialize<Job>(File.ReadAllText(JobPath)) is not { } job || !File.Exists(job.TextPath))
			return true;
		var text = File.ReadAllText(job.TextPath);
		var sentences = SpokenText.Split(text);
		var cache = new VoiceCache(text, job.VoiceId);
		using var voice = new KokoroSentenceVoice(job.VoiceId["kokoro:".Length..]);
		var first = Math.Max(0, sentences.ToList().FindLastIndex(s => s.Start <= job.FromCharacter)) / VoiceCache.BLOCK;
		var blocks = (sentences.Count + VoiceCache.BLOCK - 1) / VoiceCache.BLOCK;
		var started = DateTime.UtcNow;
		// From the listener's place to the end, then the start up to it.
		foreach (var block in Enumerable.Range(first, blocks - first).Concat(Enumerable.Range(0, first)))
		{
			if (cache.Has(block))
				continue;
			var parts = new System.Collections.Generic.List<float[]>();
			for (var i = block * VoiceCache.BLOCK; i < Math.Min(sentences.Count, (block + 1) * VoiceCache.BLOCK); i++)
			{
				if (token.IsCancellationRequested || DateTime.UtcNow - started > limit)
					return false;
				parts.Add(voice.Speak(sentences[i].Text));
			}
			cache.Save(block, parts, voice.SampleRate);
			Console.WriteLine($"Overnight voicing: block {block + 1} of {blocks} kept");
		}
		return true;
	}
}
