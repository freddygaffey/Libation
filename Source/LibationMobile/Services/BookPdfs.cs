using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>
/// Books' companion PDFs (maps, tables, figures), kept in Pdfs/ by ASIN. Downloaded with the book, so they open
/// offline, or on first opening for books downloaded before this was done.
/// </summary>
public class BookPdfs(string dataDirectory, AudibleSeries store)
{
	private readonly string directory = Path.Combine(dataDirectory, "Pdfs");

	public string PathOf(string asin) => Path.Combine(directory, asin + ".pdf");

	public bool Has(string asin) => File.Exists(PathOf(asin));

	/// <summary>Download the PDF unless it is already here. Returns its path.</summary>
	public async Task<string> EnsureAsync(string asin, CancellationToken token = default)
	{
		var path = PathOf(asin);
		if (File.Exists(path))
			return path;
		Directory.CreateDirectory(directory);
		var link = await store.GetPdfLinkAsync(asin);
		var partial = path + ".partial";
		await Task.Run(() => BookDownloader.DownloadFileAsync(link.ToString(), partial, _ => { }, token), token);
		File.Move(partial, path, overwrite: true);
		return path;
	}

	public void Delete(string asin)
	{
		File.Delete(PathOf(asin));
		File.Delete(PathOf(asin) + ".partial");
	}
}
