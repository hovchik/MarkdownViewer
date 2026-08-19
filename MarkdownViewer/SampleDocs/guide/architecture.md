# Architecture

A quick tour of how a request flows through the app.

```mermaid
flowchart LR
    A[Browser] -- GET /api/tree --> B[ASP.NET Core Minimal API]
    A -- GET /api/file?path= --> B
    B --> C[WorkspaceService]
    C --> D[(Filesystem)]
    B --> E[MarkdownRenderer]
    E -- Markdig --> F[HTML + headings]
    B -- JSON --> A
```

## Request sequence for opening a file

```mermaid
sequenceDiagram
    participant U as User
    participant FE as Frontend (JS)
    participant API as ASP.NET Core API
    participant WS as WorkspaceService
    participant MD as MarkdownRenderer

    U->>FE: click a file in the tree
    FE->>API: GET /api/file?path=notes/todo.md
    API->>WS: ResolveSafePath + ReadFileAsync
    WS-->>API: raw markdown
    API->>MD: Render(raw)
    MD-->>API: html, headings, wordCount
    API-->>FE: JSON payload
    FE-->>U: rendered page + table of contents
```

Every path coming from the client is re-validated against the configured workspace root in `WorkspaceService.ResolveSafePath`, so `..` segments and absolute paths are rejected before anything touches the disk.
