using AudioPlayer;
using System;

namespace LibationMobile.Services;

/// <summary>
/// Plays through whichever speed-up method is chosen in settings, and changes over during playback when the
/// choice changes. The fraction of a second buffered in the old method is dropped at the change.
/// </summary>
public sealed class SwitchingTimeStretcher(ITimeStretcher nonlinear, ITimeStretcher classic) : ITimeStretcher
{
	private ITimeStretcher current = Wanted(nonlinear, classic);

	/// <summary>speechwarp plays when chosen, and also for even speed-up when a pause cap or rhythm is on, which the original method lacks.</summary>
	private static ITimeStretcher Wanted(ITimeStretcher nonlinear, ITimeStretcher classic)
		=> AudioBackend.UseNonlinear || AudioBackend.NeedsSpeechwarp ? nonlinear : classic;
	private float speed = 1f;

	public float Speed
	{
		get => speed;
		set => current.Speed = speed = value;
	}

	public long BufferedSourceFrames => current.BufferedSourceFrames;

	public void Write(ReadOnlySpan<float> samples)
	{
		// Picked up here, on the thread that uses the stretchers, because the setting is changed from the UI.
		var wanted = Wanted(nonlinear, classic);
		if (wanted != current)
		{
			current.Clear();
			current = wanted;
			current.Speed = speed;
		}
		current.Write(samples);
	}

	public int Read(Span<float> samples) => current.Read(samples);
	public void Flush() => current.Flush();
	public void Clear() => current.Clear();

	public void Dispose()
	{
		nonlinear.Dispose();
		classic.Dispose();
	}
}
