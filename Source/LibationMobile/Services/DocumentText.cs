using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LibationMobile.Services;

/// <summary>A chapter of a document, as text to be read aloud.</summary>
public record TextChapter(string Title, string Text);

/// <summary>A document's text in chapters, with its title and author where the file says, and a cover where it has one.</summary>
public record DocumentContent(string? Title, string? Author, IReadOnlyList<TextChapter> Chapters, byte[]? Cover = null)
{
	public int Words => Chapters.Sum(c => DocumentText.CountWords(c.Text));
}

/// <summary>A PDF's text, page by page, and its outline. From the platform: PDFKit on iOS.</summary>
public interface IPdfReader
{
	/// <param name="Outline">Top-level outline entries with the page (from 0) each starts on; empty when there is none.</param>
	(string? Title, string? Author, IReadOnlyList<string> Pages, IReadOnlyList<(string Title, int Page)> Outline) Read(string path);

	/// <summary>The first page with anything on it, as a PNG, for a cover.</summary>
	byte[]? Cover(string path);
}

/// <summary>
/// The text of a PDF, EPUB or plain text file, cleaned for reading aloud: running headers, footers and page numbers
/// dropped, words broken across lines joined, and lines joined into paragraphs. Split into chapters by the file's own
/// outline or table of contents, or by "Chapter" headings, or else into parts of about 5,000 words.
/// </summary>
public static partial class DocumentText
{
	/// <summary>Set by the platform head. Null where PDFs cannot be read.</summary>
	public static IPdfReader? Pdf { get; set; }

	public static readonly string[] Extensions = [".pdf", ".epub", ".txt", ".md"];
	private const int PART_WORDS = 5000;

	public static DocumentContent Read(string path)
	{
		var content = Path.GetExtension(path).ToLowerInvariant() switch
		{
			".pdf" => ReadPdf(path),
			".epub" => ReadEpub(path),
			".txt" or ".md" => FromPlainText(File.ReadAllText(path), null),
			var other => throw new NotSupportedException($"{other} files cannot be voiced. Use a PDF, EPUB or text file."),
		};
		var chapters = content.Chapters.Where(c => CountWords(c.Text) > 0).SelectMany(SplitLong).ToList();
		if (chapters.Count == 0)
			throw new InvalidDataException("No text was found. A scanned PDF has only pictures of pages, which cannot be read aloud.");
		return content with { Title = string.IsNullOrWhiteSpace(content.Title) ? Path.GetFileNameWithoutExtension(path) : content.Title.Trim(), Chapters = chapters };
	}

	public static int CountWords(string text) => Words().Count(text);

	/// <summary>Longer chapters than this are split, so no chapter runs for hours, and more of a book can be read at once.</summary>
	private const int LONGEST_CHAPTER_WORDS = 9000;

	/// <summary>A chapter too long to be one, as a book whose outline has only its parts, in pieces at paragraph ends: "Rorschach 2".</summary>
	private static IEnumerable<TextChapter> SplitLong(TextChapter chapter)
	{
		var words = CountWords(chapter.Text);
		if (words <= LONGEST_CHAPTER_WORDS)
		{
			yield return chapter;
			yield break;
		}
		var pieces = (int)Math.Ceiling(words / (double)PART_WORDS);
		var target = words / pieces;
		var piece = new StringBuilder();
		var number = 1;
		foreach (var paragraph in chapter.Text.Split("\n\n"))
		{
			piece.Append(paragraph).Append("\n\n");
			if (number < pieces && CountWords(piece.ToString()) >= target)
			{
				yield return new TextChapter($"{chapter.Title} {number++}", piece.ToString().Trim());
				piece.Clear();
			}
		}
		if (piece.Length > 0)
			yield return new TextChapter(number == 1 ? chapter.Title : $"{chapter.Title} {number}", piece.ToString().Trim());
	}

	#region PDF

