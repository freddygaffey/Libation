using System;

namespace LibationMobile.Services;

/// <summary>A downloaded book, ready to play.</summary>
/// <param name="Id">The book's ASIN, which keys its saved position.</param>
public record LocalBook(string Id, string Path, string Title, string? Author, string? Narrator, TimeSpan Duration, byte[]? Cover);
