using System;
using System.IO;

namespace LibationMobile.Services;

/// <summary>
/// Documents other apps hand to this one, as Safari's share sheet does with a PDF: copied out of where iOS put them,
/// and kept until the app is ready to show them.
/// </summary>
public static class SharedDocuments
{
	private static string? pending;
	private static Action<string>? handler;

	/// <summary>Where shared documents are copied to, to be read.</summary>
	public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "voice");

	/// <summary>A document arrived. Copied at once: iOS may clear its inbox, and the original may be read-only.</summary>
	public static void Receive(string path)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			var copy = Path.Combine(Folder, Path.GetFileName(path));
			File.Copy(path, copy, overwrite: true);
			// iOS leaves shared files in Documents/Inbox; an app may delete them there, and should.
			try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
			if (handler is { } open)
				Avalonia.Threading.Dispatcher.UIThread.Post(() => open(copy));
			else
				pending = copy;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Console.WriteLine($"Shared document {path} could not be read: {ex.Message}");
		}
	}

	/// <summary>Documents from now on go to <paramref name="open"/>, and so does one that came before it was ready.</summary>
	public static void Handle(Action<string> open)
	{
		handler = open;
		if (pending is { } waiting)
		{
			pending = null;
			open(waiting);
		}
	}
}
