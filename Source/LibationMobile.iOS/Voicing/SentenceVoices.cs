using LibationMobile.iOS.Kokoro;
using Speechwarp.Voice;
using System;
using System.IO;

namespace LibationMobile.iOS.Voicing;

/// <summary>An engine that speaks one sentence at a time, at its own sample rate. Called from one thread at a time.</summary>
public interface ISentenceVoice : IDisposable
{
	int SampleRate { get; }
	float[] Speak(string sentence);

	/// <summary>Slower than listening: what it speaks is kept on disk (<see cref="VoiceCache"/>), and spoken ahead.</summary>
	bool KeepsSpoken => false;
}

/// <summary>Apple's voices, through speechwarp's voice module. They render on the main thread; this waits from another.</summary>
public sealed class AppleSentenceVoice(string identifier) : ISentenceVoice
{
	private readonly SpeechRenderer renderer = new(SpeechVoice.Find(identifier) ?? throw new InvalidOperationException("That voice is no longer on this phone."));
	private int sampleRate = 22050;

	public int SampleRate => sampleRate;

	public float[] Speak(string sentence)
	{
		var speech = renderer.RenderAsync(sentence).GetAwaiter().GetResult();
		if (speech.Samples.Length > 0)
			sampleRate = speech.SampleRate;
		return speech.Samples;
	}

	public void Dispose() => renderer.Dispose();
}

/// <summary>eSpeak NG's formant voices in one accent: the HSC library's "Eloquence" tracks.</summary>
public sealed class EspeakSentenceVoice(string language) : ISentenceVoice
{
	public int SampleRate { get; } = Espeak.SampleRate;
	public float[] Speak(string sentence) => Espeak.Speak(sentence, language);
	public void Dispose() { }
}

/// <summary>A Kokoro voice, as the HSC library's neural narrators.</summary>
public sealed class KokoroSentenceVoice(string voice) : ISentenceVoice
{
	private static KokoroEngine? shared;
	private static readonly System.Threading.Lock locker = new();

	/// <summary>Where the model and voices are kept: Documents/Kokoro.</summary>
	public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Kokoro");

	public static bool IsInstalled(string voice) => File.Exists(Path.Combine(Directory, "model_fp16.onnx")) && File.Exists(Path.Combine(Directory, "voices", voice + ".bin"));

	public int SampleRate => KokoroEngine.SAMPLE_RATE;
	public bool KeepsSpoken => true;

	/// <summary>On the GPU where the app and phone can, which is faster; otherwise ONNX Runtime on the CPU.</summary>
	public float[] Speak(string sentence)
	{
		if (KokoroGpu.IsAvailable(Directory))
		{
			try
			{
				return KokoroGpu.Speak(Directory, Path.Combine(Directory, "voices", voice + ".bin"), sentence, voice.StartsWith('b'));
			}
			catch (KokoroGpu.GpuUnavailableException)
			{
			}
		}
		lock (locker)
		{
			shared ??= new KokoroEngine(Directory);
			return shared.Speak(sentence, voice);
		}
	}

	public void Dispose() { }
}
