using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LibationMobile.Services;

/// <summary>A podcast: what Apple's directory or its feed says about it.</summary>
/// <param name="Id">Ours, made from the feed's address, so the same feed is always the same podcast.</param>
public record PodcastShow(string Id, string Title, string? Author, string? ArtworkUrl, string FeedUrl, string? Description = null);

/// <summary>One episode of a podcast.</summary>
/// <param name="Id">Ours, from the feed and the episode's own ID; it keys the saved position, like a book's ASIN.</param>
public record PodcastEpisode(string Id, string ShowId, string Title, DateTimeOffset? Published, TimeSpan? Duration, string AudioUrl, string? AudioType, string? Description, string? ShowTitle = null);

/// <summary>
/// Podcasts, which need no account: Apple's free directory to find them, their public RSS feeds for episodes, and
/// plain downloads of the audio files. Subscriptions and episode lists are saved as JSON in the app's data folder;
/// downloaded episodes go in Podcasts/.
/// </summary>
public class PodcastLibrary
{
	/// <summary>Episodes kept for each podcast, newest first. Long-running shows have thousands.</summary>
	private const int EPISODES_KEPT = 300;
	private const string USER_AGENT = "Libation/1.0 (podcast player)";
	private const string ITUNES_NAMESPACE = "http://www.itunes.com/dtds/podcast-1.0.dtd";

	private static readonly HttpClient Http = CreateHttp();

	private readonly string storeFile;
	private readonly string artDirectory;
	public string EpisodesDirectory { get; }
	private readonly Lock locker = new();
	private Store store;

	internal class Store
	{
		public List<PodcastShow> Shows { get; set; } = [];
		public Dictionary<string, List<PodcastEpisode>> Episodes { get; set; } = new();
		/// <summary>Episodes downloaded, by ID: kept even after they drop out of the feed or the podcast is unsubscribed.</summary>
		public Dictionary<string, PodcastEpisode> Downloaded { get; set; } = new();
	}

	public PodcastLibrary(string dataDirectory)
	{
		storeFile = Path.Combine(dataDirectory, "podcasts.json");
		EpisodesDirectory = Path.Combine(dataDirectory, "Podcasts");
		artDirectory = Path.Combine(EpisodesDirectory, "Art");
		Directory.CreateDirectory(artDirectory);
		store = Load(storeFile);
	}

	private static HttpClient CreateHttp()
	{
		var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		http.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
		return http;
	}

	#region Subscriptions

	public IReadOnlyList<PodcastShow> Shows
	{
		get { lock (locker) return store.Shows.ToList(); }
	}

	public bool IsSubscribed(string showId)
	{
		lock (locker) return store.Shows.Any(s => s.Id == showId);
	}

	public PodcastShow? FindShow(string showId)
	{
		lock (locker) return store.Shows.FirstOrDefault(s => s.Id == showId);
	}

	public IReadOnlyList<PodcastEpisode> Episodes(string showId)
	{
		lock (locker) return store.Episodes.TryGetValue(showId, out var episodes) ? episodes.ToList() : [];
	}

	/// <summary>Subscribe and fetch the episodes. Returns the podcast as its own feed describes it.</summary>
	public async Task<PodcastShow> SubscribeAsync(PodcastShow show, CancellationToken token = default)
	{
		var (fromFeed, episodes) = await FetchFeedAsync(show, token);
		lock (locker)
		{
			store.Shows.RemoveAll(s => s.Id == fromFeed.Id);
			store.Shows.Insert(0, fromFeed);
			store.Episodes[fromFeed.Id] = episodes;
			Save();
		}
		return fromFeed;
	}

	/// <summary>Stop following a podcast. Its downloaded episodes stay until removed.</summary>
	public void Unsubscribe(string showId)
	{
		lock (locker)
		{
			store.Shows.RemoveAll(s => s.Id == showId);
			store.Episodes.Remove(showId);
			Save();
		}
	}

	/// <summary>Fetch a podcast's feed again for new episodes. Returns how many are new.</summary>
	public async Task<int> RefreshAsync(string showId, CancellationToken token = default)
	{
		if (FindShow(showId) is not { } show)
			return 0;
		var (fromFeed, episodes) = await FetchFeedAsync(show, token);
		lock (locker)
		{
			var known = store.Episodes.TryGetValue(showId, out var old) ? old.Select(e => e.Id).ToHashSet() : [];
			var index = store.Shows.FindIndex(s => s.Id == showId);
			if (index >= 0)
				store.Shows[index] = fromFeed;
			store.Episodes[showId] = episodes;
			Save();
			return episodes.Count(e => !known.Contains(e.Id));
		}
	}

	#endregion

	#region Finding podcasts

