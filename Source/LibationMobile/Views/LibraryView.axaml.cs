using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LibationMobile.ViewModels;
using System;
using System.IO;

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

	private async void CopyJson_Click(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
			return;
		await clipboard.SetTextAsync(vm.Library.ExportJson());
		vm.Library.Message = $"Copied {vm.Library.ResultCountText} as JSON. Paste it into an AI chat.";
	}

	private async void SaveJson_Click(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
			return;
		try
		{
			var json = vm.Library.ExportJson();
			var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = "Save library as JSON",
				SuggestedFileName = LibraryViewModel.ExportFileName,
				DefaultExtension = "json",
				FileTypeChoices = [new("JSON") { Patterns = ["*.json"], MimeTypes = ["application/json"] }]
			});
			if (file is null)
				return;
			await using var stream = await file.OpenWriteAsync();
			await using var writer = new StreamWriter(stream);
			await writer.WriteAsync(json);
			vm.Library.Message = $"Saved {vm.Library.ResultCountText} to {file.Name}.";
		}
		catch (Exception ex)
		{
			vm.Library.Message = $"Could not save the library: {ex.Message}";
		}
	}
}
