using Avalonia.Controls;
using LibationMobile.ViewModels;
using System;
using System.ComponentModel;
using System.Net;

namespace LibationMobile.Views;

/// <summary>Amazon's sign-in page. Finishes the <see cref="LoginRequest"/> when Amazon redirects to its landing URL.</summary>
public partial class BrowserView : UserControl
{
	private MainViewModel? viewModel;
	private NativeWebView? webView;

	public BrowserView()
	{
		InitializeComponent();
		DataContextChanged += (_, _) =>
		{
			if (viewModel is not null)
				viewModel.PropertyChanged -= ViewModel_PropertyChanged;
			viewModel = DataContext as MainViewModel;
			if (viewModel is not null)
				viewModel.PropertyChanged += ViewModel_PropertyChanged;
		};
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainViewModel.Login))
			Show(viewModel?.Login);
	}

	private void Show(LoginRequest? request)
	{
		host.Child = null;
		webView = null;
		if (request is null)
			return;

		var view = new NativeWebView { UserAgent = request.UserAgent };
		view.AdapterCreated += (_, _) =>
		{
			// Amazon's sign-in expects these cookies to be present before the first request.
			if (view.TryGetCookieManager() is { } cookies)
				foreach (Cookie cookie in request.Cookies ?? [])
					if (!string.IsNullOrEmpty(cookie.Value))
						cookies.AddOrUpdateCookie(cookie);
			view.Navigate(new Uri(request.Url));
		};
		view.NavigationStarted += (_, e) =>
		{
			if (e.Request?.AbsolutePath.StartsWith("/ap/maplanding", StringComparison.Ordinal) is true)
				request.Complete(e.Request.ToString());
		};
		webView = view;
		host.Child = view;
	}
}
