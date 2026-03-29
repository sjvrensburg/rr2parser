using Rr2Annotate.Models;
using SkiaSharp;

namespace Rr2Annotate.Services;

public static class ScreenshotService
{
    private const int RenderDpi = 300;
    private const double PdfPointsPerInch = 72.0;
    private static readonly double Scale = RenderDpi / PdfPointsPerInch;
    private const int PaddingPx = 40;

    // PDF-point distance within which freehand annotations are merged into one screenshot.
    // ~50pt ≈ ~18mm — generous enough to catch strokes forming a single mark.
    private const double MergeDistancePt = 50.0;

    /// <summary>
    /// Renders the required pages and crops images for rect/freehand annotations.
    /// Freehand annotations on the same page are grouped when they are spatially
    /// close or share overlapping blocks, producing a single combined screenshot.
    /// Returns a dictionary mapping (pageIndex, annotationIndex) to the cropped image path.
    /// </summary>
    public static async Task<Dictionary<(int page, int annotIdx), string>> CropAnnotationsAsync(
        string cliCommand, string pdfPath, AnnotationExport export, string imageDir)
    {
        Directory.CreateDirectory(imageDir);

        // Determine which pages need rendering
        var pagesNeeding = new HashSet<int>();
        foreach (var page in export.Pages)
        {
            for (int i = 0; i < page.Annotations.Count; i++)
            {
                if (page.Annotations[i] is RectAnnotation or FreehandAnnotation)
                    pagesNeeding.Add(page.Page);
            }
        }

        if (pagesNeeding.Count == 0)
            return [];

        // Render pages to a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"rr2annotate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var pageMap = await CliRunner.RenderPagesAsync(cliCommand, pdfPath, pagesNeeding, tempDir);

            var result = new Dictionary<(int, int), string>();
            int imageCounter = 0;

            foreach (var page in export.Pages)
            {
                if (!pageMap.TryGetValue(page.Page, out var renderedPath))
                    continue;

                using var bitmap = SKBitmap.Decode(renderedPath);
                if (bitmap == null)
                    continue;

                // Separate rect annotations (individual crops) from freehand (grouped)
                var rectIndices = new List<int>();
                var freehandIndices = new List<int>();

                for (int i = 0; i < page.Annotations.Count; i++)
                {
                    switch (page.Annotations[i])
                    {
                        case RectAnnotation:
                            rectIndices.Add(i);
                            break;
                        case FreehandAnnotation:
                            freehandIndices.Add(i);
                            break;
                    }
                }

                // Crop each rect individually
                foreach (var i in rectIndices)
                {
                    var cropRect = GetBounds((RectAnnotation)page.Annotations[i]);
                    var pixelRect = ToPixelRect(cropRect, bitmap.Width, bitmap.Height);
                    if (pixelRect == null) continue;

                    imageCounter++;
                    var outPath = Path.Combine(imageDir, $"annotation_{imageCounter:D3}.png");
                    CropAndSave(bitmap, pixelRect.Value, outPath);
                    result[(page.Page, i)] = outPath;
                }

                // Group freehand annotations and crop one image per group
                var groups = GroupFreehandAnnotations(page, freehandIndices);

                foreach (var group in groups)
                {
                    // Compute the union bounding box of all freehand annotations in the group
                    var unionBounds = GetGroupBounds(page, group);
                    if (unionBounds == null) continue;

                    var pixelRect = ToPixelRect(unionBounds.Value, bitmap.Width, bitmap.Height);
                    if (pixelRect == null) continue;

                    imageCounter++;
                    var outPath = Path.Combine(imageDir, $"annotation_{imageCounter:D3}.png");
                    CropAndSave(bitmap, pixelRect.Value, outPath);

                    // Map all annotations in the group to the same image
                    foreach (var i in group)
                        result[(page.Page, i)] = outPath;
                }
            }

            return result;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Groups freehand annotation indices on a page. Two freehand annotations are
    /// in the same group if their bounding boxes are within MergeDistancePt of each
    /// other, or if they share any overlapping block.
    /// Uses union-find for transitive merging.
    /// </summary>
    private static List<List<int>> GroupFreehandAnnotations(AnnotatedPage page, List<int> indices)
    {
        if (indices.Count == 0) return [];

        // Union-Find
        var parent = new int[indices.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        // Precompute bounding boxes and block sets
        var bounds = new (double minX, double minY, double maxX, double maxY)[indices.Count];
        var blockIds = new HashSet<(double, double, double, double)>[indices.Count];

        for (int i = 0; i < indices.Count; i++)
        {
            var fh = (FreehandAnnotation)page.Annotations[indices[i]];
            if (fh.Points.Count > 0)
            {
                bounds[i] = (
                    fh.Points.Min(p => p.X), fh.Points.Min(p => p.Y),
                    fh.Points.Max(p => p.X), fh.Points.Max(p => p.Y));
            }

            blockIds[i] = new HashSet<(double, double, double, double)>(
                fh.OverlappingBlocks.Select(b => (b.BBox.X, b.BBox.Y, b.BBox.W, b.BBox.H)));
        }

        // Merge by proximity or shared blocks
        for (int i = 0; i < indices.Count; i++)
        {
            for (int j = i + 1; j < indices.Count; j++)
            {
                if (Find(i) == Find(j)) continue;

                // Check shared overlapping blocks
                if (blockIds[i].Count > 0 && blockIds[j].Count > 0 && blockIds[i].Overlaps(blockIds[j]))
                {
                    Union(i, j);
                    continue;
                }

                // Check spatial proximity: distance between bounding boxes
                if (BBoxDistance(bounds[i], bounds[j]) <= MergeDistancePt)
                {
                    Union(i, j);
                }
            }
        }

        // Collect groups
        var groupMap = new Dictionary<int, List<int>>();
        for (int i = 0; i < indices.Count; i++)
        {
            var root = Find(i);
            if (!groupMap.ContainsKey(root))
                groupMap[root] = [];
            groupMap[root].Add(indices[i]);
        }

        return groupMap.Values.ToList();
    }

    /// <summary>
    /// Minimum distance between two axis-aligned bounding boxes (0 if overlapping).
    /// </summary>
    private static double BBoxDistance(
        (double minX, double minY, double maxX, double maxY) a,
        (double minX, double minY, double maxX, double maxY) b)
    {
        var dx = Math.Max(0, Math.Max(a.minX - b.maxX, b.minX - a.maxX));
        var dy = Math.Max(0, Math.Max(a.minY - b.maxY, b.minY - a.maxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static (double x, double y, double w, double h)? GetGroupBounds(
        AnnotatedPage page, List<int> indices)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        foreach (var i in indices)
        {
            var fh = (FreehandAnnotation)page.Annotations[i];
            foreach (var p in fh.Points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                any = true;
            }
        }

        if (!any) return null;
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static (double x, double y, double w, double h) GetBounds(RectAnnotation rect)
        => (rect.X, rect.Y, rect.W, rect.H);

    private static SKRectI? ToPixelRect(
        (double x, double y, double w, double h) bounds,
        int bitmapWidth, int bitmapHeight)
    {
        var px = (int)(bounds.x * Scale) - PaddingPx;
        var py = (int)(bounds.y * Scale) - PaddingPx;
        var pw = (int)(bounds.w * Scale) + PaddingPx * 2;
        var ph = (int)(bounds.h * Scale) + PaddingPx * 2;

        px = Math.Max(0, px);
        py = Math.Max(0, py);
        pw = Math.Min(pw, bitmapWidth - px);
        ph = Math.Min(ph, bitmapHeight - py);

        if (pw <= 0 || ph <= 0) return null;
        return new SKRectI(px, py, px + pw, py + ph);
    }

    private static void CropAndSave(SKBitmap source, SKRectI rect, string outputPath)
    {
        using var cropped = new SKBitmap(rect.Width, rect.Height);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(source, rect, new SKRect(0, 0, rect.Width, rect.Height));
        canvas.Flush();

        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }
}
