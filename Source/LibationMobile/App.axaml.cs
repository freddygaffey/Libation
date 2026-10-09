using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LibationMobile.Services;
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
		// Subscribed before the view exists, so a link that started the app is not missed.
		if (this.TryGetFeature<IActivatableLifetime>() is { } links)
		{
			links.Activated += (_, e) =>
			{
				if (e is ProtocolActivatedEventArgs { Uri: { } uri } && uri.Scheme == Services.HomeWidget.LINK_SCHEME)
					Services.HomeWidget.Platform?.OpenLink(uri);
				// A PDF, EPUB or text file shared with the app, as from Safari or Files: to be voiced.
				else if (e is ProtocolActivatedEventArgs { Uri: { IsFile: true } file })
					SharedDocuments.Receive(file.LocalPath);
				else if (e is FileActivatedEventArgs { Files: { Count: > 0 } files } && files[0].TryGetLocalPath() is { } path)
					SharedDocuments.Receive(path);
			};
		}

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
			var vm = mainViewModel;
			_ = StartAsync(vm);

			// Coming back from the background is when another device is most likely to have moved on.
			if (this.TryGetFeature<IActivatableLifetime>() is { } activation)
			{
				activation.Activated += (_, e) =>
				{
					if (e.Kind == ActivationKind.Background)
						vm.OnAppResumed();
				};
				activation.Deactivated += (_, e) =>
				{
					if (e.Kind == ActivationKind.Background)
						vm.OnAppBackgrounded();
				};
			}
		}
		return new MainView { DataContext = mainViewModel };
	}

	private static async System.Threading.Tasks.Task StartAsync(MainViewModel vm)
	{
		await vm.InitializeAsync();
#if DEBUG
		// Lets emulator and simulator runs be driven without taps, e.g. SIMCTL_CHILD_LIBATION_TEST_ACTION=play:B0FAKE0001:3
		if (Environment.GetEnvironmentVariable("LIBATION_TEST_ACTION") is { Length: > 0 } action)
			await vm.RunTestActionAsync(action);
#endif
	}
}
