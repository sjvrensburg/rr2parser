# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET tool that extracts annotations from PDFs reviewed in RailReader2 and produces Markdown documents suitable for AI summarisation. Uses the RailReader2 CLI as a backend for PDF parsing and rendering.

## Build & Test

- **Build:** `dotnet build`
- **Test:** `dotnet test`
- **Single test:** `dotnet test --filter "FullyQualifiedName~TestName"`
- **Run:** `dotnet run --project Rr2Annotate/ -- <pdf> [-o output.md] [--pages] [--color] [--images] [--configure]`

The repo has a solution file (`Rr2Annotate.sln`) so `dotnet build` and `dotnet test` work from the repo root.

## Architecture

`Rr2Annotate/` is a .NET console app, `Rr2Annotate.Tests/` is the xUnit test project.

- **Models/** — C# records for deserializing the RailReader2 CLI JSON output. `Annotation.cs` uses a custom `JsonConverter` to polymorphically deserialize four annotation types (highlight, text_note, rect, freehand).
- **Services/Settings.cs** — Persists the user's RailReader2 CLI command to `~/.config/rr2annotate/settings.json`. Handles first-run onboarding and `--configure`.
- **Services/CliRunner.cs** — Shells out to the RailReader2 CLI (`annotations` and `render` commands), parses JSON output. Accepts optional page range to pass through to the CLI for faster extraction.
- **Services/ScreenshotService.cs** — Renders annotated pages via CLI, then crops to individual annotation bounding boxes using SkiaSharp. Only used when `--images` is passed.
- **Services/MarkdownBuilder.cs** — Groups annotations by their `nearest_heading` from the PDF outline, sorts by reading order (page then y-position), and emits Markdown. Key behaviours: summary table, bold-in-context highlights (with fuzzy whitespace matching), block text deduplication for consecutive annotations on the same paragraph, PDF text artifact cleanup (`CleanText`), blockquoted notes, and optional image embeds.
