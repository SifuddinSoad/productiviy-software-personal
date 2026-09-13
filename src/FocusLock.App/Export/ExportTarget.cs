using System.IO;
using FocusLock.Core.Models;
using Microsoft.Win32;

namespace FocusLock.App.Export;

/// <summary>Where an exported PDF goes.</summary>
public static class ExportTarget
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Focus Mood");

    public static string SuggestedName(Session session) =>
        $"{Sanitise(session.Name)} {DateTime.Now:yyyy-MM-dd HHmm}.pdf";

    /// <summary>
    /// During a locked session the file goes straight to a known folder with no dialog: Windows'
    /// save dialog can browse the machine and even launch things, which would be a way around the
    /// lock. Unlocked, the usual dialog is fine.
    /// </summary>
    public static string? Choose(Session session, bool locked)
    {
        var name = SuggestedName(session);
        if (locked) return Path.Combine(Folder, name);

        var dialog = new SaveFileDialog
        {
            FileName = name,
            DefaultExt = ".pdf",
            Filter = "PDF document (*.pdf)|*.pdf",
            InitialDirectory = Directory.Exists(Folder) ? Folder : null,
            AddExtension = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    static string Sanitise(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Session" : cleaned;
    }
}
