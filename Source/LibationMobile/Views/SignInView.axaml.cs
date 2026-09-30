using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LibationMobile.ViewModels;
using System.IO;
using System.Linq;

namespace LibationMobile.Views;

public partial class SignInView : UserControl
{
	public SignInView()
	{
		InitializeComponent();
	}

	/// <summary>Import straight from the clipboard: pasting into a text box is fiddly on a phone.</summary>
	private async void PasteFromClipboard_Click(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
			return;

		var text = await clipboard.TryGetTextAsync();
		await vm.ImportAccountAsync(text);
	}

	private async void ChooseExportFile_Click(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
			return;

		var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Choose Libation account export",
			AllowMultiple = false,
			FileTypeFilter = [new("Account export") { Patterns = ["*.json"], MimeTypes = ["application/json", "text/plain", "*/*"] }]
		});
		if (files.FirstOrDefault() is not IStorageFile file)
			return;

		await using var stream = await file.OpenReadAsync();
		using var reader = new StreamReader(stream);
		await vm.ImportAccountAsync(await reader.ReadToEndAsync());
	}
}
