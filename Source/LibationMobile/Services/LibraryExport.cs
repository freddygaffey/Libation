using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibationMobile.Services;

/// <summary>One book as exported, with how much and how lately it has been listened to.</summary>
public record ExportedBook(
	string Asin,
	string Title,
	string? Subtitle,
	string Authors,
	string? Narrators,
	IReadOnlyList<ExportedSeries>? Series,
	double LengthHours,
	DateTimeOffset Purchased,
	bool Downloaded,
	string Status,
	double ProgressPercent,
	double? PositionHours,
	DateTimeOffset? LastListened,
	DateTimeOffset? FirstListened,
	int TimesListened,
	int DaysListened,
	double HoursOfBookHeard,
	double HoursSpentListening,
	double? Speed);

public record ExportedSeries(string Name, string? Sequence);

public record LibraryExportFile(
	string About,
	DateTimeOffset Exported,
	string Selection,
	int BookCount,
	IReadOnlyList<ExportedBook> Books);

/// <summary>What the exporter needs to know about a book beyond the catalog entry.</summary>
public record ExportInput(CatalogBook Book, bool Downloaded, TimeSpan? Position, DateTimeOffset? PositionSaved, float? Speed);

/// <summary>
/// Writes a list of books as JSON for pasting into an AI or a spreadsheet: the catalog details, progress, and
/// the listening history from <see cref="ListeningLog"/> on this device.
/// </summary>
public static class LibraryExport
{
	private const string ABOUT =
		"An audiobook library exported from Libation. TimesListened counts listening sessions on this phone (play to pause); " +
		"DaysListened counts the different days with a session. LastListened is the latest of the last session here and the " +
		"position last saved by any device through Audible. Hours are at normal (1x) speed unless named 'spent'.";

	// Titles keep their accents and curly quotes rather than \u escapes, for whoever reads the file.
	private static readonly LibraryExportJsonContext Context = new(new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	});

	public static string ToJson(IEnumerable<ExportInput> books, IReadOnlyList<ListeningSession> sessions, string selection, DateTimeOffset now)
	{
		var byBook = sessions.GroupBy(s => s.BookId).ToDictionary(g => g.Key, g => g.ToList());
		var exported = books.Select(b => Export(b, byBook.GetValueOrDefault(b.Book.Asin) ?? [])).ToList();
		var file = new LibraryExportFile(ABOUT, now, selection, exported.Count, exported);
		return JsonSerializer.Serialize(file, Context.LibraryExportFile);
	}

	private static ExportedBook Export(ExportInput input, List<ListeningSession> sessions)
	{
		var book = input.Book;
		var length = book.Length;
		var position = input.Position;
		var progress = length > TimeSpan.Zero && position is { } at ? Math.Clamp(at / length, 0, 1) : 0;
		var status = progress >= 1 ? "finished" : progress > 0 ? "in progress" : "not started";

		DateTimeOffset? lastSession = sessions.Count > 0 ? sessions.Max(s => s.Ended) : null;
		// A position of zero is a book marked not started, not a time it was listened to.
		var lastSaved = position > TimeSpan.Zero ? input.PositionSaved : null;
		var lastListened = Latest(lastSession, lastSaved);

		return new ExportedBook(
			book.Asin,
			book.Title,
			book.Subtitle,
			book.Authors,
			book.Narrators,
			book.Series?.Select(s => new ExportedSeries(s.Name, s.Sequence)).ToList(),
			Math.Round(length.TotalHours, 2),
			book.Purchased,
			input.Downloaded,
			status,
			Math.Round(progress * 100, 1),
			position is { } p ? Math.Round(p.TotalHours, 2) : null,
			lastListened,
			sessions.Count > 0 ? sessions.Min(s => s.Started) : null,
			sessions.Count,
			sessions.Select(s => s.Started.ToLocalTime().Date).Distinct().Count(),
			Math.Round(sessions.Sum(s => s.BookSeconds) / 3600, 2),
			Math.Round(sessions.Sum(s => s.SpentSeconds) / 3600, 2),
			input.Speed);
	}

	private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b)
		=> a is null ? b : b is null ? a : a > b ? a : b;
}

[JsonSerializable(typeof(LibraryExportFile))]
internal partial class LibraryExportJsonContext : JsonSerializerContext
{
}
