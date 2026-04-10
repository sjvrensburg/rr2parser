# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET tool that extracts annotations from PDFs reviewed in RailReader2 and produces Markdown documents suitable for AI summarisation. Uses the RailReader2 CLI as a backend for PDF parsing and rendering.

## Build & Test

- **Build:** `dotnet build`
- **Test:** `dotnet test`
- **Single test:** `dotnet test --filter "FullyQualifiedName~TestName"`
- **Run:** `dotnet run --project Rr2Annotate/ -- <pdf> [-o output.md] [--pages] [--color] [--images] [--configure]`
- **Pack:** `dotnet pack Rr2Annotate/`

Solution file is `Rr2Annotate.slnx` (new XML `.slnx` format, not traditional `.sln`).

## Architecture

`Rr2Annotate/` is a .NET console app (top-level statements in `Program.cs`), `Rr2Annotate.Tests/` is the xUnit test project. All services are static classes — no dependency injection.

### Pipeline (Program.cs)

1. Manual arg parsing (no library) → `Settings.EnsureConfigured()` → `CliRunner.ExtractAnnotationsAsync()` → page/color filtering → optional `ScreenshotService.CropAnnotationsAsync()` → `MarkdownBuilder.Build()` → write file.

### Models (Models/)

C# records with `[JsonPropertyName]` for snake_case JSON. `Annotation.cs` defines an abstract base and four concrete subtypes (`HighlightAnnotation`, `TextNoteAnnotation`, `RectAnnotation`, `FreehandAnnotation`) with a hand-written `JsonConverter<Annotation>` that dispatches on a `"type"` discriminator. Each subtype exposes `SortY` for reading-order sorting. `AnnotationExport.cs` contains the top-level container and supporting types (`OutlineEntry`, `AnnotatedPage`, `LayoutBlock`, etc.).

### Services

- **Settings.cs** — Persists the CLI command to `~/.config/rr2annotate/settings.json`. Interactive first-run onboarding.
- **CliRunner.cs** — Shells out to RailReader2 CLI (`annotations` and `render` commands). The `render` command's output file paths are parsed from **stderr** (not stdout). `BuildProcessStartInfo()` is shared with `Settings` for testing the CLI command.
- **ScreenshotService.cs** — Crops rendered page PNGs to annotation bounding boxes using SkiaSharp (render at 300 DPI, scale factor 300/72). Groups nearby freehand strokes via a **union-find** algorithm (`MergeDistancePt = 50`).
- **MarkdownBuilder.cs** — Groups annotations by `NearestHeading`, sorts by (page, SortY), emits Markdown. Key behaviours: summary table, bold-in-context highlights with three-tier fuzzy matching (exact → whitespace-collapsed → fallback), block text deduplication for consecutive annotations on the same paragraph, `CleanText` (uses `[GeneratedRegex]`), blockquoted notes, optional image embeds. Heading depth maps as: depth 0 → `##`, 1 → `###`, 2+ → `####`.

### Page indexing

Three conventions coexist — be careful which you're using:
- **User input & CLI arguments:** 1-based
- **JSON / internal models:** 0-based
- **Markdown output:** 1-based (`page + 1`)

## Testing

`InternalsVisibleTo` is enabled so tests can call `internal static MarkdownBuilder.CleanText()`. Tests use inline helper methods (`MakeExport()`, `Heading()`) to construct test data without needing the CLI. No integration tests exist for `CliRunner` or `ScreenshotService` (both require the external RailReader2 CLI).
