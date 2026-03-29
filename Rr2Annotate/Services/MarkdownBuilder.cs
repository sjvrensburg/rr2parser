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

        // Build heading hierarchy lookup: maps (title, page) to depth and outline entry
        var headingDepths = new Dictionary<string, int>();
        var headingOrder = new List<(string key, string title, int depth)>();
        BuildHeadingIndex(_export.Outline, 0, headingDepths, headingOrder);

        // Collect all annotations with their page and index, keyed by heading
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

        // Emit in outline order
        foreach (var (key, title, depth) in headingOrder)
        {
            if (!grouped.TryGetValue(key, out var annotations))
                continue;

            // Sort by page then y-position (reading order)
            annotations.Sort((a, b) =>
            {
                var pageCmp = a.page.CompareTo(b.page);
                return pageCmp != 0 ? pageCmp : a.annotation.SortY.CompareTo(b.annotation.SortY);
            });

            // Heading level: depth 0 = ##, depth 1 = ###, etc., capped at ####
            var level = Math.Min(depth + 2, 4);
            sb.AppendLine($"{new string('#', level)} {title}");
            sb.AppendLine();

            foreach (var (page, annotIdx, annotation) in annotations)
            {
                EmitAnnotation(sb, page, annotIdx, annotation);
            }
        }

        // Handle annotations without a matching heading
        if (grouped.TryGetValue("__no_heading__", out var ungrouped))
        {
            sb.AppendLine("## Other Annotations");
            sb.AppendLine();
            ungrouped.Sort((a, b) =>
            {
                var pageCmp = a.page.CompareTo(b.page);
                return pageCmp != 0 ? pageCmp : a.annotation.SortY.CompareTo(b.annotation.SortY);
            });
            foreach (var (page, annotIdx, annotation) in ungrouped)
            {
                EmitAnnotation(sb, page, annotIdx, annotation);
            }
        }

        return sb.ToString();
    }

    private void EmitAnnotation(StringBuilder sb, int page, int annotIdx, Annotation annotation)
    {
        switch (annotation)
        {
            case HighlightAnnotation highlight:
                EmitHighlight(sb, page, highlight);
                break;
            case TextNoteAnnotation note:
                EmitTextNote(sb, page, note);
                break;
            case RectAnnotation rect:
                EmitRect(sb, page, annotIdx, rect);
                break;
            case FreehandAnnotation freehand:
                EmitFreehand(sb, page, annotIdx, freehand);
                break;
        }
    }

    private void EmitHighlight(StringBuilder sb, int page, HighlightAnnotation highlight)
    {
        var blockText = GetBlockText(highlight.OverlappingBlocks);
        var highlightedText = NormalizeText(highlight.Text ?? "");

        if (!string.IsNullOrWhiteSpace(blockText) && !string.IsNullOrWhiteSpace(highlightedText))
        {
            // Bold the highlighted portion within the block text
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

    private void EmitTextNote(StringBuilder sb, int page, TextNoteAnnotation note)
    {
        var blockText = GetBlockText(note.OverlappingBlocks);

        if (!string.IsNullOrWhiteSpace(blockText))
        {
            sb.AppendLine($"> {blockText}");
            sb.AppendLine(">");
        }

        sb.AppendLine($"> **Note:** {note.NoteText}");
        sb.AppendLine($">");
        sb.AppendLine($"> *(p. {page + 1}, note)*");
        sb.AppendLine();
    }

    private void EmitRect(StringBuilder sb, int page, int annotIdx, RectAnnotation rect)
    {
        var hasImage = TryGetImagePath(page, annotIdx, out var imagePath);
        var blockText = GetBlockText(rect.OverlappingBlocks);
        var rectText = NormalizeText(rect.Text ?? "");

        if (hasImage)
        {
            sb.AppendLine($"![Rectangle annotation, p. {page + 1}]({imagePath})");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(blockText))
        {
            sb.AppendLine($"> {blockText}");
        }
        else if (!string.IsNullOrWhiteSpace(rectText))
        {
            sb.AppendLine($"> {rectText}");
        }

        sb.AppendLine($">");
        sb.AppendLine($"> *(p. {page + 1}, rectangle)*");
        sb.AppendLine();
    }

    private void EmitFreehand(StringBuilder sb, int page, int annotIdx, FreehandAnnotation freehand)
    {
        var hasImage = TryGetImagePath(page, annotIdx, out var imagePath);
        var blockText = GetBlockText(freehand.OverlappingBlocks);

        if (hasImage)
        {
            sb.AppendLine($"![Freehand annotation, p. {page + 1}]({imagePath})");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(blockText))
        {
            sb.AppendLine($"> {blockText}");
            sb.AppendLine($">");
        }

        if (!hasImage)
        {
            sb.AppendLine($"> *[Freehand drawing — use `--images` to include a screenshot]*");
            sb.AppendLine($">");
        }

        sb.AppendLine($"> *(p. {page + 1}, freehand)*");
        sb.AppendLine();
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

        // Concatenate text from all overlapping blocks in reading order
        var ordered = blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .OrderBy(b => b.ReadingOrder ?? 0);

        var combined = string.Join("\n\n", ordered.Select(b => NormalizeText(b.Text!)));
        return combined;
    }

    private static string NormalizeText(string text)
    {
        // Replace \r\n and \r with spaces, collapse multiple spaces
        return text
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("  ", " ")
            .Trim();
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

        // Fuzzy match: collapse all whitespace to single spaces in both strings,
        // then find where the highlight sits in the block text
        var normBlock = CollapseWhitespace(blockText);
        var normHighlight = CollapseWhitespace(highlightText);

        var normIdx = normBlock.IndexOf(normHighlight, StringComparison.OrdinalIgnoreCase);
        if (normIdx >= 0)
        {
            // Map normalized indices back to the original block text
            var origStart = MapNormalizedIndex(blockText, normIdx);
            var origEnd = MapNormalizedIndex(blockText, normIdx + normHighlight.Length);

            return blockText[..origStart]
                + "**" + blockText[origStart..origEnd] + "**"
                + blockText[origEnd..];
        }

        // Last resort: show highlighted text separately
        return $"{blockText}\n>\n> **Highlighted:** {highlightText}";
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool lastWasSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Maps an index in the collapsed-whitespace version back to the original string.
    /// </summary>
    private static int MapNormalizedIndex(string original, int normalizedIndex)
    {
        int ni = 0;
        bool lastWasSpace = false;
        // Skip leading whitespace (trimmed in normalized)
        int oi = 0;
        while (oi < original.Length && char.IsWhiteSpace(original[oi])) oi++;

        while (oi < original.Length && ni < normalizedIndex)
        {
            if (char.IsWhiteSpace(original[oi]))
            {
                if (!lastWasSpace) ni++;
                lastWasSpace = true;
            }
            else
            {
                ni++;
                lastWasSpace = false;
            }
            oi++;
        }
        return oi;
    }

    private static string HeadingKey(string title, int page) => $"{title}||{page}";

    private static void BuildHeadingIndex(
        List<OutlineEntry> entries, int depth,
        Dictionary<string, int> depths,
        List<(string key, string title, int depth)> order)
    {
        foreach (var entry in entries)
        {
            var key = HeadingKey(entry.Title, entry.Page);
            depths[key] = depth;
            order.Add((key, entry.Title, depth));
            BuildHeadingIndex(entry.Children, depth + 1, depths, order);
        }
    }
}
