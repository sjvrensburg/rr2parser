using Rr2Annotate.Models;
using Rr2Annotate.Services;

namespace Rr2Annotate.Tests;

public class MarkdownBuilderTests
{
    private static AnnotationExport MakeExport(
        List<OutlineEntry>? outline = null,
        List<AnnotatedPage>? pages = null)
    {
        return new AnnotationExport(
            Source: "test.pdf",
            ExportedAt: "2026-01-01T00:00:00Z",
            PageCount: 100,
            Outline: outline ?? [],
            Pages: pages ?? []);
    }

    private static NearestHeading Heading(string title, int page) => new(title, "outline", page);

    [Fact]
    public void Highlight_Bolds_Text_In_Context()
    {
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "The quick brown fox jumps over the lazy dog.")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "brown fox jumps"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("**brown fox jumps**", md);
        Assert.Contains("The quick", md);
        Assert.Contains("lazy dog.", md);
    }

    [Fact]
    public void Highlight_Fuzzy_Match_Handles_Whitespace_Differences()
    {
        var export = MakeExport(
            outline: [new("Section A", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "word one word two word three")],
                    NearestHeading = Heading("Section A", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "word  two" // double space
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("**word two**", md);
        Assert.DoesNotContain("Highlighted:", md);
    }

    [Fact]
    public void TextNote_Shows_Note_And_Context()
    {
        var export = MakeExport(
            outline: [new("Methods", 5, [])],
            pages: [new(5, 595, 842, [
                new TextNoteAnnotation
                {
                    Type = "text_note", Color = "#FFCC00", Opacity = 0.9,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "Surrounding paragraph text.")],
                    NearestHeading = Heading("Methods", 5),
                    X = 500, Y = 300,
                    NoteText = "Clarify this sentence"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("## Methods", md);
        Assert.Contains("Surrounding paragraph text.", md);
        Assert.Contains("**Note:** Clarify this sentence", md);
        Assert.Contains("*(p. 6, note)*", md);
    }

    [Fact]
    public void Rect_Without_Images_Shows_Text()
    {
        var export = MakeExport(
            outline: [new("Results", 10, [])],
            pages: [new(10, 595, 842, [
                new RectAnnotation
                {
                    Type = "rect", Color = "#00F", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "Block text under the rectangle.")],
                    NearestHeading = Heading("Results", 10),
                    X = 50, Y = 50, W = 400, H = 200, Filled = false, StrokeWidth = 2,
                    Text = "Direct rect text"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("Block text under the rectangle.", md);
        Assert.Contains("*(p. 11, rectangle)*", md);
        Assert.DoesNotContain("![", md);
    }

    [Fact]
    public void Freehand_Without_Images_Shows_Placeholder()
    {
        var export = MakeExport(
            outline: [new("Discussion", 20, [])],
            pages: [new(20, 595, 842, [
                new FreehandAnnotation
                {
                    Type = "freehand", Color = "#F00", Opacity = 0.8,
                    OverlappingBlocks = [],
                    NearestHeading = Heading("Discussion", 20),
                    Points = [new(100, 200), new(110, 210)],
                    StrokeWidth = 2
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("--images", md);
        Assert.Contains("*(p. 21, freehand)*", md);
    }

    [Fact]
    public void Annotations_Grouped_By_Heading_In_Outline_Order()
    {
        var export = MakeExport(
            outline: [
                new("Introduction", 0, []),
                new("Methods", 5, []),
                new("Results", 10, [])
            ],
            pages: [
                new(10, 595, 842, [
                    new TextNoteAnnotation
                    {
                        Type = "text_note", Color = "#FF0", Opacity = 0.9,
                        OverlappingBlocks = [], NearestHeading = Heading("Results", 10),
                        X = 100, Y = 100, NoteText = "Result note"
                    }
                ]),
                new(0, 595, 842, [
                    new TextNoteAnnotation
                    {
                        Type = "text_note", Color = "#FF0", Opacity = 0.9,
                        OverlappingBlocks = [], NearestHeading = Heading("Introduction", 0),
                        X = 100, Y = 100, NoteText = "Intro note"
                    }
                ])
            ]);

        var md = new MarkdownBuilder(export).Build();

        var introIdx = md.IndexOf("## Introduction");
        var resultsIdx = md.IndexOf("## Results");
        Assert.True(introIdx < resultsIdx, "Introduction should appear before Results");

        var introNoteIdx = md.IndexOf("Intro note");
        var resultNoteIdx = md.IndexOf("Result note");
        Assert.True(introNoteIdx < resultNoteIdx);
    }

    [Fact]
    public void Nested_Headings_Use_Correct_Levels()
    {
        var export = MakeExport(
            outline: [
                new("Chapter 1", 0, [
                    new("Section 1.1", 1, [
                        new("Subsection 1.1.1", 2, [])
                    ])
                ])
            ],
            pages: [
                new(0, 595, 842, [
                    new TextNoteAnnotation
                    {
                        Type = "text_note", Color = "#FF0", Opacity = 0.9,
                        OverlappingBlocks = [], NearestHeading = Heading("Chapter 1", 0),
                        X = 100, Y = 100, NoteText = "ch1 note"
                    }
                ]),
                new(1, 595, 842, [
                    new TextNoteAnnotation
                    {
                        Type = "text_note", Color = "#FF0", Opacity = 0.9,
                        OverlappingBlocks = [], NearestHeading = Heading("Section 1.1", 1),
                        X = 100, Y = 100, NoteText = "sec note"
                    }
                ]),
                new(2, 595, 842, [
                    new TextNoteAnnotation
                    {
                        Type = "text_note", Color = "#FF0", Opacity = 0.9,
                        OverlappingBlocks = [], NearestHeading = Heading("Subsection 1.1.1", 2),
                        X = 100, Y = 100, NoteText = "subsec note"
                    }
                ])
            ]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("## Chapter 1", md);
        Assert.Contains("### Section 1.1", md);
        Assert.Contains("#### Subsection 1.1.1", md);
    }

    [Fact]
    public void Summary_Table_Is_Emitted()
    {
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [], NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)], Text = "test"
                },
                new TextNoteAnnotation
                {
                    Type = "text_note", Color = "#FFCC00", Opacity = 0.9,
                    OverlappingBlocks = [], NearestHeading = Heading("Chapter 1", 0),
                    X = 100, Y = 200, NoteText = "a note"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("## Summary", md);
        Assert.Contains("**2 annotations**", md);
        Assert.Contains("**1 pages**", md);
        Assert.Contains("| Chapter 1 | 1 | 1 | 0 | 0 |", md);
    }

    [Fact]
    public void Deduplicates_Block_Text_For_Consecutive_Annotations_On_Same_Block()
    {
        var sharedBlock = new LayoutBlock("text", 22, new(50, 50, 400, 100), 0.9, 0,
            "This is the shared paragraph text that should not be repeated.");

        var export = MakeExport(
            outline: [new("Section", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [sharedBlock],
                    NearestHeading = Heading("Section", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "shared paragraph"
                },
                new TextNoteAnnotation
                {
                    Type = "text_note", Color = "#FFCC00", Opacity = 0.9,
                    OverlappingBlocks = [sharedBlock],
                    NearestHeading = Heading("Section", 0),
                    X = 450, Y = 60,
                    NoteText = "Fix this paragraph"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        // The block text should appear once (for the highlight), not twice
        // The highlight embeds the block text with "shared paragraph" bolded,
        // so look for the unique tail of the block text
        var count = CountOccurrences(md, "should not be repeated");
        Assert.Equal(1, count);
        // But the note text should still appear
        Assert.Contains("Fix this paragraph", md);
    }

    [Fact]
    public void CleanText_Removes_Soft_Hyphens_And_Control_Chars()
    {
        var input = "hyper\u00ADpara\u0002meter opti\u0003misation";
        var cleaned = MarkdownBuilder.CleanText(input);
        Assert.Equal("hyperparameter optimisation", cleaned);
    }

    [Fact]
    public void CleanText_Collapses_Whitespace()
    {
        var input = "word one  word   two\r\nword\tthree";
        var cleaned = MarkdownBuilder.CleanText(input);
        Assert.Equal("word one word two word three", cleaned);
    }

    [Fact]
    public void Enriched_Label_For_Equation_Block()
    {
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("equation", 3, new(50, 50, 400, 100), 0.9, 0,
                        "E = mc^2")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "E = mc^2"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("*(p. 1, highlighted equation)*", md);
        Assert.DoesNotContain("*(p. 1, highlight)*", md);
    }

    [Fact]
    public void Enriched_Label_Falls_Back_For_Unknown_Class()
    {
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "Normal paragraph text")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "Normal"
                }
            ])]);

        var md = new MarkdownBuilder(export).Build();

        Assert.Contains("*(p. 1, highlight)*", md);
    }

    [Fact]
    public void Highlight_Uses_Page_Context_When_Block_Text_Fails()
    {
        // Highlight text is NOT in block text but IS in page context
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "A short block text snippet.")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "distant phrase"
                }
            ])]);

        var pageContext = new Dictionary<int, string>
        {
            [0] = "Some introductory text before the distant phrase that continues after."
        };

        var md = new MarkdownBuilder(export, pageContext: pageContext).Build();

        Assert.Contains("**distant phrase**", md);
        Assert.DoesNotContain("Highlighted:", md); // no fallback
    }

    [Fact]
    public void Highlight_Block_Text_Takes_Precedence_Over_Page_Context()
    {
        // Highlight text is in both block text and page context — block text wins
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "Block text with the keyword inside it.")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "keyword"
                }
            ])]);

        var pageContext = new Dictionary<int, string>
        {
            [0] = "Full page text with keyword somewhere else entirely."
        };

        var md = new MarkdownBuilder(export, pageContext: pageContext).Build();

        // Should use block text (has "Block text with" prefix), not page context
        Assert.Contains("Block text with the **keyword** inside it.", md);
    }

    [Fact]
    public void Highlight_Page_Context_Fallback_Still_Fails_Gracefully()
    {
        // Highlight text is in neither block text nor page context
        var export = MakeExport(
            outline: [new("Chapter 1", 0, [])],
            pages: [new(0, 595, 842, [
                new HighlightAnnotation
                {
                    Type = "highlight", Color = "#FF0", Opacity = 0.5,
                    OverlappingBlocks = [new("text", 22, new(50, 50, 400, 100), 0.9, 0,
                        "Some unrelated block text.")],
                    NearestHeading = Heading("Chapter 1", 0),
                    Rects = [new(50, 50, 100, 10)],
                    Text = "completely missing text"
                }
            ])]);

        var pageContext = new Dictionary<int, string>
        {
            [0] = "Page text that also does not contain the highlight."
        };

        var md = new MarkdownBuilder(export, pageContext: pageContext).Build();

        Assert.Contains("Highlighted:", md);
    }

    [Fact]
    public void Extracted_Figures_Section_Appended()
    {
        var export = MakeExport(pages: [new(0, 595, 842, [])]);

        var figures = new List<FigureReference>
        {
            new("Bar chart showing results", "figures/fig-p5-001.png", 4),
            new("Scatter plot of correlations", "figures/fig-p12-002.png", 11)
        };

        var md = new MarkdownBuilder(export, extractedFigures: figures).Build();

        Assert.Contains("## Extracted Figures", md);
        Assert.Contains("![Bar chart showing results](figures/fig-p5-001.png)", md);
        Assert.Contains("![Scatter plot of correlations](figures/fig-p12-002.png)", md);
        Assert.Contains("*(p. 5)*", md);
        Assert.Contains("*(p. 12)*", md);
    }

    [Fact]
    public void No_Extracted_Figures_Section_When_Empty()
    {
        var export = MakeExport(pages: [new(0, 595, 842, [])]);

        var md = new MarkdownBuilder(export, extractedFigures: []).Build();

        Assert.DoesNotContain("## Extracted Figures", md);
    }

    private static int CountOccurrences(string text, string search)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }
}
