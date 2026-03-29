using System.Text.Json;
using Rr2Annotate.Models;

namespace Rr2Annotate.Tests;

public class AnnotationConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserializes_Highlight()
    {
        var json = """
        {
            "type": "highlight",
            "color": "#FFB6C1",
            "opacity": 0.35,
            "rects": [{"x": 100, "y": 200, "w": 50, "h": 10}],
            "text": "some highlighted text",
            "overlapping_blocks": [],
            "nearest_heading": {"title": "Abstract", "source": "outline", "page": 1}
        }
        """;

        var annotation = JsonSerializer.Deserialize<Annotation>(json, Options);

        var highlight = Assert.IsType<HighlightAnnotation>(annotation);
        Assert.Equal("highlight", highlight.Type);
        Assert.Equal("#FFB6C1", highlight.Color);
        Assert.Equal("some highlighted text", highlight.Text);
        Assert.Single(highlight.Rects);
        Assert.Equal(100, highlight.Rects[0].X);
        Assert.Equal("Abstract", highlight.NearestHeading!.Title);
    }

    [Fact]
    public void Deserializes_TextNote()
    {
        var json = """
        {
            "type": "text_note",
            "color": "#FFCC00",
            "opacity": 0.9,
            "x": 558.0,
            "y": 390.0,
            "note_text": "Rephrase this",
            "overlapping_blocks": [],
            "nearest_heading": {"title": "Introduction", "source": "outline", "page": 5}
        }
        """;

        var annotation = JsonSerializer.Deserialize<Annotation>(json, Options);

        var note = Assert.IsType<TextNoteAnnotation>(annotation);
        Assert.Equal("Rephrase this", note.NoteText);
        Assert.Equal(558.0, note.X);
        Assert.Equal(390.0, note.Y);
    }

    [Fact]
    public void Deserializes_Rect()
    {
        var json = """
        {
            "type": "rect",
            "color": "#0066FF",
            "opacity": 0.5,
            "stroke_width": 2,
            "x": 56.0,
            "y": 477.0,
            "w": 506.0,
            "h": 253.0,
            "filled": false,
            "text": "content inside rect",
            "overlapping_blocks": [
                {
                    "class": "text",
                    "class_id": 22,
                    "b_box": {"x": 57, "y": 426, "w": 494, "h": 87},
                    "confidence": 0.94,
                    "reading_order": 0,
                    "text": "block text here"
                }
            ],
            "nearest_heading": null
        }
        """;

        var annotation = JsonSerializer.Deserialize<Annotation>(json, Options);

        var rect = Assert.IsType<RectAnnotation>(annotation);
        Assert.Equal(506.0, rect.W);
        Assert.False(rect.Filled);
        Assert.Equal("content inside rect", rect.Text);
        Assert.Single(rect.OverlappingBlocks);
        Assert.Equal("text", rect.OverlappingBlocks[0].Class);
        Assert.Null(rect.NearestHeading);
    }

    [Fact]
    public void Deserializes_Freehand()
    {
        var json = """
        {
            "type": "freehand",
            "color": "#FF0000",
            "opacity": 0.8,
            "stroke_width": 2,
            "points": [{"x": 100, "y": 200}, {"x": 105, "y": 210}],
            "overlapping_blocks": [],
            "nearest_heading": {"title": "Methods", "source": "outline", "page": 10}
        }
        """;

        var annotation = JsonSerializer.Deserialize<Annotation>(json, Options);

        var freehand = Assert.IsType<FreehandAnnotation>(annotation);
        Assert.Equal(2, freehand.Points.Count);
        Assert.Equal(200, freehand.SortY); // min Y
    }

    [Fact]
    public void Deserializes_Full_Export()
    {
        var json = """
        {
            "source": "test.pdf",
            "exported_at": "2026-01-01T00:00:00Z",
            "page_count": 10,
            "outline": [
                {"title": "Chapter 1", "page": 0, "children": [
                    {"title": "Section 1.1", "page": 1, "children": []}
                ]}
            ],
            "pages": [
                {
                    "page": 0,
                    "width": 595.0,
                    "height": 842.0,
                    "annotations": [
                        {
                            "type": "highlight",
                            "color": "#FF0",
                            "opacity": 0.5,
                            "rects": [],
                            "text": "test",
                            "overlapping_blocks": [],
                            "nearest_heading": {"title": "Chapter 1", "source": "outline", "page": 0}
                        }
                    ]
                }
            ],
            "bookmarks": []
        }
        """;

        var export = JsonSerializer.Deserialize<AnnotationExport>(json, Options)!;

        Assert.Equal("test.pdf", export.Source);
        Assert.Equal(10, export.PageCount);
        Assert.Single(export.Outline);
        Assert.Equal("Chapter 1", export.Outline[0].Title);
        Assert.Single(export.Outline[0].Children);
        Assert.Single(export.Pages);
        Assert.IsType<HighlightAnnotation>(export.Pages[0].Annotations[0]);
    }
}
