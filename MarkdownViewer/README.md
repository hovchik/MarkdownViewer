# Markdown Viewer

A modern, self-contained Markdown workspace: an ASP.NET Core backend (minimal APIs + Markdig)
serving a fast vanilla-JS frontend — file tree, full-text search, live-rendered Mermaid diagrams,
KaTeX math, syntax-highlighted code, an auto-generated table of contents, light/dark themes, and
a split-pane editor with autosave-on-demand.

> Looking for the Windows desktop app (opens `.md` files directly from Explorer's "Open with")?
> See [`../MarkdownViewer.Desktop/README.md`](../MarkdownViewer.Desktop/README.md) — it embeds
> this same backend, via `AppHost.cs` below.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

No Node.js, no build step for the frontend — everything the browser needs (highlight.js, KaTeX,
Mermaid, CodeMirror, and the IBM Plex Sans / JetBrains Mono fonts) is vendored under
`wwwroot/vendor` and `wwwroot/css/fonts.css`, so the app also runs with **no internet access**
once it's built.

## Running it

```bash
cd MarkdownViewer
dotnet run
```

Then open the URL printed in the console (typically `http://localhost:5270`, or check the
`applicationUrl` in `Properties/launchSettings.json`). The app ships with a small sample
workspace under `SampleDocs/` so there's something to explore immediately — start with
`welcome.md`, then look at `features/formatting-showcase.md` for a full feature tour.

## Pointing it at your own notes

Edit `appsettings.json`:

```json
{
  "Workspace": {
    "RootPath": "SampleDocs"
  }
}
```

Set `RootPath` to any folder — relative paths resolve against the project's content root,
absolute paths (`C:/Users/you/Notes` or `/home/you/notes`) work too. Only `.md`, `.markdown`,
and `.mdx` files are shown and editable; every path from the client is re-validated against this
root before touching disk, so the app can't read or write outside the configured folder.

## Features

- **File tree** with nested folders, collapsible directories, and a live search box that matches
  file names and content (`Ctrl/Cmd+K`).
- **Rendering**: GitHub-flavored Markdown (tables, task lists, footnotes, strikethrough,
  definition lists, YAML front matter), syntax-highlighted code blocks, Mermaid diagrams,
  inline/display KaTeX math, and emoji shortcodes — all via a single Markdig pipeline in
  `Services/MarkdownRenderer.cs`.
- **Table of contents** generated from headings, sticky, with scroll-spy highlighting.
- **Editing**: a CodeMirror-powered split-pane editor with a debounced live preview
  (`Ctrl/Cmd+E` to toggle, `Ctrl/Cmd+S` to save).
- **Themes**: light/dark, respecting system preference on first load, persisted afterwards.
- **New files**: create a new markdown file from the sidebar and start writing immediately.

## Project layout

```
Program.cs                    Standalone entry point — just calls AppHost.Build(args).Run()
AppHost.cs                    Builds the app: pipeline, DI, and all API endpoints. Also called
                               in-process by MarkdownViewer.Desktop (the Windows app), which is
                               why this is factored out of Program.cs.
Services/WorkspaceService.cs  Safe filesystem access — every path is validated against the root
Services/MarkdownRenderer.cs  Markdig pipeline configuration + heading/TOC extraction
Models/Models.cs              DTOs shared between the API and the frontend
wwwroot/                      Static frontend — index.html, css/, js/app.js, vendor/ (3rd-party
                               libraries), fonts/
SampleDocs/                   Bundled example workspace
```

## API

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/tree` | File tree for the configured workspace |
| GET | `/api/file?path=` | Raw markdown + rendered HTML + headings for one file |
| PUT | `/api/file?path=` | Save edited content, returns the re-rendered HTML |
| POST | `/api/render` | Render arbitrary markdown text (used for live preview) |
| POST | `/api/entries` | Create a new file or folder |
| DELETE | `/api/entries?path=` | Delete a file or folder |
| GET | `/api/search?q=` | Search file names and content |

## A note on the vendored packages

`Markdig` is included as a small local NuGet feed (`LocalPackages/Markdig.0.37.0.nupkg`) in
addition to the normal `nuget.org` source in `NuGet.Config`, so the project builds even with no
network access. If you'd rather pull it from nuget.org as usual, just delete the `LocalPackages`
folder and the corresponding `<add>` line in `NuGet.Config` — `dotnet restore` will fetch it
normally.
