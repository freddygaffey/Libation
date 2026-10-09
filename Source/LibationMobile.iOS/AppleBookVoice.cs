using AudioToolbox;
using AVFoundation;
using CoreMedia;
using Foundation;
using LibationMobile.Services;
using Speechwarp.Voice;
using LibationMobile.iOS.Voicing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>
/// The voices a book can be read in, and text read aloud with them as it plays: the HSC library's Kokoro narrators
/// (af_heart first) where their model is on the phone, its eSpeak "Eloquence" accents, then the phone's own voices.
/// </summary>
public sealed class AppleBookVoice : IBookVoice
{
	/// <summary>The HSC library's narrators, its default first.</summary>
	private static readonly string[] KokoroVoices = ["af_heart", "am_onyx", "am_santa", "bf_isabella", "bm_daniel", "bm_george"];

	/// <summary>The HSC library's eSpeak accents, at its settings, which it called "Eloquence".</summary>
	private static readonly (string Name, string Code)[] EspeakAccents =
	[
		("American", "en-us"), ("New York", "en-us-nyc"), ("British", "en-gb"), ("Received", "en-gb-x-rp"),
		("Scottish", "en-gb-scotland"), ("Lancaster", "en-gb-x-gbclan"), ("West Midlands", "en-gb-x-gbcwmd"), ("Caribbean", "en-029"),
	];

	public IReadOnlyList<VoiceChoice> Voices(string language = "en")
	{
		var voices = new List<VoiceChoice>();
		foreach (var kokoro in KokoroVoices.Where(KokoroSentenceVoice.IsInstalled))
			voices.Add(new VoiceChoice("kokoro:" + kokoro, char.ToUpperInvariant(kokoro[3]) + kokoro[4..], "Kokoro", kokoro[0] == 'b' ? "en-GB" : "en-US"));
		foreach (var (name, code) in EspeakAccents)
			voices.Add(new VoiceChoice("espeak:" + code, "Eloquence " + name, "Default", code));
		var local = NSLocale.CurrentLocale.Identifier.Replace('_', '-');
		voices.AddRange(SpeechVoice.All
			.Where(v => v.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase))
			.Select(v => (Voice: v, System: AVSpeechSynthesisVoice.FromIdentifier(v.Identifier)))
			// Novelty voices (Bells, Bubbles, Whisper) are no way to hear a book.
			.Where(v => v.System is not null && (!OperatingSystem.IsIOSVersionAtLeast(17) || !v.System.VoiceTraits.HasFlag(AVSpeechSynthesisVoiceTraits.IsNoveltyVoice)))
			.Select(v => (v.Voice, Quality: v.Voice.IsEloquence ? "Apple Eloquence" : v.System!.Quality switch
			{
				AVSpeechSynthesisVoiceQuality.Premium => "Premium",
				AVSpeechSynthesisVoiceQuality.Enhanced => "Enhanced",
				_ => "Default",
			}))
			.OrderBy(v => v.Quality switch { "Premium" => 0, "Enhanced" => 1, "Apple Eloquence" => 2, _ => 3 })
			.ThenByDescending(v => v.Voice.Language == local)
			.ThenBy(v => v.Voice.Name)
			.Select(v => new VoiceChoice(v.Voice.Identifier, v.Voice.Name, v.Quality, v.Voice.Language)));
		return voices;
	}

	public void VoiceAhead(string textPath, string voiceId)
	{
		if (voiceId.StartsWith("kokoro:"))
			OvernightVoicing.Ask(new OvernightVoicing.Job(textPath, voiceId, 0));
	}

	public bool HasNeuralVoices => KokoroVoices.All(KokoroSentenceVoice.IsInstalled);

	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
	private const string KOKORO_SOURCE = "https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/main/";

	/// <summary>Kokoro-82M (half precision) and the HSC narrators' voices, from Hugging Face into Documents/Kokoro.</summary>
	public async Task InstallNeuralVoicesAsync(IProgress<double> progress, CancellationToken token)
	{
		var files = new List<(string From, string To)> { ("onnx/model_fp16.onnx", "model_fp16.onnx"), ("tokenizer.json", "tokenizer.json") };
		files.AddRange(KokoroVoices.Select(v => ($"voices/{v}.bin", $"voices/{v}.bin")));
		Directory.CreateDirectory(Path.Combine(KokoroSentenceVoice.Directory, "voices"));
		for (var i = 0; i < files.Count; i++)
		{
			var target = Path.Combine(KokoroSentenceVoice.Directory, files[i].To);
			if (File.Exists(target))
				continue;
			using var response = await Http.GetAsync(KOKORO_SOURCE + files[i].From, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			var total = response.Content.Headers.ContentLength ?? 0;
			var index = i;
			await using (var input = await response.Content.ReadAsStreamAsync(token))
			await using (var output = File.Create(target + ".part"))
			{
				var buffer = new byte[1 << 16];
				long done = 0;
				int read;
				while ((read = await input.ReadAsync(buffer, token)) > 0)
				{
					await output.WriteAsync(buffer.AsMemory(0, read), token);
					done += read;
					// The model is nearly all of it.
					if (index == 0 && total > 0)
						progress.Report(0.95 * done / total);
				}
			}
			File.Move(target + ".part", target, overwrite: true);
			progress.Report(index == 0 ? 0.95 : 0.95 + 0.05 * index / files.Count);
		}
		progress.Report(1);
	}

	public IVoicedSource Open(string text, string voiceId, double secondsPerCharacter, string? textPath = null)
		=> new LiveVoicedSource(text, voiceId, secondsPerCharacter, Make, textPath);

	/// <summary>The engine for a voice: "kokoro:af_heart", "espeak:en-us", or one of Apple's identifiers.</summary>
	private static ISentenceVoice Make(string voiceId) => voiceId switch
	{
		_ when voiceId.StartsWith("kokoro:") => new KokoroSentenceVoice(voiceId["kokoro:".Length..]),
		_ when voiceId.StartsWith("espeak:") => new EspeakSentenceVoice(voiceId["espeak:".Length..]),
		_ => new AppleSentenceVoice(voiceId),
	};
}
