using Avalonia;
using Avalonia.iOS;
using Foundation;
using LibationMobile.Services;

namespace LibationMobile.iOS;

[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711
{
	protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
	{
		// iOS plays through Apple's own audio stack: it keeps playing in the background and on the lock screen,
		// and it works in the simulator, which SoundFlow's native libraries do not.
		AudioBackend.OpenSource = path => new AppleAudioFileSource(path);
		AudioBackend.CreateOutput = (sampleRate, channels, render) => new AppleAudioOutput(sampleRate, channels, render);
		return base.CustomizeAppBuilder(builder).WithInterFont();
	}
}
