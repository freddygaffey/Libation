using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LibationMobile.Services;
using LibationMobile.ViewModels;
using System;
using System.IO;

namespace LibationMobile.Views;

public partial class DownloadsView : UserControl
{
	public DownloadsView() => InitializeComponent();

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		if (DataContext is MainViewModel vm)
			vm.Voice.PickFile = PickDocumentAsync;
	}

	/// <summary>The system's file picker, for a document to voice; copied into the app's inbox, where it can be read.</summary>
	private async System.Threading.Tasks.Task<string?> PickDocumentAsync()
	{
		if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
			return null;
		var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Choose a document",
			AllowMultiple = false,
			FileTypeFilter =
			[
				new FilePickerFileType("PDF, EPUB or text")
				{
					Patterns = ["*.pdf", "*.epub", "*.txt", "*.md"],
					AppleUniformTypeIdentifiers = ["com.adobe.pdf", "org.idpf.epub-container", "public.plain-text"],
					MimeTypes = ["application/pdf", "application/epub+zip", "text/plain"],
				},
			],
		});
		if (files.Count == 0)
			return null;
		var file = files[0];
		if (DataContext is not MainViewModel vm)
			return null;
		var inbox = vm.Voice.TempDirectory;
		Directory.CreateDirectory(inbox);
		var name = file.Name is { Length: > 0 } n ? n : "document.pdf";
		if (!Array.Exists(DocumentText.Extensions, x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
			name += ".pdf";
		var path = Path.Combine(inbox, name);
		await using (var source = await file.OpenReadAsync())
		await using (var target = File.Create(path))
			await source.CopyToAsync(target);
		return path;
	}
}
