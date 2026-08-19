using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// Wraps Markdig with the pipeline used across the app (GFM tables, task lists, footnotes,
/// auto-identified headings, math, emoji, and fenced-block-to-div diagrams for Mermaid).
/// Also extracts a heading outline for the client-side table of contents and a rough word count.
/// </summary>
public class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()   // tables, footnotes, task lists, definition lists, abbreviations, etc.
            .UseDiagrams()             // ```mermaid fenced blocks -> <div class="mermaid">
            .UseMathematics()          // $inline$ and $$block$$ math -> spans/divs for KaTeX
            .UseEmojiAndSmiley()       // :smile: -> 😄
            .UseYamlFrontMatter()      // parse (and hide) YAML front matter blocks
            .UseAutoIdentifiers()      // stable heading ids for TOC + deep links
            .Build();
    }

    public (string Html, List<Heading> Headings, int WordCount) Render(string markdown)
    {
        markdown ??= "";
        var document = Markdown.Parse(markdown, _pipeline);

        var headings = new List<Heading>();
        foreach (var node in document.Descendants().OfType<HeadingBlock>())
        {
            var text = ExtractText(node);
            var id = node.GetAttributes().Id ?? Slugify(text);
            headings.Add(new Heading(id, text, node.Level));
        }

        var html = document.ToHtml(_pipeline);
        var wordCount = CountWords(markdown);

        return (html, headings, wordCount);
    }

    private static string ExtractText(HeadingBlock heading)
    {
        if (heading.Inline is null) return "";
        var sb = new System.Text.StringBuilder();
        AppendInlineText(heading.Inline, sb);
        return sb.ToString();
    }

    private static void AppendInlineText(ContainerInline container, System.Text.StringBuilder sb)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline nested:
                    AppendInlineText(nested, sb);
                    break;
            }
        }
    }

    private static string Slugify(string text)
    {
        var lower = text.ToLowerInvariant();
        var chars = lower.Select(c => char.IsLetterOrDigit(c) ? c : (c == ' ' ? '-' : '\0'))
                          .Where(c => c != '\0');
        return new string(chars.ToArray());
    }

    private static int CountWords(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return 0;
        return markdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
