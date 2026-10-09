using CommunityToolkit.Mvvm.Input;
using LibationMobile.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibationMobile.ViewModels;

/// <summary>A book read aloud as it plays: which voice reads it, changed while listening.</summary>
public partial class NowPlayingViewModel
{
	private IVoicedSource? voicedSource;

	/// <summary>The voice was changed; the main view model keeps it with the book.</summary>
	public event Action<VoiceChoice>? VoiceChanged;

	public bool IsVoiced => voicedSource is not null;

	public IReadOnlyList<VoiceChoice> VoiceChoices => IsVoiced ? BookVoice.Platform?.Voices() ?? [] : [];

	public VoiceChoice? CurrentVoice
	{
		get => VoiceChoices.FirstOrDefault(v => v.Id == voicedSource?.VoiceId);
		set
		{
			if (value is null || voicedSource is null || value.Id == voicedSource.VoiceId)
				return;
			voicedSource.SetVoice(value.Id);
			LogEvent("voice", detail: value.Name);
			OnPropertyChanged();
			VoiceChanged?.Invoke(value);
		}
	}
}
