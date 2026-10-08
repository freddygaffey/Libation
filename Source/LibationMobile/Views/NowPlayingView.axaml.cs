using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibationMobile.ViewModels;
using System.ComponentModel;

namespace LibationMobile.Views;

public partial class NowPlayingView : UserControl
{
	private NowPlayingViewModel? viewModel;

	public NowPlayingView()
	{
		InitializeComponent();
		DataContextChanged += (_, _) =>
		{
			if (viewModel is not null)
				viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			viewModel = DataContext as NowPlayingViewModel;
			if (viewModel is not null)
				viewModel.PropertyChanged += ViewModel_PropertyChanged;
		};

		// The scrubber seeks only while a finger is on it; see NowPlayingViewModel.IsScrubbing.
		foreach (var slider in new[] { scrubber })
		{
			slider.AddHandler(PointerPressedEvent, (_, _) => SetScrubbing(true), RoutingStrategies.Tunnel, handledEventsToo: true);
			slider.AddHandler(PointerReleasedEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Tunnel, handledEventsToo: true);
			slider.AddHandler(PointerCaptureLostEvent, (_, _) => SetScrubbing(false), RoutingStrategies.Bubble, handledEventsToo: true);
		}
	}

	private void SetScrubbing(bool scrubbing)
	{
		if (viewModel is not null)
			viewModel.IsScrubbing = scrubbing;
	}

	/// <summary>Open the chapter list at the chapter playing now, not at the top.</summary>
	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NowPlayingViewModel.IsChapterListOpen) && viewModel is { IsChapterListOpen: true, CurrentChapterRow: { } current })
			Dispatcher.UIThread.Post(() => chapterList.ScrollIntoView(current), DispatcherPriority.Loaded);
	}
}
