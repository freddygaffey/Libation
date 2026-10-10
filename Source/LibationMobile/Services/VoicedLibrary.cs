using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A chapter of a voiced book: its title, and where it starts in the book's text.</summary>
public class VoicedChapter
{
	public string Title { get; set; } = "";
	/// <summary>The character it starts at in text.txt.</summary>
	public int Start { get; set; }
	public int Words { get; set; }
}

/// <summary>A book made from a document, read aloud as it plays by one of the phone's voices.</summary>
public class VoicedBook
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string? Author { get; set; }
	/// <summary>The file or address it was made from.</summary>
	public string Source { get; set; } = "";
	/// <summary>The voice it was last read in; it can be changed while listening.</summary>
	public string VoiceId { get; set; } = "";
	public string VoiceName { get; set; } = "";
	public DateTimeOffset Added { get; set; }
	public List<VoicedChapter> Chapters { get; set; } = [];
	public int Characters { get; set; }

	[JsonIgnore]
	public int Words => Chapters.Sum(c => c.Words);

	/// <summary>Its length on the timeline, at the fixed pace (<see cref="BookVoice.SECONDS_PER_CHARACTER"/>).</summary>
	[JsonIgnore]
	public TimeSpan Length => TimeSpan.FromSeconds(Characters * BookVoice.SECONDS_PER_CHARACTER);
}

/// <summary>
/// Books made from documents: each kept in its own folder (book.json, the text, a cover), and read aloud as they play,
/// so one can be listened to the moment it is added.
/// </summary>
public class VoicedLibrary
{
	private const string TEXT_FILE = "text.txt";
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

	private readonly string root;
	private readonly Lock locker = new();
	private readonly List<VoicedBook> books;

	/// <summary>A book was added, removed or changed.</summary>
	public event Action<VoicedBook>? Changed;

	public VoicedLibrary(string dataDirectory)
	{
		root = Path.Combine(dataDirectory, "Voiced");
		Directory.CreateDirectory(root);
		books = Directory.EnumerateDirectories(root)
			.Select(Load)
			.OfType<VoicedBook>()
			.OrderByDescending(b => b.Added)
			.ToList();
	}

	public IReadOnlyList<VoicedBook> Books { get { lock (locker) return books.ToList(); } }

	public VoicedBook? Find(string? id) { lock (locker) return books.FirstOrDefault(b => b.Id == id); }

