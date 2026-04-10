using System.Text;
using Rr2Annotate.Models;

namespace Rr2Annotate.Services;

public class MarkdownBuilder
{
    private readonly AnnotationExport _export;
    private readonly Dictionary<(int page, int annotIdx), string>? _images;
    private readonly string? _imageRelDir;

    public MarkdownBuilder(
        AnnotationExport export,
        Dictionary<(int page, int annotIdx), string>? images = null,
        string? imageRelDir = null)
    {
        _export = export;
        _images = images;
        _imageRelDir = imageRelDir;
    }

    public string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Annotations: {_export.Source}");
        sb.AppendLine();

        // Build heading hierarchy lookup
        var headingOrder = new List<(string key, string title, int depth)>();
        BuildHeadingIndex(_export.Outline, 0, headingOrder);

        // Collect all annotations keyed by heading
        var grouped = new Dictionary<string, List<(int page, int annotIdx, Annotation annotation)>>();

        foreach (var page in _export.Pages)
        {
            for (int i = 0; i < page.Annotations.Count; i++)
            {
                var annot = page.Annotations[i];
                var headingKey = annot.NearestHeading != null
                    ? HeadingKey(annot.NearestHeading.Title, annot.NearestHeading.Page)
                    : "__no_heading__";

                if (!grouped.ContainsKey(headingKey))
                    grouped[headingKey] = [];

                grouped[headingKey].Add((page.Page, i, annot));
            }
        }

        // Sort each group by reading order
        foreach (var annotations in grouped.Values)
        {
            annotations.Sort((a, b) =>
            {
                var pageCmp = a.page.CompareTo(b.page);
                return pageCmp != 0 ? pageCmp : a.annotation.SortY.CompareTo(b.annotation.SortY);
            });
        }

        // Emit summary header
        EmitSummary(sb, headingOrder, grouped);

        // Emit annotations per heading in outline order
        foreach (var (key, title, depth) in headingOrder)
        {
            if (!grouped.TryGetValue(key, out var annotations))
                continue;

            var level = Math.Min(depth + 2, 4);
            sb.AppendLine($"{new string('#', level)} {title}");
            sb.AppendLine();

            EmitAnnotationGroup(sb, annotations);
        }

        // Annotations without a matching heading
        if (grouped.TryGetValue("__no_heading__", out var ungrouped))
        {
            sb.AppendLine("## Other Annotations");
            sb.AppendLine();
            EmitAnnotationGroup(sb, ungrouped);
        }

