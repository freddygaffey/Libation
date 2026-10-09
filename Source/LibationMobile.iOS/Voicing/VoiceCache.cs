using AudioToolbox;
using AVFoundation;
using Foundation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LibationMobile.iOS.Voicing;

/// <summary>
/// Sentences already spoken by a slow voice (Kokoro), kept so they need not be spoken again: in blocks of
/// <see cref="BLOCK"/> sentences, each an AAC file with the sentences' lengths beside it, under Library/Caches, which
/// iOS may clear when space runs short (they are spoken again then). About 150 MB for a 10-hour book.
/// </summary>
public sealed class VoiceCache
{
	public const int BLOCK = 40;
	private const int BIT_RATE = 32000;
	private readonly string directory;

	public VoiceCache(string text, string voiceId)
	{
		var book = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
		directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache), "Voiced", book, voiceId.Replace(':', '-'));
	}

	private string AudioPath(int block) => Path.Combine(directory, $"{block:00000}.m4a");
	private string LengthsPath(int block) => Path.Combine(directory, $"{block:00000}.json");

	public bool Has(int block) => File.Exists(LengthsPath(block));

	/// <summary>The block's sentences, converted to <paramref name="sampleRate"/>; null if it is not kept or cannot be read.</summary>
	public float[][]? Load(int block, int sampleRate)
	{
		if (!Has(block))
			return null;
		try
		{
			var lengths = JsonSerializer.Deserialize<int[]>(File.ReadAllText(LengthsPath(block)))!;
			using var file = new AVAudioFile(NSUrl.FromFilename(AudioPath(block)), AVAudioCommonFormat.PCMFloat32, false, out var error);
			if (error is not null)
				throw new IOException(error.LocalizedDescription);
			var rate = (int)file.ProcessingFormat.SampleRate;
			var total = (uint)file.Length;
			using var buffer = new AVAudioPcmBuffer(file.ProcessingFormat, Math.Max(1, total));
			file.ReadIntoBuffer(buffer, out error);
			if (error is not null)
				throw new IOException(error.LocalizedDescription);
			float[] all;
			unsafe
			{
				all = new ReadOnlySpan<float>(((float**)buffer.FloatChannelData)[0], (int)buffer.FrameLength).ToArray();
			}
			var sentences = new float[lengths.Length][];
			var at = 0;
			for (var i = 0; i < lengths.Length; i++)
			{
				var take = Math.Clamp(lengths[i], 0, all.Length - at);
				sentences[i] = Resample(all.AsSpan(at, take), rate, sampleRate);
				at += take;
			}
			return sentences;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Voiced block {block} could not be read, so is spoken again: {ex.Message}");
			File.Delete(LengthsPath(block));
			return null;
		}
	}

	/// <summary>Keep a block's sentences, spoken at <paramref name="sampleRate"/>.</summary>
	public void Save(int block, IReadOnlyList<float[]> sentences, int sampleRate)
	{
		Directory.CreateDirectory(directory);
		var settings = new AudioSettings { Format = AudioFormatType.MPEG4AAC, SampleRate = sampleRate, NumberChannels = 1, EncoderBitRate = BIT_RATE };
		var temp = AudioPath(block) + ".part.m4a";
		using (var file = new AVAudioFile(NSUrl.FromFilename(temp), settings, AVAudioCommonFormat.PCMFloat32, false, out var error))
		{
			if (error is not null)
				throw new IOException(error.LocalizedDescription);
			var total = sentences.Sum(s => s.Length);
			using var buffer = new AVAudioPcmBuffer(file.ProcessingFormat, (uint)Math.Max(1, total)) { FrameLength = (uint)total };
			unsafe
			{
				var target = new Span<float>(((float**)buffer.FloatChannelData)[0], total);
				var at = 0;
				foreach (var s in sentences)
				{
					s.CopyTo(target[at..]);
					at += s.Length;
				}
			}
			file.WriteFromBuffer(buffer, out error);
			if (error is not null)
				throw new IOException(error.LocalizedDescription);
		}
		File.Move(temp, AudioPath(block), overwrite: true);
		File.WriteAllText(LengthsPath(block), JsonSerializer.Serialize(sentences.Select(s => s.Length).ToArray()));
	}

	internal static float[] Resample(ReadOnlySpan<float> samples, int rate, int target)
	{
		if (rate == target || samples.Length == 0)
			return samples.ToArray();
		var result = new float[(int)((long)samples.Length * target / rate)];
		for (var i = 0; i < result.Length; i++)
		{
			var x = (double)i * rate / target;
			var j = (int)x;
			var f = (float)(x - j);
			result[i] = j + 1 < samples.Length ? samples[j] * (1 - f) + samples[j + 1] * f : samples[^1];
		}
		return result;
	}
}
