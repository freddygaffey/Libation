using Avalonia.Controls;
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
	}

	/// <summary>Open the chapter list at the chapter playing now, not at the top.</summary>
	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NowPlayingViewModel.IsChapterListOpen) && viewModel is { IsChapterListOpen: true, CurrentChapterRow: { } current })
			Dispatcher.UIThread.Post(() => chapterList.ScrollIntoView(current), DispatcherPriority.Loaded);
	}
}
