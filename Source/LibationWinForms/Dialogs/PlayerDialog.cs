using LibationFileManager;
using LibationUiBase.Player;
using Mpeg4Lib;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace LibationWinForms.Dialogs;

/// <summary>Plays one downloaded audiobook. Disposes its <see cref="PlayerViewModel"/> when closed.</summary>
public partial class PlayerDialog : Form
{
	// TrackBar values are ints, so speed is held in tenths.
	private const int SPEED_SCALE = 10;

	private readonly PlayerViewModel viewModel;

	public PlayerDialog(PlayerViewModel viewModel)
	{
		this.viewModel = viewModel;

		InitializeComponent();
		this.SetLibationIcon();
		this.RestoreSizeAndLocation(Configuration.Instance);

		Text = $"Playing: {viewModel.Title}";
		titleLbl.Text = viewModel.Title;
		authorsLbl.Text = viewModel.Authors;

		chapterCb.Visible = viewModel.HasChapters;
		prevChapterBtn.Enabled = nextChapterBtn.Enabled = viewModel.HasChapters;
		chapterCb.DisplayMember = nameof(Chapter.Title);
		chapterCb.DataSource = viewModel.Chapters.ToList();

		positionTbar.Maximum = (int)Math.Ceiling(viewModel.DurationSeconds);
		positionTbar.SmallChange = 30;
		positionTbar.LargeChange = 300;

		speedTbar.Minimum = (int)(PlayerViewModel.MinSpeed * SPEED_SCALE);
		speedTbar.Maximum = (int)(PlayerViewModel.MaxSpeed * SPEED_SCALE);

		foreach (var speed in PlayerViewModel.SpeedPresets)
		{
			var button = new Button { Text = $"{speed:0.##}x", AutoSize = true };
			button.Click += (_, _) => viewModel.Speed = speed;
			speedPresetsPanel.Controls.Add(button);
		}

		UpdateAll();
		viewModel.PropertyChanged += ViewModel_PropertyChanged;
		FormClosed += PlayerDialog_FormClosed;
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (IsDisposed)
			return;

		switch (e.PropertyName)
		{
			case nameof(PlayerViewModel.IsPlaying):
				playPauseBtn.Text = viewModel.IsPlaying ? "Pause" : "Play";
				break;
			case nameof(PlayerViewModel.PositionSeconds):
				positionTbar.Value = Math.Clamp((int)viewModel.PositionSeconds, positionTbar.Minimum, positionTbar.Maximum);
				break;
			case nameof(PlayerViewModel.ElapsedText):
				elapsedLbl.Text = viewModel.ElapsedText;
				break;
			case nameof(PlayerViewModel.RemainingText):
				remainingLbl.Text = viewModel.RemainingText;
				break;
			case nameof(PlayerViewModel.Speed):
				speedTbar.Value = Math.Clamp((int)Math.Round(viewModel.Speed * SPEED_SCALE), speedTbar.Minimum, speedTbar.Maximum);
				speedValueLbl.Text = viewModel.SpeedText;
				break;
			case nameof(PlayerViewModel.Volume):
				volumeTbar.Value = (int)Math.Round(viewModel.Volume * volumeTbar.Maximum);
				break;
			case nameof(PlayerViewModel.CurrentChapter):
				if (viewModel.CurrentChapter is not null && !chapterCb.DroppedDown)
					chapterCb.SelectedItem = viewModel.CurrentChapter;
				break;
		}
	}

	private void UpdateAll()
	{
		foreach (var name in new[]
		{
			nameof(PlayerViewModel.IsPlaying), nameof(PlayerViewModel.PositionSeconds), nameof(PlayerViewModel.ElapsedText),
			nameof(PlayerViewModel.RemainingText), nameof(PlayerViewModel.Speed), nameof(PlayerViewModel.Volume), nameof(PlayerViewModel.CurrentChapter)
		})
			ViewModel_PropertyChanged(this, new PropertyChangedEventArgs(name));
	}

	private void playPauseBtn_Click(object sender, EventArgs e) => viewModel.PlayPause();
	private void skipBackBtn_Click(object sender, EventArgs e) => viewModel.SkipBack();
	private void skipForwardBtn_Click(object sender, EventArgs e) => viewModel.SkipForward();
	private void prevChapterBtn_Click(object sender, EventArgs e) => viewModel.PreviousChapter();
	private void nextChapterBtn_Click(object sender, EventArgs e) => viewModel.NextChapter();

	// Scroll is raised only by the user, never by setting Value, so these do not echo the view model's own updates.
	private void positionTbar_Scroll(object? sender, EventArgs e) => viewModel.Seek(TimeSpan.FromSeconds(positionTbar.Value));
	private void speedTbar_Scroll(object? sender, EventArgs e) => viewModel.Speed = speedTbar.Value / (double)SPEED_SCALE;
	private void volumeTbar_Scroll(object? sender, EventArgs e) => viewModel.Volume = volumeTbar.Value / (double)volumeTbar.Maximum;

	private void chapterCb_SelectionChangeCommitted(object? sender, EventArgs e)
	{
		if (chapterCb.SelectedItem is Chapter chapter)
			viewModel.CurrentChapter = chapter;
	}

	/// <summary>
	/// Handled here rather than in KeyDown because a focused button consumes the arrow keys for focus
	/// navigation before any KeyDown event is raised.
	/// </summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (ActiveControl is not ComboBox)
		{
			switch (keyData)
			{
				case Keys.Space:
					viewModel.PlayPause();
					return true;
				case Keys.Left:
					viewModel.SkipBack();
					return true;
				case Keys.Right:
					viewModel.SkipForward();
					return true;
			}
		}

		if (keyData == Keys.Escape)
		{
			Close();
			return true;
		}

		return base.ProcessCmdKey(ref msg, keyData);
	}

	private void PlayerDialog_FormClosed(object? sender, FormClosedEventArgs e)
	{
		viewModel.PropertyChanged -= ViewModel_PropertyChanged;
		this.SaveSizeAndLocation(Configuration.Instance);
		viewModel.Dispose();
	}
}
