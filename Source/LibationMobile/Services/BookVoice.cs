using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A voice the phone can read with.</summary>
/// <param name="Quality">"Premium", "Enhanced" or "Default".</param>
public record VoiceChoice(string Id, string Name, string Quality, string Language)
{
	public string Label => Quality == "Default" ? $"{Name} ({Language})" : $"{Name}, {Quality} ({Language})";
}

/// <summary>
/// Reads text into audio files with the platform's own voices, at their normal pace; the player speeds the result up
/// with speechwarp like any other book.
/// </summary>
public interface IBookVoice
{
	/// <summary>Voices for the language, best first.</summary>
	IReadOnlyList<VoiceChoice> Voices(string language = "en");

	/// <summary>Read the paragraphs, with a pause after each, into an audio file at <paramref name="path"/>. Returns its length.</summary>
	Task<TimeSpan> RenderAsync(IReadOnlyList<string> paragraphs, string voiceId, string path, IProgress<double>? progress, CancellationToken token);

	/// <summary>Join audio files, in order, into one at <paramref name="path"/>.</summary>
	Task JoinAsync(IReadOnlyList<string> parts, string path, CancellationToken token);
}

public static class BookVoice
{
	/// <summary>Set by the platform head. Null where books cannot be voiced.</summary>
	public static IBookVoice? Platform { get; set; }
}
