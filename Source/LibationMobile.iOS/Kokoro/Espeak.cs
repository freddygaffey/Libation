using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LibationMobile.iOS.Kokoro;

/// <summary>
/// eSpeak NG, linked into the app: text to phonemes (IPA) for Kokoro, and speech in its own formant voices. Not
/// thread-safe in itself, so calls take turns.
/// </summary>
public static unsafe class Espeak
{
	private const string Lib = "__Internal";
	/// <summary>ENOUTPUT_MODE_SYNCHRONOUS: sound handed back, never played.</summary>
	private const int OUTPUT_SYNCHRONOUS = 1;
	private const int CHARS_UTF8 = 1;
	private const int PHONEMES_IPA = 2;

	// The espeak_ng_ calls report errors; the older espeak_Initialize ends the whole app on one.
	[DllImport(Lib)] private static extern void espeak_ng_InitializePath(string path);
	[DllImport(Lib)] private static extern int espeak_ng_Initialize(IntPtr context);
	[DllImport(Lib)] private static extern int espeak_ng_InitializeOutput(int mode, int bufferLength, IntPtr device);
	[DllImport(Lib)] private static extern int espeak_SetVoiceByName(string name);
	[DllImport(Lib)] private static extern IntPtr espeak_TextToPhonemes(ref IntPtr textptr, int textmode, int phonememode);

	[DllImport(Lib)] private static extern void espeak_SetSynthCallback(delegate* unmanaged<short*, int, IntPtr, int> callback);
	[DllImport(Lib)] private static extern int espeak_Synth(byte* text, nuint size, uint position, int positionType, uint endPosition, uint flags, IntPtr uniqueIdentifier, IntPtr userData);
	[DllImport(Lib)] private static extern int espeak_SetParameter(int parameter, int value, int relative);
	[DllImport(Lib)] private static extern int espeak_ng_GetSampleRate();
	private const int RATE = 1, PITCH = 3;

	private static readonly Lock locker = new();
	private static List<short>? synthesized;
	[UnmanagedCallersOnly]
	private static int OnSynth(short* wav, int samples, IntPtr events)
	{
		if (wav != null && samples > 0)
			synthesized?.AddRange(new ReadOnlySpan<short>(wav, samples));
		return 0;
	}

	/// <summary>The rate eSpeak speaks at, 22,050 Hz.</summary>
	public static int SampleRate { get { lock (locker) { Start(); return espeak_ng_GetSampleRate(); } } }

	/// <summary>
	/// <paramref name="text"/> spoken by eSpeak in the accent <paramref name="language"/>, at the HSC library's settings
	/// (175 words a minute, pitch 50): the formant voices it called "Eloquence".
	/// </summary>
	public static float[] Speak(string text, string language, int wordsPerMinute = 175, int pitch = 50)
	{
		lock (locker)
		{
			Start();
			Use(language);
			espeak_SetParameter(RATE, wordsPerMinute, 0);
			espeak_SetParameter(PITCH, pitch, 0);
			synthesized = [];
			var bytes = Encoding.UTF8.GetBytes(text + "\0");
			fixed (byte* start = bytes)
				espeak_Synth(start, (nuint)bytes.Length, 0, 1, 0, CHARS_UTF8, IntPtr.Zero, IntPtr.Zero);
			var result = new float[synthesized.Count];
			for (var i = 0; i < result.Length; i++)
				result[i] = synthesized[i] / 32768f;
			synthesized = null;
			return result;
		}
	}

	private static void Start()
	{
		if (initialized)
			return;
		espeak_ng_InitializePath(DataPath);
		if (espeak_ng_Initialize(IntPtr.Zero) is var status and not 0)
			throw new InvalidOperationException($"eSpeak could not start with its data at {DataPath} (status {status:X}).");
		espeak_ng_InitializeOutput(OUTPUT_SYNCHRONOUS, 0, IntPtr.Zero);
		espeak_SetSynthCallback(&OnSynth);
		initialized = true;
	}

	private static void Use(string language)
	{
		if (voice == language)
			return;
		espeak_SetVoiceByName(language);
		voice = language;
	}
	private static string? voice;
	private static bool initialized;

	/// <summary>The data bundled with the app: English, in each of eSpeak's accents.</summary>
	public static string DataPath => System.IO.Path.Combine(Foundation.NSBundle.MainBundle.ResourcePath!, "espeak-ng-data");

	/// <summary>The phonemes of <paramref name="text"/> in IPA, clause by clause, for the accent <paramref name="language"/> ("en-us", "en").</summary>
	public static string Phonemes(string text, string language = "en-us")
	{
		lock (locker)
		{
			Start();
			Use(language);
			var bytes = Encoding.UTF8.GetBytes(text + "\0");
			var result = new StringBuilder();
			fixed (byte* start = bytes)
			{
				var at = (IntPtr)start;
				while (at != IntPtr.Zero)
				{
					var clause = espeak_TextToPhonemes(ref at, CHARS_UTF8, PHONEMES_IPA);
					if (clause == IntPtr.Zero)
						continue;
					if (result.Length > 0)
						result.Append(' ');
					result.Append(Marshal.PtrToStringUTF8(clause));
				}
			}
			return result.ToString();
		}
	}
}
