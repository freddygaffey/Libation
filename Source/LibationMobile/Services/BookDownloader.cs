using AAXClean;
using AudibleApi;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>Downloads a book from Audible and decrypts it to a plain m4b the player can open.</summary>
public class BookDownloader
{
	// Share of the progress bar given to the network download; decrypting fills the rest.
	private const double DOWNLOAD_SHARE = 0.9;
	private const int BUFFER_SIZE = 1 << 16;

	private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

	private readonly LibraryCatalog catalog;

	public BookDownloader(LibraryCatalog catalog) => this.catalog = catalog;

	/// <param name="progress">Reports 0 to 1.</param>
	public async Task DownloadAsync(Api api, CatalogBook book, IProgress<double> progress, CancellationToken token)
	{
		var license = await api.GetDownloadLicenseAsync(book.Asin, DownloadQuality.High);
		var url = license.ContentMetadata?.ContentUrl?.OfflineUrl
			?? throw new InvalidDataException("Audible did not provide a download link for this book.");
		if (license.Voucher?.Key is not string key || license.Voucher.Iv is not string iv)
			throw new InvalidDataException("Audible did not provide a decryption key for this book.");

		var encrypted = Path.Combine(catalog.BooksDirectory, book.Asin + ".aaxc");
		var decrypted = catalog.BookPath(book.Asin) + ".partial";

		await DownloadFileAsync(url, encrypted, p => progress.Report(p * DOWNLOAD_SHARE), token);

		try
		{
			await using (var input = File.OpenRead(encrypted))
			await using (var output = File.Create(decrypted))
			{
				var aax = new AaxFile(input);
				aax.SetDecryptionKey(Convert.FromHexString(key), Convert.FromHexString(iv));
				var operation = aax.ConvertToMp4aAsync(output);
				operation.ConversionProgressUpdate += (_, e) => progress.Report(DOWNLOAD_SHARE + e.FractionCompleted * (1 - DOWNLOAD_SHARE));
				using var registration = token.Register(() => _ = operation.CancelAsync());
				await operation;
				token.ThrowIfCancellationRequested();
			}
			File.Move(decrypted, catalog.BookPath(book.Asin), overwrite: true);
			File.Delete(encrypted);
			progress.Report(1);
		}
		finally
		{
			File.Delete(decrypted);
		}
	}

	/// <summary>
	/// Download to <paramref name="path"/>, resuming from what is already there. A phone connection can drop
	/// halfway through a book, and restarting a 500 MB download from zero would be wasteful.
	/// </summary>
	private static async Task DownloadFileAsync(string url, string path, Action<double> progress, CancellationToken token)
	{
		var existing = File.Exists(path) ? new FileInfo(path).Length : 0;

		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("User-Agent", DeviceRegistrationProfile.Default.DownloadUserAgent);
		if (existing > 0)
			request.Headers.Range = new RangeHeaderValue(existing, null);

		using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
		if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
		{
			// Already complete.
			progress(1);
			return;
		}
		response.EnsureSuccessStatusCode();

		// The server may ignore the range and send the whole file again.
		var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
		if (!resuming)
			existing = 0;
		var total = existing + (response.Content.Headers.ContentLength ?? 0);

		await using var source = await response.Content.ReadAsStreamAsync(token);
		await using var target = new FileStream(path, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE);

		var buffer = new byte[BUFFER_SIZE];
		var written = existing;
		int read;
		while ((read = await source.ReadAsync(buffer, token)) > 0)
		{
			await target.WriteAsync(buffer.AsMemory(0, read), token);
			written += read;
			if (total > 0)
				progress((double)written / total);
		}
	}
}
