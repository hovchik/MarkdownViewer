# Markdown Viewer

A modern Markdown workspace, in two flavors that share one backend:

- **`MarkdownViewer`** — the ASP.NET Core web app (minimal APIs + Markdig) and the frontend
  (file tree, search, Mermaid/KaTeX/syntax-highlighted rendering, split-pane editor, light/dark
  themes). Runs anywhere the .NET 8 SDK does — Windows, macOS, Linux. See
  [`MarkdownViewer/README.md`](MarkdownViewer/README.md).
- **`MarkdownViewer.Desktop`** — a native Windows app (WPF + embedded WebView2) that hosts the
  exact same backend in-process and lets you open `.md` files directly from Windows Explorer's
  "Open with" menu. Windows-only to build and run. See
  [`MarkdownViewer.Desktop/README.md`](MarkdownViewer.Desktop/README.md).

## Quick start

**Web app (any OS):**

```bash
cd MarkdownViewer
dotnet run
```

**Windows desktop app:**

```powershell
dotnet run --project MarkdownViewer.Desktop
```

Or open `MarkdownViewer.sln` in Visual Studio and run either project.

## How the two fit together

```
MarkdownViewer.sln
├── MarkdownViewer/              ASP.NET Core app: Program.cs + AppHost.cs (backend), wwwroot/ (frontend)
│   ├── AppHost.cs               Builds the app — used standalone AND embedded by the desktop app
│   ├── Services/                WorkspaceService (safe file access), MarkdownRenderer (Markdig)
│   ├── wwwroot/                 Static frontend, vendored 3rd-party libraries, self-hosted fonts
│   └── SampleDocs/               Bundled example workspace
└── MarkdownViewer.Desktop/      WPF + WebView2 shell (Windows only)
    └── MainWindow.xaml.cs       Starts AppHost.Build(...) in-process, points WebView2 at it
```

`AppHost.Build(args, rootPathOverride, urls, contentRootPath)` is the one piece both entry points
call. The standalone web app just calls it with defaults; the desktop app calls it with a random
free port and the folder of whatever file Windows told it to open, so the two never drift apart —
there's exactly one rendering pipeline and one API.

Everything the frontend needs (Markdig, highlight.js, KaTeX, Mermaid, CodeMirror, and the
IBM Plex Sans / JetBrains Mono fonts) is vendored locally under `MarkdownViewer/wwwroot/vendor`
and `MarkdownViewer/LocalPackages`, so both apps build and run without internet access once
you've restored once (Markdig is fully offline; only the desktop app's WebView2 SDK package needs
nuget.org, since Windows-only packages couldn't be pre-vendored from this environment).
