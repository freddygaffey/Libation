using System;

namespace LibationMobile.Services;

/// <summary>A downloaded book, ready to play.</summary>
/// <param name="Id">The book's ASIN, which keys its saved position.</param>
/// <param name="Chapters">Chapters known apart from the file, as for a voiced book; null to read the file's own.</param>
public record LocalBook(string Id, string Path, string Title, string? Author, string? Narrator, TimeSpan Duration, byte[]? Cover,
	System.Collections.Generic.IReadOnlyList<Mpeg4Lib.Chapter>? Chapters = null);
