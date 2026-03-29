using System.Diagnostics;
using System.Text.Json;
using Rr2Annotate.Models;

namespace Rr2Annotate.Services;

public static class CliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ProcessStartInfo BuildProcessStartInfo(string command, string arguments)
    {
        // If the command contains spaces and isn't quoted, treat the first token as the executable
        // and prepend any remaining tokens to the arguments.
        string fileName;
        string fullArgs;

        // Support commands like "/path/to/binary" or wrapper scripts
        if (command.Contains(' '))
        {
            var parts = command.Split(' ', 2);
            fileName = parts[0];
            fullArgs = parts.Length > 1 ? $"{parts[1]} {arguments}" : arguments;
        }
        else
        {
            fileName = command;
            fullArgs = arguments;
        }

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = fullArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    public static async Task<AnnotationExport> ExtractAnnotationsAsync(string cliCommand, string pdfPath)
    {
        var args = $"annotations \"{pdfPath}\" --include-text --include-blocks --format json";
        var (json, _) = await RunAsync(cliCommand, args);
        return JsonSerializer.Deserialize<AnnotationExport>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize annotation export.");
    }

    /// <summary>
    /// Renders pages and returns a mapping of 0-based page index to rendered file path,
    /// parsed from the CLI's stderr output.
    /// </summary>
    public static async Task<Dictionary<int, string>> RenderPagesAsync(
        string cliCommand, string pdfPath, IEnumerable<int> pages, string outputDir)
    {
        // Pages in CLI are 1-based, but the JSON uses 0-based page numbers
        var pageRange = string.Join(",", pages.Select(p => p + 1));
        var args = $"render \"{pdfPath}\" --pages \"{pageRange}\" --annotations --output-dir \"{outputDir}\"";
        var (_, stderr) = await RunAsync(cliCommand, args);

        // Parse "Rendered page N/total -> /path/to/file.png" lines from stderr
        var result = new Dictionary<int, string>();
        foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // e.g. "  Rendered page 21/489 -> /tmp/.../page_021.png"
            var arrowIdx = line.IndexOf("->", StringComparison.Ordinal);
            if (arrowIdx < 0) continue;

            var leftPart = line[..arrowIdx].Trim();
            var filePath = line[(arrowIdx + 2)..].Trim();

            // Extract page number from "Rendered page N/total"
            var pageToken = leftPart.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (pageToken == null) continue;
            var slashIdx = pageToken.IndexOf('/');
            var pageNumStr = slashIdx >= 0 ? pageToken[..slashIdx] : pageToken;
            if (int.TryParse(pageNumStr, out var pageNum1Based))
            {
                result[pageNum1Based - 1] = filePath; // convert to 0-based
            }
        }

        // Fallback: if parsing found nothing, discover files by sorted listing
        if (result.Count == 0)
        {
            var pagesList = pages.OrderBy(p => p).ToList();
            var files = Directory.GetFiles(outputDir, "*.png").OrderBy(f => f).ToArray();
            for (int i = 0; i < Math.Min(pagesList.Count, files.Length); i++)
                result[pagesList[i]] = files[i];
        }

        return result;
    }

    private static async Task<(string stdout, string stderr)> RunAsync(string command, string arguments)
    {
        var psi = BuildProcessStartInfo(command, arguments);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {command}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"CLI exited with code {process.ExitCode}.\nstderr: {stderr}");

        return (stdout, stderr);
    }
}
