# Getting Started

This guide covers the basics of running and configuring Markdown Viewer.

## Running the app

```bash
cd MarkdownViewer
dotnet run
```

Then open **http://localhost:5080** (or whatever port the console prints).

## Pointing it at your own notes

By default the app serves the bundled `SampleDocs/` folder. To point it at your own
Markdown folder instead, edit `appsettings.json`:

```json
{
  "Workspace": {
    "RootPath": "C:/Users/you/Notes"
  }
}
```

Relative paths are resolved against the project's content root; absolute paths work too.

## Project layout

| Path | Purpose |
|---|---|
| `Program.cs` | API endpoints (tree, file, render, search) |
| `Services/WorkspaceService.cs` | Safe filesystem access, path validation |
| `Services/MarkdownRenderer.cs` | Markdig pipeline configuration |
| `wwwroot/` | The static frontend (HTML/CSS/JS, no build step) |

## Keyboard shortcuts

- [x] `Ctrl/Cmd + K` — jump to search
- [x] `Ctrl/Cmd + E` — toggle edit mode
- [x] `Ctrl/Cmd + S` — save (in edit mode)
- [ ] `Ctrl/Cmd + \` — toggle the table of contents *(coming soon)*

Next: see the [Formatting Showcase](../features/formatting-showcase.md) for everything the renderer supports.
