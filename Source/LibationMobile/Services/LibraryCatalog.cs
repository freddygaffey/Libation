using AudibleApi;
using AudibleApi.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A series a book belongs to, and the book's place in it.</summary>
public record BookSeries(string Id, string Name, string? Sequence);

/// <summary>A title in the user's Audible library, whether or not it has been downloaded.</summary>
public record CatalogBook(
	string Asin,
	string Title,
	string? Subtitle,
	string Authors,
	string? Narrators,
	int LengthMinutes,
	string? CoverUrl,
	DateTimeOffset Purchased,
	IReadOnlyList<BookSeries>? Series = null,
	bool HasPdf = false)
{
	public TimeSpan Length => TimeSpan.FromMinutes(LengthMinutes);
}

/// <summary>
/// The user's Audible library, cached on the device: titles and cover art for every book, and the audio
/// for books that have been downloaded.
/// </summary>
public class LibraryCatalog
{
	private static readonly HttpClient Http = new();

	private readonly string catalogFile;
	private readonly string coversDirectory;
	public string BooksDirectory { get; }

	public LibraryCatalog(string dataDirectory)
	{
		catalogFile = Path.Combine(dataDirectory, "library.json");
		coversDirectory = Path.Combine(dataDirectory, "Covers");
		BooksDirectory = Path.Combine(dataDirectory, "Books");
		Directory.CreateDirectory(coversDirectory);
		Directory.CreateDirectory(BooksDirectory);
	}

	public string BookPath(string asin) => Path.Combine(BooksDirectory, asin + ".m4b");
	public string CoverPath(string asin) => Path.Combine(coversDirectory, asin + ".jpg");
	public bool IsDownloaded(string asin) => File.Exists(BookPath(asin));

	/// <summary>The library as of the last sync. Empty before the first one.</summary>
	public async Task<IReadOnlyList<CatalogBook>> LoadAsync()
	{
		if (!File.Exists(catalogFile))
			return [];
		try
		{
			await using var stream = File.OpenRead(catalogFile);
			return await JsonSerializer.DeserializeAsync(stream, CatalogJsonContext.Default.ListCatalogBook) ?? [];
		}
		catch (JsonException)
		{
			// A corrupt cache is rebuilt by the next sync.
			return [];
		}
	}

	/// <summary>Fetch the whole library from Audible and replace the cache. Newest purchases first.</summary>
	public async Task<IReadOnlyList<CatalogBook>> SyncAsync(Api api)
	{
		var items = await api.GetAllLibraryItemsAsync(LibraryOptions.ResponseGroupOptions.ALL_OPTIONS, 50, 4);
		var books = items
			// A podcast's parent entry has no audio of its own; its episodes are separate items.
			.Where(i => i.Asin is not null && !i.IsSeriesParent)
			.Select(ToCatalogBook)
			.OrderByDescending(b => b.Purchased)
			// Audible's pages can list a book twice, as one just bought; once is enough.
			.DistinctBy(b => b.Asin)
			.ToList();

		var temp = catalogFile + ".tmp";
		await using (var stream = File.Create(temp))
			await JsonSerializer.SerializeAsync(stream, books, CatalogJsonContext.Default.ListCatalogBook);
		File.Move(temp, catalogFile, overwrite: true);
		return books;
	}

	/// <summary>The cover art, downloading it once if it is not cached yet.</summary>
	/// <returns>The image bytes, or null if the book has no cover or it could not be fetched.</returns>
	public async Task<byte[]?> GetCoverAsync(CatalogBook book)
	{
		var path = CoverPath(book.Asin);
		if (File.Exists(path))
			return await File.ReadAllBytesAsync(path);
		if (book.CoverUrl is null)
			return null;

		try
		{
			var bytes = await Http.GetByteArrayAsync(book.CoverUrl);
			await File.WriteAllBytesAsync(path, bytes);
			return bytes;
		}
		catch (HttpRequestException)
		{
			// Offline: show the placeholder and try again next time.
			return null;
		}
	}

	public void DeleteDownload(string asin) => File.Delete(BookPath(asin));

	private static CatalogBook ToCatalogBook(Item item) => new(
		item.Asin!,
		item.Title ?? item.Asin!,
		item.Subtitle,
		string.Join(", ", item.Authors?.Select(a => a.Name) ?? []),
		item.Narrators is { Length: > 0 } narrators ? string.Join(", ", narrators.Select(n => n.Name)) : null,
		item.LengthInMinutes,
		item.ProductImages?.The500?.ToString(),
		item.PurchaseDate,
		ToSeries(item),
		item.PdfUrl is not null);

	internal static IReadOnlyList<BookSeries> ToSeries(Item item)
		=> item.Series?.Where(s => s.SeriesId is not null).Select(s => new BookSeries(s.SeriesId!, s.SeriesName ?? "Series", s.Sequence)).ToList() ?? [];
}

[JsonSerializable(typeof(List<CatalogBook>))]
internal partial class CatalogJsonContext : JsonSerializerContext
{
}
