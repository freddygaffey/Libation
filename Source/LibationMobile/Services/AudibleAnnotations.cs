using AudibleApi;
using AudibleApi.Common;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>Where a book was last heard on any device, according to Audible.</summary>
public record RemotePosition(TimeSpan Position, DateTimeOffset Updated);

/// <summary>
/// The listening position and annotations Audible holds for a book.
/// </summary>
/// <remarks>
/// Audible has two stores. Positions go through <c>/1.0/lastpositions</c>, which the official apps use and which
/// works for every book. Bookmarks and clips are only reachable on an older annotation server
/// (<see cref="Api.GetRecordsAsync"/>), which accepts writes for every book but silently keeps them only for
/// books that already have a record there. So bookmarks and clips are kept on the device (see
/// <see cref="LocalAnnotations"/>), and the server is read, and written on a best-effort basis, as an extra.
/// </remarks>
public class AudibleAnnotations
{
	private readonly AudibleAccount account;
	private readonly MobileSettings settings;

	public AudibleAnnotations(AudibleAccount account, MobileSettings settings)
	{
		this.account = account;
		this.settings = settings;
	}

	/// <summary>Audible accepts this many books in one position request.</summary>
	private const int POSITIONS_PER_REQUEST = 50;

	/// <summary>Where the book was last heard. Null if Audible has no position for it.</summary>
	public async Task<RemotePosition?> GetPositionAsync(string asin)
		=> (await GetPositionsAsync([asin])).GetValueOrDefault(asin);

	/// <summary>Where each of these books was last heard. Books Audible has no position for are left out.</summary>
	public async Task<Dictionary<string, RemotePosition>> GetPositionsAsync(IEnumerable<string> asins)
	{
		var api = await account.GetApiAsync();
		var positions = new Dictionary<string, RemotePosition>();
		foreach (var batch in asins.Chunk(POSITIONS_PER_REQUEST))
		{
			var response = await api.AdHocAuthenticatedGetAsync($"/1.0/annotations/lastpositions?asins={string.Join(',', batch)}");
			response.EnsureSuccessStatusCode();

			foreach (var entry in JObject.Parse(await response.Content.ReadAsStringAsync())["asin_last_position_heard_annots"] ?? new JArray())
			{
				var heard = entry["last_position_heard"];
				if (entry.Value<string>("asin") is not string asin || heard?.Value<string>("status") != "Exists" || heard.Value<long?>("position_ms") is not long ms)
					continue;

				// "2026-09-30 12:33:04.122", in UTC with no zone.
				var updated = DateTimeOffset.TryParse(heard.Value<string>("last_updated"), CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : DateTimeOffset.MinValue;
				positions[asin] = new RemotePosition(TimeSpan.FromMilliseconds(ms), updated);
			}
		}
		return positions;
	}

	/// <summary>Tell Audible where this book was last heard, so the official app and other devices pick up from here.</summary>
	public async Task SetPositionAsync(string asin, TimeSpan position)
	{
		var api = await account.GetApiAsync();
		var body = new JObject
		{
			["acr"] = await GetContentReferenceAsync(api, asin),
			["asin"] = asin,
			["position_ms"] = (long)position.TotalMilliseconds
		};
		var client = new HttpClientSharer().GetSharedHttpClient($"https://api.audible.{account.Locale.TopDomain}");
		var response = await api.AdHocAuthenticatedRequestAsync($"/1.0/lastpositions/{asin}", HttpMethod.Put, client, body);
		response.EnsureSuccessStatusCode();
	}

	/// <summary>
	/// The content reference comes with a download license, and is saved when a book is downloaded. For a book
	/// downloaded before that was kept, ask for a license once.
	/// </summary>
	private async Task<string> GetContentReferenceAsync(Api api, string asin)
	{
		if (settings.GetContentReference(asin) is string known)
			return known;

		var license = await api.GetDownloadLicenseAsync(asin, DownloadQuality.High);
		var acr = license.Acr ?? throw new InvalidOperationException("Audible did not provide a content reference for this book.");
		settings.SetContentReference(asin, acr);
		return acr;
	}

	/// <summary>Bookmarks and clips on the older annotation server. Empty for a book with no record there.</summary>
	public async Task<IReadOnlyList<IRecord>> GetServerAnnotationsAsync(string asin)
	{
		var api = await account.GetApiAsync();
		var records = await api.GetRecordsAsync(asin);
		return records.Where(r => r is Bookmark or Clip).ToList();
	}

	/// <summary>Try to add a bookmark or clip on the older server. Returns normally whether or not the server keeps it.</summary>
	public async Task TryAddToServerAsync(string asin, TimeSpan start, TimeSpan? end, string? title = null, string? note = null)
	{
		var api = await account.GetApiAsync();
		var builder = new AnnotationBuilder();
		if (end is TimeSpan clipEnd)
			builder.AddClip((long)start.TotalMilliseconds, (long)clipEnd.TotalMilliseconds, title, note);
		else
			builder.AddBookmark((long)start.TotalMilliseconds);
		await api.CreateRecordsAsync(asin, builder);
	}

	public async Task DeleteFromServerAsync(string asin, IRecord record)
	{
		var api = await account.GetApiAsync();
		await api.DeleteRecordAsync(asin, record);
		// A clip's companion bookmark can only go once the clip has.
		if (record is Clip clip)
			await api.DeleteRecordAsync(asin, new Bookmark(clip.Created, clip.Start, null, clip.LastModified));
	}
}
