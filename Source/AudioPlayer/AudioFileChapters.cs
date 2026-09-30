using AAXClean;
using Mpeg4Lib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AudioPlayer;

public static class AudioFileChapters
{
	/// <summary>Read the chapters embedded in an MPEG-4 audio file (m4b, m4a).</summary>
	/// <returns>The chapters in playback order, or an empty list if the file has none or is not MPEG-4.</returns>
	public static async Task<IReadOnlyList<Chapter>> ReadAsync(string path)
	{
		try
		{
			using var mp4 = new Mp4File(path);
			var chapterInfo = await mp4.GetChapterInfoAsync();
			return chapterInfo?.Chapters ?? [];
		}
		catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
		{
			// Not an MPEG-4 container, or its chapter track could not be parsed. Play without chapters.
			return [];
		}
	}
}
