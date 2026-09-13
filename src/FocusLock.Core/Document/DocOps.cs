using System.Globalization;
using FocusLock.Core.Board;

namespace FocusLock.Core.Document;

/// <summary>Rules for formatted text that do not need an editor.</summary>
public static class DocOps
{
    public const string InkOnLight = "#17181a";
    public const string InkOnDark = "#e9e9e7";

    static bool SameFormat(DocRun a, DocRun b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline &&
        a.Color == b.Color && a.Highlight == b.Highlight && a.Size == b.Size;

    /// <summary>Joins neighbouring runs that look the same and drops empty ones, so an edited paragraph stays small.</summary>
    public static List<DocRun> NormaliseRuns(IEnumerable<DocRun> runs)
    {
        var result = new List<DocRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            if (result.Count > 0 && SameFormat(result[^1], run))
            {
                result[^1].Text += run.Text;
                continue;
            }
            result.Add(new DocRun
            {
                Text = run.Text, Bold = run.Bold, Italic = run.Italic, Underline = run.Underline,
                Color = run.Color, Highlight = run.Highlight, Size = run.Size,
            });
        }
        return result;
    }

    public static double SectionWidth(string size, double textWidth) => size switch
    {
        SectionSize.Small => textWidth * 0.40,
        SectionSize.Medium => textWidth * 0.65,
        _ => textWidth,
    };

    /// <summary>Colour for text that has none of its own on this paper.</summary>
    public static string InkFor(string paper)
    {
        var hex = paper.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return InkOnLight;
        return Palette.IsLight(paper) ? InkOnLight : InkOnDark;
    }

    /// <summary>True when the blocks hold nothing a reader would see.</summary>
    public static bool IsBlank(IEnumerable<DocBlock> blocks) => blocks.All(b => b switch
    {
        ParagraphBlock p => p.List == DocList.None && p.Runs.All(r => string.IsNullOrWhiteSpace(r.Text)),
        SectionBlock => true,
        _ => false,
    });
}
