using AudioToolbox;
using AVFoundation;
using CoreMedia;
using Foundation;
using LibationMobile.Services;
using Speechwarp.Voice;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>The phone's own voices, Eloquence among them, and text read aloud with them by speechwarp's voice module.</summary>
public sealed class AppleBookVoice : IBookVoice
{
	public IReadOnlyList<VoiceChoice> Voices(string language = "en")
	{
		var local = NSLocale.CurrentLocale.Identifier.Replace('_', '-');
		return SpeechVoice.All
			.Where(v => v.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase))
			.Select(v => (Voice: v, System: AVSpeechSynthesisVoice.FromIdentifier(v.Identifier)))
			// Novelty voices (Bells, Bubbles, Whisper) are no way to hear a book.
			.Where(v => v.System is not null && (!OperatingSystem.IsIOSVersionAtLeast(17) || !v.System.VoiceTraits.HasFlag(AVSpeechSynthesisVoiceTraits.IsNoveltyVoice)))
			.Select(v => (v.Voice, Quality: v.Voice.IsEloquence ? "Eloquence" : v.System!.Quality switch
			{
				AVSpeechSynthesisVoiceQuality.Premium => "Premium",
				AVSpeechSynthesisVoiceQuality.Enhanced => "Enhanced",
				_ => "Default",
			}))
			// Best sounding first; Eloquence, which stays clear at high speed, next.
			.OrderBy(v => v.Quality switch { "Premium" => 0, "Enhanced" => 1, "Eloquence" => 2, _ => 3 })
			.ThenByDescending(v => v.Voice.Language == local)
			.ThenBy(v => v.Voice.Name)
			.Select(v => new VoiceChoice(v.Voice.Identifier, v.Voice.Name, v.Quality, v.Voice.Language))
			.ToList();
	}

	public IVoicedSource Open(string text, string voiceId, double secondsPerCharacter)
		=> new SpokenTextSource(text, voiceId, secondsPerCharacter);
}
