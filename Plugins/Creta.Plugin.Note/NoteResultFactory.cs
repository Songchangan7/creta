using System;
using System.Collections.Generic;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note;

internal sealed class NoteResultFactory
{
    private readonly string _actionKeyword;
    private readonly Func<NoteItem, bool> _activateNote;

    internal NoteResultFactory(string actionKeyword, Func<NoteItem, bool> activateNote)
    {
        _actionKeyword = actionKeyword;
        _activateNote = activateNote;
    }

    internal Result CreateSectionResult(string title, string subtitle, int score)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = Main.IcoPathValue,
            Score = score,
            Action = _ => false
        };
    }

    internal Result CreateViewJumpResult(string title, string subtitle, string viewKeyword, int score)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = Main.IcoPathValue,
            Score = score,
            Action = _ =>
            {
                Main.Context.API.ChangeQuery($"{_actionKeyword} {viewKeyword}", true);
                return false;
            }
        };
    }

    internal Result CreateTagJumpResult(KeyValuePair<string, int> tag)
    {
        return new Result
        {
            Title = $"#{tag.Key}",
            SubTitle = Localize.creta_plugin_note_tag_jump_subtitle(tag.Key, tag.Value),
            IcoPath = Main.IcoPathValue,
            Score = 934,
            Action = _ =>
            {
                Main.Context.API.ChangeQuery($"{_actionKeyword} tag {tag.Key}", true);
                return false;
            }
        };
    }

    internal Result CreateRecentNoteResult(NoteItem note)
    {
        var updatedLabel = note.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        return new Result
        {
            Title = NotePresentation.BuildNoteDisplayTitle(note),
            SubTitle = note.IsArchived
                ? Localize.creta_plugin_note_archived_note_subtitle(updatedLabel)
                : note.IsPinned
                    ? Localize.creta_plugin_note_pinned_note_subtitle(updatedLabel)
                    : Localize.creta_plugin_note_recent_note_subtitle(updatedLabel),
            IcoPath = Main.IcoPathValue,
            Score = note.IsArchived ? 820 : note.IsPinned ? 950 : 850,
            CopyText = note.Content,
            AutoCompleteText = string.IsNullOrWhiteSpace(note.Content)
                ? _actionKeyword
                : $"{_actionKeyword} {note.Content}",
            TitleToolTip = NotePresentation.BuildNoteTitleToolTip(note),
            SubTitleToolTip = NotePresentation.BuildNoteDisplayTitle(note),
            Preview = NotePresentation.BuildPreviewInfo(note),
            ContextData = note,
            Action = _ => _activateNote(note)
        };
    }

    internal Result CreateSearchNoteResult(NoteSearchMatch match)
    {
        return new Result
        {
            Title = NotePresentation.BuildNoteDisplayTitle(match.Note),
            SubTitle = NotePresentation.BuildSearchResultSubtitle(match),
            IcoPath = Main.IcoPathValue,
            Score = match.Score,
            CopyText = match.Note.Content,
            AutoCompleteText = string.IsNullOrWhiteSpace(match.Note.Content)
                ? _actionKeyword
                : $"{_actionKeyword} {match.Note.Content}",
            TitleHighlightData = match.HighlightData,
            TitleToolTip = NotePresentation.BuildNoteTitleToolTip(match.Note),
            SubTitleToolTip = NotePresentation.BuildNoteDisplayTitle(match.Note),
            Preview = NotePresentation.BuildPreviewInfo(match.Note),
            ContextData = match.Note,
            Action = _ => _activateNote(match.Note)
        };
    }
}