	/// <summary>Fetch a document from the web to a temporary file, keeping its type from the address or the server.</summary>
	public static async Task<string> DownloadAsync(Uri url, string tempDirectory, CancellationToken token)
	{
		using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode();
		var extension = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
		if (!DocumentText.Extensions.Contains(extension))
			extension = response.Content.Headers.ContentType?.MediaType switch
			{
				"application/pdf" => ".pdf",
				"application/epub+zip" => ".epub",
				"text/plain" or "text/markdown" => ".txt",
				var type => throw new NotSupportedException($"That address gives {type ?? "an unknown kind of file"}, not a PDF, EPUB or text file."),
			};
		var name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(url.AbsolutePath)) is { Length: > 0 } stem ? stem : "document";
		Directory.CreateDirectory(tempDirectory);
		var path = Path.Combine(tempDirectory, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + extension);
		await using (var file = File.Create(path))
			await response.Content.CopyToAsync(file, token);
		return path;
	}

	/// <summary>Make a book from a document: its chapters, each with its title read first, in one text.</summary>
	public VoicedBook Add(DocumentContent content, string source, string title, string? author, VoiceChoice voice)
	{
		var text = new StringBuilder();
		var chapters = new List<VoicedChapter>();
		foreach (var chapter in content.Chapters)
		{
			chapters.Add(new VoicedChapter { Title = chapter.Title, Start = text.Length, Words = DocumentText.CountWords(chapter.Text) });
			text.Append(chapter.Title).Append(".\n\n").Append(chapter.Text).Append("\n\n");
		}
		var book = new VoicedBook
		{
			Id = "voiced-" + Guid.NewGuid().ToString("N")[..10],
			Title = title,
			Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
			Source = source,
			VoiceId = voice.Id,
			VoiceName = voice.Name,
			Added = DateTimeOffset.Now,
			Chapters = chapters,
			Characters = text.Length,
		};
		var dir = Directory.CreateDirectory(Folder(book)).FullName;
		File.WriteAllText(Path.Combine(dir, TEXT_FILE), text.ToString());
		if (content.Cover is { } cover)
			File.WriteAllBytes(Path.Combine(dir, "cover.png"), cover);
		// The same book made again replaces the copy made before.
		foreach (var older in Books.Where(b => string.Equals(b.Title, book.Title, StringComparison.CurrentCultureIgnoreCase)).ToList())
			Delete(older);
		lock (locker)
			books.Insert(0, book);
		Save(book);
		Changed?.Invoke(book);
		return book;
	}

	/// <summary>Older copies of a book made more than once, before making it again replaced them. Returns how many went.</summary>
	public int RemoveDuplicates()
	{
		var older = Books.GroupBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase).SelectMany(g => g.OrderByDescending(b => b.Added).Skip(1)).ToList();
		foreach (var book in older)
			Delete(book);
		return older.Count;
	}

	public void Delete(VoicedBook book)
	{
		lock (locker)
			books.Remove(book);
		try
		{
			Directory.Delete(Folder(book), recursive: true);
		}
		catch (IOException ex)
		{
			Console.WriteLine($"Voiced book {book.Id} not removed: {ex.Message}");
		}
		Changed?.Invoke(book);
	}

	/// <summary>The voice changed while listening: offered first next time, and named in Downloads.</summary>
	public void SetVoice(VoicedBook book, VoiceChoice voice)
	{
		book.VoiceId = voice.Id;
		book.VoiceName = voice.Name;
		Save(book);
		Changed?.Invoke(book);
	}

	/// <summary>The book to play: its text read aloud as it plays, with chapters at their places on the timeline.</summary>
	public LocalBook ToLocalBook(VoicedBook book, IBookVoice voice)
	{
		var pace = BookVoice.SECONDS_PER_CHARACTER;
		var chapters = new Mpeg4Lib.ChapterInfo(TimeSpan.Zero);
		for (var i = 0; i < book.Chapters.Count; i++)
		{
			var end = i + 1 < book.Chapters.Count ? book.Chapters[i + 1].Start : book.Characters;
			chapters.AddChapter(book.Chapters[i].Title, TimeSpan.FromSeconds((end - book.Chapters[i].Start) * pace));
		}
		var textPath = Path.Combine(Folder(book), TEXT_FILE);
		return new LocalBook(book.Id, textPath, book.Title, book.Author, book.VoiceName, book.Length, Cover(book), chapters.Chapters.ToList(),
			OpenSource: () => voice.Open(File.ReadAllText(textPath), book.VoiceId, pace, textPath));
	}

	public string TextPath(VoicedBook book) => Path.Combine(Folder(book), TEXT_FILE);

	/// <summary>The cover made from the document's first page, if it had one.</summary>
	public byte[]? Cover(VoicedBook book) => File.Exists(Path.Combine(Folder(book), "cover.png")) ? File.ReadAllBytes(Path.Combine(Folder(book), "cover.png")) : null;

	private string Folder(VoicedBook book) => Path.Combine(root, book.Id);

	private static VoicedBook? Load(string dir)
	{
		try
		{
			var path = Path.Combine(dir, "book.json");
			if (!File.Exists(path))
				return null;
			var json = File.ReadAllText(path);
			var book = JsonSerializer.Deserialize(json, VoicedJsonContext.Default.VoicedBook);
			if (book is null)
				return null;
			if (!File.Exists(Path.Combine(dir, TEXT_FILE)))
				Upgrade(book, dir);
			return book;
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			Console.WriteLine($"Voiced book in {dir} not loaded: {ex.Message}");
			return null;
		}
	}

	/// <summary>A book voiced to audio files before books were read as they play: its chapters' text in one, the audio removed.</summary>
	private static void Upgrade(VoicedBook book, string dir)
	{
		var text = new StringBuilder();
		for (var i = 0; i < book.Chapters.Count; i++)
		{
			var part = Path.Combine(dir, $"{i:000}.txt");
			book.Chapters[i].Start = text.Length;
			text.Append(book.Chapters[i].Title).Append(".\n\n").Append(File.Exists(part) ? File.ReadAllText(part) : "").Append("\n\n");
		}
		book.Characters = text.Length;
		File.WriteAllText(Path.Combine(dir, TEXT_FILE), text.ToString());
		foreach (var old in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".m4a") || Path.GetFileName(f) is var n && n.Length == 7 && n.EndsWith(".txt")))
			File.Delete(old);
		File.WriteAllText(Path.Combine(dir, "book.json"), JsonSerializer.Serialize(book, VoicedJsonContext.Default.VoicedBook));
	}

	private void Save(VoicedBook book)
	{
		if (!Directory.Exists(Folder(book)))
			return;
		var path = Path.Combine(Folder(book), "book.json");
		File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(book, VoicedJsonContext.Default.VoicedBook));
		File.Move(path + ".tmp", path, overwrite: true);
	}
}

[JsonSerializable(typeof(VoicedBook))]
internal partial class VoicedJsonContext : JsonSerializerContext
{
}
