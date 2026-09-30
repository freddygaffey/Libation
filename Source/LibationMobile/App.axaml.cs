using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LibationMobile.Views;

namespace LibationMobile;

public partial class App : Application
{
	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
			activityLifetime.MainViewFactory = () => new SpikeView();
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
			singleView.MainView = new SpikeView();

		base.OnFrameworkInitializationCompleted();
	}
}
