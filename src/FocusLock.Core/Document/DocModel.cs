using System.Text.Json.Serialization;

namespace FocusLock.Core.Document;

public static class DocStyle
{
    public const string Normal = "normal";
    public const string Title = "title";
    public const string H1 = "h1";
    public const string H2 = "h2";
}

public static class DocAlign
{
    public const string Left = "left";
    public const string Center = "center";
    public const string Right = "right";
}

public static class DocList
{
    public const string None = "none";
    public const string Bullet = "bullet";
    public const string Number = "number";
    public const string Check = "check";
}

public static class CalloutTone
{
    public const string Note = "note";
    public const string Important = "important";
    public const string Tip = "tip";
}

public static class SectionSize
{
    public const string Small = "small";
    public const string Medium = "medium";
    public const string Full = "full";
}

/// <summary>
/// The shape of a document saved by the separate Document mode that briefly existed. Sessions are
/// only read in this shape, so its text can move into a text box.
/// </summary>
public sealed class DocModel
{
    public List<DocBlock> Blocks { get; set; } = [];
    public bool ShowHeader { get; set; }
    public string HeaderText { get; set; } = "";
    public bool ShowPageNumbers { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(CalloutBlock), "callout")]
[JsonDerivedType(typeof(DividerBlock), "divider")]
[JsonDerivedType(typeof(PageBreakBlock), "pageBreak")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(SectionBlock), "section")]
public abstract class DocBlock;

/// <summary>A line of text. List items are paragraphs too; neighbours of the same list kind form one list.</summary>
public sealed class ParagraphBlock : DocBlock
{
    public string Style { get; set; } = DocStyle.Normal;
    public string Align { get; set; } = DocAlign.Left;
    public string List { get; set; } = DocList.None;

    /// <summary>Only meaningful for a checklist item.</summary>
    public bool Checked { get; set; }

    public List<DocRun> Runs { get; set; } = [];
}

/// <summary>A stretch of text with one format.</summary>
public sealed class DocRun
{
    public string Text { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Color { get; set; }
    public string? Highlight { get; set; }

    /// <summary>Points; null means the paragraph style's size.</summary>
    public double? Size { get; set; }
}

public sealed class CalloutBlock : DocBlock
{
    public string Tone { get; set; } = CalloutTone.Note;
    public List<ParagraphBlock> Paragraphs { get; set; } = [];
}

public sealed class DividerBlock : DocBlock;

public sealed class PageBreakBlock : DocBlock;

/// <summary>A grid of cells; the first row is drawn as a header.</summary>
public sealed class TableBlock : DocBlock
{
    public List<List<TableCell>> Rows { get; set; } = [];
}

public sealed class TableCell
{
    public List<ParagraphBlock> Paragraphs { get; set; } = [];
}

/// <summary>An extract's picture, drawn from its canvas when the document is shown or exported.</summary>
public sealed class SectionBlock : DocBlock
{
    public string ExtractId { get; set; } = "";
    public string Size { get; set; } = SectionSize.Full;
    public string Align { get; set; } = DocAlign.Center;
    public bool Caption { get; set; } = true;
}
