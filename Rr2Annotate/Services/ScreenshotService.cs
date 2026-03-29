using Rr2Annotate.Models;
using SkiaSharp;

namespace Rr2Annotate.Services;

public static class ScreenshotService
{
    private const int RenderDpi = 300;
    private const double PdfPointsPerInch = 72.0;
    private static readonly double Scale = RenderDpi / PdfPointsPerInch;
    private const int PaddingPx = 40;

    /// <summary>
    /// Renders the required pages and crops images for rect/freehand annotations.
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
            // RenderPagesAsync returns a map of 0-based page index -> rendered file path
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

                for (int i = 0; i < page.Annotations.Count; i++)
                {
                    var annotation = page.Annotations[i];
                    var cropRect = GetCropRect(annotation, bitmap.Width, bitmap.Height);
                    if (cropRect == null) continue;

                    imageCounter++;
                    var outPath = Path.Combine(imageDir, $"annotation_{imageCounter:D3}.png");
                    CropAndSave(bitmap, cropRect.Value, outPath);
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

    private static SKRectI? GetCropRect(Annotation annotation, int bitmapWidth, int bitmapHeight)
    {
        double x, y, w, h;

        switch (annotation)
        {
            case RectAnnotation rect:
                x = rect.X;
                y = rect.Y;
                w = rect.W;
                h = rect.H;
                break;
            case FreehandAnnotation freehand when freehand.Points.Count > 0:
                var minX = freehand.Points.Min(p => p.X);
                var minY = freehand.Points.Min(p => p.Y);
                var maxX = freehand.Points.Max(p => p.X);
                var maxY = freehand.Points.Max(p => p.Y);
                x = minX;
                y = minY;
                w = maxX - minX;
                h = maxY - minY;
                break;
            default:
                return null;
        }

        // Convert PDF coordinates to pixel coordinates
        var px = (int)(x * Scale) - PaddingPx;
        var py = (int)(y * Scale) - PaddingPx;
        var pw = (int)(w * Scale) + PaddingPx * 2;
        var ph = (int)(h * Scale) + PaddingPx * 2;

        // Clamp to bitmap bounds
        px = Math.Max(0, px);
        py = Math.Max(0, py);
        pw = Math.Min(pw, bitmapWidth - px);
        ph = Math.Min(ph, bitmapHeight - py);

        if (pw <= 0 || ph <= 0)
            return null;

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
