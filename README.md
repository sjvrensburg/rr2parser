# Rr2Annotate

A .NET command-line tool that extracts annotations from PDFs reviewed in [RailReader2](https://github.com/sjvrensburg/railreader2) and produces structured Markdown documents. Designed for feeding annotated documents to AI for summarisation or explanation.

## Features

- Extracts all annotation types: highlights, text notes, rectangles, and freehand drawings
- Groups annotations under document headings from the PDF outline
- Arranges content in reading order
- Highlights appear **bold** within their surrounding text context
- Text notes are rendered as blockquotes with the nearby document text
- Optional cropped screenshots for rectangle and freehand annotations
- Page range filtering to export only specific pages

## Prerequisites

- [.NET 9+](https://dotnet.microsoft.com/download)
- [RailReader2 CLI](https://github.com/sjvrensburg/railreader2) installed and accessible

## Setup

Build the project:

```bash
dotnet build Rr2Annotate/
```

Configure the path to your RailReader2 CLI on first run (or any time with `--configure`):

```bash
dotnet run --project Rr2Annotate/ -- --configure
```

This stores your CLI command in `~/.config/rr2annotate/settings.json`. You can point it to a wrapper script, a direct binary path, or any command that invokes the RailReader2 CLI.

## Usage

```
rr2annotate <pdf> [options]

Options:
  -o <path>       Output markdown file (default: <pdf-stem>-annotations.md)
  --pages <range> Only include annotations from these pages (e.g. "1,3,5-10")
  --images        Include cropped screenshots for rect/freehand annotations
  --configure     Set or update the path to the RailReader2 CLI
  -h, --help      Show this help
```

### Text-only export

```bash
dotnet run --project Rr2Annotate/ -- document.pdf -o notes.md
```

### With images

```bash
dotnet run --project Rr2Annotate/ -- document.pdf -o notes.md --images
```

This creates `notes.md` and a `notes-images/` directory with cropped screenshots of rectangle and freehand annotations.

### Specific pages only

```bash
dotnet run --project Rr2Annotate/ -- document.pdf -o notes.md --pages "1,3,5-10"
```

### Install as a global tool

```bash
dotnet pack Rr2Annotate/
dotnet tool install --global --add-source Rr2Annotate/nupkg Rr2Annotate
```

Then use directly:

```bash
rr2annotate document.pdf -o notes.md --images
```

## License

MIT
