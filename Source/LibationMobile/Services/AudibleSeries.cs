using AudibleApi;
using AudibleApi.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A book in a series, as Audible's store lists it. The listener may or may not own it.</summary>
public record SeriesBook(string Asin, string Title, string Authors, string? Sequence, string? CoverUrl);

/// <summary>What the store says about one book: its description and the series it is in.</summary>
public record BookInfo(string? Summary, IReadOnlyList<BookSeries> Series);

/// <summary>Looks books and series up in Audible's store catalogue.</summary>
public partial class AudibleSeries(AudibleAccount account)
{
	private const CatalogOptions.ResponseGroupOptions BookGroups
		= CatalogOptions.ResponseGroupOptions.ProductDesc | CatalogOptions.ResponseGroupOptions.ProductAttrs
		| CatalogOptions.ResponseGroupOptions.Contributors | CatalogOptions.ResponseGroupOptions.Media
		| CatalogOptions.ResponseGroupOptions.ProductExtendedAttrs | CatalogOptions.ResponseGroupOptions.Series;

	public async Task<BookInfo> GetBookInfoAsync(string asin)
	{
		var api = await account.GetApiAsync();
		var item = await api.GetCatalogProductAsync(asin, BookGroups);
		return new BookInfo(ToPlainText(item.PublisherSummary) ?? ToPlainText(item.MerchandisingSummary), LibraryCatalog.ToSeries(item));
	}

	/// <summary>Every book in a series that the store lists, in reading order.</summary>
	public async Task<IReadOnlyList<SeriesBook>> GetSeriesBooksAsync(string seriesId)
	{
		var api = await account.GetApiAsync();
		var series = await api.GetCatalogProductAsync(seriesId, CatalogOptions.ResponseGroupOptions.Relationships);
		var children = (series.Relationships ?? [])
			.Where(r => r.RelationshipToProduct == "child" && r.Asin is not null)
			.OrderBy(r => r.Sort ?? int.MaxValue)
			.ToList();
		if (children.Count == 0)
			return [];

		var items = (await api.GetCatalogProductsAsync(children.Select(c => c.Asin!), BookGroups))
			.Where(i => i.Asin is not null)
			.GroupBy(i => i.Asin!)
			.ToDictionary(g => g.Key, g => g.First());
		// Books the store does not sell in this region come back without a title, or not at all.
		return children
			.Where(c => items.TryGetValue(c.Asin!, out var item) && !string.IsNullOrWhiteSpace(item.Title))
			.Select(c =>
			{
				var item = items[c.Asin!];
				return new SeriesBook(
					c.Asin!,
					item.Title!,
					string.Join(", ", item.Authors?.Select(a => a.Name) ?? []),
					string.IsNullOrWhiteSpace(c.Sequence) ? null : c.Sequence,
					item.ProductImages?.The500?.ToString());
			})
			.ToList();
	}

	/// <summary>A short-lived address for the PDF that comes with some books.</summary>
	public async Task<Uri> GetPdfLinkAsync(string asin)
	{
		var api = await account.GetApiAsync();
		return new Uri(await api.GetPdfDownloadLinkAsync(asin));
	}

	/// <summary>The book's page in the Audible store, which opens in the Audible app where it is installed.</summary>
	public Uri StorePage(string asin) => new($"https://www.audible.{account.Locale.TopDomain}/pd/{asin}");

	/// <summary>Audible's descriptions are HTML. Keep the paragraphs, drop the markup.</summary>
	private static string? ToPlainText(string? html)
	{
		if (string.IsNullOrWhiteSpace(html))
			return null;
		var text = ParagraphEnd().Replace(html, "\n\n");
		text = WebUtility.HtmlDecode(Tag().Replace(text, ""));
		return BlankLines().Replace(text, "\n\n").Trim();
	}

	[GeneratedRegex(@"</p\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
	private static partial Regex ParagraphEnd();
	[GeneratedRegex("<[^>]+>")]
	private static partial Regex Tag();
	[GeneratedRegex(@"\n\s*\n\s*")]
	private static partial Regex BlankLines();
}
