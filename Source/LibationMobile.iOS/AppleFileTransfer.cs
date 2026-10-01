using Foundation;
using LibationMobile.Services;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UIKit;

namespace LibationMobile.iOS;

/// <summary>
/// Downloads through a background URL session: iOS fetches the file itself, carrying on while the app is in the
/// background or suspended, and relaunching it when the download ends if it was closed meanwhile.
/// </summary>
public sealed class AppleFileTransfer : NSUrlSessionDownloadDelegate, IFileTransfer
{
	public const string SESSION_ID = "io.github.freddygaffey.libation.downloads";

	/// <summary>A finished download whose file has been moved into place, waiting for the app to decrypt it.</summary>
	private static string CompleteMarker(string path) => path + ".complete";

	private sealed class Transfer(string path, Action<double> progress)
	{
		public string Path { get; } = path;
		public Action<double> Progress { get; set; } = progress;
		public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly ConcurrentDictionary<string, Transfer> transfers = new();
	private readonly NSUrlSession session;

	/// <summary>Given by iOS when it wakes the app for this session's events; called once they are handled.</summary>
	public static Action? BackgroundEventsHandled { get; set; }

	public AppleFileTransfer()
	{
		var configuration = NSUrlSessionConfiguration.CreateBackgroundSessionConfiguration(SESSION_ID);
		configuration.SessionSendsLaunchEvents = true;
		configuration.Discretionary = false;
		session = NSUrlSession.FromConfiguration(configuration, (INSUrlSessionDelegate)this, null);
	}

	public async Task DownloadAsync(Uri url, string path, string userAgent, Action<double> progress, CancellationToken token)
	{
		// Finished while the app was closed: the file is already in place.
		if (File.Exists(CompleteMarker(path)) && File.Exists(path))
		{
			File.Delete(CompleteMarker(path));
			progress(1);
			return;
		}

		var transfer = transfers.AddOrUpdate(path, _ => new Transfer(path, progress), (_, existing) =>
		{
			existing.Progress = progress;
			return existing;
		});

		// Still running in the system from before the app was closed: wait for that one rather than start again.
		var running = (await session.GetAllTasksAsync()).OfType<NSUrlSessionDownloadTask>().FirstOrDefault(t => t.TaskDescription == path && t.State == NSUrlSessionTaskState.Running);
		var task = running;
		if (task is null)
		{
			// A background download cannot append to a part-file left by an earlier, in-app download.
			File.Delete(path);
			var request = new NSMutableUrlRequest(new NSUrl(url.AbsoluteUri));
			request["User-Agent"] = userAgent;
			task = session.CreateDownloadTask(request);
			task.TaskDescription = path;
			task.Resume();
			Console.WriteLine($"Background download started: {System.IO.Path.GetFileName(path)}");
		}

		using var registration = token.Register(() =>
		{
			task.Cancel();
			transfer.Completion.TrySetCanceled(token);
		});
		try
		{
			await transfer.Completion.Task;
		}
		finally
		{
			transfers.TryRemove(path, out _);
			File.Delete(CompleteMarker(path));
		}
	}

	public override void DidWriteData(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, long bytesWritten, long totalBytesWritten, long totalBytesExpectedToWrite)
	{
		if (totalBytesExpectedToWrite > 0 && downloadTask.TaskDescription is string path && transfers.TryGetValue(path, out var transfer))
			transfer.Progress((double)totalBytesWritten / totalBytesExpectedToWrite);
	}

	public override void DidFinishDownloading(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, NSUrl location)
	{
		// The system deletes the file when this returns, so it has to be moved now.
		if (downloadTask.TaskDescription is not string path || location.Path is not string temporary)
			return;
		if (downloadTask.Response is NSHttpUrlResponse { StatusCode: < 200 or >= 300 } response)
		{
			Fail(path, new IOException($"The download failed with status {response.StatusCode}."));
			return;
		}
		try
		{
			File.Move(temporary, path, overwrite: true);
			// For a download the app is not waiting on, because it was closed: picked up when it next runs.
			File.WriteAllText(CompleteMarker(path), "");
			if (transfers.TryGetValue(path, out var transfer))
				transfer.Completion.TrySetResult();
		}
		catch (IOException ex)
		{
			Fail(path, ex);
		}
	}

	public override void DidCompleteWithError(NSUrlSession session, NSUrlSessionTask task, NSError? error)
	{
		if (error is not null && task.TaskDescription is string path)
			Fail(path, new IOException($"{error.LocalizedDescription} ({error.Domain} {error.Code})"));
	}

	public override void DidFinishEventsForBackgroundSession(NSUrlSession session)
	{
		var handled = BackgroundEventsHandled;
		BackgroundEventsHandled = null;
		if (handled is not null)
			UIApplication.SharedApplication.BeginInvokeOnMainThread(handled);
	}

	private void Fail(string path, Exception error)
	{
		Console.WriteLine($"Background download failed: {System.IO.Path.GetFileName(path)}: {error.Message}");
		if (transfers.TryGetValue(path, out var transfer))
			transfer.Completion.TrySetException(error);
	}
}

/// <summary>Asks iOS for time to finish, such as decrypting a book, after the app leaves the screen.</summary>
public sealed class AppleBackgroundWork : IBackgroundWork
{
	public IDisposable Begin(string name)
	{
		var id = UIApplication.BackgroundTaskInvalid;
		id = UIApplication.SharedApplication.BeginBackgroundTask(name, () => End(ref id));
		return new Ending(() => End(ref id));
	}

	private static void End(ref nint id)
	{
		if (id == UIApplication.BackgroundTaskInvalid)
			return;
		UIApplication.SharedApplication.EndBackgroundTask(id);
		id = UIApplication.BackgroundTaskInvalid;
	}

	private sealed class Ending(Action end) : IDisposable
	{
		private int done;
		public void Dispose()
		{
			if (Interlocked.Exchange(ref done, 1) == 0)
				end();
		}
	}
}
