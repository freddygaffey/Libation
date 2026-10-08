namespace LibationMobile.Services;

/// <summary>Shows a document, such as a book's PDF, in the platform's own viewer inside the app.</summary>
public interface IDocumentViewer
{
	void Show(string path, string title);
}

public static class DocumentViewer
{
	/// <summary>Set by the platform head. Null where there is none; the PDF's link is then opened instead.</summary>
	public static IDocumentViewer? Platform { get; set; }
}
