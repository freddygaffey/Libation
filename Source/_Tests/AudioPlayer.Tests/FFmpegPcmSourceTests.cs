using AudioPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace FFmpegPcmSourceTests;

[TestClass]
public class DecodeWav
{
	private const int SAMPLE_RATE = 8000;
	private const int FRAMES = 3 * SAMPLE_RATE;
	private string wavPath = string.Empty;

	/// <summary>A mono 16-bit wav whose sample values are their own frame index, so any decoded sample identifies its position.</summary>
	[TestInitialize]
	public void Initialize()
	{
		wavPath = Path.Combine(Path.GetTempPath(), $"libation-audioplayer-tests-{Guid.NewGuid():N}.wav");
		using var writer = new BinaryWriter(File.Create(wavPath));
		writer.Write("RIFF"u8);
		writer.Write(36 + FRAMES * 2);
		writer.Write("WAVEfmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)1);
		writer.Write(SAMPLE_RATE);
		writer.Write(SAMPLE_RATE * 2);
		writer.Write((short)2);
		writer.Write((short)16);
		writer.Write("data"u8);
		writer.Write(FRAMES * 2);
		for (short i = 0; i < FRAMES; i++)
			writer.Write(i);
	}

	[TestCleanup]
	public void Cleanup() => File.Delete(wavPath);

	private static int FrameIndexOf(float sample) => (int)Math.Round(sample * 32768);

	[TestMethod]
	public void reports_format_and_duration()
	{
		using var source = new FFmpegPcmSource(wavPath);

		Assert.AreEqual(SAMPLE_RATE, source.SampleRate);
		Assert.AreEqual(1, source.Channels);
		Assert.AreEqual(3, source.Duration.TotalSeconds, 0.001);
	}

	[TestMethod]
	public void decodes_from_the_start()
	{
		using var source = new FFmpegPcmSource(wavPath);
		var buffer = new float[100];

		Assert.AreEqual(100, source.Read(buffer));
		for (var i = 0; i < buffer.Length; i++)
			Assert.AreEqual(i, FrameIndexOf(buffer[i]));
	}

	/// <summary>
	/// SoundFlow's native seek discards the first packet after seeking, so decoding resumes up to one packet
	/// after the requested frame (512 frames for this wav, at most 1024 for AAC). Never before it.
	/// </summary>
	[TestMethod]
	[DataRow(0)]
	[DataRow(100)]
	[DataRow(12000)]
	[DataRow(20000)]
	public void seek_resumes_within_one_packet_after_the_requested_frame(int target)
	{
		const int maxPacketFrames = 1024;
		using var source = new FFmpegPcmSource(wavPath);
		var buffer = new float[10];

		var frame = source.Seek(TimeSpan.FromSeconds(target / (double)SAMPLE_RATE));
		source.Read(buffer);

		Assert.AreEqual(target, frame);
		var resumedAt = FrameIndexOf(buffer[0]);
		Assert.IsTrue(resumedAt >= target && resumedAt <= target + maxPacketFrames, $"Resumed at {resumedAt}");
	}

	[TestMethod]
	public void seek_before_start_is_clamped()
	{
		using var source = new FFmpegPcmSource(wavPath);

		Assert.AreEqual(0, source.Seek(TimeSpan.FromSeconds(-5)));
	}

	[TestMethod]
	public void read_returns_zero_at_end_of_stream()
	{
		using var source = new FFmpegPcmSource(wavPath);
		var buffer = new float[4096];
		var total = 0;
		int read;

		while ((read = source.Read(buffer)) > 0)
			total += read;

		Assert.AreEqual(FRAMES, total);
	}

	[TestMethod]
	public void non_audio_file_throws()
	{
		File.WriteAllText(wavPath, "not audio");

		Assert.Throws<Exception>(() => new FFmpegPcmSource(wavPath));
	}
}
