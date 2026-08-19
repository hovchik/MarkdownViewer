using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MarkdownViewer;
using MarkdownViewer.Services;

namespace MarkdownViewer.Desktop;

public partial class MainWindow : Window
{
    private readonly string? _filePath;
    private WebApplication? _app;
    private CoreWebView2Environment? _webViewEnvironment;
    private bool _webViewReady;

    public MainWindow(string? filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await OpenAsync(_filePath);
    }

    /// <summary>
    /// (Re)starts the in-process backend rooted at the file's folder (or the notes folder as a
    /// fallback) and navigates the WebView there. Used both for the initial launch and whenever
    /// the user opens a different file via the Open dialog or drag-and-drop.
    /// </summary>
    private async Task OpenAsync(string? filePath)
    {
        try
        {
            var (rootPath, openRelativePath) = ResolveWorkspace(filePath);

            WebView.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;

            if (_app is not null)
            {
                try { await _app.StopAsync(TimeSpan.FromSeconds(2)); }
                catch { /* best-effort shutdown of the previous instance */ }
            }

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

            if (!_webViewReady)
            {
                // Keep the WebView2 user data folder somewhere writable regardless of where the
                // exe is installed (e.g. Program Files, which is read-only for normal users).
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MarkdownViewer", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                _webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await WebView.EnsureCoreWebView2Async(_webViewEnvironment);
                WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
                _webViewReady = true;
            }

            WebView.Source = new Uri(url);
            WebView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;

            Title = openRelativePath != null
                ? $"{Path.GetFileName(openRelativePath)} — Markdown Viewer"
                : "Markdown Viewer";
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

    private void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e) => ShowOpenFileDialog();

    /// <summary>
    /// Handles messages posted from the web frontend (window.chrome.webview.postMessage), e.g.
    /// the in-page "Open" button, since the frontend itself has no filesystem access.
    /// </summary>
    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "open-file")
            {
                ShowOpenFileDialog();
            }
        }
        catch { /* ignore malformed messages */ }
    }

    private void ShowOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Markdown File",
            Filter = "Markdown files (*.md;*.markdown;*.mdx)|*.md;*.markdown;*.mdx|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            _ = OpenAsync(dialog.FileName);
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedMarkdownFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedMarkdownFile(e, out var path))
        {
            _ = OpenAsync(path);
        }
        e.Handled = true;
    }

    private static bool TryGetDroppedMarkdownFile(DragEventArgs e, out string? path)
    {
        path = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return false;

        var candidate = files.FirstOrDefault(f => File.Exists(f) && WorkspaceService.IsMarkdownFile(f));
        if (candidate is null)
            return false;

        path = candidate;
        return true;
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
