using System.Windows;

namespace MarkdownViewer.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // When Windows launches this app via "Open with" (or it's set as the default handler
        // for .md files), it passes the full path of the double-clicked file as the first
        // argument. If launched with no argument (e.g. from a shortcut), MainWindow falls back
        // to a default notes folder.
        var filePath = e.Args.Length > 0 ? e.Args[0] : null;

        var window = new MainWindow(filePath);
        window.Show();
    }
}
