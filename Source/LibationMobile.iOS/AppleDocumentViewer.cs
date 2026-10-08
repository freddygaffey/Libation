using Foundation;
using LibationMobile.Services;
using QuickLook;
using System.Linq;
using UIKit;

namespace LibationMobile.iOS;

/// <summary>
/// iOS's Quick Look viewer, over the app: pages, zoom, search, and the share button for printing or saving
/// elsewhere. Closing it returns to where the listener was.
/// </summary>
public sealed class AppleDocumentViewer : IDocumentViewer
{
	public void Show(string path, string title)
	{
		UIApplication.SharedApplication.InvokeOnMainThread(() =>
		{
			var viewer = new QLPreviewController { DataSource = new Source(path, title) };
			Top()?.PresentViewController(viewer, true, null);
		});
	}

	/// <summary>The view controller on screen, to present over.</summary>
	private static UIViewController? Top()
	{
		var root = UIApplication.SharedApplication.ConnectedScenes.ToArray()
			.OfType<UIWindowScene>()
			.SelectMany(s => s.Windows)
			.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
		while (root?.PresentedViewController is { } presented)
			root = presented;
		return root;
	}

	private sealed class Source(string path, string title) : QLPreviewControllerDataSource
	{
		public override nint PreviewItemCount(QLPreviewController controller) => 1;
		public override IQLPreviewItem GetPreviewItem(QLPreviewController controller, nint index) => new Item(path, title);
	}

	private sealed class Item(string path, string title) : QLPreviewItem
	{
		public override NSUrl PreviewItemUrl => NSUrl.FromFilename(path);
		public override string PreviewItemTitle => title;
	}
}
