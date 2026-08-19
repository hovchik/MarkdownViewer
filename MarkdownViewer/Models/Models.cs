namespace MarkdownViewer.Models;

/// <summary>A single node (file or directory) in the workspace file tree.</summary>
public class FileNode
{
    public string Name { get; set; } = "";
    /// <summary>Path relative to the workspace root, using forward slashes.</summary>
    public string Path { get; set; } = "";
    public string Type { get; set; } = "file"; // "file" | "dir"
    public long? SizeBytes { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public List<FileNode>? Children { get; set; }
}

/// <summary>A heading extracted from rendered markdown, used to build the table of contents.</summary>
public record Heading(string Id, string Text, int Level);

/// <summary>Full payload returned when opening a file: raw source + rendered HTML + TOC.</summary>
public record FileContent(string Path, string Name, string RawContent, string Html, List<Heading> Headings, DateTime ModifiedUtc, long SizeBytes, int WordCount);

/// <summary>Request body for rendering arbitrary markdown text (used for live preview while editing).</summary>
public record RenderRequest(string Content);

public record RenderResponse(string Html, List<Heading> Headings, int WordCount);

/// <summary>Request body for saving edited content back to a file.</summary>
public record SaveRequest(string Content);

/// <summary>Request body for creating a new file or folder.</summary>
public record CreateRequest(string Path, string Type); // type: "file" | "dir"

public record SearchHit(string Path, string Name, string Snippet, int Line, bool MatchedInTitle);
