using System;
using System.IO;
using System.Linq;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note;

internal static class NotePresentation
{
    internal static string BuildSavedSubtitle(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        const int maxLength = 60;
        return content.Length <= maxLength ? content : $"{content[..maxLength]}...";
    }

    internal static string BuildNoteDisplayTitle(NoteItem note)
    {
        if (note is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(note.Content))
        {
            return BuildSavedSubtitle(note.Content);
        }

        return note.HasAttachments
            ? Localize.creta_plugin_note_image_note_title(note.Attachments.Count)
            : string.Empty;
    }

    internal static string BuildTagText(NoteItem note)
    {
        return note.Tags.Count == 0
            ? "-"
            : string.Join(", ", note.Tags.Select(tag => $"#{tag}"));
    }

    internal static Result.PreviewInfo BuildPreviewInfo(NoteItem note)
    {
        var created = note.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var updated = note.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var status = note.IsArchived
            ? Localize.creta_plugin_note_preview_archived_label()
            : note.IsPinned
                ? Localize.creta_plugin_note_preview_pinned_label()
                : Localize.creta_plugin_note_preview_recent_label();

        var preview = new Result.PreviewInfo
        {
            Description =
                $"{BuildNoteDisplayTitle(note)}\n\n" +
                $"{status}\n" +
                $"{Localize.creta_plugin_note_preview_created_label()} {created}\n" +
                $"{Localize.creta_plugin_note_preview_updated_label()} {updated}\n" +
                $"{Localize.creta_plugin_note_preview_tags_label()} {BuildTagText(note)}"
        };

        if (note.HasAttachments && Main.ActiveRepository is not null)
        {
            var imagePath = Main.ActiveRepository.GetAttachmentFullPath(note.Attachments[0]);
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                preview.PreviewImagePath = imagePath;
                preview.IsMedia = true;
            }
        }

        return preview;
    }

    internal static string BuildNoteTitleToolTip(NoteItem note)
    {
        var created = note.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var updated = note.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var status = note.IsArchived
            ? Localize.creta_plugin_note_preview_archived_label()
            : note.IsPinned
                ? Localize.creta_plugin_note_preview_pinned_label()
                : Localize.creta_plugin_note_preview_recent_label();

        return
            $"{status}\n" +
            $"{Localize.creta_plugin_note_preview_created_label()} {created}\n" +
            $"{Localize.creta_plugin_note_preview_updated_label()} {updated}\n" +
            $"{Localize.creta_plugin_note_preview_tags_label()} {BuildTagText(note)}";
    }

    internal static string BuildSearchResultSubtitle(NoteSearchMatch match)
    {
        var updatedLabel = match.Note.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        if (match.IsExactMatch)
        {
            return Localize.creta_plugin_note_search_exact_subtitle(updatedLabel);
        }

        if (match.IsHighSimilarity)
        {
            return Localize.creta_plugin_note_search_similar_result_subtitle(updatedLabel);
        }

        return match.Note.IsPinned
            ? Localize.creta_plugin_note_pinned_note_subtitle(updatedLabel)
            : Localize.creta_plugin_note_recent_note_subtitle(updatedLabel);
    }
}
