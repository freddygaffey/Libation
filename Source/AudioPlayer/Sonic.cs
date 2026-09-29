/*
 * Ported to C# from the Sonic Library's Sonic.java, trimmed to speed and volume changes.
 * https://github.com/waywardgeek/sonic
 *
 * Sonic library
 * Copyright 2010, 2011 Bill Cox
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;

namespace AudioPlayer;

/// <summary>
/// Pitch-preserving speed change for speech, using pitch-synchronous overlap-add.
/// Unlike fixed-window time stretchers, Sonic removes whole pitch periods, which keeps speech
/// intelligible at high speed-up factors.
/// </summary>
/// <remarks>
/// Samples are interleaved. All counts named "frames" are per-channel sample counts.
/// This class is not thread safe.
/// </remarks>
public sealed class Sonic
{
	private const int MIN_PITCH = 65;
	private const int MAX_PITCH = 400;
	// Pitch detection is done on input down-sampled to about this rate, to improve speed.
	private const int AMDF_FREQ = 4000;

	public int SampleRate { get; }
	public int Channels { get; }

	/// <summary>Speed-up factor. 1 is unchanged, 2 is twice as fast.</summary>
	public float Speed
	{
		get => speed;
		set => speed = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Speed must be greater than zero.");
	}

	/// <summary>Output gain. 1 is unchanged.</summary>
	public float Volume { get; set; } = 1f;

	/// <summary>Number of processed frames waiting to be read.</summary>
	public int FramesAvailable => numOutputFrames;

	/// <summary>Number of written frames not yet processed into output.</summary>
	public int InputFramesPending => numInputFrames;

	private float speed = 1f;
	private short[] inputBuffer;
	private short[] outputBuffer;
	private readonly short[] downSampleBuffer;
	private int inputBufferFrames;
	private int outputBufferFrames;
	private int numInputFrames;
	private int numOutputFrames;
	private readonly int minPeriod;
	private readonly int maxPeriod;
	private readonly int maxRequired;
	private int remainingInputToCopy;
	private int prevPeriod;
	private int prevMinDiff;
	private int minDiff;
	private int maxDiff;

	public Sonic(int sampleRate, int channels)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, MAX_PITCH, nameof(sampleRate));
		ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1, nameof(channels));

		SampleRate = sampleRate;
		Channels = channels;
		minPeriod = sampleRate / MAX_PITCH;
		maxPeriod = sampleRate / MIN_PITCH;
		maxRequired = 2 * maxPeriod;
		inputBufferFrames = maxRequired;
		inputBuffer = new short[maxRequired * channels];
		outputBufferFrames = maxRequired;
		outputBuffer = new short[maxRequired * channels];
		downSampleBuffer = new short[maxRequired];
	}

	/// <summary>Discard all buffered input and output, e.g. after seeking.</summary>
	public void Clear()
	{
		numInputFrames = 0;
		numOutputFrames = 0;
		remainingInputToCopy = 0;
		prevPeriod = 0;
		prevMinDiff = 0;
	}

	/// <summary>Add interleaved samples in the range [-1, 1] and process as much as possible.</summary>
	public void Write(ReadOnlySpan<float> samples)
	{
		var frames = samples.Length / Channels;
		if (frames == 0)
			return;

		EnlargeInputBufferIfNeeded(frames);
		var dest = inputBuffer.AsSpan(numInputFrames * Channels, frames * Channels);
		for (var i = 0; i < dest.Length; i++)
			dest[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
		numInputFrames += frames;
		ProcessStreamInput();
	}

	/// <summary>Read processed interleaved samples.</summary>
	/// <returns>The number of samples (not frames) written to <paramref name="samples"/>.</returns>
	public int Read(Span<float> samples)
	{
		var frames = Math.Min(numOutputFrames, samples.Length / Channels);
		if (frames == 0)
			return 0;

		var count = frames * Channels;
		var source = outputBuffer.AsSpan(0, count);
		for (var i = 0; i < count; i++)
			samples[i] = source[i] / (float)short.MaxValue;

		RemoveFrames(outputBuffer, ref numOutputFrames, frames);
		return count;
	}

	/// <summary>
	/// Force the stream to generate output from whatever input it has, e.g. at end of stream.
	/// Flushing in the middle of words could introduce distortion.
	/// </summary>
	public void Flush()
	{
		var remainingFrames = numInputFrames;
		var expectedOutputFrames = numOutputFrames + (int)(remainingFrames / speed + 0.5f);

		// Add enough silence to flush the input buffer.
		EnlargeInputBufferIfNeeded(remainingFrames + 2 * maxRequired);
		inputBuffer.AsSpan(remainingFrames * Channels, 2 * maxRequired * Channels).Clear();
		numInputFrames += 2 * maxRequired;
		ProcessStreamInput();

		// Throw away any extra frames generated from the silence added above.
		if (numOutputFrames > expectedOutputFrames)
			numOutputFrames = expectedOutputFrames;

		numInputFrames = 0;
		remainingInputToCopy = 0;
	}

	private void ProcessStreamInput()
	{
		var originalNumOutputFrames = numOutputFrames;

		if (speed > 1.00001f || speed < 0.99999f)
			ChangeSpeed(speed);
		else
		{
			CopyToOutput(inputBuffer, 0, numInputFrames);
			numInputFrames = 0;
		}

		if (Volume != 1f)
			ScaleSamples(outputBuffer.AsSpan(originalNumOutputFrames * Channels, (numOutputFrames - originalNumOutputFrames) * Channels), Volume);
	}

	private void ChangeSpeed(float speed)
	{
		if (numInputFrames < maxRequired)
			return;

		var numFrames = numInputFrames;
		var position = 0;
		do
		{
			if (remainingInputToCopy > 0)
				position += CopyInputToOutput(position);
			else
			{
				var period = FindPitchPeriod(inputBuffer, position, true);
				if (speed > 1f)
				{
					var newFrames = SkipPitchPeriod(inputBuffer, position, speed, period);
					position += period + newFrames;
				}
				else
					position += InsertPitchPeriod(inputBuffer, position, speed, period);
			}
		} while (position + maxRequired <= numFrames);

		RemoveFrames(inputBuffer, ref numInputFrames, position);
	}

	/// <summary>Skip over a pitch period, and copy period/speed frames to the output.</summary>
	private int SkipPitchPeriod(short[] samples, int position, float speed, int period)
	{
		int newFrames;
		if (speed >= 2f)
			newFrames = (int)(period / (speed - 1f));
		else
		{
			newFrames = period;
			remainingInputToCopy = (int)(period * (2f - speed) / (speed - 1f));
		}

		EnlargeOutputBufferIfNeeded(newFrames);
		OverlapAdd(newFrames, outputBuffer, numOutputFrames, samples, position, samples, position + period);
		numOutputFrames += newFrames;
		return newFrames;
	}

	/// <summary>Insert a pitch period, and determine how much input to copy directly.</summary>
	private int InsertPitchPeriod(short[] samples, int position, float speed, int period)
	{
		int newFrames;
		if (speed < 0.5f)
			newFrames = (int)(period * speed / (1f - speed));
		else
		{
			newFrames = period;
			remainingInputToCopy = (int)(period * (2f * speed - 1f) / (1f - speed));
		}

		EnlargeOutputBufferIfNeeded(period + newFrames);
		Move(outputBuffer, numOutputFrames, samples, position, period);
		OverlapAdd(newFrames, outputBuffer, numOutputFrames + period, samples, position + period, samples, position);
		numOutputFrames += period + newFrames;
		return newFrames;
	}

	private int CopyInputToOutput(int position)
	{
		var numFrames = Math.Min(remainingInputToCopy, maxRequired);
		CopyToOutput(inputBuffer, position, numFrames);
		remainingInputToCopy -= numFrames;
		return numFrames;
	}

	private void CopyToOutput(short[] samples, int position, int numFrames)
	{
		EnlargeOutputBufferIfNeeded(numFrames);
		Move(outputBuffer, numOutputFrames, samples, position, numFrames);
		numOutputFrames += numFrames;
	}

	/// <summary>
	/// Find the pitch period. This uses AMDF. To improve speed, the input is first down-sampled
	/// by an integer factor to about 4 kHz, then searched again at full rate in a narrower range.
	/// </summary>
	private int FindPitchPeriod(short[] samples, int position, bool preferNewPeriod)
	{
		var skip = SampleRate > AMDF_FREQ ? SampleRate / AMDF_FREQ : 1;
		int period;

		if (Channels == 1 && skip == 1)
			period = FindPitchPeriodInRange(samples, position, minPeriod, maxPeriod);
		else
		{
			DownSampleInput(samples, position, skip);
			period = FindPitchPeriodInRange(downSampleBuffer, 0, minPeriod / skip, maxPeriod / skip);
			if (skip != 1)
			{
				period *= skip;
				var minP = Math.Max(period - (skip << 2), minPeriod);
				var maxP = Math.Min(period + (skip << 2), maxPeriod);
				if (Channels == 1)
					period = FindPitchPeriodInRange(samples, position, minP, maxP);
				else
				{
					DownSampleInput(samples, position, 1);
					period = FindPitchPeriodInRange(downSampleBuffer, 0, minP, maxP);
				}
			}
		}

		var retPeriod = PrevPeriodBetter(preferNewPeriod) ? prevPeriod : period;
		prevMinDiff = minDiff;
		prevPeriod = period;
		return retPeriod;
	}

	/// <summary>Average <paramref name="skip"/> frames together, mixing all channels, into the down-sample buffer.</summary>
	private void DownSampleInput(short[] samples, int position, int skip)
	{
		var numFrames = maxRequired / skip;
		var samplesPerValue = Channels * skip;

		position *= Channels;
		for (var i = 0; i < numFrames; i++)
		{
			var value = 0;
			for (var j = 0; j < samplesPerValue; j++)
				value += samples[position + i * samplesPerValue + j];
			downSampleBuffer[i] = (short)(value / samplesPerValue);
		}
	}

	/// <summary>Find the best frequency match in the range. Only the first channel is examined.</summary>
	/// <remarks><paramref name="samples"/> is either a mono buffer or the interleaved input buffer when <see cref="Channels"/> is 1.</remarks>
	private int FindPitchPeriodInRange(short[] samples, int position, int minPeriod, int maxPeriod)
	{
		int bestPeriod = 0, worstPeriod = 255;
		int minDiff = 1, maxDiff = 0;

		for (var period = minPeriod; period <= maxPeriod; period++)
		{
			var diff = 0;
			for (var i = 0; i < period; i++)
			{
				var sVal = samples[position + i];
				var pVal = samples[position + period + i];
				diff += sVal >= pVal ? sVal - pVal : pVal - sVal;
			}
			// diff is at most a 24-bit number because samples are skipped, so these products cannot overflow.
			if (diff * bestPeriod < minDiff * period)
			{
				minDiff = diff;
				bestPeriod = period;
			}
			if (diff * worstPeriod > maxDiff * period)
			{
				maxDiff = diff;
				worstPeriod = period;
			}
		}

		this.minDiff = minDiff / bestPeriod;
		this.maxDiff = maxDiff / worstPeriod;
		return bestPeriod;
	}

	/// <summary>
	/// At abrupt ends of voiced words, pitch periods can be better approximated by the
	/// previous pitch period estimate. Try to detect this case.
	/// </summary>
	private bool PrevPeriodBetter(bool preferNewPeriod)
	{
		if (minDiff == 0 || prevPeriod == 0)
			return false;

		if (preferNewPeriod)
		{
			// A reasonable match this period, or the mismatch is not much greater than last period.
			if (maxDiff > minDiff * 3 || minDiff * 2 <= prevMinDiff * 3)
				return false;
		}
		else if (minDiff <= prevMinDiff)
			return false;

		return true;
	}

	/// <summary>
	/// Overlap two sound segments, ramping the volume of one down while ramping the other up,
	/// and add them, storing the result at the output.
	/// </summary>
	private void OverlapAdd(int numFrames, short[] output, int outPos, short[] rampDown, int rampDownPos, short[] rampUp, int rampUpPos)
	{
		for (var i = 0; i < Channels; i++)
		{
			var o = outPos * Channels + i;
			var u = rampUpPos * Channels + i;
			var d = rampDownPos * Channels + i;
			for (var t = 0; t < numFrames; t++)
			{
				output[o] = (short)((rampDown[d] * (numFrames - t) + rampUp[u] * t) / numFrames);
				o += Channels;
				d += Channels;
				u += Channels;
			}
		}
	}

	private static void ScaleSamples(Span<short> samples, float volume)
	{
		// Fixed-point volume with a 12 bit fraction.
		var fixedPointVolume = (int)(volume * 4096f);
		for (var i = 0; i < samples.Length; i++)
			samples[i] = (short)Math.Clamp((samples[i] * fixedPointVolume) >> 12, -short.MaxValue, short.MaxValue);
	}

	private void EnlargeOutputBufferIfNeeded(int numFrames)
	{
		if (numOutputFrames + numFrames > outputBufferFrames)
		{
			outputBufferFrames += (outputBufferFrames >> 1) + numFrames;
			Array.Resize(ref outputBuffer, outputBufferFrames * Channels);
		}
	}

	private void EnlargeInputBufferIfNeeded(int numFrames)
	{
		if (numInputFrames + numFrames > inputBufferFrames)
		{
			inputBufferFrames += (inputBufferFrames >> 1) + numFrames;
			Array.Resize(ref inputBuffer, inputBufferFrames * Channels);
		}
	}

	/// <summary>Remove <paramref name="frames"/> frames from the start of a buffer holding <paramref name="bufferFrames"/> frames.</summary>
	private void RemoveFrames(short[] buffer, ref int bufferFrames, int frames)
	{
		Move(buffer, 0, buffer, frames, bufferFrames - frames);
		bufferFrames -= frames;
	}

	/// <summary>Move frames from one array to another. May move frames down within an array, but not up.</summary>
	private void Move(short[] dest, int destPos, short[] source, int sourcePos, int numFrames)
		=> Array.Copy(source, sourcePos * Channels, dest, destPos * Channels, numFrames * Channels);
}
