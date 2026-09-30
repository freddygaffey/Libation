using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using LibationMobile.ViewModels;

namespace LibationMobile.Views;

public partial class MainView : UserControl
{
	private TopLevel? topLevel;

	public MainView()
	{
		InitializeComponent();
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		topLevel = TopLevel.GetTopLevel(this);
		if (topLevel is null)
			return;

		topLevel.BackRequested += TopLevel_BackRequested;
		// Colour the system bars to match. TopLevel's AutoSafeAreaPadding already keeps content clear of them.
		if (topLevel.InsetsManager is IInsetsManager insets)
			insets.SystemBarColor = (Color)this.FindResource("InkColor")!;
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		if (topLevel is null)
			return;

		topLevel.BackRequested -= TopLevel_BackRequested;
		topLevel = null;
	}

	/// <summary>Back from Now Playing returns to the library; back from the library leaves the app as usual.</summary>
	private void TopLevel_BackRequested(object? sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel vm && vm.ShowLibrary())
			e.Handled = true;
	}
}
