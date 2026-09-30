using AAXClean;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>An audiobook file stored on the device.</summary>
/// <param name="Id">Stable key for saved positions: the file name, which the app controls.</param>
public record LocalBook(string Id, string Path, string Title, string? Author, string? Narrator, TimeSpan Duration, byte[]? Cover);

/// <summary>The audiobooks in the app's own storage.</summary>
public class BookLibrary
{
	private static readonly string[] AudioExtensions = [".m4b", ".m4a", ".mp3"];

	public string BooksDirectory { get; }

	public BookLibrary(string booksDirectory)
	{
		BooksDirectory = booksDirectory;
		Directory.CreateDirectory(BooksDirectory);
	}

	public Task<IReadOnlyList<LocalBook>> LoadAsync() => Task.Run<IReadOnlyList<LocalBook>>(() =>
		Directory.EnumerateFiles(BooksDirectory)
			.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
			.Select(ReadBook)
			.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
			.ToList());

	/// <summary>Copy an audio file into the library.</summary>
	/// <returns>The imported book.</returns>
	public async Task<LocalBook> ImportAsync(Stream source, string fileName)
	{
		var name = Path.GetFileName(fileName);
		if (!AudioExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
			throw new InvalidDataException($"{name} is not an m4b, m4a or mp3 file.");

		var destination = UniquePath(Path.Combine(BooksDirectory, name));
		var partial = destination + ".partial";
		try
		{
			await using (var target = File.Create(partial))
				await source.CopyToAsync(target);
			File.Move(partial, destination);
		}
		finally
		{
			File.Delete(partial);
		}
		return ReadBook(destination);
	}

	public void Delete(LocalBook book) => File.Delete(book.Path);

	private static LocalBook ReadBook(string path)
	{
		var id = Path.GetFileName(path);
		var fallbackTitle = Path.GetFileNameWithoutExtension(path);
		try
		{
			using var mp4 = new Mp4File(path);
			var tags = mp4.MetadataItems;
			return new LocalBook(id, path, tags.TitleSansUnabridged ?? fallbackTitle, tags.FirstAuthor, tags.Narrator, mp4.Duration, tags.Cover);
		}
		catch
		{
			// Not MPEG-4 (e.g. mp3), or unreadable tags: list it by file name. Duration is filled in when played.
			return new LocalBook(id, path, fallbackTitle, null, null, TimeSpan.Zero, null);
		}
	}

	private static string UniquePath(string path)
	{
		var dir = Path.GetDirectoryName(path)!;
		var stem = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);
		for (var i = 2; File.Exists(path); i++)
			path = Path.Combine(dir, $"{stem} ({i}){ext}");
		return path;
	}
}
