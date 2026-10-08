using Avalonia;
using Avalonia.iOS;
using Foundation;
using System;
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
		var mediaSession = new AppleMediaSession();
		MediaSession.Platform = mediaSession;
		HomeWidget.Platform = new AppleHomeWidget(mediaSession);
		DocumentViewer.Platform = new AppleDocumentViewer();
		// Created at launch, so a download that finished while the app was closed is delivered.
		FileTransfer.Platform = new AppleFileTransfer();
		FileTransfer.BackgroundWork = new AppleBackgroundWork();
		return base.CustomizeAppBuilder(builder).WithInterFont();
	}

	/// <summary>iOS woke the app because the background download session has news.</summary>
	[Export("application:handleEventsForBackgroundURLSession:completionHandler:")]
	public void HandleEventsForBackgroundUrl(UIKit.UIApplication application, string sessionIdentifier, Action completionHandler)
	{
		if (sessionIdentifier == AppleFileTransfer.SESSION_ID)
			AppleFileTransfer.BackgroundEventsHandled = completionHandler;
		else
			completionHandler();
	}
}
