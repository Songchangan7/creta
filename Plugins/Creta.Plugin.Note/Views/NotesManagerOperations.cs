using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

internal static class NotesManagerOperations
{
    internal static bool TryEditNote(NoteRepository repository, NoteItem note)
    {
        repository.RecordLastViewed(note.Id, out _);
        var window = new NoteEditorWindow(
            Localize.creta_plugin_note_editor_edit_title(),
            Localize.creta_plugin_note_editor_edit_subtitle(),
            Localize.creta_plugin_note_editor_save_edit(),
            NoteRepository.BuildEditableContent(note),
            note.Attachments,
            repository.NotesDirectoryPath);

        if (window.ShowDialog() != true)
        {
            return false;
        }

        if (repository.UpdateNoteWithAttachments(
            note.Id,
            window.EditedContent,
            window.PendingImages,
            window.RemovedAttachmentIds,
            out _,
            out var errorMessage))
        {
            return true;
        }

        ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    internal static bool TryDeleteNote(NoteRepository repository, NoteItem note)
    {
        return TryDeleteNotes(repository, [note]);
    }

    internal static bool TryDeleteNotes(NoteRepository repository, IReadOnlyList<NoteItem> notes)
    {
        if (notes is null || notes.Count == 0)
        {
            return false;
        }

        var message = notes.Count == 1
            ? Localize.creta_plugin_note_delete_confirm_message(
                Environment.NewLine,
                NotePresentation.BuildNoteDisplayTitle(notes[0]))
            : Localize.creta_plugin_note_settings_notes_batch_delete_confirm_message(notes.Count);
        var result = Main.Context.API.ShowMsgBox(
            message,
            Localize.creta_plugin_note_delete_confirm_caption(),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var note in notes)
        {
            if (!repository.DeleteNote(note.Id, out var errorMessage))
            {
                ShowOperationError(Localize.creta_plugin_note_error_delete_title(), errorMessage);
                return false;
            }
        }

        return true;
    }

    internal static bool TrySetPinned(NoteRepository repository, NoteItem note, bool isPinned)
    {
        if (repository.SetPinned(note.Id, isPinned, out _, out var errorMessage))
        {
            return true;
        }

        ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    internal static bool TrySetArchived(NoteRepository repository, NoteItem note, bool isArchived)
    {
        if (repository.SetArchived(note.Id, isArchived, out _, out var errorMessage))
        {
            return true;
        }

        ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    internal static bool TryArchiveNotes(NoteRepository repository, IReadOnlyList<NoteItem> notes)
    {
        if (notes is null || notes.Count == 0)
        {
            return false;
        }

        if (notes.Count > 1)
        {
            var result = Main.Context.API.ShowMsgBox(
                Localize.creta_plugin_note_settings_notes_batch_archive_confirm_message(notes.Count),
                Localize.creta_plugin_note_delete_confirm_caption(),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        foreach (var note in notes)
        {
            if (!repository.SetArchived(note.Id, true, out _, out var errorMessage))
            {
                ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
                return false;
            }
        }

        return true;
    }

    internal static bool TryEditTags(NoteRepository repository, NoteItem note)
    {
        var window = new NoteTagsEditorWindow(NotesManagerTagParser.Format(note.Tags));
        if (window.ShowDialog() != true)
        {
            return false;
        }

        if (repository.SetTags(note.Id, window.EditedTags, out _, out var errorMessage))
        {
            return true;
        }

        ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    internal static bool TryBatchAddTags(NoteRepository repository, IReadOnlyList<NoteItem> notes)
    {
        if (notes is null || notes.Count == 0)
        {
            return false;
        }

        var window = new NoteTagsEditorWindow(
            string.Empty,
            Localize.creta_plugin_note_settings_notes_batch_tags_editor_title(),
            Localize.creta_plugin_note_settings_notes_batch_tags_editor_hint(notes.Count),
            Localize.creta_plugin_note_settings_notes_batch_tags_editor_save());
        if (window.ShowDialog() != true)
        {
            return false;
        }

        var tagsToAdd = window.EditedTags;
        if (tagsToAdd.Count == 0)
        {
            return false;
        }

        foreach (var note in notes)
        {
            var mergedTags = NotesManagerTagParser.Merge(note.Tags, tagsToAdd);
            if (!repository.SetTags(note.Id, mergedTags, out _, out var errorMessage))
            {
                ShowOperationError(Localize.creta_plugin_note_error_update_title(), errorMessage);
                return false;
            }
        }

        return true;
    }

    internal static void TryOpenNotesFileFolder(NoteRepository repository)
    {
        try
        {
            var notesFilePath = repository.NotesFilePath;
            var directoryPath = Path.GetDirectoryName(notesFilePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new InvalidOperationException("Notes directory path is empty.");
            }

            Main.Context.API.OpenDirectory(directoryPath, notesFilePath);
        }
        catch (Exception ex)
        {
            ShowOperationError(
                Localize.creta_plugin_note_settings_notes_open_folder_failed_title(),
                ex.Message);
        }
    }

    private static void ShowOperationError(string title, string message)
    {
        Main.Context.API.ShowMsgError(title, message);
    }
}
