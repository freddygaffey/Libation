using AVFoundation;
using System.Linq;

namespace LibationMobile.iOS;

/// <summary>Where the sound is going, for the research log: read from the audio session, which is cheap.</summary>
public static class AppleAudioRoute
{
	public static string? Describe()
	{
		// AVAudioSessionPort names, as iOS gives them.
		return AVAudioSession.SharedInstance().CurrentRoute.Outputs.FirstOrDefault()?.PortType?.ToString() switch
		{
			null => null,
			"BluetoothA2DPOutput" or "BluetoothHFP" or "BluetoothLE" => "bluetooth",
			"Headphones" => "headphones",
			"CarAudio" => "car",
			"AirPlay" => "airplay",
			"Speaker" or "Receiver" => "speaker",
			var other => other,
		};
	}
}
