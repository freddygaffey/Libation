using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>A chapter of a voiced book: its text on disk, and its audio once read.</summary>
public class VoicedChapter
{
	public string Title { get; set; } = "";
	public int Words { get; set; }
	/// <summary>Length of its audio; 0 until read.</summary>
	public double Seconds { get; set; }
	public bool Done { get; set; }
}

/// <summary>A book made from a document, read by one of the phone's voices.</summary>
public class VoicedBook
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string? Author { get; set; }
	/// <summary>The file or address it was made from.</summary>
	public string Source { get; set; } = "";
	public string VoiceId { get; set; } = "";
	public string VoiceName { get; set; } = "";
	public DateTimeOffset Added { get; set; }
	public List<VoicedChapter> Chapters { get; set; } = [];
	/// <summary>Every chapter read and joined into one file, ready to play.</summary>
	public bool Ready { get; set; }
	public string? Error { get; set; }

	[JsonIgnore]
	public int Words => Chapters.Sum(c => c.Words);
}

/// <summary>
/// Books voiced on the phone: each kept in its own folder (book.json, the chapters' text, their audio), read a few
/// chapters at a time, and joined into one file when all are done. Survives the app closing: reading carries on from
/// the first unread chapter when the app is next open.
/// </summary>
public class VoicedLibrary
{
	/// <summary>Chapters read at once. The phone's voices run on the CPU; a few at once uses its cores.</summary>
	public const int PARALLEL = 3;
	private const string BOOK_FILE = "book.m4a";
	private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

	private readonly string root;
	private readonly Lock locker = new();
	private readonly List<VoicedBook> books;
	private readonly Dictionary<string, double> chapterProgress = [];
	private CancellationTokenSource? running;

	/// <summary>A book was added, read further, finished, or failed. Raised on a worker thread.</summary>
	public event Action<VoicedBook>? Changed;

	public VoicedLibrary(string dataDirectory)
	{
		root = Path.Combine(dataDirectory, "Voiced");
		Directory.CreateDirectory(root);
		books = Directory.EnumerateDirectories(root)
			.Select(dir => Path.Combine(dir, "book.json"))
			.Where(File.Exists)
			.Select(path =>
			{
				try { return JsonSerializer.Deserialize(File.ReadAllText(path), VoicedJsonContext.Default.VoicedBook); }
				catch (JsonException) { return null; }
			})
			.OfType<VoicedBook>()
			.OrderByDescending(b => b.Added)
			.ToList();
	}

	public IReadOnlyList<VoicedBook> Books { get { lock (locker) return books.ToList(); } }

	public VoicedBook? Find(string? id) { lock (locker) return books.FirstOrDefault(b => b.Id == id); }

	public bool IsRunning => running is not null;

	/// <summary>How far through reading a book is, 0 to 1, by words.</summary>
	public double Progress(VoicedBook book)
	{
		if (book.Ready)
			return 1;
		lock (locker)
		{
			var done = book.Chapters.Select((c, i) => c.Done ? c.Words : c.Words * chapterProgress.GetValueOrDefault($"{book.Id}/{i}")).Sum();
			return book.Words > 0 ? done / book.Words : 0;
		}
	}

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

