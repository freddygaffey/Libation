#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LibationMobile.iOS.Kokoro;

/// <summary>Debug builds only: Kokoro's speed on this device, on the newest voiced book's text. Nothing is played.</summary>
public static class KokoroBench
{
	public static void Run(string setting)
	{
		try
		{
			var parts = setting.Split(':');
			var seconds = int.Parse(parts[0]);
			var voice = parts.Length > 1 ? parts[1] : "af_heart";
			var model = parts.Length > 2 ? parts[2] : "model_fp16.onnx";
			var threads = parts.Length > 3 ? int.Parse(parts[3]) : 0;
			var accelerator = parts.Length > 4 ? parts[4] : "cpu";
			var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Kokoro");
			// A text put beside the model, or else the newest voiced book's.
			var textFile = File.Exists(Path.Combine(dir, "text.txt")) ? Path.Combine(dir, "text.txt")
				: Directory.EnumerateFiles(Path.Combine(data, "Voiced"), "text.txt", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
			var text = File.ReadAllText(textFile);
			Console.WriteLine($"LIBATION_TEST kokoro: phonemes test: {Espeak.Phonemes("Hello world, this is Blindsight.")}");
			var watch = Stopwatch.StartNew();
			using var engine = new KokoroEngine(dir, model, threads, accelerator);
			Console.WriteLine($"LIBATION_TEST kokoro: {model}, {threads} threads, {accelerator}: loaded in {watch.ElapsedMilliseconds} ms, {Environment.ProcessorCount} cores");
			watch.Restart();
			double audio = 0;
			var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
			foreach (var paragraph in paragraphs)
			{
				audio += engine.Speak(paragraph, voice).Length / (double)KokoroEngine.SAMPLE_RATE;
				Console.WriteLine($"LIBATION_TEST kokoro: {watch.Elapsed.TotalSeconds:0}s, {audio:0}s of audio, {audio / watch.Elapsed.TotalSeconds:0.00}x real time");
				if (watch.Elapsed.TotalSeconds > seconds)
					break;
			}
			Console.WriteLine($"LIBATION_TEST kokoro: done, {audio / watch.Elapsed.TotalSeconds:0.00}x real time");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"LIBATION_TEST kokoro: failed: {ex}");
		}
	}
}
#endif
