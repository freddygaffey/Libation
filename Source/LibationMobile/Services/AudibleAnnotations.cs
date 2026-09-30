using AudibleApi;
using AudibleApi.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>What Audible holds for one book: where it was last heard on any device, and its bookmarks and clips.</summary>
public record BookAnnotations(LastHeard? LastHeard, IReadOnlyList<Bookmark> Bookmarks, IReadOnlyList<Clip> Clips)
{
	public static BookAnnotations Empty { get; } = new(null, [], []);
}

/// <summary>
/// Position, bookmarks and clips kept in the user's Audible account, so they match the official app and other
/// devices. Positions are in the book's own timeline, which the downloaded file shares.
/// </summary>
public class AudibleAnnotations
{
	/// <summary>Audible's own limit on the length of a clip.</summary>
	public static readonly TimeSpan MaxClipLength = TimeSpan.FromSeconds(45);

	private readonly AudibleAccount account;

	public AudibleAnnotations(AudibleAccount account) => this.account = account;

	public async Task<BookAnnotations> GetAsync(string asin)
	{
		var api = await account.GetApiAsync();
		var records = await api.GetRecordsAsync(asin);
		return new BookAnnotations(
			records.OfType<LastHeard>().OrderByDescending(r => r.Created).FirstOrDefault(),
			records.OfType<Bookmark>().OrderBy(r => r.Start).ToList(),
			records.OfType<Clip>().OrderBy(r => r.Start).ToList());
	}

	/// <summary>Tell Audible where this book was last heard, so other devices can pick up from here.</summary>
	public Task<bool> SetLastHeardAsync(string asin, TimeSpan position)
		=> CreateAsync(asin, builder => builder.SetLastHeard((long)position.TotalMilliseconds));

	public Task<bool> AddBookmarkAsync(string asin, TimeSpan position)
		=> CreateAsync(asin, builder => builder.AddBookmark((long)position.TotalMilliseconds));

	public Task<bool> AddClipAsync(string asin, TimeSpan start, TimeSpan end, string? title)
		=> CreateAsync(asin, builder => builder.AddClip((long)start.TotalMilliseconds, (long)end.TotalMilliseconds, title));

	public async Task<bool> DeleteAsync(string asin, IRecord record)
	{
		var api = await account.GetApiAsync();
		return await api.DeleteRecordAsync(asin, record);
	}

	private async Task<bool> CreateAsync(string asin, Action<AnnotationBuilder> build)
	{
		var api = await account.GetApiAsync();
		var builder = new AnnotationBuilder();
		build(builder);
		return await api.CreateRecordsAsync(asin, builder);
	}
}
