using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibationMobile.iOS.Kokoro;

/// <summary>
/// Kokoro-82M, the neural voices of the HSC library (af_heart and the rest), run on the phone with ONNX Runtime. Text
/// goes to phonemes through eSpeak, as kokoro.js and kokoro-onnx do, then through the model with the voice's style.
/// 24 kHz mono.
/// </summary>
public sealed partial class KokoroEngine : IDisposable
{
	public const int SAMPLE_RATE = 24000;
	private const int MAX_TOKENS = 510;
	private const int STYLE_SIZE = 256;

	private readonly InferenceSession session;
	private readonly Dictionary<char, long> vocab;
	private readonly Dictionary<string, float[]> voices = [];
	private readonly string directory;

	/// <param name="directory">Holds the model (model_fp16.onnx), tokenizer.json and voices/NAME.bin.</param>
	/// <param name="threads">CPU threads; 0 for one a core.</param>
	/// <param name="accelerator">"cpu", or "coreml" for Apple's Neural Engine and GPU where the model allows.</param>
	public KokoroEngine(string directory, string model = "model_fp16.onnx", int threads = 0, string accelerator = "cpu")
	{
		this.directory = directory;
		var options = new SessionOptions { IntraOpNumThreads = threads > 0 ? threads : Environment.ProcessorCount, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
		if (accelerator == "coreml")
			options.AppendExecutionProvider("CoreML", new Dictionary<string, string> { ["ModelFormat"] = "MLProgram", ["MLComputeUnits"] = "ALL" });
		session = new InferenceSession(Path.Combine(directory, model), options);
		using var tokenizer = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "tokenizer.json")));
		vocab = tokenizer.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()
			.Where(p => p.Name.Length == 1)
			.ToDictionary(p => p.Name[0], p => (long)p.Value.GetInt32());
	}

	private float[] Voice(string name)
	{
		if (!voices.TryGetValue(name, out var style))
			voices[name] = style = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(Path.Combine(directory, "voices", name + ".bin"))).ToArray();
		return style;
	}

	/// <summary>Speak <paramref name="text"/> (a sentence or a few) in <paramref name="voice"/>, such as "af_heart".</summary>
	public float[] Speak(string text, string voice, float speed = 1)
	{
		var language = voice.StartsWith('b') ? "en" : "en-us";
		var audio = new List<float>();
		foreach (var chunk in Chunks(text))
		{
			var ids = Phonemize(chunk, language).Select(c => vocab.TryGetValue(c, out var id) ? id : -1).Where(id => id >= 0).Take(MAX_TOKENS).ToList();
			if (ids.Count == 0)
				continue;
			var input = new DenseTensor<long>([1, ids.Count + 2]);
			for (var i = 0; i < ids.Count; i++)
				input[0, i + 1] = ids[i];
			var style = new DenseTensor<float>(Voice(voice).AsMemory(Math.Min(ids.Count, 509) * STYLE_SIZE, STYLE_SIZE), [1, STYLE_SIZE]);
			using var results = session.Run(
			[
				NamedOnnxValue.CreateFromTensor("input_ids", input),
				NamedOnnxValue.CreateFromTensor("style", style),
				NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, [1])),
			]);
			audio.AddRange(results[0].AsEnumerable<float>());
		}
		return audio.ToArray();
	}

	/// <summary>Sentences grouped so each stays well within the model's 510 tokens.</summary>
	private static IEnumerable<string> Chunks(string text)
	{
		var chunk = new StringBuilder();
		foreach (var sentence in SentenceEnd().Split(text).Where(s => s.Trim().Length > 0))
		{
			if (chunk.Length + sentence.Length > 300 && chunk.Length > 0)
			{
				yield return chunk.ToString();
				chunk.Clear();
			}
			chunk.Append(sentence.Trim()).Append(' ');
		}
		if (chunk.Length > 0)
			yield return chunk.ToString();
	}

	/// <summary>eSpeak's IPA between the punctuation, which is kept for the model's phrasing, adjusted as kokoro.js does.</summary>
	private static string Phonemize(string text, string language)
	{
		var result = new StringBuilder();
		var last = 0;
		foreach (Match m in Punctuation().Matches(text))
		{
			if (m.Index > last)
				result.Append(Espeak.Phonemes(text[last..m.Index], language));
			result.Append(m.Value);
			last = m.Index + m.Length;
		}
		if (last < text.Length)
			result.Append(Espeak.Phonemes(text[last..], language));
		var ps = result.ToString().Replace("ʲ", "j").Replace("r", "ɹ").Replace("x", "k").Replace("ɬ", "l");
		ps = Hundred().Replace(ps, " ");
		ps = LoneZ().Replace(ps, "z");
		if (language == "en-us")
			ps = Ninety().Replace(ps, "di");
		return ps.Trim();
	}

	public void Dispose() => session.Dispose();

	[GeneratedRegex(@"(?<=[.!?…])\s+|\n{2,}")]
	private static partial Regex SentenceEnd();
	[GeneratedRegex(@"(\s*[;:,.!?¡¿—…""«»“”(){}\[\]]+\s*)+")]
	private static partial Regex Punctuation();
	[GeneratedRegex(@"(?<=[a-zɹː])(?=hˈʌndɹɪd)")]
	private static partial Regex Hundred();
	[GeneratedRegex(@" z(?=[;:,.!?¡¿—…""«»“” ]|$)")]
	private static partial Regex LoneZ();
	[GeneratedRegex(@"(?<=nˈaɪn)ti(?!ː)")]
	private static partial Regex Ninety();
}
