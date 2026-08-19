using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;
using MarkdownViewer;

namespace MarkdownViewer.Desktop;

public partial class MainWindow : Window
{
    private readonly string? _filePath;
    private WebApplication? _app;

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var (rootPath, openRelativePath) = ResolveWorkspace(_filePath);

            // Bind to an OS-assigned free port on loopback only -- this window is the only client.
            _app = AppHost.Build(
                args: Array.Empty<string>(),
                rootPathOverride: rootPath,
                urls: "http://127.0.0.1:0",
                contentRootPath: AppContext.BaseDirectory);

            await _app.StartAsync();

            var baseAddress = _app.Urls.First();
            var url = openRelativePath is null
                ? baseAddress
                : $"{baseAddress}/#open={Uri.EscapeDataString(openRelativePath)}";

            // Keep the WebView2 user data folder somewhere writable regardless of where the
            // exe is installed (e.g. Program Files, which is read-only for normal users).
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkdownViewer", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.Source = new Uri(url);
            WebView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;

            if (openRelativePath != null)
                Title = $"{Path.GetFileName(openRelativePath)} — Markdown Viewer";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError(
                "The WebView2 Runtime isn't installed.\n\n" +
                "Markdown Viewer needs it to display content. Install it from " +
                "https://developer.microsoft.com/microsoft-edge/webview2/ (the \"Evergreen Bootstrapper\") " +
                "and reopen this app.");
        }
        catch (Exception ex)
        {
            ShowStartupError($"Markdown Viewer couldn't start:\n\n{ex.Message}");
        }
    }

    private void ShowStartupError(string message)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        if (LoadingPanel.Children.Count > 0 && LoadingPanel.Children[0] is System.Windows.Controls.TextBlock tb)
        {
            tb.Text = message;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.MaxWidth = 480;
            tb.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    /// <summary>
    /// Decides the workspace root and, if a specific file was opened, its path relative to
    /// that root (so the frontend can deep-link straight to it via #open=...).
    /// </summary>
    private static (string RootPath, string? OpenRelativePath) ResolveWorkspace(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            var fullPath = Path.GetFullPath(filePath);
            var folder = Path.GetDirectoryName(fullPath)!;
            return (folder, Path.GetFileName(fullPath));
        }

        // No file was passed in (e.g. launched from a shortcut/Start menu) -- fall back to a
        // per-user notes folder so the app is still immediately useful.
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var notesFolder = Path.Combine(documents, "Markdown Viewer Notes");
        Directory.CreateDirectory(notesFolder);

        if (!Directory.EnumerateFileSystemEntries(notesFolder).Any())
        {
            File.WriteAllText(Path.Combine(notesFolder, "Welcome.md"),
                "# Welcome to Markdown Viewer\n\n" +
                "This folder (`Documents\\Markdown Viewer Notes`) is where this app looks for " +
                "files when it isn't opened directly on one.\n\n" +
                "Right-click any `.md` file in Windows Explorer and choose **Open with → " +
                "Markdown Viewer** to open it here instead — its folder becomes the workspace, " +
                "so you'll see its sibling files in the sidebar too.\n");
        }

        return (notesFolder, null);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_app is not null)
        {
            try { await _app.StopAsync(TimeSpan.FromSeconds(2)); }
            catch { /* best-effort shutdown */ }
        }
    }
}
