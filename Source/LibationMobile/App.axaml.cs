using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LibationMobile.ViewModels;
using LibationMobile.Views;
using System;

namespace LibationMobile;

public partial class App : Application
{
	/// <summary>
	/// One for the life of the process. Android recreates the view on rotation and similar changes, and playback
	/// must carry on across that.
	/// </summary>
	private MainViewModel? mainViewModel;

	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
			activityLifetime.MainViewFactory = CreateMainView;
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
			singleView.MainView = CreateMainView();

		base.OnFrameworkInitializationCompleted();
	}

	private MainView CreateMainView()
	{
		if (mainViewModel is null)
		{
			mainViewModel = new MainViewModel(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
			_ = mainViewModel.InitializeAsync();
		}
		return new MainView { DataContext = mainViewModel };
	}
}
