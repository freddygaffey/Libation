using AVFoundation;
using Foundation;
using LibationMobile.Services;
using Speech;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.iOS;

/// <summary>
/// Says a question with the system voice, then listens through the headphones' microphone for a number, recognised on
/// the phone where it can be. The audio session switches to play-and-record for the question and back afterwards; the
/// player is paused throughout.
/// </summary>
public sealed class AppleVoicePrompt : IVoicePrompt
{
	/// <summary>How long to wait for an answer once the question has been asked.</summary>
	private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(6);
	private readonly AVSpeechSynthesizer synthesizer = new();
	private readonly SemaphoreSlim oneAtATime = new(1, 1);

	public string Access
	{
		get
		{
			var speech = SFSpeechRecognizer.AuthorizationStatus;
#pragma warning disable CA1422 // The iOS 17 replacement, AVAudioApplication, is not on iOS 16.
			var microphone = AVAudioSession.SharedInstance().RecordPermission;
#pragma warning restore CA1422
			if (speech == SFSpeechRecognizerAuthorizationStatus.Restricted)
				return "unavailable";
			if (speech == SFSpeechRecognizerAuthorizationStatus.Denied || microphone == AVAudioSessionRecordPermission.Denied)
				return "denied";
			if (speech == SFSpeechRecognizerAuthorizationStatus.Authorized && microphone == AVAudioSessionRecordPermission.Granted)
				return "allowed";
			return "not asked";
		}
	}

	public async Task<bool> RequestAccessAsync()
	{
		var speech = new TaskCompletionSource<bool>();
		SFSpeechRecognizer.RequestAuthorization(status => speech.TrySetResult(status == SFSpeechRecognizerAuthorizationStatus.Authorized));
		var microphone = new TaskCompletionSource<bool>();
#pragma warning disable CA1422
		AVAudioSession.SharedInstance().RequestRecordPermission(granted => microphone.TrySetResult(granted));
#pragma warning restore CA1422
		return await speech.Task & await microphone.Task;
	}

	public async Task<int?> AskNumberAsync(string question, int max, CancellationToken cancellation)
	{
		if (Access != "allowed" || Recognizer() is not { Available: true } speech)
			return null;
		if (!await oneAtATime.WaitAsync(0, cancellation))
			return null;
		var engine = new AVAudioEngine();
		SFSpeechAudioBufferRecognitionRequest? request = null;
		SFSpeechRecognitionTask? recognition = null;
		var tapped = false;
		try
		{
			// Play and record, through the headphones (Bluetooth hands-free for AirPods), or the speaker without them.
			var session = AVAudioSession.SharedInstance();
			session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
				AVAudioSessionCategoryOptions.AllowBluetooth | AVAudioSessionCategoryOptions.DefaultToSpeaker, out var categoryError);
			if (categoryError is not null)
				throw new InvalidOperationException(categoryError.LocalizedDescription);
			session.SetActive(true, out _);

			// The microphone starts before the question, while the app is still sounding, as iOS lets an app in the
			// background keep recording but not always start; what it hears before the question ends is not used.
			var input = engine.InputNode;
			var format = input.GetBusOutputFormat(0);
			if (format.SampleRate <= 0)
				return null;
			var listening = false;
			request = new SFSpeechAudioBufferRecognitionRequest
			{
				ShouldReportPartialResults = true,
				RequiresOnDeviceRecognition = speech.SupportsOnDeviceRecognition,
				ContextualStrings = ["zero", "one", "two", "three", "four", "skip"],
			};
			var recognitionRequest = request;
			input.InstallTapOnBus(0, 1024, format, (buffer, _) =>
			{
				if (listening)
					recognitionRequest.Append(buffer);
			});
			tapped = true;
			engine.Prepare();
			engine.StartAndReturnError(out var engineError);
			if (engineError is not null)
				throw new InvalidOperationException(engineError.LocalizedDescription);

			await SayAsync(question, cancellation);

			var heard = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
			listening = true;
			recognition = speech.GetRecognitionTask(request, (result, error) =>
			{
				var text = result?.BestTranscription.FormattedString;
				var said = text?.Trim().ToLowerInvariant();
				if (said is "skip" or "cancel" or "pass")
					heard.TrySetResult(null);
				else if (VoicePrompt.ParseNumber(text, max) is int number)
					heard.TrySetResult(number);
				else if (error is not null || result?.Final == true)
					heard.TrySetResult(null);
			});
			using (cancellation.Register(() => heard.TrySetCanceled()))
			{
				var answered = await Task.WhenAny(heard.Task, Task.Delay(AnswerWait, cancellation));
				var answer = answered == heard.Task && heard.Task.IsCompletedSuccessfully ? heard.Task.Result : null;
				cancellation.ThrowIfCancellationRequested();
				listening = false;
				if (answer is int got)
					await SayAsync($"Got it, {got}.", cancellation);
				return answer;
			}
		}
		finally
		{
			recognition?.Cancel();
			request?.EndAudio();
			if (engine.Running)
				engine.Stop();
			if (tapped)
				engine.InputNode.RemoveTapOnBus(0);
			engine.Dispose();
			synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);
			AppleAudioOutput.UsePlaybackSession();
			oneAtATime.Release();
		}
	}

	private SFSpeechRecognizer? recognizer;

	/// <summary>English numbers in the listener's own accent where it is English, otherwise American English.</summary>
	private SFSpeechRecognizer? Recognizer()
	{
		if (recognizer is not null)
			return recognizer;
		try
		{
			if (NSLocale.CurrentLocale.LanguageCode == "en")
				recognizer = new SFSpeechRecognizer(NSLocale.CurrentLocale);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"No speech recognition for {NSLocale.CurrentLocale.Identifier}: {ex.Message}");
		}
		return recognizer ??= new SFSpeechRecognizer(new NSLocale("en-US"));
	}

	private async Task SayAsync(string text, CancellationToken cancellation)
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var utterance = new AVSpeechUtterance(text) { Voice = AVSpeechSynthesisVoice.FromLanguage(Recognizer()?.Locale?.Identifier?.Replace('_', '-') ?? "en-US") };
		void Finished(object? sender, AVSpeechSynthesizerUteranceEventArgs e)
		{
			if (e.Utterance == utterance)
				done.TrySetResult();
		}
		synthesizer.DidFinishSpeechUtterance += Finished;
		synthesizer.DidCancelSpeechUtterance += Finished;
		try
		{
			synthesizer.SpeakUtterance(utterance);
			// Never stuck on a voice that does not report finishing.
			await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(10), cancellation));
			cancellation.ThrowIfCancellationRequested();
		}
		finally
		{
			synthesizer.DidFinishSpeechUtterance -= Finished;
			synthesizer.DidCancelSpeechUtterance -= Finished;
		}
	}
}