	private static DocumentContent ReadPdf(string path)
	{
		if (Pdf is not { } reader)
			throw new NotSupportedException("PDFs cannot be read on this device.");
		var (title, author, pages, outline) = reader.Read(path);
		var cover = reader.Cover(path);
		var cleaned = DropRunningLines(pages);
		title = BookTitle(title, cleaned);
		var marks = outline.Where(o => o.Page >= 0 && o.Page < pages.Count).OrderBy(o => o.Page).ToList();
		if (marks.Count < 2)
			return FromPlainText(string.Join("\n", cleaned), title) with { Author = author, Cover = cover };

		// All the text, and where each page starts in it, so a chapter can start part-way down its page, at its heading.
		var text = new StringBuilder();
		var pageStarts = new List<int>();
		foreach (var page in cleaned)
		{
			pageStarts.Add(text.Length);
			text.Append(page).Append('\n');
		}
		var all = text.ToString();
		var starts = marks.Select(m =>
		{
			var pageEnd = m.Page + 1 < pageStarts.Count ? pageStarts[m.Page + 1] : all.Length;
			var at = all.IndexOf(m.Title, pageStarts[m.Page], pageEnd - pageStarts[m.Page], StringComparison.OrdinalIgnoreCase);
			return (m.Title, Start: at >= 0 ? at : pageStarts[m.Page]);
		}).ToList();

		var chapters = new List<TextChapter>();
		// Front matter before the first entry, if there is more to it than a title and contents.
		if (CountWords(all[..starts[0].Start]) > 150)
			chapters.Add(new TextChapter("Opening", Paragraphs(all[..starts[0].Start])));
		for (var i = 0; i < starts.Count; i++)
		{
			var end = i + 1 < starts.Count ? starts[i + 1].Start : all.Length;
			// An entry with nothing before the next, such as a part whose first chapter starts at once, gives way to it.
			if (CountWords(all[starts[i].Start..Math.Max(starts[i].Start, end)]) <= CountWords(starts[i].Title) + 2)
				continue;
			chapters.Add(new TextChapter(starts[i].Title, Paragraphs(all[starts[i].Start..end])));
		}
		return new DocumentContent(title, author, chapters, cover);
	}