	/// <summary>Search Apple's podcast directory, which is free and needs no key.</summary>
	public static async Task<IReadOnlyList<PodcastShow>> SearchAsync(string term, CancellationToken token = default)
	{
		var url = $"https://itunes.apple.com/search?media=podcast&entity=podcast&limit=40&term={Uri.EscapeDataString(term)}";
		using var json = JsonDocument.Parse(await Http.GetStringAsync(url, token));
		var shows = new List<PodcastShow>();
		foreach (var result in json.RootElement.GetProperty("results").EnumerateArray())
		{
			// Some listings have no public feed (Apple-exclusive shows); they cannot be played here.
			if (Text(result, "feedUrl") is not { Length: > 0 } feed)
				continue;
			shows.Add(new PodcastShow(ShowId(feed), Text(result, "collectionName") ?? "Untitled", Text(result, "artistName"),
				Text(result, "artworkUrl600") ?? Text(result, "artworkUrl100"), feed));
		}
		return shows;

		static string? Text(JsonElement element, string name)
			=> element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	}

	/// <summary>A podcast from a feed address typed in, for shows not in Apple's directory.</summary>
	public static PodcastShow FromFeedUrl(string feedUrl) => new(ShowId(feedUrl), feedUrl, null, null, feedUrl);

	public static string ShowId(string feedUrl) => "podshow-" + Hash(feedUrl.Trim().ToLowerInvariant());

	private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

	#endregion

	#region Feeds

	/// <summary>A podcast's episodes without following it, to look before subscribing.</summary>
	public static Task<(PodcastShow Show, List<PodcastEpisode> Episodes)> PreviewAsync(PodcastShow show, CancellationToken token = default)
		=> FetchFeedAsync(show, token);

	private static async Task<(PodcastShow Show, List<PodcastEpisode> Episodes)> FetchFeedAsync(PodcastShow show, CancellationToken token)
	{
		await using var stream = await Http.GetStreamAsync(show.FeedUrl, token);
		var document = await XDocument.LoadAsync(stream, LoadOptions.None, token);
		return ParseFeed(document, show);
	}

	/// <summary>An RSS podcast feed: the channel, and each item with an audio enclosure.</summary>
	internal static (PodcastShow Show, List<PodcastEpisode> Episodes) ParseFeed(XDocument document, PodcastShow show)
	{
		XNamespace itunes = ITUNES_NAMESPACE;
		var channel = document.Root?.Element("channel") ?? throw new InvalidDataException("That address is not a podcast feed.");

		var art = channel.Element(itunes + "image")?.Attribute("href")?.Value ?? channel.Element("image")?.Element("url")?.Value;
		var fromFeed = show with
		{
			Title = Clean(channel.Element("title")?.Value) ?? show.Title,
			Author = Clean(channel.Element(itunes + "author")?.Value) ?? show.Author,
			ArtworkUrl = show.ArtworkUrl ?? art,
			Description = Shorten(channel.Element("description")?.Value ?? channel.Element(itunes + "summary")?.Value, 600),
		};

		var episodes = new List<PodcastEpisode>();
		foreach (var item in channel.Elements("item"))
		{
			var enclosure = item.Element("enclosure");
			if (enclosure?.Attribute("url")?.Value is not { Length: > 0 } audio)
				continue;
			var type = enclosure.Attribute("type")?.Value;
			if (type is not null && !type.StartsWith("audio", StringComparison.OrdinalIgnoreCase) && !type.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
				continue; // Video podcasts.
			var guid = item.Element("guid")?.Value is { Length: > 0 } g ? g : audio;
			episodes.Add(new PodcastEpisode(
				"podep-" + Hash(show.FeedUrl + "\n" + guid),
				show.Id,
				Clean(item.Element("title")?.Value) ?? "Untitled episode",
				ParseDate(item.Element("pubDate")?.Value),
				ParseDuration(item.Element(itunes + "duration")?.Value),
				audio,
				type,
				Shorten(item.Element(itunes + "summary")?.Value ?? item.Element("description")?.Value, 400),
				fromFeed.Title));
		}
		return (fromFeed, episodes.OrderByDescending(e => e.Published ?? DateTimeOffset.MinValue).Take(EPISODES_KEPT).ToList());
	}

	private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

	/// <summary>Descriptions are often HTML: keep the words.</summary>
	private static string? Shorten(string? html, int length)
	{
		if (string.IsNullOrWhiteSpace(html))
			return null;
		var text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ").Trim();
		text = System.Net.WebUtility.HtmlDecode(text);
		return text.Length <= length ? text : text[..length].TrimEnd() + "…";
	}

	/// <summary>RSS dates are RFC 822, often with a named time zone .NET does not know.</summary>
	internal static DateTimeOffset? ParseDate(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;
		text = text.Trim();
		if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
			return date;
		// "Tue, 03 Jun 2025 07:00:00 PDT": drop the zone name, and the day name in case it is wrong.
		var match = Regex.Match(text, @"(\d{1,2} \w{3} \d{4} \d{1,2}:\d{2}(:\d{2})?)");
		return match.Success && DateTimeOffset.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date) ? date : null;
	}

