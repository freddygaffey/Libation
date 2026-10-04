using AudioPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace SyllableCounterTests;

/// <remarks>
/// On speech from macOS's say (five voices, 140 to 280 words a minute, 102 syllables) the counter came within -12% to +1%
/// of the true count. These tests use synthetic sound so they need no recordings.
/// </remarks>
[TestClass]
public class SyllableCounterTests
{
	private const int RATE = 44100;

	/// <summary>Vowel-like bursts: a 120 Hz voice with harmonics, swelling and fading <paramref name="perSecond"/> times a second.</summary>
	private static float[] Syllables(double perSecond, double seconds, int channels = 1)
	{
		var samples = new float[(int)(RATE * seconds) * channels];
		for (var i = 0; i < samples.Length / channels; i++)
		{
			var t = i / (double)RATE;
			var envelope = Math.Pow(Math.Sin(Math.PI * t * perSecond), 2);
			var voice = 0.0;
			for (var h = 1; h <= 10; h++)
				voice += Math.Sin(2 * Math.PI * 120 * h * t) / h;
			for (var c = 0; c < channels; c++)
				samples[i * channels + c] = (float)(0.2 * envelope * voice);
		}
		return samples;
	}

	private static double Rate(float[] samples, int channels = 1)
	{
		var counter = new SyllableCounter(RATE, channels);
		counter.Reset(0);
		// In uneven pieces, as a decoder hands them over.
		for (int i = 0, step = 1000 * channels; i < samples.Length; i += step, step = step == 1000 * channels ? 4410 * channels : 1000 * channels)
			counter.Write(samples.AsSpan(i, Math.Min(step, samples.Length - i)));
		return counter.Rate(TimeSpan.FromMinutes(1), TimeSpan.Zero) ?? double.NaN;
	}

	[TestMethod]
	[DataRow(3.0)]
	[DataRow(5.0)]
	[DataRow(8.0)]
	public void CountsVowelBursts(double perSecond)
	{
		var rate = Rate(Syllables(perSecond, 10));
		Assert.AreEqual(perSecond, rate, perSecond * 0.1);
	}

	[TestMethod]
	public void StereoCountsTheSameAsMono()
		=> Assert.AreEqual(Rate(Syllables(4, 10)), Rate(Syllables(4, 10, channels: 2), channels: 2), 0.01);

	[TestMethod]
	public void SilenceHasNoSyllables() => Assert.AreEqual(0, Rate(new float[RATE * 5]));

	[TestMethod]
	public void HissIsNotSyllables()
	{
		// Bursts of noise, like a run of "s" sounds, 4 a second.
		var random = new Random(1);
		var samples = new float[RATE * 10];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = (float)(0.2 * Math.Pow(Math.Sin(Math.PI * i / RATE * 4), 2) * (random.NextDouble() * 2 - 1));
		Assert.IsTrue(Rate(samples) < 0.5, $"Counted {Rate(samples)} a second in hiss");
	}

	[TestMethod]
	public void NothingUntilEnoughHeard()
	{
		var counter = new SyllableCounter(RATE, 1);
		counter.Reset(0);
		counter.Write(Syllables(4, 2));
		Assert.IsNull(counter.Rate(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5)));
	}

	[TestMethod]
	public void ResetForgetsWhatCameBefore()
	{
		var counter = new SyllableCounter(RATE, 1);
		counter.Reset(0);
		counter.Write(Syllables(8, 10));
		counter.Reset(RATE * 100);
		counter.Write(new float[RATE * 5]);
		Assert.AreEqual(0, counter.Rate(TimeSpan.FromMinutes(1), TimeSpan.Zero));
	}
}