	/// <summary>
	/// A PDF's title: its own, unless that is not on its first pages, as when it is left over from a draft; then the first
	/// short line of the first page.
	/// </summary>
	private static string? BookTitle(string? stored, IReadOnlyList<string> pages)
	{
		var opening = string.Join("\n", pages.Where(p => !string.IsNullOrWhiteSpace(p)).Take(3));
		if (!string.IsNullOrWhiteSpace(stored) && opening.Contains(stored.Trim(), StringComparison.OrdinalIgnoreCase))
			return stored.Trim();
		var first = opening.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length is > 1 and <= 80);
		return first ?? stored;
	}

	/// <summary>
	/// Each page's lines, without the running header, footer and page number: a line that recurs at the top or bottom of
	/// many pages once its digits are ignored, or one that is only a number.
	/// </summary>
	internal static List<string> DropRunningLines(IReadOnlyList<string> pages)
	{
		var lines = pages.Select(p => p.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList()).ToList();
		static string Shape(string line) => Digits().Replace(line, "#").ToLowerInvariant();
		var edges = lines.SelectMany(l => l.Take(2).Concat(l.TakeLast(2)).Select(Shape).Distinct())
			.GroupBy(s => s)
			.Where(g => g.Count() >= Math.Max(3, pages.Count / 4))
			.Select(g => g.Key)
			.ToHashSet();
		return lines.Select(page =>
		{
			var kept = page.Where((line, i) =>
			{
				var atEdge = i < 2 || i >= page.Count - 2;
				return !(atEdge && (edges.Contains(Shape(line)) || PageNumber().IsMatch(line)));
			});
			return string.Join("\n", kept);
		}).ToList();
	}

	#endregion

	#region EPUB

	private static DocumentContent ReadEpub(string path)
	{
		using var zip = ZipFile.OpenRead(path);
		string Read(string name) => new StreamReader(zip.GetEntry(name)?.Open() ?? throw new InvalidDataException($"The EPUB is missing {name}.")).ReadToEnd();

		var container = XDocument.Parse(Read("META-INF/container.xml"));
		var opfPath = container.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;
		var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') is { Length: > 0 } dir ? dir + "/" : "";
		var opf = XDocument.Parse(Read(opfPath));
		string? Meta(string name) => opf.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

		var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item")
			.ToDictionary(e => e.Attribute("id")!.Value, e => (Href: e.Attribute("href")!.Value, Type: e.Attribute("media-type")?.Value));
		var titles = EpubTitles(opf, manifest, opfDir, Read);

		var chapters = new List<TextChapter>();
		foreach (var itemref in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
		{
			if (!manifest.TryGetValue(itemref.Attribute("idref")?.Value ?? "", out var item) || item.Type is not ("application/xhtml+xml" or "text/html"))
				continue;
			var href = Uri.UnescapeDataString(item.Href.Split('#')[0]);
			var html = Read(opfDir + href);
			var text = Paragraphs(HtmlToText(html));
			if (CountWords(text) == 0)
				continue;
			var title = titles.GetValueOrDefault(href) ?? HtmlHeading(html) ?? $"Part {chapters.Count + 1}";
			chapters.Add(new TextChapter(title, text));
		}
		return new DocumentContent(Meta("title"), Meta("creator"), chapters);
	}

	/// <summary>Chapter titles by file, from the EPUB 3 navigation document or the EPUB 2 NCX.</summary>
	private static Dictionary<string, string> EpubTitles(XDocument opf, Dictionary<string, (string Href, string? Type)> manifest, string opfDir, Func<string, string> read)
	{
		var titles = new Dictionary<string, string>();
		try
		{
			var nav = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "item" && (e.Attribute("properties")?.Value.Contains("nav") ?? false));
			var ncx = manifest.Values.FirstOrDefault(m => m.Type == "application/x-dtbncx+xml");
			if (nav is not null)
			{
				var doc = XDocument.Parse(read(opfDir + nav.Attribute("href")!.Value));
				var navDir = Path.GetDirectoryName(nav.Attribute("href")!.Value)?.Replace('\\', '/') is { Length: > 0 } d ? d + "/" : "";
				foreach (var a in doc.Descendants().Where(e => e.Name.LocalName == "a" && e.Attribute("href") is not null))
					titles.TryAdd(navDir + Uri.UnescapeDataString(a.Attribute("href")!.Value.Split('#')[0]), Whitespace().Replace(a.Value, " ").Trim());
			}
			else if (ncx.Href is not null)
			{
				var doc = XDocument.Parse(read(opfDir + ncx.Href));
				foreach (var point in doc.Descendants().Where(e => e.Name.LocalName == "navPoint"))
				{
					var label = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value;
					var src = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;
					if (label is not null && src is not null)
						titles.TryAdd(Uri.UnescapeDataString(src.Split('#')[0]), label.Trim());
				}
			}
		}
		catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException)
		{
			// No usable contents: chapters take their headings.
		}
		return titles;
	}

	private static string? HtmlHeading(string html)
		=> Heading().Match(html) is { Success: true } m && WebUtility.HtmlDecode(Tag().Replace(m.Groups[1].Value, "")).Trim() is { Length: > 0 } heading ? heading : null;

	private static string HtmlToText(string html)
	{
		var text = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
		text = BlockEnd().Replace(text, "\n\n");
		return WebUtility.HtmlDecode(Tag().Replace(text, ""));
	}

	#endregion

	#region Plain text

	/// <summary>Chapters from "Chapter 3", "CHAPTER III", "Part Two" headings, or else parts of about 5,000 words.</summary>
	internal static DocumentContent FromPlainText(string text, string? title)
	{
		// Headings are found on the lines as they are, before lines are joined into paragraphs.
		var headings = ChapterHeading().Matches(text.Replace("\r", "")).ToList();
		var chapters = new List<TextChapter>();
		if (headings.Count >= 2)
		{
			var raw = text.Replace("\r", "");
			if (CountWords(raw[..headings[0].Index]) > 50)
				chapters.Add(new TextChapter("Opening", Paragraphs(raw[..headings[0].Index])));
			for (var i = 0; i < headings.Count; i++)
			{
				var start = headings[i].Index + headings[i].Length;
				var end = i + 1 < headings.Count ? headings[i + 1].Index : raw.Length;
				chapters.Add(new TextChapter(headings[i].Value.Trim(), Paragraphs(raw[start..end])));
			}
			return new DocumentContent(title, null, chapters);
		}

		var paragraphs = Paragraphs(text);
		var part = new StringBuilder();
		foreach (var paragraph in paragraphs.Split("\n\n"))
		{
			part.Append(paragraph).Append("\n\n");
			if (CountWords(part.ToString()) >= PART_WORDS)
			{
				chapters.Add(new TextChapter($"Part {chapters.Count + 1}", part.ToString().Trim()));
				part.Clear();
			}
		}
		if (part.Length > 0)
			chapters.Add(new TextChapter(chapters.Count == 0 ? "Part 1" : $"Part {chapters.Count + 1}", part.ToString().Trim()));
		return new DocumentContent(title, null, chapters);
	}

	/// <summary>
	/// Lines into paragraphs: a blank line, or a line ending a sentence well short of the usual line length, ends one;
	/// otherwise lines join with a space, and a word hyphenated across a line break is put back together.
	/// </summary>
	internal static string Paragraphs(string text)
	{
		var lines = text.Replace("\r", "").Split('\n').Select(l => Whitespace().Replace(l, " ").Trim()).ToList();
		var full = lines.Where(l => l.Length > 0).Select(l => l.Length).DefaultIfEmpty(60).OrderBy(n => n).ElementAt(lines.Count(l => l.Length > 0) * 3 / 4);
		var result = new StringBuilder();
		var paragraph = new StringBuilder();
		void End()
		{
			if (paragraph.Length > 0)
				result.Append(paragraph.ToString().Trim()).Append("\n\n");
			paragraph.Clear();
		}
		foreach (var line in lines)
		{
			if (line.Length == 0)
			{
				End();
				continue;
			}
			// A closing quote or bracket left on a line of its own belongs to the paragraph before.
			if (!line.Any(char.IsLetterOrDigit))
			{
				if (paragraph.Length > 0)
					paragraph.Append(line);
				else if (result.Length > 2)
				{
					result.Length -= 2;
					result.Append(line).Append("\n\n");
				}
				continue;
			}
			if (paragraph.Length > 0 && paragraph[^1] == '-' && char.IsLower(line[0]))
				paragraph.Length--;
			else if (paragraph.Length > 0)
				paragraph.Append(' ');
			paragraph.Append(line);
			if (line.Length < full * 0.7 && ".!?:\"”’)".Contains(line[^1]))
				End();
		}
		End();
		return result.ToString().Trim();
	}

	#endregion

	[GeneratedRegex(@"\d+")]
	private static partial Regex Digits();
	[GeneratedRegex(@"^(page\s*)?[\divxlc]+(\s*(of|/)\s*\d+)?$", RegexOptions.IgnoreCase)]
	private static partial Regex PageNumber();
	[GeneratedRegex(@"[ \t ]+")]
	private static partial Regex Whitespace();
	[GeneratedRegex(@"\b[\w'’-]+\b")]
	private static partial Regex Words();
	[GeneratedRegex("<[^>]+>")]
	private static partial Regex Tag();
	[GeneratedRegex(@"</(p|div|h[1-6]|li|blockquote|section|tr)\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
	private static partial Regex BlockEnd();
	[GeneratedRegex(@"<h[1-3][^>]*>(.*?)</h[1-3]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex Heading();
	[GeneratedRegex(@"(?m)^[ \t]*(chapter|part|book)\s+([0-9]+|[ivxlc]+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|[a-z]+teen|twenty[a-z-]*|thirty[a-z-]*)\b[^\n]{0,80}$", RegexOptions.IgnoreCase)]
	private static partial Regex ChapterHeading();
}
