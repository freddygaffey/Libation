using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LibationMobile.Views;

public partial class LibraryView : UserControl
{
	public LibraryView()
	{
		InitializeComponent();

		// Put the keyboard away once the listener turns to the results: on scrolling or touching the list, or Return.
		bookList.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => HideKeyboard());
		bookList.AddHandler(PointerPressedEvent, (_, _) => HideKeyboard(), RoutingStrategies.Tunnel);
		searchBox.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter)
				HideKeyboard();
		};
	}

	private void HideKeyboard()
	{
		if (searchBox.IsFocused)
			bookList.Focus(); // Moving focus off the text box hides the keyboard.
	}
}
