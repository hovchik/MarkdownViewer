using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>Thrown when a requested path escapes the configured workspace root or is otherwise invalid.</summary>
public class InvalidWorkspacePathException(string message) : Exception(message);

/// <summary>
/// All filesystem access for the markdown workspace goes through here. Every public method
/// re-resolves and re-validates the incoming relative path against the configured root so a
/// caller can never read or write outside the workspace folder (no "..", no absolute paths,
/// no symlink escapes).
/// </summary>
public class WorkspaceService
{
    private readonly string _rootPath;
    private static readonly string[] AllowedExtensions = [".md", ".markdown", ".mdx"];

    public WorkspaceService(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Workspace:RootPath"] ?? "SampleDocs";
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    /// <summary>Resolves a client-supplied relative path to an absolute path, guaranteed to live under the root.</summary>
    public string ResolveSafePath(string? relativePath)
    {
        relativePath ??= "";
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');

        if (relativePath.Split('/').Any(segment => segment == ".."))
            throw new InvalidWorkspacePathException("Path traversal is not allowed.");

        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var normalizedRoot = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!combined.Equals(normalizedRoot, StringComparison.Ordinal) &&
            !combined.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidWorkspacePathException("Resolved path escapes the workspace root.");
        }

        return combined;
    }

    public static bool IsMarkdownFile(string path) =>
        AllowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public string ToRelative(string absolutePath)
    {
        var rel = Path.GetRelativePath(_rootPath, absolutePath).Replace('\\', '/');
        return rel == "." ? "" : rel;
    }

    public FileNode BuildTree()
    {
        var root = new FileNode { Name = Path.GetFileName(_rootPath.TrimEnd('/')), Path = "", Type = "dir", Children = [] };
        PopulateChildren(_rootPath, root);
        return root;
    }

    private void PopulateChildren(string absoluteDir, FileNode node)
    {
        var children = new List<FileNode>();

        foreach (var dir in Directory.EnumerateDirectories(absoluteDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            var child = new FileNode { Name = name, Path = ToRelative(dir), Type = "dir", Children = [] };
            PopulateChildren(dir, child);
            if (child.Children!.Count > 0) children.Add(child);
        }

        foreach (var file in Directory.EnumerateFiles(absoluteDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsMarkdownFile(file)) continue;
            var info = new FileInfo(file);
            children.Add(new FileNode
            {
                Name = Path.GetFileName(file),
                Path = ToRelative(file),
                Type = "file",
                SizeBytes = info.Length,
                ModifiedUtc = info.LastWriteTimeUtc
            });
        }

        node.Children = children;
    }

    public async Task<string> ReadFileAsync(string absolutePath)
    {
        if (!File.Exists(absolutePath)) throw new FileNotFoundException("File not found.", absolutePath);
        return await File.ReadAllTextAsync(absolutePath);
    }

    public async Task WriteFileAsync(string absolutePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllTextAsync(absolutePath, content);
    }

    public void CreateEntry(string absolutePath, string type)
    {
        if (type == "dir")
        {
            Directory.CreateDirectory(absolutePath);
        }
        else
        {
            if (File.Exists(absolutePath)) throw new IOException("A file with that name already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, $"# {Path.GetFileNameWithoutExtension(absolutePath)}\n\n");
        }
    }

    public void Delete(string absolutePath)
    {
        if (Directory.Exists(absolutePath)) Directory.Delete(absolutePath, recursive: true);
        else if (File.Exists(absolutePath)) File.Delete(absolutePath);
        else throw new FileNotFoundException("Not found.", absolutePath);
    }

    public IEnumerable<(string AbsolutePath, string RelativePath)> AllMarkdownFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*.*", SearchOption.AllDirectories))
        {
            if (!IsMarkdownFile(file)) continue;
            yield return (file, ToRelative(file));
        }
    }
}
