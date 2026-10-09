using AudioToolbox;
using AVFoundation;
using CoreMedia;
using Foundation;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>
/// Reads text into AAC files with the phone's own voices (AVSpeechSynthesizer writing to buffers rather than the
/// speaker, so nothing is heard and it runs faster than speaking), and joins the files without re-encoding.
/// </summary>
public sealed class AppleBookVoice : IBookVoice
{
	private const int BIT_RATE = 32000;
	/// <summary>Silence after each paragraph, and after the chapter title (the first).</summary>
	private static readonly TimeSpan ParagraphPause = TimeSpan.FromMilliseconds(450);
	/// <summary>A paragraph that produces nothing for this long is taken as finished; some voices never say so.</summary>
	private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(20);

	public IReadOnlyList<VoiceChoice> Voices(string language = "en")
	{
		static int Rank(AVSpeechSynthesisVoiceQuality q) => q switch
		{
			AVSpeechSynthesisVoiceQuality.Premium => 0,
			AVSpeechSynthesisVoiceQuality.Enhanced => 1,
			_ => 2,
		};
		var local = NSLocale.CurrentLocale.Identifier.Replace('_', '-');
		return AVSpeechSynthesisVoice.GetSpeechVoices()
			.Where(v => v.Language?.StartsWith(language, StringComparison.OrdinalIgnoreCase) == true)
			// Novelty voices (Bells, Bubbles, Whisper) and eloquence voices are no way to hear a book.
			.Where(v => !OperatingSystem.IsIOSVersionAtLeast(17) || !v.VoiceTraits.HasFlag(AVSpeechSynthesisVoiceTraits.IsNoveltyVoice))
			.OrderBy(v => Rank(v.Quality))
			.ThenByDescending(v => v.Language == local)
			.ThenBy(v => v.Name)
			.Select(v => new VoiceChoice(v.Identifier, v.Name, v.Quality switch
			{
				AVSpeechSynthesisVoiceQuality.Premium => "Premium",
				AVSpeechSynthesisVoiceQuality.Enhanced => "Enhanced",
				_ => "Default",
			}, v.Language ?? language))
			.ToList();
	}

	public async Task<TimeSpan> RenderAsync(IReadOnlyList<string> paragraphs, string voiceId, string path, IProgress<double>? progress, CancellationToken token)
	{
		var voice = AVSpeechSynthesisVoice.FromIdentifier(voiceId) ?? throw new InvalidOperationException("That voice is no longer on this phone.");
		var synthesizer = new AVSpeechSynthesizer();
		AVAudioFile? file = null;
		long frames = 0;
		var sampleRate = 0.0;
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
				var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var lastBuffer = DateTime.UtcNow;
				Exception? failure = null;
				AVAudioPcmBuffer? format = null;
				synthesizer.WriteUtterance(new AVSpeechUtterance(paragraph) { Voice = voice }, buffer =>
				{
					lastBuffer = DateTime.UtcNow;
					if (buffer is not AVAudioPcmBuffer pcm || pcm.FrameLength == 0)
					{
						finished.TrySetResult();
						return;
					}
					try
					{
						file ??= CreateFile(temp, pcm.Format);
						sampleRate = pcm.Format.SampleRate;
						file.WriteFromBuffer(pcm, out var error);
						if (error is not null)
							throw new IOException(error.LocalizedDescription);
						frames += pcm.FrameLength;
						format ??= pcm;
					}
					catch (Exception ex)
					{
						failure = ex;
						finished.TrySetResult();
					}
				});
				while (!finished.Task.IsCompleted)
				{
					await Task.WhenAny(finished.Task, Task.Delay(500, token));
					if (DateTime.UtcNow - lastBuffer > StallLimit)
						break;
				}
				if (failure is not null)
					throw failure;
				if (file is not null && format is not null)
					frames += WriteSilence(file, format.Format, ParagraphPause);
				spoken += paragraph.Length;
				progress?.Report((double)spoken / total);
			}
		}
		finally
		{
			synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);
			synthesizer.Dispose();
			// Closing the file finishes the AAC stream.
			file?.Dispose();
		}
		if (file is null || sampleRate <= 0)
			throw new InvalidOperationException("The voice produced no sound.");
		File.Move(temp, path, overwrite: true);
		return TimeSpan.FromSeconds(frames / sampleRate);
	}

	private static AVAudioFile CreateFile(string path, AVAudioFormat format)
	{
		var settings = new AudioSettings
		{
			Format = AudioFormatType.MPEG4AAC,
			SampleRate = format.SampleRate,
			NumberChannels = (int)format.ChannelCount,
			EncoderBitRate = BIT_RATE,
		};
		var file = new AVAudioFile(NSUrl.FromFilename(path), settings, format.CommonFormat, format.Interleaved, out var error);
		if (error is not null)
			throw new IOException($"The audio file could not be made: {error.LocalizedDescription}");
		return file;
	}

	private static unsafe uint WriteSilence(AVAudioFile file, AVAudioFormat format, TimeSpan length)
	{
		var count = (uint)(format.SampleRate * length.TotalSeconds);
		using var silence = new AVAudioPcmBuffer(format, count) { FrameLength = count };
		var bytes = format.CommonFormat switch
		{
			AVAudioCommonFormat.PCMInt16 => 2,
			AVAudioCommonFormat.PCMInt32 or AVAudioCommonFormat.PCMFloat32 => 4,
			_ => 8,
		};
		var buffers = silence.AudioBufferList;
		for (var i = 0; i < buffers.Count; i++)
			new Span<byte>((void*)buffers[i].Data, (int)buffers[i].DataByteSize).Clear();
		file.WriteFromBuffer(silence, out var error);
		return error is null ? count : 0;
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
