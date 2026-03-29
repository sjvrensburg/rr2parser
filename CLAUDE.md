# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET tool that extracts annotations from PDFs reviewed in RailReader2 and produces Markdown documents suitable for AI summarisation. Uses the RailReader2 CLI as a backend for PDF parsing and rendering.

## Build & Run

- **Build:** `dotnet build Rr2Annotate/`
- **Run:** `dotnet run --project Rr2Annotate/ -- <pdf> [-o output.md] [--images] [--configure]`

## Architecture

`Rr2Annotate/` is a .NET console app. Key components:

- **Models/** — C# records for deserializing the RailReader2 CLI JSON output. `Annotation.cs` uses a custom `JsonConverter` to polymorphically deserialize four annotation types (highlight, text_note, rect, freehand).
- **Services/Settings.cs** — Persists the user's RailReader2 CLI command to `~/.config/rr2annotate/settings.json`. Handles first-run onboarding and `--configure`.
- **Services/CliRunner.cs** — Shells out to the RailReader2 CLI (`annotations` and `render` commands), parses JSON output.
- **Services/ScreenshotService.cs** — Renders annotated pages via CLI, then crops to individual annotation bounding boxes using SkiaSharp. Only used when `--images` is passed.
- **Services/MarkdownBuilder.cs** — Groups annotations by their `nearest_heading` from the PDF outline, sorts by reading order (page then y-position), and emits Markdown with bold-in-context highlights, blockquoted notes, and optional image embeds.
