using AudioPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace SonicTests;

internal static class Signal
{
	public const int SAMPLE_RATE = 44100;

	/// <summary>A voiced, speech-like tone: harmonics of <paramref name="fundamental"/>, interleaved across <paramref name="channels"/>.</summary>
	public static float[] Voiced(double seconds, int channels, double fundamental = 150)
	{
		var frames = (int)(seconds * SAMPLE_RATE);
		var samples = new float[frames * channels];
		for (var i = 0; i < frames; i++)
		{
			var t = (double)i / SAMPLE_RATE;
			var value = (float)(0.4 * Math.Sin(2 * Math.PI * fundamental * t)
				+ 0.2 * Math.Sin(2 * Math.PI * 2 * fundamental * t)
				+ 0.1 * Math.Sin(2 * Math.PI * 3 * fundamental * t));
			for (var c = 0; c < channels; c++)
				samples[i * channels + c] = value;
		}
		return samples;
	}

	/// <summary>Feed <paramref name="input"/> through <paramref name="sonic"/> in chunks, flush, and return all output.</summary>
	public static float[] Process(Sonic sonic, float[] input, int chunkFrames = 1024)
	{
		var output = new List<float>();
		var buffer = new float[8192 * sonic.Channels];
		var chunk = chunkFrames * sonic.Channels;

		void drain()
		{
			int read;
			while ((read = sonic.Read(buffer)) > 0)
				output.AddRange(buffer.AsSpan(0, read));
		}

		for (var pos = 0; pos < input.Length; pos += chunk)
		{
			sonic.Write(input.AsSpan(pos, Math.Min(chunk, input.Length - pos)));
			drain();
		}
		sonic.Flush();
		drain();
		return output.ToArray();
	}

	/// <summary>Estimate the fundamental frequency of the first channel from rising zero crossings.</summary>
	public static double EstimateFrequency(float[] samples, int channels)
	{
		var crossings = 0;
		for (var i = channels; i < samples.Length; i += channels)
			if (samples[i - channels] < 0 && samples[i] >= 0)
				crossings++;
		return crossings / ((double)samples.Length / channels / SAMPLE_RATE);
	}
}

[TestClass]
public class ChangeSpeed
{
	[TestMethod]
	[DataRow(0.5f, 1)]
	[DataRow(1f, 1)]
	[DataRow(1.5f, 2)]
	[DataRow(2f, 1)]
	[DataRow(3f, 2)]
	[DataRow(5f, 1)]
	[DataRow(8f, 2)]
	[DataRow(10f, 1)]
	[DataRow(10f, 2)]
	public void output_duration_is_input_duration_divided_by_speed(float speed, int channels)
	{
		const double seconds = 5;
		var sonic = new Sonic(Signal.SAMPLE_RATE, channels) { Speed = speed };

		var output = Signal.Process(sonic, Signal.Voiced(seconds, channels));

		var outputSeconds = (double)output.Length / channels / Signal.SAMPLE_RATE;
		Assert.AreEqual(seconds / speed, outputSeconds, seconds / speed * 0.02);
	}

	[TestMethod]
	[DataRow(2f)]
	[DataRow(5f)]
	[DataRow(10f)]
	public void pitch_is_preserved(float speed)
	{
		const int channels = 1;
		var input = Signal.Voiced(3, channels);
		var sonic = new Sonic(Signal.SAMPLE_RATE, channels) { Speed = speed };

		var output = Signal.Process(sonic, input);

		var inputFrequency = Signal.EstimateFrequency(input, channels);
		var outputFrequency = Signal.EstimateFrequency(output, channels);
		Assert.AreEqual(inputFrequency, outputFrequency, inputFrequency * 0.05);
	}

	[TestMethod]
	public void unit_speed_passes_samples_through()
	{
		var input = Signal.Voiced(0.5, 2);
		var sonic = new Sonic(Signal.SAMPLE_RATE, 2);

		var output = Signal.Process(sonic, input);

		Assert.HasCount(input.Length, output);
		for (var i = 0; i < input.Length; i++)
			Assert.AreEqual(input[i], output[i], 1f / short.MaxValue * 2);
	}

	[TestMethod]
	public void speed_can_change_mid_stream()
	{
		const int channels = 1;
		var sonic = new Sonic(Signal.SAMPLE_RATE, channels) { Speed = 2f };
		var output = new List<float>();
		var buffer = new float[8192];

		sonic.Write(Signal.Voiced(2, channels));
		sonic.Speed = 8f;
		sonic.Write(Signal.Voiced(2, channels));
		sonic.Flush();
		int read;
		while ((read = sonic.Read(buffer)) > 0)
			output.AddRange(buffer.AsSpan(0, read));

		// Input already processed at 2x stays at 2x; the rest is processed at 8x.
		var outputSeconds = (double)output.Count / Signal.SAMPLE_RATE;
		Assert.IsTrue(outputSeconds > 2 / 8.0 + 2 / 8.0 && outputSeconds < 2 / 2.0 + 2 / 2.0, $"Output was {outputSeconds:F3}s");
	}
}

[TestClass]
public class StreamState
{
	[TestMethod]
	public void clear_discards_pending_input_and_output()
	{
		var sonic = new Sonic(Signal.SAMPLE_RATE, 2) { Speed = 3f };
		sonic.Write(Signal.Voiced(1, 2));
		Assert.IsGreaterThan(0, sonic.FramesAvailable + sonic.InputFramesPending);

		sonic.Clear();

		Assert.AreEqual(0, sonic.FramesAvailable);
		Assert.AreEqual(0, sonic.InputFramesPending);
		Assert.AreEqual(0, sonic.Read(new float[1024]));
	}

	[TestMethod]
	public void read_returns_whole_frames_only()
	{
		var sonic = new Sonic(Signal.SAMPLE_RATE, 2);
		sonic.Write(Signal.Voiced(0.1, 2));

		var read = sonic.Read(new float[5]);

		Assert.AreEqual(4, read);
	}

	[TestMethod]
	public void volume_scales_output()
	{
		var input = Signal.Voiced(0.5, 1);
		var sonic = new Sonic(Signal.SAMPLE_RATE, 1) { Volume = 0.5f };

		var output = Signal.Process(sonic, input);

		for (var i = 0; i < input.Length; i++)
			Assert.AreEqual(input[i] * 0.5f, output[i], 1f / short.MaxValue * 4);
	}

	[TestMethod]
	public void invalid_arguments_throw()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sonic(Signal.SAMPLE_RATE, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sonic(100, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sonic(Signal.SAMPLE_RATE, 1).Speed = 0);
	}
}
