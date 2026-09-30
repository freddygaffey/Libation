using AudioPlayer;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;

namespace LibationMobile.Views;

public partial class SpikeView : UserControl
{
	private AudioFilePlayer? player;
	private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
	private readonly string booksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Books");

	public SpikeView()
	{
		InitializeComponent();
		Directory.CreateDirectory(booksDir);
		var file = Directory.EnumerateFiles(booksDir).FirstOrDefault();
		fileLbl.Text = file ?? $"No file in {booksDir}";

		play1.Click += (_, _) => Play(file, 1f);
		play3.Click += (_, _) => Play(file, 3f);
		play10.Click += (_, _) => Play(file, 10f);
		stopBtn.Click += (_, _) => { player?.Dispose(); player = null; };
		timer.Tick += (_, _) => statusLbl.Text = player is null ? "stopped" : $"pos {player.Position.TotalSeconds:F2}s / {player.Duration.TotalSeconds:F2}s\nspeed {player.Speed}x playing={player.IsPlaying}";
		timer.Start();
	}

	private void Play(string? file, float speed)
	{
		if (file is null)
			return;
		try
		{
			player ??= new AudioFilePlayer(new FFmpegPcmSource(file));
			player.Speed = speed;
			player.Play();
		}
		catch (Exception ex)
		{
			statusLbl.Text = ex.ToString();
		}
	}
}
