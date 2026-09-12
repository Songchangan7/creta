using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

internal static class NotesManagerExporter
{
    internal static string BuildTextExport(IReadOnlyList<NoteItem> notes)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < notes.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
                builder.AppendLine(new string('-', 40));
                builder.AppendLine();
            }

            AppendNoteText(builder, notes[index]);
        }

        return builder.ToString();
    }

    internal static string BuildMarkdownExport(IReadOnlyList<NoteItem> notes)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < notes.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
            }

            AppendNoteMarkdown(builder, notes[index], index + 1);
        }

        return builder.ToString();
    }

    internal static bool TryExportNotes(
        IReadOnlyList<NoteItem> notes,
        string dialogTitle,
        string defaultFileName,
        string filter,
        Func<IReadOnlyList<NoteItem>, string> buildContent)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = dialogTitle,
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return false;
        }

        try
        {
            File.WriteAllText(dialog.FileName, buildContent(notes), Encoding.UTF8);
            Main.Context.API.ShowMsg(
                Localize.creta_plugin_note_settings_notes_export_success_title(),
                Localize.creta_plugin_note_settings_notes_export_success_subtitle(notes.Count, dialog.FileName));
            return true;
        }
        catch (Exception ex)
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_export_failed_title(),
                ex.Message);
            return false;
        }
    }

    internal static bool TryExportJsonBackup(string sourceFilePath, bool hasAttachments = false)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_export_json_missing_title(),
                Localize.creta_plugin_note_settings_notes_export_json_missing_subtitle(sourceFilePath ?? string.Empty));
            return false;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localize.creta_plugin_note_settings_notes_export_dialog_title_json(),
            Filter = Localize.creta_plugin_note_settings_notes_export_filter_json(),
            FileName = $"notes-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = string.IsNullOrWhiteSpace(sourceDirectory) ? null : sourceDirectory
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return false;
        }

        try
        {
            File.Copy(sourceFilePath, dialog.FileName, overwrite: true);
            Main.Context.API.ShowMsg(
                Localize.creta_plugin_note_settings_notes_export_json_success_title(),
                hasAttachments
                    ? Localize.creta_plugin_note_settings_notes_export_json_success_subtitle_with_attachments(dialog.FileName)
                    : Localize.creta_plugin_note_settings_notes_export_json_success_subtitle(dialog.FileName));
            return true;
        }
        catch (Exception ex)
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_export_failed_title(),
                ex.Message);
            return false;
        }
    }

    internal static bool TryExportFullBackup(NoteRepository repository)
    {
        if (repository is null || !File.Exists(repository.NotesFilePath))
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_export_json_missing_title(),
                Localize.creta_plugin_note_settings_notes_export_json_missing_subtitle(repository?.NotesFilePath ?? string.Empty));
            return false;
        }

        var sourceDirectory = Path.GetDirectoryName(repository.NotesFilePath);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localize.creta_plugin_note_settings_notes_export_dialog_title_zip(),
            Filter = Localize.creta_plugin_note_settings_notes_export_filter_zip(),
            FileName = $"notes-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = string.IsNullOrWhiteSpace(sourceDirectory) ? null : sourceDirectory
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return false;
        }

        if (repository.ExportFullBackup(dialog.FileName, out var errorMessage))
        {
            Main.Context.API.ShowMsg(
                Localize.creta_plugin_note_settings_notes_export_zip_success_title(),
                Localize.creta_plugin_note_settings_notes_export_zip_success_subtitle(dialog.FileName));
            return true;
        }

        Main.Context.API.ShowMsgError(
            Localize.creta_plugin_note_settings_notes_export_failed_title(),
            errorMessage);
        return false;
    }

    private static void AppendNoteText(StringBuilder builder, NoteItem note)
    {
        builder.AppendLine($"{Localize.creta_plugin_note_preview_created_label()} {FormatDateTime(note.CreatedAt)}");
        builder.AppendLine($"{Localize.creta_plugin_note_preview_updated_label()} {FormatDateTime(note.UpdatedAt)}");
        builder.AppendLine($"{Localize.creta_plugin_note_preview_tags_label()} {NotePresentation.BuildTagText(note)}");
        builder.AppendLine(note.Content);
        AppendAttachmentLines(builder, note, markdown: false);
    }

    private static void AppendNoteMarkdown(StringBuilder builder, NoteItem note, int index)
    {
        var title = NotePresentation.BuildNoteDisplayTitle(note);
        builder.AppendLine($"## {index}. {EscapeMarkdown(title)}");
        builder.AppendLine();
        builder.AppendLine($"- **{Localize.creta_plugin_note_preview_created_label()}** {FormatDateTime(note.CreatedAt)}");
        builder.AppendLine($"- **{Localize.creta_plugin_note_preview_updated_label()}** {FormatDateTime(note.UpdatedAt)}");
        builder.AppendLine($"- **{Localize.creta_plugin_note_preview_tags_label()}** {NotePresentation.BuildTagText(note)}");
        builder.AppendLine();
        builder.AppendLine(note.Content);
        AppendAttachmentLines(builder, note, markdown: true);
    }

    private static void AppendAttachmentLines(StringBuilder builder, NoteItem note, bool markdown)
    {
        if (!note.HasAttachments)
        {
            return;
        }

        builder.AppendLine();
        foreach (var attachment in note.Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.RelativePath))
            {
                continue;
            }

            if (markdown)
            {
                builder.AppendLine($"![{EscapeMarkdown(attachment.FileName)}]({attachment.RelativePath})");
            }
            else
            {
                builder.AppendLine(attachment.RelativePath);
            }
        }
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string EscapeMarkdown(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
    }
}
