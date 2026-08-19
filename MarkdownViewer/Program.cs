using MarkdownViewer;

var app = AppHost.Build(args);
app.Run();

// Exposed so the app can be hosted in-process by other entry points (e.g. the WPF desktop
// shell in MarkdownViewer.Desktop), following the standard WebApplicationFactory-friendly pattern.
public partial class Program { }
