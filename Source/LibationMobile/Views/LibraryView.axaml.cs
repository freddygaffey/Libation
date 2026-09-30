using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LibationMobile.ViewModels;
using System.Linq;

namespace LibationMobile.Views;

public partial class LibraryView : UserControl
{
	private static readonly FilePickerFileType Audiobooks = new("Audiobooks")
	{
		Patterns = ["*.m4b", "*.m4a", "*.mp3"],
		MimeTypes = ["audio/mp4", "audio/x-m4b", "audio/mpeg", "audio/*"]
	};

	public LibraryView()
	{
		InitializeComponent();
	}

	private async void AddBook_Click(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
			return;

		var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Add audiobook",
			AllowMultiple = false,
			FileTypeFilter = [Audiobooks]
		});

		if (files.FirstOrDefault() is not IStorageFile file)
			return;

		await using var stream = await file.OpenReadAsync();
		await vm.Library.ImportAsync(stream, file.Name);
	}
}
