using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    /// <summary>Custom command for "Open Folder…", since WPF has no built-in ApplicationCommand for it.</summary>
    public static readonly RoutedCommand OpenFolderCommand = new("OpenFolder", typeof(MainWindow));

    private readonly string? _filePath;
    private WebApplication? _app;
    private CoreWebView2Environment? _webViewEnvironment;
    private bool _webViewReady;
    private string? _currentRootPath;

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
    /// Resolves the given file path to a workspace (its containing folder) and opens it.
    /// Used for the initial launch and whenever the user opens a different file via the
    /// Open dialog, Recent Files, or drag-and-drop.
    /// </summary>
    private Task OpenAsync(string? filePath)
    {
        var (rootPath, openRelativePath) = ResolveWorkspace(filePath);
        return OpenWorkspaceAsync(rootPath, openRelativePath);
    }

    /// <summary>Opens a folder directly as the workspace root, with no specific file selected.</summary>
    private Task OpenFolderAsync(string folderPath) => OpenWorkspaceAsync(folderPath, null);

    /// <summary>
    /// (Re)starts the in-process backend rooted at <paramref name="rootPath"/> and navigates the
    /// WebView there, optionally deep-linking straight to <paramref name="openRelativePath"/>.
    /// </summary>
    private async Task OpenWorkspaceAsync(string rootPath, string? openRelativePath)
    {
        try
        {
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

                // WebView2 hosts its own native child HWND with its own OLE drop-target
                // registration, so OS file drops over its surface never reach WPF's Window.Drop
                // at all (setting AllowExternalDrop=false only suppresses WebView2's default
                // "navigate to file://" behavior -- it doesn't hand the event back to WPF).
                // Instead, let WebView2 keep handling the drop, but intercept the resulting
                // file:// navigation and redirect it through the same OpenAsync path used by
                // File > Open, so a dropped markdown file opens exactly like it, complete with
                // being added to the left sidebar's workspace tree.
                WebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

                // For some drop sources/positions WebView2 doesn't navigate the current
                // document at all -- it instead requests a brand new top-level WebView2 popup
                // window pointed at the dropped file's file:// URL (this is what produced the
                // separate mini-browser window with its own toolbar/tabs). Intercept that too,
                // and reroute it the same way instead of letting a second window appear.
                WebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                _webViewReady = true;
            }

            WebView.Source = new Uri(url);
            WebView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;

            _currentRootPath = rootPath;

            if (openRelativePath != null)
            {
                Title = $"{Path.GetFileName(openRelativePath)} — Markdown Viewer";
                RecentFilesStore.Add(Path.Combine(rootPath, openRelativePath));
            }
            else
            {
                Title = $"{Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar))} — Markdown Viewer";
            }
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

    private void OpenFolderCommand_Executed(object sender, ExecutedRoutedEventArgs e) => ShowOpenFolderDialog();

    /// <summary>
    /// When a file is dropped onto the WebView2 surface, WebView2's own drop handling tries to
    /// navigate to the dropped file's file:// URL (which would render it as raw text/binary
    /// instead of through the app). Cancel that navigation for markdown files and open them
    /// through the normal workspace-switching path instead -- identical to File > Open, so the
    /// file also gets added to the left sidebar tree.
    /// </summary>
    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !uri.IsFile)
            return;

        var localPath = uri.LocalPath;
        if (!File.Exists(localPath) || !WorkspaceService.IsMarkdownFile(localPath))
            return;

        e.Cancel = true;
        _ = OpenAsync(localPath);
    }

    /// <summary>
    /// Handles the case where WebView2 responds to a dropped file by requesting an entirely new
    /// top-level popup window (its own mini-browser chrome) navigated to the file's file:// URL,
    /// rather than navigating the existing document. Suppress that popup and open the file
    /// through the normal workspace-switching path in this window instead.
    /// </summary>
    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var localPath = uri.LocalPath;
            if (File.Exists(localPath) && WorkspaceService.IsMarkdownFile(localPath))
                _ = OpenAsync(localPath);
        }

        e.Handled = true;
    }

    private void RecentFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
            _ = OpenAsync(path);
    }

    private bool _webViewVisibleBeforeMenu;

    /// <summary>
    /// WebView2 renders in its own child HWND, which sits in a different "airspace" than WPF's
    /// own rendering surface. That breaks hit-testing/visuals for WPF popups (menu flyouts,
    /// submenus, tooltips) drawn on top of it -- clicks pass straight through to the page below.
    /// Hiding the WebView for the duration the menu is open is the standard workaround.
    /// </summary>
    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        // SubmenuOpened/Closed bubble up from nested submenus (e.g. Recent Files) too; only react
        // to the top-level File menu itself opening/closing.
        if (!ReferenceEquals(e.OriginalSource, sender)) return;

        _webViewVisibleBeforeMenu = WebView.Visibility == Visibility.Visible;
        WebView.Visibility = Visibility.Hidden;
    }

    private void FileMenu_SubmenuClosed(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;

        if (_webViewVisibleBeforeMenu)
            WebView.Visibility = Visibility.Visible;
    }

    private void RecentFilesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu) return;

        menu.Items.Clear();
        var recent = RecentFilesStore.Load();
        if (recent.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var item = new MenuItem { Header = path, Tag = path };
            item.Click += RecentFileMenuItem_Click;
            menu.Items.Add(item);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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
            CheckFileExists = true,
            InitialDirectory = _currentRootPath is not null && Directory.Exists(_currentRootPath)
                ? _currentRootPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _ = OpenAsync(dialog.FileName);
        }
    }

    private void ShowOpenFolderDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open Workspace Folder",
            InitialDirectory = _currentRootPath is not null && Directory.Exists(_currentRootPath)
                ? _currentRootPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _ = OpenFolderAsync(dialog.FolderName);
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
