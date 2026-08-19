# Markdown Viewer — Windows Desktop App

A native Windows shell around the same Markdown Viewer web app: a WPF window with an embedded
[WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) control, hosting the
ASP.NET Core backend (`AppHost.Build(...)` from the `MarkdownViewer` project) in-process on a
random localhost port. There's no separate server to start — running the .exe starts everything.

## Requirements (on the machine that *builds* it)

- **Windows**, with the **.NET desktop development** workload for the .NET 8 SDK (WPF isn't
  available on Linux/macOS, so this project can only be built and run on Windows — the rest of
  the solution, `MarkdownViewer`, is cross-platform).
- The [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — this comes
  preinstalled on Windows 11 and current Windows 10 machines. If it's missing, the app shows an
  in-window message with a download link instead of crashing.

## Building & running

From the solution root:

```powershell
dotnet build MarkdownViewer.Desktop
dotnet run --project MarkdownViewer.Desktop
```

Or open `MarkdownViewer.sln` in Visual Studio, set `MarkdownViewer.Desktop` as the startup
project, and press F5.

## Opening a file

Right-click any `.md` file in Windows Explorer → **Open with** → **Choose another app** → browse
to `MarkdownViewer.Desktop.exe` (in `bin\Debug\net8.0-windows\` after building, or wherever you
publish it) → check "Always use this app to open .md files" if you want it to stick. Windows
passes the file's path as a command-line argument, which the app uses to:

1. Set the workspace root to that file's **folder** (so you'll see its sibling files in the
   sidebar too, not just the one file).
2. Open that file immediately via the same `#open=<path>` deep link the web app supports.

Launching the .exe directly with no file (e.g. from the Start menu) opens a default
`Documents\Markdown Viewer Notes` folder instead, seeded with a short welcome note on first run.

## How it's wired up

- `MarkdownViewer.Desktop.csproj` references the `MarkdownViewer` project directly
  (`ProjectReference`) and links its `wwwroot` folder in as content, so the same frontend
  (including the vendored highlight.js/KaTeX/Mermaid/CodeMirror and fonts — no internet needed)
  ships with the .exe.
- `MainWindow.xaml.cs` calls `AppHost.Build(...)` with `urls: "http://127.0.0.1:0"` to bind an
  OS-assigned free port, starts it, then points the `WebView2` control at
  `http://127.0.0.1:<port>/#open=<file>`.
- Closing the window stops the in-process web host.

## Publishing a standalone .exe

```powershell
dotnet publish MarkdownViewer.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

This produces a single `.exe` under
`MarkdownViewer.Desktop\bin\Release\net8.0-windows\win-x64\publish\` that doesn't require the
.NET runtime to be separately installed (the WebView2 Runtime is still required, as noted above).
