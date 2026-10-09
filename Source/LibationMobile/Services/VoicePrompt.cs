using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>
/// Asks a question aloud and listens for a spoken number, so a question can be answered with the phone in a pocket.
/// Speech is recognised on the phone where it can be.
/// </summary>
public interface IVoicePrompt
{
	/// <summary>Microphone and speech recognition: "allowed", "denied", "not asked" or "unavailable".</summary>
	string Access { get; }

	/// <summary>Ask for the microphone and speech recognition, as the listener turns voice prompts on. True if both are allowed.</summary>
	Task<bool> RequestAccessAsync();

	/// <summary>
	/// Say the question, then listen a few seconds for a number from 0 to <paramref name="max"/>. Null when nothing
	/// usable was heard, "skip" was said, or it could not listen. Playback must be paused.
	/// </summary>
	Task<int?> AskNumberAsync(string question, int max, CancellationToken cancellation);
}

public static class VoicePrompt
{
	/// <summary>Set by the platform head at startup. Null where there is none.</summary>
	public static IVoicePrompt? Platform { get; set; }

	/// <summary>
	/// The number in what was heard: a digit or a word, allowing for the usual mishearings ("to" for 2, "for" for 4).
	/// Null for nothing usable, or for "skip".
	/// </summary>
	public static int? ParseNumber(string? heard, int max)
	{
		if (string.IsNullOrWhiteSpace(heard))
			return null;
		foreach (var raw in heard.ToLowerInvariant().Split([' ', ',', '.', '!', '?', '-'], System.StringSplitOptions.RemoveEmptyEntries))
		{
			int? number = raw switch
			{
				"skip" or "cancel" or "pass" => -1,
				"0" or "zero" or "nought" or "naught" or "nil" or "none" or "nothing" or "oh" => 0,
				"1" or "one" or "won" => 1,
				"2" or "two" or "to" or "too" => 2,
				"3" or "three" or "free" or "tree" => 3,
				"4" or "four" or "for" or "fore" => 4,
				"5" or "five" => 5,
				_ => null,
			};
			// "to" and "for" are also ordinary words: only count them when they are all that was said.
			if (raw is "to" or "too" or "for" or "fore" or "oh" or "won" or "free" or "tree" && heard.Trim().Contains(' '))
				continue;
			if (number == -1)
				return null;
			if (number is int n && n <= max)
				return n;
		}
		return null;
	}
}
