using System.Globalization;
using FocusLock.Core.Board;
using FocusLock.Core.Models;

namespace FocusLock.Core.Document;

/// <summary>The document rules that do not need an editor: seeding, keeping sections in step with the extract list, tidying runs.</summary>
public static class DocOps
{
    public const string InkOnLight = "#17181a";
    public const string InkOnDark = "#e9e9e7";

    /// <summary>
    /// Gives a document its first content the first time Document mode is used: an empty paragraph
    /// to type into, then every extract so far. A document that already has anything is left alone.
    /// </summary>
    public static void Seed(Session session)
    {
        var doc = session.Document;
        if (doc.Blocks.Count > 0) return;

        doc.Blocks.Add(new ParagraphBlock());
        foreach (var item in session.Extracts) AppendSection(doc, item.Id);
    }

    /// <summary>Adds the extract at the end, unless it is already somewhere in the document.</summary>
    public static void AppendSection(DocModel doc, string extractId)
    {
        if (doc.Blocks.Any(b => b is SectionBlock s && s.ExtractId == extractId)) return;
        doc.Blocks.Add(new SectionBlock { ExtractId = extractId });
    }

    public static void RemoveSections(DocModel doc, string extractId) =>
        doc.Blocks.RemoveAll(b => b is SectionBlock s && s.ExtractId == extractId);

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
}