	/// <summary>Make a book from a document: its text is split into chapters and kept, ready to be read.</summary>
	public VoicedBook Add(DocumentContent content, string source, string title, string? author, VoiceChoice voice)
	{
		var book = new VoicedBook
		{
			Id = "voiced-" + Guid.NewGuid().ToString("N")[..10],
			Title = title,
			Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
			Source = source,
			VoiceId = voice.Id,
			VoiceName = voice.Name,
			Added = DateTimeOffset.Now,
			Chapters = content.Chapters.Select(c => new VoicedChapter { Title = c.Title, Words = DocumentText.CountWords(c.Text) }).ToList(),
		};
		var dir = Directory.CreateDirectory(Folder(book)).FullName;
		for (var i = 0; i < content.Chapters.Count; i++)
			File.WriteAllText(Path.Combine(dir, $"{i:000}.txt"), content.Chapters[i].Text);
		if (content.Cover is { } cover)
			File.WriteAllBytes(Path.Combine(dir, "cover.png"), cover);
		lock (locker)
			books.Insert(0, book);
		Save(book);
		Changed?.Invoke(book);
		return book;
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

	/// <summary>Read whatever is unread, a few chapters at a time, until every book is ready. Does nothing if already reading.</summary>
	public void Start()
	{
		if (BookVoice.Platform is not { } voice)
			return;
		CancellationTokenSource token;
		lock (locker)
		{
			if (running is not null)
				return;
			running = token = new CancellationTokenSource();
		}
		_ = Task.Run(async () =>
		{
			try
			{
				while (!token.IsCancellationRequested && Books.FirstOrDefault(b => !b.Ready && b.Error is null) is { } book)
					await ReadAsync(book, voice, token.Token);
			}
			finally
			{
				lock (locker)
					running = null;
			}
		});
	}

	public void Stop() => running?.Cancel();

	private async Task ReadAsync(VoicedBook book, IBookVoice voice, CancellationToken token)
	{
		var dir = Folder(book);
		try
		{
			using var gate = new SemaphoreSlim(PARALLEL);
			var tasks = book.Chapters.Select(async (chapter, i) =>
			{
				if (chapter.Done && File.Exists(PartPath(dir, i)))
					return;
				await gate.WaitAsync(token);
				try
				{
					var text = await File.ReadAllTextAsync(Path.Combine(dir, $"{i:000}.txt"), token);
					// The chapter's title is read first, then its paragraphs.
					var paragraphs = new[] { chapter.Title }.Concat(text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)).ToList();
					var key = $"{book.Id}/{i}";
					var progress = new Progress<double>(p =>
					{
						lock (locker)
							chapterProgress[key] = p;
						Changed?.Invoke(book);
					});
					var length = await voice.RenderAsync(paragraphs, book.VoiceId, PartPath(dir, i), progress, token);
					lock (locker)
					{
						chapter.Seconds = length.TotalSeconds;
						chapter.Done = true;
						chapterProgress.Remove(key);
					}
					Save(book);
					Changed?.Invoke(book);
				}
				finally
				{
					gate.Release();
				}
			}).ToList();
			await Task.WhenAll(tasks);

			await voice.JoinAsync(book.Chapters.Select((_, i) => PartPath(dir, i)).ToList(), Path.Combine(dir, BOOK_FILE), token);
			for (var i = 0; i < book.Chapters.Count; i++)
				File.Delete(PartPath(dir, i));
			book.Ready = true;
			Save(book);
			Changed?.Invoke(book);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			book.Error = ex.Message;
			Save(book);
			Changed?.Invoke(book);
		}
	}

	/// <summary>Try a failed book again, from where it stopped.</summary>
	public void Retry(VoicedBook book)
	{
		book.Error = null;
		Save(book);
		Start();
	}

	/// <summary>The finished book, to play, with its chapters from the lengths of their parts.</summary>
	public LocalBook ToLocalBook(VoicedBook book)
	{
		var chapters = new Mpeg4Lib.ChapterInfo(TimeSpan.Zero);
		foreach (var chapter in book.Chapters)
			chapters.AddChapter(chapter.Title, TimeSpan.FromSeconds(chapter.Seconds));
		return new LocalBook(book.Id, Path.Combine(Folder(book), BOOK_FILE), book.Title, book.Author, book.VoiceName,
			TimeSpan.FromSeconds(book.Chapters.Sum(c => c.Seconds)), Cover(book), chapters.Chapters.ToList());
	}

	/// <summary>The cover made from the document's first page, if it had one.</summary>
	public byte[]? Cover(VoicedBook book) => File.Exists(Path.Combine(Folder(book), "cover.png")) ? File.ReadAllBytes(Path.Combine(Folder(book), "cover.png")) : null;

	private string Folder(VoicedBook book) => Path.Combine(root, book.Id);
	private static string PartPath(string dir, int i) => Path.Combine(dir, $"{i:000}.m4a");

	private void Save(VoicedBook book)
	{
		string json;
		lock (locker)
			json = JsonSerializer.Serialize(book, VoicedJsonContext.Default.VoicedBook);
		var path = Path.Combine(Folder(book), "book.json");
		if (!Directory.Exists(Folder(book)))
			return;
		File.WriteAllText(path + ".tmp", json);
		File.Move(path + ".tmp", path, overwrite: true);
	}
}

[JsonSerializable(typeof(VoicedBook))]
internal partial class VoicedJsonContext : JsonSerializerContext
{
}
