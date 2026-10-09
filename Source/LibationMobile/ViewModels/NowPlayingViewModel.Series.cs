using System;

namespace LibationMobile.ViewModels;

/// <summary>A series played through: the player says what comes next, and tells the main view model when a book ends.</summary>
public partial class NowPlayingViewModel
{
	/// <summary>The book has played to its end. The main view model carries on with the next in its series.</summary>
	public event Action? BookEnded;

	/// <summary>Within this much listening of the end, at the speed playing, the next book in the series is fetched.</summary>
	private static readonly TimeSpan NearEnd = TimeSpan.FromHours(1);
	private bool nearingEndRaised;

	/// <summary>The book is within an hour's listening of its end. Raised once per opening.</summary>
	public event Action? NearingEnd;

	/// <summary>From Update.</summary>
	private void CheckNearingEnd()
	{
		if (nearingEndRaised || !IsPlaying || Duration <= TimeSpan.Zero || Speed <= 0)
			return;
		if ((Duration - Position) / Speed > NearEnd)
			return;
		nearingEndRaised = true;
		NearingEnd?.Invoke();
	}

	private string? upNext;

	/// <summary>The next book in the series, as the status line shows it, such as "Book 2, The Dark Forest". Null for none.</summary>
	public string? UpNext
	{
		get => upNext;
		set
		{
			if (SetProperty(ref upNext, value))
				UpdateStatusLine();
		}
	}

	/// <summary>Say something under the chapter for a few seconds, such as the next book starting.</summary>
	public void Announce(string message) => ShowStatus(message);
}
