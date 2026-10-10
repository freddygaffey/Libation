using Foundation;
using ObjCRuntime;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LibationMobile.iOS.Kokoro;

/// <summary>
/// Kokoro on the phone's GPU, through MLX (mlalma/kokoro-ios, built by Native/build-kokoro-gpu.sh), with misaki's
/// pronunciations as the HSC library had. Faster than ONNX Runtime on the CPU. Devices only, iOS 18 and later, and only
/// in builds that include it; <see cref="IsAvailable"/> says so.
/// </summary>
public static unsafe class KokoroGpu
{
	// Looked up when first used, rather than imported, so builds without the framework (the simulator's) still link.
	private static delegate* unmanaged<byte*, IntPtr> open;
	private static delegate* unmanaged<IntPtr, byte*, byte*, int, float, float**, int*, int> speak;
	private static delegate* unmanaged<float*, void> free;

	private static void Bind()
	{
		if (open != null)
			return;
		var library = NativeLibrary.Load(Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "KokoroSwift.framework", "KokoroSwift"));
		open = (delegate* unmanaged<byte*, IntPtr>)NativeLibrary.GetExport(library, "kokoro_open");
		speak = (delegate* unmanaged<IntPtr, byte*, byte*, int, float, float**, int*, int>)NativeLibrary.GetExport(library, "kokoro_speak");
		free = (delegate* unmanaged<float*, void>)NativeLibrary.GetExport(library, "kokoro_free");
	}

	private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

	/// <summary>The app left the screen before the GPU could be used: speak on the CPU instead.</summary>
	public sealed class GpuUnavailableException : Exception;

	public const string MODEL_FILE = "kokoro-v1_0.safetensors";
	private static readonly Lock locker = new();
	private static IntPtr engine;
	private static bool firstSpoken = true;

	public static string ModelPath(string directory) => Path.Combine(directory, MODEL_FILE);

	/// <summary>
	/// The app is on screen. iOS refuses GPU work from an app in the background, and MLX ends the app when it is
	/// refused, so the GPU is used only while this is true; Kokoro falls back to the CPU otherwise.
	/// </summary>
	private static volatile bool active;
	private static bool watching;

	/// <summary>Follow whether the app is on screen. Call once, at launch, on the main thread.</summary>
	public static void WatchAppState()
	{
		if (watching)
			return;
		watching = true;
		active = UIKit.UIApplication.SharedApplication.ApplicationState == UIKit.UIApplicationState.Active;
		UIKit.UIApplication.Notifications.ObserveDidBecomeActive((_, _) => active = true);
		// Leaving the screen: no new GPU work, and the sentence under way finishes before the app goes.
		UIKit.UIApplication.Notifications.ObserveWillResignActive((_, _) =>
		{
			active = false;
			lock (locker) { }
		});
	}

	/// <summary>
	/// A device on iOS 18 or later, on screen, with the GPU build in the app and its model downloaded, and, until it
	/// has been shown to work on the phone, a file named use-gpu beside the model.
	/// </summary>
	public static bool IsAvailable(string directory)
		=> active && File.Exists(Path.Combine(directory, "use-gpu")) && Runtime.Arch == Arch.DEVICE && OperatingSystem.IsIOSVersionAtLeast(18)
			&& NSBundle.MainBundle.PathForResource("mlx-swift_Cmlx", "bundle") is not null && File.Exists(ModelPath(directory));

	/// <summary>Speak a text in the voice whose style file is <paramref name="voicePath"/>; 24 kHz mono.</summary>
	public static float[] Speak(string directory, string voicePath, string text, bool british)
	{
		lock (locker)
		{
			// Gone to the background while waiting for the lock.
			if (!active)
				throw new GpuUnavailableException();
			Bind();
			Console.WriteLine($"Kokoro GPU: speaking {text.Length} characters");
			if (engine == IntPtr.Zero)
			{
				var watch = System.Diagnostics.Stopwatch.StartNew();
				fixed (byte* model = Utf8(ModelPath(directory)))
					engine = open(model);
				Console.WriteLine($"Kokoro GPU: model opened in {watch.ElapsedMilliseconds} ms");
				if (engine == IntPtr.Zero)
					throw new InvalidOperationException("Kokoro's GPU model could not be loaded.");
			}
			float* samples;
			int count;
			var spoke = System.Diagnostics.Stopwatch.StartNew();
			fixed (byte* voice = Utf8(voicePath))
			fixed (byte* words = Utf8(text))
				if (speak(engine, voice, words, british ? 1 : 0, 1f, &samples, &count) != 0)
					throw new InvalidOperationException("Kokoro on the GPU could not speak that.");
			if (firstSpoken)
			{
				firstSpoken = false;
				Console.WriteLine($"Kokoro GPU: first sentence ({text.Length} characters, {count / 24000.0:0.0} s of audio) in {spoke.ElapsedMilliseconds} ms");
			}
			try
			{
				return new ReadOnlySpan<float>(samples, count).ToArray();
			}
			finally
			{
				free(samples);
			}
		}
	}
}
