using AudioPlayer;
using AVFoundation;
using Foundation;
using System;
using System.IO;

namespace LibationMobile.iOS;

/// <summary>
/// Decodes with AVAudioFile, Apple's decoder for m4b/m4a/mp3. Seeking is by 64-bit frame index and sample accurate.
/// </summary>
public sealed class AppleAudioFileSource : IPcmSource
{
	private const uint CHUNK_FRAMES = 4096;

	public int SampleRate { get; }
	public int Channels { get; }
	public TimeSpan Duration { get; }

	private readonly AVAudioFile file;
	private readonly AVAudioPcmBuffer buffer;
	private int bufferedFrames;
	private int bufferOffset;

	public AppleAudioFileSource(string path)
	{
		file = new AVAudioFile(NSUrl.FromFilename(path), out var error);
		if (error is not null)
			throw new IOException($"Could not open {Path.GetFileName(path)}: {error.LocalizedDescription}");

		// Always 32-bit float, one buffer per channel.
		var format = file.ProcessingFormat;
		SampleRate = (int)format.SampleRate;
		Channels = (int)format.ChannelCount;
		Duration = TimeSpan.FromSeconds((double)file.Length / SampleRate);
		buffer = new AVAudioPcmBuffer(format, CHUNK_FRAMES);
	}

	public unsafe int Read(Span<float> samples)
	{
		var framesWanted = samples.Length / Channels;
		var written = 0;
		while (written < framesWanted)
		{
			if (bufferOffset >= bufferedFrames)
			{
				buffer.FrameLength = 0;
				if (!file.ReadIntoBuffer(buffer, CHUNK_FRAMES, out var error) || error is not null || buffer.FrameLength == 0)
					break;
				bufferedFrames = (int)buffer.FrameLength;
				bufferOffset = 0;
			}

			var frames = Math.Min(framesWanted - written, bufferedFrames - bufferOffset);
			var channelData = (float**)buffer.FloatChannelData;
			for (var c = 0; c < Channels; c++)
			{
				var channel = channelData[c] + bufferOffset;
				for (var f = 0; f < frames; f++)
					samples[(written + f) * Channels + c] = channel[f];
			}
			written += frames;
			bufferOffset += frames;
		}
		return written * Channels;
	}

	public long Seek(TimeSpan position)
	{
		var frame = Math.Clamp((long)(position.TotalSeconds * SampleRate), 0, file.Length);
		file.FramePosition = frame;
		bufferedFrames = bufferOffset = 0;
		return frame;
	}

	public void Dispose()
	{
		buffer.Dispose();
		file.Dispose();
	}
}