	/// <summary>itunes:duration is seconds, or M:SS, or H:MM:SS.</summary>
	internal static TimeSpan? ParseDuration(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;
		var parts = text.Trim().Split(':');
		var seconds = 0.0;
		foreach (var part in parts)
		{
			if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				return null;
			seconds = seconds * 60 + value;
		}
		return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
	}

	#endregion

	#region Downloads

	public IReadOnlyList<PodcastEpisode> DownloadedEpisodes
	{
		get { lock (locker) return store.Downloaded.Values.Where(e => File.Exists(EpisodePath(e))).ToList(); }
	}

	public PodcastEpisode? FindDownloaded(string episodeId)
	{
		lock (locker) return store.Downloaded.TryGetValue(episodeId, out var episode) && File.Exists(EpisodePath(episode)) ? episode : null;
	}

	public bool IsDownloaded(PodcastEpisode episode) => File.Exists(EpisodePath(episode));

	public string EpisodePath(PodcastEpisode episode) => Path.Combine(EpisodesDirectory, episode.Id + Extension(episode));

	/// <summary>The file's type, which the decoders go by: from the feed's type, else the address.</summary>
	private static string Extension(PodcastEpisode episode)
	{
		var type = episode.AudioType?.ToLowerInvariant() ?? "";
		if (type.Contains("mp4") || type.Contains("m4a") || type.Contains("aac"))
			return ".m4a";
		if (type.Contains("mpeg") || type.Contains("mp3"))
			return ".mp3";
		var fromUrl = Path.GetExtension(new Uri(episode.AudioUrl).AbsolutePath).ToLowerInvariant();
		return fromUrl is ".m4a" or ".mp4" or ".aac" ? ".m4a" : fromUrl is ".ogg" or ".opus" or ".wav" ? fromUrl : ".mp3";
	}

	public async Task DownloadAsync(PodcastEpisode episode, Action<double> progress, CancellationToken token)
	{
		using var backgroundWork = FileTransfer.BeginBackgroundWork($"Download {episode.Id}");
		var path = EpisodePath(episode);
		var partial = path + ".partial";

		var transferred = false;
		if (FileTransfer.Platform is { } transfer)
		{
			try
			{
				await transfer.DownloadAsync(new Uri(episode.AudioUrl), partial, USER_AGENT, progress, token);
				transferred = true;
			}
			catch (IOException ex)
			{
				Console.WriteLine($"Background download of {episode.Id} failed, downloading in the app: {ex.Message}");
			}
		}
		if (!transferred)
			await BookDownloader.DownloadFileAsync(episode.AudioUrl, partial, progress, token, USER_AGENT);

		File.Move(partial, path, overwrite: true);
		lock (locker)
		{
			store.Downloaded[episode.Id] = episode;
			Save();
		}
	}

	public void DeleteDownload(PodcastEpisode episode)
	{
		File.Delete(EpisodePath(episode));
		File.Delete(EpisodePath(episode) + ".partial");
		lock (locker)
		{
			if (store.Downloaded.Remove(episode.Id))
				Save();
		}
	}

	#endregion

	/// <summary>A podcast's artwork, downloaded once and kept.</summary>
	public async Task<byte[]?> GetArtworkAsync(string showId, string? url)
	{
		var path = Path.Combine(artDirectory, showId + ".jpg");
		try
		{
			if (File.Exists(path))
				return await File.ReadAllBytesAsync(path);
			if (url is null)
				return null;
			var bytes = await Http.GetByteArrayAsync(url);
			await File.WriteAllBytesAsync(path, bytes);
			return bytes;
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
		{
			return null;
		}
	}

	private void Save()
	{
		var temp = storeFile + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(store, PodcastJsonContext.Default.Store));
		File.Move(temp, storeFile, overwrite: true);
	}

	private static Store Load(string path)
	{
		try
		{
			if (File.Exists(path))
				return JsonSerializer.Deserialize(File.ReadAllText(path), PodcastJsonContext.Default.Store) ?? new();
		}
		catch (JsonException)
		{
			// A damaged file starts again rather than stopping the app; downloaded audio stays on disk.
		}
		return new();
	}
}

[JsonSerializable(typeof(PodcastLibrary.Store))]
internal partial class PodcastJsonContext : JsonSerializerContext
{
}
