using Rr2Annotate.Models;
using Rr2Annotate.Services;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

// Handle --configure
if (args.Contains("--configure"))
{
    await Settings.EnsureConfigured(forceReconfigure: true);
    return 0;
}

// Parse arguments
string? pdfPath = null;
string? outputPath = null;
bool includeImages = false;
string? pagesArg = null;
string? colorArg = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--images":
            includeImages = true;
            break;
        case "--pages" when i + 1 < args.Length:
            pagesArg = args[++i];
            break;
        case "--color" when i + 1 < args.Length:
            colorArg = args[++i];
            break;
        default:
            if (!args[i].StartsWith('-'))
                pdfPath = args[i];
            break;
    }
}

if (string.IsNullOrWhiteSpace(pdfPath))
{
    Console.Error.WriteLine("Error: No PDF path specified.");
    PrintUsage();
    return 1;
}

if (!File.Exists(pdfPath))
{
    Console.Error.WriteLine($"Error: File not found: {pdfPath}");
    return 1;
}

// Pass page range directly to CLI (CLI validates natively)
string? cliPageRange = pagesArg;

// Stdout mode: -o - writes markdown to stdout (incompatible with --images)
bool stdoutMode = outputPath == "-";
if (stdoutMode && includeImages)
{
    Console.Error.WriteLine("Error: --images is not compatible with -o - (stdout output).");
    return 1;
}

// Parse colour filter
HashSet<string>? colorFilter = null;
if (colorArg != null)
{
    colorFilter = ParseColorFilter(colorArg);
    if (colorFilter == null || colorFilter.Count == 0)
    {
        Console.Error.WriteLine($"Error: Invalid colour filter: {colorArg}");
        return 1;
    }
}

// Ensure CLI is configured
var settings = await Settings.EnsureConfigured();

// Default output path (skip for stdout mode)
if (!stdoutMode)
    outputPath ??= Path.ChangeExtension(pdfPath, null) + "-annotations.md";

Console.Error.WriteLine($"Extracting annotations from: {Path.GetFileName(pdfPath)}");

// Extract annotations (pass page range to CLI for faster extraction)
var export = await CliRunner.ExtractAnnotationsAsync(settings.CliCommand, pdfPath, cliPageRange);

// Apply colour filter
if (colorFilter != null)
{
    export = export with
    {
        Pages = export.Pages
            .Select(p => p with
            {
                Annotations = p.Annotations
                    .Where(a => colorFilter.Contains(NormalizeColor(a.Color)))
                    .ToList()
            })
            .Where(p => p.Annotations.Count > 0)
            .ToList()
    };
}

var totalAnnotations = export.Pages.Sum(p => p.Annotations.Count);
Console.Error.WriteLine($"Found {totalAnnotations} annotations across {export.Pages.Count} pages.");

if (totalAnnotations == 0)
{
    Console.Error.WriteLine("No annotations found. Nothing to export.");
    return 0;
}

// Optionally render and crop screenshots
Dictionary<(int page, int annotIdx), string>? images = null;
string? imageRelDir = null;

if (includeImages)
{
    var imageDir = Path.ChangeExtension(outputPath, null) + "-images";
    imageRelDir = Path.GetFileName(imageDir);
    Console.Error.WriteLine("Rendering screenshots...");
    images = await ScreenshotService.CropAnnotationsAsync(settings.CliCommand, pdfPath, export, imageDir);
    Console.Error.WriteLine($"Cropped {images.Count} annotation images.");
}

// Build markdown
var builder = new MarkdownBuilder(export, images, imageRelDir);
var markdown = builder.Build();

if (stdoutMode)
{
    Console.Write(markdown);
}
else
{
    File.WriteAllText(outputPath!, markdown);
    Console.Error.WriteLine($"Written to: {outputPath}");
}

return 0;

static HashSet<string>? ParseColorFilter(string input)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var color = NormalizeColor(part);
        if (string.IsNullOrEmpty(color)) return null;
        result.Add(color);
    }
    return result;
}

static string NormalizeColor(string color)
{
    // Strip # prefix, lowercase
    var c = color.TrimStart('#').ToLowerInvariant();
    // Expand 3-char hex to 6-char: "f00" -> "ff0000"
    if (c.Length == 3)
        c = $"{c[0]}{c[0]}{c[1]}{c[1]}{c[2]}{c[2]}";
    return c;
}

static void PrintUsage()
{
    Console.WriteLine("""
        rr2annotate — Export RailReader2 annotations as Markdown

        Usage: rr2annotate <pdf> [options]

        Options:
          -o <path>       Output file (default: <pdf-stem>-annotations.md). Use - to write to stdout
          --pages <range> Only include annotations from these pages (e.g. "1,3,5-10")
          --color <hex>   Filter by annotation colour (e.g. "#FF0000" or "ff0000,ffcc00")
          --images        Include cropped screenshots for rect/freehand annotations
          --configure     Set or update the path to the RailReader2 CLI
          -h, --help      Show this help
        """);
}
