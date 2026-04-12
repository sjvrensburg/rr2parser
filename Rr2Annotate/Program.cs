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
bool exportMode = false;
bool contextMode = false;
bool noVlm = false;
string? vlmEndpoint = null;
string? vlmModel = null;
string? vlmApiKey = null;

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
        case "--export":
            exportMode = true;
            break;
        case "--context":
            contextMode = true;
            break;
        case "--no-vlm":
            noVlm = true;
            break;
        case "--vlm-endpoint" when i + 1 < args.Length:
            vlmEndpoint = args[++i];
            break;
        case "--vlm-model" when i + 1 < args.Length:
            vlmModel = args[++i];
            break;
        case "--vlm-api-key" when i + 1 < args.Length:
            vlmApiKey = args[++i];
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

// Validate mode combinations
if (exportMode && contextMode)
{
    Console.Error.WriteLine("Error: --export and --context cannot be used together.");
    return 1;
}

if (exportMode && (colorArg != null || includeImages))
{
    Console.Error.WriteLine("Error: --export cannot be combined with --color or --images.");
    return 1;
}

if (includeImages && contextMode)
{
    Console.Error.WriteLine("Error: --images with --context is not yet supported (use --images or --context separately).");
    return 1;
}

// Stdout mode: -o - writes markdown to stdout (incompatible with --images)
bool stdoutMode = outputPath == "-";
if (stdoutMode && includeImages)
{
    Console.Error.WriteLine("Error: --images is not compatible with -o - (stdout output).");
    return 1;
}

// Default output path (skip for stdout mode)
if (!stdoutMode)
    outputPath ??= exportMode
        ? Path.ChangeExtension(pdfPath, null) + "-export.md"
        : Path.ChangeExtension(pdfPath, null) + "-annotations.md";

// Export passthrough: delegate entirely to RailReader2's export command
if (exportMode)
{
    Console.Error.WriteLine($"Exporting via RailReader2: {Path.GetFileName(pdfPath)}");
    var exportMarkdown = await CliRunner.ExportAsync(
        settings.CliCommand, pdfPath, cliPageRange,
        noVlm, vlmEndpoint, vlmModel, vlmApiKey);

    if (stdoutMode)
    {
        Console.Write(exportMarkdown);
    }
    else
    {
        File.WriteAllText(outputPath!, exportMarkdown);
        Console.Error.WriteLine($"Written to: {outputPath}");
    }
    return 0;
}

// Warn if VLM flags provided without --export or --context
if ((noVlm || vlmEndpoint != null || vlmModel != null || vlmApiKey != null) && !contextMode)
{
    Console.Error.WriteLine("Warning: VLM flags have no effect without --export or --context.");
}

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

// Context mode: fetch full page text from RailReader2 export
Dictionary<int, string>? pageContext = null;

if (contextMode)
{
    Console.Error.WriteLine("Fetching page context via RailReader2 export...");

    var contextMarkdown = await CliRunner.ExportAsync(
        settings.CliCommand, pdfPath, cliPageRange,
        noVlm, vlmEndpoint, vlmModel, vlmApiKey,
        noAnnotations: true);

    // Split on page breaks (---) to get per-page context
    var chunks = contextMarkdown.Split("\n---\n", StringSplitOptions.None);
    pageContext = new Dictionary<int, string>();

    if (cliPageRange != null)
    {
        var requestedPages = ParsePageRange(cliPageRange, export.PageCount);
        for (int i = 0; i < chunks.Length && i < requestedPages.Count; i++)
            pageContext[requestedPages[i] - 1] = chunks[i].Trim(); // 1-based → 0-based
    }
    else
    {
        for (int i = 0; i < chunks.Length; i++)
            pageContext[i] = chunks[i].Trim();
    }

    Console.Error.WriteLine($"Retrieved context for {pageContext.Count} pages.");
}

// Optionally render and crop screenshots
Dictionary<(int page, int annotIdx), string>? images = null;
string? imageRelDir = null;
List<FigureReference>? extractedFigures = null;

if (includeImages)
{
    var imageDir = Path.ChangeExtension(outputPath, null) + "-images";
    imageRelDir = Path.GetFileName(imageDir);
    Console.Error.WriteLine("Rendering screenshots...");
    images = await ScreenshotService.CropAnnotationsAsync(settings.CliCommand, pdfPath, export, imageDir);
    Console.Error.WriteLine($"Cropped {images.Count} annotation images.");

    // Extract figures via RailReader2 export
    Console.Error.WriteLine("Extracting figures via RailReader2 export...");
    var figureDir = Path.ChangeExtension(outputPath, null) + "-figures";
    var figureRelDir = Path.GetFileName(figureDir);

    var figureMarkdown = await CliRunner.ExportAsync(
        settings.CliCommand, pdfPath, cliPageRange,
        noVlm, vlmEndpoint, vlmModel, vlmApiKey,
        figureDir: figureDir, noAnnotations: true);

    extractedFigures = ExtractFigureReferences(figureMarkdown, figureRelDir);
    Console.Error.WriteLine($"Extracted {extractedFigures.Count} figures to {figureDir}");
}

// Build markdown
var builder = new MarkdownBuilder(export, images, imageRelDir, pageContext, extractedFigures);
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

static List<int> ParsePageRange(string range, int maxPage)
{
    var pages = new List<int>();
    foreach (var part in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (part.Contains('-'))
        {
            var bounds = part.Split('-', 2);
            var start = int.Parse(bounds[0]);
            var end = bounds.Length > 1 && !string.IsNullOrEmpty(bounds[1]) ? int.Parse(bounds[1]) : maxPage;
            for (int p = start; p <= end; p++) pages.Add(p);
        }
        else
        {
            pages.Add(int.Parse(part));
        }
    }
    return pages;
}

static List<FigureReference> ExtractFigureReferences(string markdown, string figureRelDir)
{
    var figures = new List<FigureReference>();
    var regex = new System.Text.RegularExpressions.Regex(@"!\[([^\]]*)\]\(([^)]+)\)");

    foreach (System.Text.RegularExpressions.Match match in regex.Matches(markdown))
    {
        var description = match.Groups[1].Value;
        var fileName = Path.GetFileName(match.Groups[2].Value);
        var relativePath = Path.Combine(figureRelDir, fileName);
        figures.Add(new FigureReference(description, relativePath, 0));
    }

    return figures;
}

static void PrintUsage()
{
    Console.WriteLine("""
        rr2annotate — Export RailReader2 annotations as Markdown

        Usage: rr2annotate <pdf> [options]

        Options:
          -o <path>            Output file (default: <pdf-stem>-annotations.md). Use - for stdout
          --pages <range>      Only include annotations from these pages (e.g. "1,3,5-10")
          --color <hex>        Filter by annotation colour (e.g. "#FF0000" or "ff0000,ffcc00")
          --images             Include cropped screenshots for rect/freehand annotations
          --export             Delegate to RailReader2 export (full-page Markdown, no annotation processing)
          --context            Enrich highlights with full page context from RailReader2 export
          --no-vlm             Disable VLM transcription (with --export or --context)
          --vlm-endpoint <url> Override VLM endpoint URL
          --vlm-model <name>   Override VLM model name
          --vlm-api-key <key>  Override VLM API key
          --configure          Set or update the path to the RailReader2 CLI
          -h, --help           Show this help
        """);
}
