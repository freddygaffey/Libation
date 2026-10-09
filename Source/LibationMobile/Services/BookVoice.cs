using AudioPlayer;
using System.Collections.Generic;

namespace LibationMobile.Services;

/// <summary>A voice the phone can read with.</summary>
/// <param name="Quality">"Premium", "Enhanced", "Eloquence" or "Default".</param>
public record VoiceChoice(string Id, string Name, string Quality, string Language)
{
	public string Label => Quality == "Default" ? $"{Name} ({Language})" : $"{Name}, {Quality} ({Language})";
}

/// <summary>
/// Text read aloud as it plays, a little ahead of what is heard, by one of the phone's voices. The player speeds it up
/// like any other book. Its timeline is the text's characters at a fixed pace, so places and chapters stay put
/// whichever voice reads.
/// </summary>
public interface IVoicedSource : ILiveSource
{
	string VoiceId { get; }

	/// <summary>Carry on in another voice from the sentence being heard.</summary>
	void SetVoice(string voiceId);
}

/// <summary>The phone's voices, and text read aloud with them.</summary>
public interface IBookVoice
{
	/// <summary>Voices for the language, best first.</summary>
	IReadOnlyList<VoiceChoice> Voices(string language = "en");

	/// <param name="secondsPerCharacter">The timeline's pace: a character is this long.</param>
	/// <param name="textPath">Where the text is kept, so a slow voice can read ahead of it later, as overnight.</param>
	IVoicedSource Open(string text, string voiceId, double secondsPerCharacter, string? textPath = null);

	/// <summary>A slow voice (Kokoro): ask for the book to be read ahead to disk while the phone charges.</summary>
	void VoiceAhead(string textPath, string voiceId);

	/// <summary>Whether the neural voices (Kokoro, the HSC library's narrators) are on the phone.</summary>
	bool HasNeuralVoices { get; }

	/// <summary>Download the neural voices' model and voices, about 160 MB. Progress 0 to 1.</summary>
	System.Threading.Tasks.Task InstallNeuralVoicesAsync(System.IProgress<double> progress, System.Threading.CancellationToken token);
}

public static class BookVoice
{
	/// <summary>Set by the platform head. Null where books cannot be voiced.</summary>
	public static IBookVoice? Platform { get; set; }

	/// <summary>
	/// The timeline's pace: about 17 characters a second, the phone's voices at their normal rate (Karen measured 18). Fixed, so a saved
	/// place is the same character whatever the voice.
	/// </summary>
	public const double SECONDS_PER_CHARACTER = 1 / 17.0;
}