        return sb.ToString();
    }

    private void EmitSummary(
        StringBuilder sb,
        List<(string key, string title, int depth)> headingOrder,
        Dictionary<string, List<(int page, int annotIdx, Annotation annotation)>> grouped)
    {
        sb.AppendLine("## Summary");
        sb.AppendLine();

        var totalAnnotations = grouped.Values.Sum(g => g.Count);
        var totalPages = grouped.Values.SelectMany(g => g).Select(a => a.page).Distinct().Count();
        sb.AppendLine($"**{totalAnnotations} annotations** across **{totalPages} pages**");
        sb.AppendLine();

        sb.AppendLine("| Section | Highlights | Notes | Rectangles | Freehand |");
        sb.AppendLine("|---------|-----------|-------|------------|----------|");

        foreach (var (key, title, _) in headingOrder)
        {
            if (!grouped.TryGetValue(key, out var annotations))
                continue;

            var highlights = annotations.Count(a => a.annotation is HighlightAnnotation);
            var notes = annotations.Count(a => a.annotation is TextNoteAnnotation);
            var rects = annotations.Count(a => a.annotation is RectAnnotation);
            var freehand = annotations.Count(a => a.annotation is FreehandAnnotation);

            sb.AppendLine($"| {title} | {highlights} | {notes} | {rects} | {freehand} |");
        }

        if (grouped.ContainsKey("__no_heading__"))
        {
            var ug = grouped["__no_heading__"];
            sb.AppendLine($"| *(Other)* | {ug.Count(a => a.annotation is HighlightAnnotation)} | {ug.Count(a => a.annotation is TextNoteAnnotation)} | {ug.Count(a => a.annotation is RectAnnotation)} | {ug.Count(a => a.annotation is FreehandAnnotation)} |");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Emits a group of annotations, deduplicating block text when consecutive
    /// annotations share the same overlapping blocks, and deduplicating images
    /// when grouped freehand annotations share the same screenshot.
    /// </summary>
    private void EmitAnnotationGroup(
        StringBuilder sb,
        List<(int page, int annotIdx, Annotation annotation)> annotations)
    {
        string? lastBlockTextKey = null;
        var emittedImages = new HashSet<string>();

        foreach (var (page, annotIdx, annotation) in annotations)
        {
            var blockText = GetBlockText(annotation.OverlappingBlocks);
            var blockTextKey = string.IsNullOrWhiteSpace(blockText) ? null : blockText;

            bool suppressContext = blockTextKey != null && blockTextKey == lastBlockTextKey;

            // Check if this annotation's image has already been emitted (grouped freehand)
            bool suppressImage = false;
            if (TryGetImagePath(page, annotIdx, out var imgPath) && !emittedImages.Add(imgPath))
                suppressImage = true;

            EmitAnnotation(sb, page, annotIdx, annotation, suppressContext, suppressImage);
            lastBlockTextKey = blockTextKey;
        }
    }

    private void EmitAnnotation(StringBuilder sb, int page, int annotIdx, Annotation annotation,
        bool suppressContext, bool suppressImage = false)
    {
        switch (annotation)
        {
            case HighlightAnnotation highlight:
                EmitHighlight(sb, page, highlight, suppressContext);
                break;
            case TextNoteAnnotation note:
                EmitTextNote(sb, page, note, suppressContext);
                break;
            case RectAnnotation rect:
                EmitRect(sb, page, annotIdx, rect, suppressContext);
                break;
            case FreehandAnnotation freehand:
                EmitFreehand(sb, page, annotIdx, freehand, suppressContext, suppressImage);
                break;
        }
    }

    private void EmitHighlight(StringBuilder sb, int page, HighlightAnnotation highlight, bool suppressContext)
    {
        var blockText = suppressContext ? null : GetBlockText(highlight.OverlappingBlocks);
        var highlightedText = CleanText(highlight.Text ?? "");

        if (!string.IsNullOrWhiteSpace(blockText) && !string.IsNullOrWhiteSpace(highlightedText))
        {
            var bolded = BoldHighlightInContext(blockText, highlightedText);
            sb.AppendLine($"> {bolded}");
        }
        else if (!string.IsNullOrWhiteSpace(highlightedText))
        {
            sb.AppendLine($"> **{highlightedText}**");
        }
        else if (!string.IsNullOrWhiteSpace(blockText))
        {
            sb.AppendLine($"> {blockText}");
        }

        sb.AppendLine($">");
        sb.AppendLine($"> *(p. {page + 1}, highlight)*");
        sb.AppendLine();
    }

    private void EmitTextNote(StringBuilder sb, int page, TextNoteAnnotation note, bool suppressContext)
    {
        if (!suppressContext)
        {
            var blockText = GetBlockText(note.OverlappingBlocks);
            if (!string.IsNullOrWhiteSpace(blockText))
            {
                sb.AppendLine($"> {blockText}");
                sb.AppendLine(">");
            }
        }

        sb.AppendLine($"> **Note:** {note.NoteText}");
        sb.AppendLine($">");
        sb.AppendLine($"> *(p. {page + 1}, note)*");
        sb.AppendLine();
    }

    private void EmitRect(StringBuilder sb, int page, int annotIdx, RectAnnotation rect, bool suppressContext)
    {
        var hasImage = TryGetImagePath(page, annotIdx, out var imagePath);

        if (hasImage)
        {
            sb.AppendLine($"![Rectangle annotation, p. {page + 1}]({imagePath})");
            sb.AppendLine();
        }

        if (!suppressContext)
        {
            var blockText = GetBlockText(rect.OverlappingBlocks);
            var rectText = CleanText(rect.Text ?? "");

            if (!string.IsNullOrWhiteSpace(blockText))
            {
                sb.AppendLine($"> {blockText}");
            }
            else if (!string.IsNullOrWhiteSpace(rectText))
            {
                sb.AppendLine($"> {rectText}");
            }
        }

        sb.AppendLine($">");
        sb.AppendLine($"> *(p. {page + 1}, rectangle)*");
        sb.AppendLine();
    }

    private void EmitFreehand(StringBuilder sb, int page, int annotIdx, FreehandAnnotation freehand,
        bool suppressContext, bool suppressImage = false)
    {
        var hasImage = TryGetImagePath(page, annotIdx, out var imagePath);

        if (hasImage && !suppressImage)
        {
            sb.AppendLine($"![Freehand annotation, p. {page + 1}]({imagePath})");
            sb.AppendLine();
        }

        if (!suppressContext)
        {
            var blockText = GetBlockText(freehand.OverlappingBlocks);
            if (!string.IsNullOrWhiteSpace(blockText))
            {
                sb.AppendLine($"> {blockText}");
                sb.AppendLine($">");
            }
        }

        if (!hasImage)
        {
            sb.AppendLine($"> *[Freehand drawing — use `--images` to include a screenshot]*");
            sb.AppendLine($">");
        }

        // Suppress the per-stroke label for grouped freehand annotations
        if (!suppressImage)
        {
            sb.AppendLine($"> *(p. {page + 1}, freehand)*");
            sb.AppendLine();
        }
    }

    private bool TryGetImagePath(int page, int annotIdx, out string relativePath)
    {
        relativePath = "";
        if (_images == null || _imageRelDir == null)
            return false;

        if (!_images.TryGetValue((page, annotIdx), out var absPath))
            return false;

        relativePath = Path.Combine(_imageRelDir, Path.GetFileName(absPath));
        return true;
    }

    private static string GetBlockText(List<LayoutBlock> blocks)
    {
        if (blocks.Count == 0) return "";

        var ordered = blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .OrderBy(b => b.ReadingOrder ?? 0);

        var combined = string.Join("\n\n", ordered.Select(b => CleanText(b.Text!)));
        return combined;
    }

    /// <summary>
    /// Strips PDF artifacts and collapses whitespace in a single pass.
    /// Returns the cleaned text and a map from normalised positions back to
    /// original string indices.
    /// </summary>
    private static (string text, int[] map) NormalizeWithMap(string original)
    {
        var sb = new StringBuilder(original.Length);
        var map = new int[original.Length + 1];
        bool lastWasSpace = true; // suppress leading whitespace

        for (int i = 0; i < original.Length; i++)
        {
            char c = original[i];

            // Skip PDF artifacts and control characters
            if (c == '\u00AD' || c == '\u0002' || c == '\u0003') continue;
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    map[sb.Length] = i;
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                map[sb.Length] = i;
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        // Trim trailing whitespace
        int len = sb.Length;
        while (len > 0 && sb[len - 1] == ' ') len--;
        map[len] = original.Length;

        return (sb.ToString(0, len), map);
    }

    /// <summary>
    /// Cleans extracted PDF text: removes control characters, soft hyphens,
    /// normalises whitespace, and trims.
    /// </summary>
    internal static string CleanText(string text)
    {
        var (result, _) = NormalizeWithMap(text);
        return result;
    }

    private static string BoldHighlightInContext(string blockText, string highlightText)
    {
        // Try exact match first
        var idx = blockText.IndexOf(highlightText, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return blockText[..idx]
                + "**" + blockText.Substring(idx, highlightText.Length) + "**"
                + blockText[(idx + highlightText.Length)..];
        }

        // Fuzzy match: normalize whitespace and retry
        var (normBlock, blockMap) = NormalizeWithMap(blockText);
        var normHighlight = CleanText(highlightText);

        var normIdx = normBlock.IndexOf(normHighlight, StringComparison.OrdinalIgnoreCase);
        if (normIdx >= 0)
        {
            var origStart = blockMap[normIdx];
            var origEnd = blockMap[normIdx + normHighlight.Length];

            return blockText[..origStart]
                + "**" + blockText[origStart..origEnd] + "**"
                + blockText[origEnd..];
        }

        // Last resort
        return $"{blockText}\n>\n> **Highlighted:** {highlightText}";
    }

    private static string HeadingKey(string title, int page) => $"{title}||{page}";

    private static void BuildHeadingIndex(
        List<OutlineEntry> entries, int depth,
        List<(string key, string title, int depth)> order)
    {
        foreach (var entry in entries)
        {
            var key = HeadingKey(entry.Title, entry.Page);
            order.Add((key, entry.Title, depth));
            BuildHeadingIndex(entry.Children, depth + 1, order);
        }
    }
}
