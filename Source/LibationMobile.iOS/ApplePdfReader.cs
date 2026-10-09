using Foundation;
using LibationMobile.Services;
using PdfKit;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LibationMobile.iOS;

/// <summary>A PDF's text by page, its outline, and its title and author, with PDFKit.</summary>
public sealed class ApplePdfReader : IPdfReader
{
	public (string? Title, string? Author, IReadOnlyList<string> Pages, IReadOnlyList<(string Title, int Page)> Outline) Read(string path)
	{
		using var document = new PdfDocument(NSUrl.FromFilename(path));
		if (document.PageCount == 0)
			throw new InvalidDataException("The PDF could not be opened, or has no pages.");
		if (document.IsLocked)
			throw new InvalidDataException("The PDF is locked with a password.");

		var pages = new List<string>((int)document.PageCount);
		for (nint i = 0; i < document.PageCount; i++)
			pages.Add(document.GetPage(i)?.Text ?? "");

		// Every entry, nested ones too, in page order: a book's parts hold its chapters. A bare number takes its
		// parent's name: "Rorschach 3".
		var outline = new List<(string, int)>();
		void Walk(PdfOutline node, string? parent)
		{
			for (nint i = 0; i < node.ChildrenCount; i++)
			{
				if (node.Child(i) is not { } entry)
					continue;
				var label = entry.Label?.Trim() ?? "";
				var title = parent is not null && label.Length <= 4 && label.Length > 0 && !label.Any(char.IsLetter) ? $"{parent} {label}" : label;
				if (title.Length > 0 && entry.Destination?.Page is { } page)
					outline.Add((title, (int)document.GetPageIndex(page)));
				Walk(entry, label.Length > 0 ? label : parent);
			}
		}
		if (document.OutlineRoot is { } root)
			Walk(root, null);

		var attributes = document.GetDocumentAttributes();
		static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
		return (Value(attributes?.Title), Value(attributes?.Author), pages, outline);
	}

	public byte[]? Cover(string path)
	{
		using var document = new PdfDocument(NSUrl.FromFilename(path));
		for (nint i = 0; i < document.PageCount && i < 10; i++)
		{
			if (document.GetPage(i) is not { } page || string.IsNullOrWhiteSpace(page.Text))
				continue;
			using var image = page.GetThumbnail(new CoreGraphics.CGSize(600, 600), PdfDisplayBox.Crop);
			return image.AsPNG()?.ToArray();
		}
		return null;
	}
}
