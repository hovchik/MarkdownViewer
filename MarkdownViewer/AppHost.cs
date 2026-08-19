using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace MarkdownViewer;

/// <summary>
/// Builds the ASP.NET Core app (API + static frontend). Factored out of Program.cs so it can be
/// started two ways: as a standalone `dotnet run` web server, or in-process by the Windows
/// desktop shell (MarkdownViewer.Desktop), which embeds it in a WebView2 window pointed at a
/// single opened file.
/// </summary>
public static class AppHost
{
    /// <summary>
    /// Builds (but does not start) the app.
    /// </summary>
    /// <param name="args">Command-line args, forwarded to <see cref="WebApplication.CreateBuilder(WebApplicationOptions)"/>.</param>
    /// <param name="rootPathOverride">
    /// When set, overrides the "Workspace:RootPath" config value — used by the desktop host to
    /// point the workspace at the folder containing whatever file the user double-clicked.
    /// </param>
    /// <param name="urls">
    /// When set, overrides the URLs Kestrel binds to (e.g. "http://127.0.0.1:0" to get an
    /// OS-assigned free port for the embedded desktop scenario).
    /// </param>
    /// <param name="contentRootPath">
    /// When set, overrides the content root (and therefore where "wwwroot" is resolved from).
    /// The desktop host passes its own base directory here since it isn't launched via `dotnet run`.
    /// </param>
    public static WebApplication Build(string[] args, string? rootPathOverride = null, string? urls = null, string? contentRootPath = null)
    {
        var options = new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRootPath,
        };
        var builder = WebApplication.CreateBuilder(options);

        if (rootPathOverride != null)
            builder.Configuration["Workspace:RootPath"] = rootPathOverride;
        if (urls != null)
            builder.WebHost.UseUrls(urls);

        builder.Services.AddSingleton<MarkdownRenderer>();
        builder.Services.AddSingleton<WorkspaceService>();
        builder.Services.AddCors(o => o.AddPolicy("default", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors("default");

        MapApi(app);

        return app;
    }

    private static void MapApi(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // --- Workspace tree -------------------------------------------------------
        api.MapGet("/tree", (WorkspaceService ws) => Results.Ok(ws.BuildTree()));

        // --- Open a file: raw content + rendered HTML + TOC -----------------------
        api.MapGet("/file", async (string path, WorkspaceService ws, MarkdownRenderer md) =>
        {
            try
            {
                var abs = ws.ResolveSafePath(path);
                if (!WorkspaceService.IsMarkdownFile(abs))
                    return Results.BadRequest(new { error = "Only .md/.markdown/.mdx files can be opened." });

                var raw = await ws.ReadFileAsync(abs);
                var (html, headings, wordCount) = md.Render(raw);
                var info = new FileInfo(abs);

                return Results.Ok(new FileContent(
                    Path: ws.ToRelative(abs),
                    Name: Path.GetFileName(abs),
                    RawContent: raw,
                    Html: html,
                    Headings: headings,
                    ModifiedUtc: info.LastWriteTimeUtc,
                    SizeBytes: info.Length,
                    WordCount: wordCount));
            }
            catch (InvalidWorkspacePathException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (FileNotFoundException) { return Results.NotFound(new { error = "File not found." }); }
        });

        // --- Save edits to a file ---------------------------------------------------
        api.MapPut("/file", async (string path, SaveRequest body, WorkspaceService ws, MarkdownRenderer md) =>
        {
            try
            {
                var abs = ws.ResolveSafePath(path);
                if (!WorkspaceService.IsMarkdownFile(abs))
                    return Results.BadRequest(new { error = "Only .md/.markdown/.mdx files can be saved." });

                await ws.WriteFileAsync(abs, body.Content ?? "");
                var (html, headings, wordCount) = md.Render(body.Content ?? "");
                return Results.Ok(new RenderResponse(html, headings, wordCount));
            }
            catch (InvalidWorkspacePathException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // --- Render arbitrary markdown (used for live preview while typing) --------
        api.MapPost("/render", (RenderRequest body, MarkdownRenderer md) =>
        {
            var (html, headings, wordCount) = md.Render(body.Content ?? "");
            return Results.Ok(new RenderResponse(html, headings, wordCount));
        });

        // --- Create a new file or folder -------------------------------------------
        api.MapPost("/entries", (CreateRequest body, WorkspaceService ws) =>
        {
            try
            {
                var abs = ws.ResolveSafePath(body.Path);
                if (body.Type == "file" && !WorkspaceService.IsMarkdownFile(abs))
                    return Results.BadRequest(new { error = "New files must end in .md, .markdown, or .mdx." });

                ws.CreateEntry(abs, body.Type);
                return Results.Ok(new { path = ws.ToRelative(abs) });
            }
            catch (InvalidWorkspacePathException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (IOException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        // --- Delete a file or folder -------------------------------------------------
        api.MapDelete("/entries", (string path, WorkspaceService ws) =>
        {
            try
            {
                var abs = ws.ResolveSafePath(path);
                if (abs == ws.RootPath.TrimEnd(Path.DirectorySeparatorChar))
                    return Results.BadRequest(new { error = "Cannot delete the workspace root." });

                ws.Delete(abs);
                return Results.Ok(new { deleted = path });
            }
            catch (InvalidWorkspacePathException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (FileNotFoundException) { return Results.NotFound(new { error = "Not found." }); }
        });

        // --- Full-text search across the workspace ----------------------------------
        api.MapGet("/search", (string q, WorkspaceService ws) =>
        {
            var hits = new List<SearchHit>();
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(hits);

            foreach (var (absPath, relPath) in ws.AllMarkdownFiles())
            {
                var name = Path.GetFileName(absPath);
                var matchedInTitle = name.Contains(q, StringComparison.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(absPath);

                var lineHit = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        var snippet = lines[i].Trim();
                        if (snippet.Length > 160) snippet = snippet[..160] + "…";
                        hits.Add(new SearchHit(relPath, name, snippet, i + 1, matchedInTitle));
                        lineHit = true;
                        if (hits.Count(h => h.Path == relPath) >= 3) break; // cap snippets per file
                    }
                }

                if (matchedInTitle && !lineHit)
                    hits.Add(new SearchHit(relPath, name, "", 0, true));
            }

            return Results.Ok(hits.Take(50));
        });
    }
}
