using Avalonia.Controls;
using Avalonia.Input;
using LibationMobile.ViewModels;

namespace LibationMobile.Views;

public partial class PodcastsView : UserControl
{
	public PodcastsView()
	{
		InitializeComponent();
		// Search on Return: each search goes to Apple's directory.
		searchBox.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter && DataContext is MainViewModel vm)
				vm.Podcasts.SearchCommand.Execute(null);
		};
	}
}
