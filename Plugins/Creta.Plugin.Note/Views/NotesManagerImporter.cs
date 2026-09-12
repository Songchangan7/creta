using System;
using System.IO;
using System.Text;
using System.Windows;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

internal static class NotesManagerImporter
{
    internal static bool TryImport(NoteRepository repository)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localize.creta_plugin_note_settings_notes_import_dialog_title(),
            Filter = Localize.creta_plugin_note_settings_notes_import_filter(),
            Multiselect = false
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return false;
        }

        var extension = Path.GetExtension(dialog.FileName);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return TryImportZip(repository, dialog.FileName);
        }

        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return TryImportJson(repository, dialog.FileName);
        }

        return TryImportTextFile(repository, dialog.FileName);
    }

    private static bool TryImportZip(NoteRepository repository, string filePath)
    {
        var result = Main.Context.API.ShowMsgBox(
            Localize.creta_plugin_note_settings_notes_import_zip_confirm_message(),
            Localize.creta_plugin_note_settings_notes_import_zip_confirm_caption(),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        var importResult = repository.ImportFullBackup(filePath);
        return ShowImportResult(importResult, isJsonImport: true);
    }

    private static bool TryImportJson(NoteRepository repository, string filePath)
    {
        var result = Main.Context.API.ShowMsgBox(
            Localize.creta_plugin_note_settings_notes_import_json_confirm_message(),
            Localize.creta_plugin_note_settings_notes_import_json_confirm_caption(),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        var importResult = repository.ImportJsonNotes(filePath);
        return ShowImportResult(importResult, isJsonImport: true);
    }

    private static bool TryImportTextFile(NoteRepository repository, string filePath)
    {
        string content;
        try
        {
            content = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_import_failed_title(),
                ex.Message);
            return false;
        }

        var isMarkdown = string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase);
        var suggestedSplitMode = NoteTextImportParser.SuggestSplitMode(content, isMarkdown);
        var optionsWindow = new NoteImportOptionsWindow(suggestedSplitMode, isMarkdown);
        if (optionsWindow.ShowDialog() != true)
        {
            return false;
        }

        var chunks = NoteTextImportParser.Split(content, optionsWindow.SplitMode);
        var importResult = repository.ImportTextNotes(chunks, optionsWindow.SkipDuplicates);
        return ShowImportResult(importResult, isJsonImport: false);
    }

    private static bool ShowImportResult(NoteImportResult importResult, bool isJsonImport)
    {
        if (!importResult.Succeeded)
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_import_failed_title(),
                importResult.ErrorMessage);
            return false;
        }

        var subtitle = isJsonImport
            ? Localize.creta_plugin_note_settings_notes_import_json_success_subtitle(
                importResult.ImportedCount,
                importResult.UpdatedCount)
            : Localize.creta_plugin_note_settings_notes_import_text_success_subtitle(
                importResult.ImportedCount,
                importResult.SkippedDuplicateCount,
                importResult.SkippedEmptyCount);

        if (importResult.ImportedCount == 0 &&
            importResult.UpdatedCount == 0 &&
            importResult.SkippedDuplicateCount == 0 &&
            importResult.SkippedEmptyCount > 0)
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_import_failed_title(),
                Localize.creta_plugin_note_settings_notes_import_empty_subtitle());
            return false;
        }

        Main.Context.API.ShowMsg(
            Localize.creta_plugin_note_settings_notes_import_success_title(),
            subtitle);
        return importResult.ImportedCount > 0 || importResult.UpdatedCount > 0;
    }
}
