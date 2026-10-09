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

/// <summary>
/// Reads text into AAC files with the phone's own voices, Eloquence among them, through speechwarp's voice module
/// (Speechwarp.Voice), which renders to memory rather than the speaker, so nothing is heard and it runs faster than
/// speaking. Joins the files into one.
/// </summary>
public sealed class AppleBookVoice : IBookVoice
{
	private const int BIT_RATE = 32000;
	/// <summary>Silence after each paragraph, and after the chapter title (the first).</summary>
	private static readonly TimeSpan ParagraphPause = TimeSpan.FromMilliseconds(450);

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

	public async Task<TimeSpan> RenderAsync(IReadOnlyList<string> paragraphs, string voiceId, string path, IProgress<double>? progress, CancellationToken token)
	{
		var voice = SpeechVoice.Find(voiceId) ?? throw new InvalidOperationException("That voice is no longer on this phone.");
		using var renderer = new SpeechRenderer(voice);
		AVAudioFile? file = null;
		long frames = 0;
		var sampleRate = 0;
		var temp = path + ".part.m4a";
		File.Delete(temp);
		var total = Math.Max(1, paragraphs.Sum(p => p.Length));
		var spoken = 0;
		try
		{
			foreach (var paragraph in paragraphs)
			{
				token.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(paragraph))
					continue;
				var speech = await renderer.RenderAsync(paragraph);
				if (speech.Samples.Length > 0)
				{
					sampleRate = speech.SampleRate;
					file ??= CreateFile(temp, sampleRate);
					frames += Write(file, speech.Samples);
					frames += Write(file, new float[(int)(sampleRate * ParagraphPause.TotalSeconds)]);
				}
				spoken += paragraph.Length;
				progress?.Report((double)spoken / total);
			}
		}
		finally
		{
			// Closing the file finishes the AAC stream.
			file?.Dispose();
		}
		if (file is null || sampleRate <= 0)
			throw new InvalidOperationException("The voice produced no sound.");
		File.Move(temp, path, overwrite: true);
		return TimeSpan.FromSeconds((double)frames / sampleRate);
	}

	/// <summary>An AAC file taking mono float samples.</summary>
	private static AVAudioFile CreateFile(string path, int sampleRate)
	{
		var settings = new AudioSettings
		{
			Format = AudioFormatType.MPEG4AAC,
			SampleRate = sampleRate,
			NumberChannels = 1,
			EncoderBitRate = BIT_RATE,
		};
		var file = new AVAudioFile(NSUrl.FromFilename(path), settings, AVAudioCommonFormat.PCMFloat32, false, out var error);
		if (error is not null)
			throw new IOException($"The audio file could not be made: {error.LocalizedDescription}");
		return file;
	}

	private static unsafe int Write(AVAudioFile file, float[] samples)
	{
		using var buffer = new AVAudioPcmBuffer(file.ProcessingFormat, (uint)samples.Length) { FrameLength = (uint)samples.Length };
		samples.CopyTo(new Span<float>(((float**)buffer.FloatChannelData)[0], samples.Length));
		file.WriteFromBuffer(buffer, out var error);
		if (error is not null)
			throw new IOException($"The audio could not be written: {error.LocalizedDescription}");
		return samples.Length;
	}

	public async Task JoinAsync(IReadOnlyList<string> parts, string path, CancellationToken token)
	{
		var composition = new AVMutableComposition();
		var track = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
		var cursor = CMTime.Zero;
		foreach (var part in parts)
		{
			token.ThrowIfCancellationRequested();
			var asset = AVUrlAsset.Create(NSUrl.FromFilename(part));
			var source = asset.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!).FirstOrDefault()
				?? throw new IOException($"A chapter's audio ({Path.GetFileName(part)}) is missing or empty.");
			var range = new CMTimeRange { Start = CMTime.Zero, Duration = asset.Duration };
			if (!track!.InsertTimeRange(range, source, cursor, out var error))
				throw new IOException($"The chapters could not be joined: {error?.LocalizedDescription}");
			cursor = CMTime.Add(cursor, asset.Duration);
		}

		// The parts are AAC already: copy them across rather than encode them again, if the phone allows.
		foreach (var preset in new[] { AVAssetExportSessionPreset.Passthrough, AVAssetExportSessionPreset.AppleM4A })
		{
			File.Delete(path);
			using var export = new AVAssetExportSession(composition, preset.GetConstant()!)
			{
				OutputUrl = NSUrl.FromFilename(path),
				OutputFileType = AVFileTypes.AppleM4a.GetConstant(),
			};
			using var registration = token.Register(export.CancelExport);
			var done = new TaskCompletionSource();
			export.ExportAsynchronously(done.SetResult);
			await done.Task;
			token.ThrowIfCancellationRequested();
			if (export.Status == AVAssetExportSessionStatus.Completed)
				return;
			Console.WriteLine($"Joining with {preset} failed: {export.Error?.LocalizedDescription}");
		}
		throw new IOException("The chapters could not be joined into one file.");
	}
}
