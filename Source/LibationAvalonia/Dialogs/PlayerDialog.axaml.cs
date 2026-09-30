using Avalonia.Controls;
using Avalonia.Input;
using LibationUiBase.Player;
using System;

namespace LibationAvalonia.Dialogs;

public partial class PlayerDialog : DialogWindow
{
	private PlayerViewModel? ViewModel => DataContext as PlayerViewModel;

	public PlayerDialog()
	{
		InitializeComponent();
		SaveOnEnter = false;

		foreach (var speed in PlayerViewModel.SpeedPresets)
		{
			var button = new Button { Content = $"{speed:0.##}x", Margin = new(2) };
			button.Click += (_, _) =>
			{
				if (ViewModel is not null)
					ViewModel.Speed = speed;
			};
			speedPresetsPanel.Children.Add(button);
		}

		// Tunnel so the keys work even when a slider or button has focus.
		AddHandler(KeyDownEvent, PlayerDialog_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		Closed += PlayerDialog_Closed;
	}

	public PlayerDialog(PlayerViewModel viewModel) : this()
	{
		DataContext = viewModel;
		ControlToFocusOnShow = playPauseButton;
	}

	private void PlayerDialog_KeyDown(object? sender, KeyEventArgs e)
	{
		if (ViewModel is not { } vm || e.Source is ComboBox)
			return;

		switch (e.Key)
		{
			case Key.Space:
				vm.PlayPause();
				break;
			case Key.Left:
				vm.SkipBack();
				break;
			case Key.Right:
				vm.SkipForward();
				break;
			default:
				return;
		}
		e.Handled = true;
	}

	private void PlayerDialog_Closed(object? sender, EventArgs e)
	{
		ViewModel?.Dispose();
		DataContext = null;
	}
}
