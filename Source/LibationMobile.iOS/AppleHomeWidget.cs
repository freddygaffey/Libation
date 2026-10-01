using CoreFoundation;
using CoreGraphics;
using Foundation;
using LibationMobile.Services;
using Security;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using UIKit;

namespace LibationMobile.iOS;

/// <summary>
/// The home-screen widget (Widget/ in this project, built by Widget/build-widget.sh). The app writes what is
/// playing to the team's shared keychain group, which the widget reads; the widget writes its button presses
/// back and posts a Darwin notification. Item names and JSON fields match Widget/Shared/WidgetState.swift.
/// </summary>
public sealed class AppleHomeWidget : IHomeWidget
{
	private const string SERVICE = "io.github.freddygaffey.libation.widget";
	private const string COMMAND_POSTED = "io.github.freddygaffey.libation.widget.command";
	/// <summary>A play or pause older than this was not meant for now: the app was closed and has only just started.
	/// A speed is kept however old: it is what the widget shows.</summary>
	private static readonly TimeSpan CommandLifetime = TimeSpan.FromMinutes(2);
	private const int COVER_SIZE = 300;

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private sealed record State(string BookId, string Title, string? Author, double RemainingSeconds, double Speed, bool IsPlaying, double UpdatedAt);
	private sealed record WidgetCommand(string Command, string? BookId, double? Speed, double At);

	private readonly AppleMediaSession mediaSession;
	private readonly ConcurrentDictionary<string, double> pendingSpeeds = new();
	private State? shown;
	private string? coverBookId;

	public event Action<string, double>? SpeedRequested;

	public AppleHomeWidget(AppleMediaSession mediaSession)
	{
		this.mediaSession = mediaSession;
		CFNotificationCenter.Darwin.AddObserver(COMMAND_POSTED, null, (_, _) => TakeCommand(), CFNotificationSuspensionBehavior.DeliverImmediately);
		// Pressed while the app was suspended or closed: picked up when it comes back.
		UIApplication.Notifications.ObserveDidBecomeActive((_, _) => TakeCommand());
		TakeCommand();
	}

	public void Show(WidgetInfo info)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
		var state = new State(info.BookId, info.Title, info.Author, info.Remaining.TotalSeconds, info.Speed, info.IsPlaying, now);
		try
		{
			Write("state", JsonSerializer.SerializeToUtf8Bytes(state, Json));
			if (coverBookId != info.BookId)
			{
				coverBookId = info.BookId;
				if (SmallCover(info.Cover) is NSData cover)
					Write("cover", cover);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Widget state not saved: {ex.Message}");
			return;
		}

		// The widget counts down by itself while playing; it only needs redrawing when that would go wrong.
		var previous = shown;
		shown = state;
		if (previous is null || previous.BookId != state.BookId || previous.IsPlaying != state.IsPlaying
			|| Math.Abs(previous.Speed - state.Speed) > 0.001 || Math.Abs(Predicted(previous, now) - state.RemainingSeconds) > 60)
			Reload();
	}

	private static double Predicted(State state, double at)
		=> state.IsPlaying ? state.RemainingSeconds - (at - state.UpdatedAt) * state.Speed : state.RemainingSeconds;

	public double? TakePendingSpeed(string bookId) => pendingSpeeds.TryRemove(bookId, out var speed) ? speed : null;

	private void TakeCommand()
	{
		WidgetCommand? command;
		try
		{
			var data = Read("command");
			if (data is null)
				return;
			Remove("command");
			command = JsonSerializer.Deserialize<WidgetCommand>(data.AsSpan(), Json);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Widget command not read: {ex.Message}");
			return;
		}
		if (command is null)
			return;
		var stale = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds((long)(command.At * 1000)) > CommandLifetime;

		switch (command.Command)
		{
			case "play" when !stale:
				mediaSession.RequestPlay();
				break;
			case "pause" when !stale:
				mediaSession.RequestPause();
				break;
			case "speed" when command.BookId is string bookId && command.Speed is double speed:
				if (SpeedRequested is null)
					pendingSpeeds[bookId] = speed;
				else
					SpeedRequested.Invoke(bookId, speed);
				break;
		}
	}

	private static NSData? SmallCover(byte[]? cover)
	{
		if (cover is null || UIImage.LoadFromData(NSData.FromArray(cover)) is not UIImage image)
			return null;
		var renderer = new UIGraphicsImageRenderer(new CGSize(COVER_SIZE, COVER_SIZE), new UIGraphicsImageRendererFormat { Scale = 1 });
		var small = renderer.CreateImage(_ => image.Draw(new CGRect(0, 0, COVER_SIZE, COVER_SIZE)));
		return small.AsJPEG(0.8f);
	}

	// Saved without naming a keychain group, so in the first of the app's keychain-access-groups: the shared one.
	private static SecRecord Query(string account) => new(SecKind.GenericPassword) { Service = SERVICE, Account = account };

	private static byte[]? Read(string account)
		=> SecKeyChain.QueryAsData(Query(account), false, out var status) is NSData data && status == SecStatusCode.Success ? data.ToArray() : null;

	private static void Write(string account, byte[] value) => Write(account, NSData.FromArray(value));

	private static void Write(string account, NSData value)
	{
		var status = SecKeyChain.Update(Query(account), new SecRecord(SecKind.GenericPassword) { ValueData = value });
		if (status == SecStatusCode.ItemNotFound)
		{
			var item = Query(account);
			item.ValueData = value;
			// The widget is drawn while the phone is locked.
			item.Accessible = SecAccessible.AfterFirstUnlock;
			status = SecKeyChain.Add(item);
		}
		if (status != SecStatusCode.Success)
			throw new InvalidOperationException($"keychain {status}");
	}

	private static void Remove(string account) => SecKeyChain.Remove(Query(account));

	[DllImport("@rpath/LibationWidgetBridge.framework/LibationWidgetBridge", EntryPoint = "libation_widget_reload")]
	private static extern void ReloadWidgets();

	private static void Reload()
	{
		try
		{
			ReloadWidgets();
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			// Built without the widget (Widget/build-widget.sh not run): nothing to redraw.
		}
	}
}
