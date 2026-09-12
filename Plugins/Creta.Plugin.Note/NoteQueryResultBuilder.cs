using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note;

internal sealed class NoteQueryResultBuilder
{
    private readonly NoteRepository _repository;
    private readonly NoteResultFactory _resultFactory;
    private readonly Func<string> _getNotesCountText;
    private readonly Func<string, bool> _saveNote;
    private readonly Func<string, bool> _openEditorForNewNote;
    private readonly Func<string, bool> _updateEditingNote;
    private readonly Func<string> _getStoragePathText;
    private readonly Action _cancelEdit;
    private readonly Func<bool> _openNotesManager;
    private readonly Func<bool> _hasClipboardImage;
    private readonly Func<bool> _saveClipboardImage;
    private readonly string _actionKeyword;

    internal NoteQueryResultBuilder(
        NoteRepository repository,
        NoteResultFactory resultFactory,
        Func<string> getNotesCountText,
        Func<string, bool> saveNote,
        Func<string, bool> openEditorForNewNote,
        Func<string, bool> updateEditingNote,
        Func<string> getStoragePathText,
        Action cancelEdit,
        Func<bool> openNotesManager,
        Func<bool> hasClipboardImage,
        Func<bool> saveClipboardImage,
        string actionKeyword)
    {
        _repository = repository;
        _resultFactory = resultFactory;
        _getNotesCountText = getNotesCountText;
        _saveNote = saveNote;
        _openEditorForNewNote = openEditorForNewNote;
        _updateEditingNote = updateEditingNote;
        _getStoragePathText = getStoragePathText;
        _cancelEdit = cancelEdit;
        _openNotesManager = openNotesManager;
        _hasClipboardImage = hasClipboardImage;
        _saveClipboardImage = saveClipboardImage;
        _actionKeyword = actionKeyword;
    }

    internal List<Result> BuildHomeResults(int recentNotesLimit, int browseNotesLimit, int tagListLimit)
    {
        var stats = NoteViewStats.Create(_repository, browseNotesLimit, tagListLimit);
        var results = new List<Result>();
        if (_hasClipboardImage())
        {
            results.Add(new Result
            {
                Title = Localize.creta_plugin_note_save_clipboard_title(),
                SubTitle = Localize.creta_plugin_note_save_clipboard_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = Result.MaxScore,
                Action = _ => _saveClipboardImage()
            });
        }

        results.Add(_resultFactory.CreateSectionResult(
            Localize.creta_plugin_note_home_ready_title(),
            Localize.creta_plugin_note_home_ready_subtitle(_getNotesCountText()),
            1000));

        results.AddRange(BuildShortcutResults(stats));

        if (_repository.Notes.Count == 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_empty_view_title(),
                Localize.creta_plugin_note_empty_view_subtitle(),
                985));
            return results;
        }

        AddSection(results, Localize.creta_plugin_note_pinned_section_title(),
            Localize.creta_plugin_note_pinned_section_subtitle(), 980, _repository.GetPinnedNotes(recentNotesLimit));
        AddSection(results, Localize.creta_plugin_note_today_section_title(),
            Localize.creta_plugin_note_today_section_subtitle(), 960, _repository.GetNotesCreatedOn(DateTime.Now, recentNotesLimit));
        AddSection(results, Localize.creta_plugin_note_week_section_title(),
            Localize.creta_plugin_note_week_section_subtitle(), 950, _repository.GetNotesCreatedThisWeek(recentNotesLimit));
        AddSection(results, Localize.creta_plugin_note_recent_section_title(),
            Localize.creta_plugin_note_recent_section_subtitle(), 940, _repository.GetRecentNotes(recentNotesLimit));

        if (stats.TopTags.Count > 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_tags_section_title(),
                Localize.creta_plugin_note_tags_section_subtitle(),
                935));
            results.AddRange(stats.TopTags.Select(_resultFactory.CreateTagJumpResult));
        }

        results.Add(_resultFactory.CreateSectionResult(
            Localize.creta_plugin_note_home_browse_title(),
            Localize.creta_plugin_note_home_browse_subtitle(),
            930));

        return results;
    }

    internal List<Result> BuildEditingResults(string trimmedContent)
    {
        return
        [
            new Result
            {
                Title = Localize.creta_plugin_note_update_title(trimmedContent),
                SubTitle = Localize.creta_plugin_note_update_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = Result.MaxScore,
                Action = _ => _updateEditingNote(trimmedContent)
            },
            new Result
            {
                Title = Localize.creta_plugin_note_cancel_edit_title(),
                SubTitle = Localize.creta_plugin_note_cancel_edit_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = 950,
                Action = _ =>
                {
                    _cancelEdit();
                    Main.Context.API.ChangeQuery(_actionKeyword, true);
                    return false;
                }
            }
        ];
    }

    internal List<Result> BuildSearchAndSaveResults(string trimmedContent, IReadOnlyList<NoteSearchMatch> matches)
    {
        var similarMatches = matches.Where(match => match.IsHighSimilarity).ToList();
        var otherMatches = matches.Where(match => !match.IsHighSimilarity).ToList();

        var results = new List<Result>
        {
            new()
            {
                Title = Localize.creta_plugin_note_save_title(trimmedContent),
                SubTitle = similarMatches.Count > 0
                    ? Localize.creta_plugin_note_save_with_similar_subtitle()
                    : Localize.creta_plugin_note_save_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = Result.MaxScore,
                Action = _ => _saveNote(trimmedContent)
            },
            new()
            {
                Title = Localize.creta_plugin_note_editor_open_title(),
                SubTitle = Localize.creta_plugin_note_editor_open_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = Result.MaxScore - 1,
                Action = _ => _openEditorForNewNote(trimmedContent)
            }
        };

        if (similarMatches.Count > 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_search_similar_title(),
                Localize.creta_plugin_note_search_similar_subtitle(trimmedContent),
                970));
            results.AddRange(similarMatches.Select(_resultFactory.CreateSearchNoteResult));
        }

        if (otherMatches.Count > 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_search_matches_title(),
                Localize.creta_plugin_note_search_matches_subtitle(trimmedContent),
                960));
            results.AddRange(otherMatches.Select(_resultFactory.CreateSearchNoteResult));
        }

        if (matches.Count == 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_search_no_matches_title(),
                Localize.creta_plugin_note_search_no_matches_subtitle(),
                970));
        }

        results.Add(new Result
        {
            Title = Localize.creta_plugin_note_storage_ready_title(),
            SubTitle = $"{Localize.creta_plugin_note_storage_ready_subtitle(_getNotesCountText())} {_getStoragePathText()}",
            IcoPath = Main.IcoPathValue,
            Score = 900,
            Action = _ => false
        });

        return results;
    }

    internal List<Result> BuildNoteViewResults(string title, string subtitle, IReadOnlyList<NoteItem> notes, string currentView, int browseNotesLimit)
    {
        var results = new List<Result>
        {
            _resultFactory.CreateSectionResult(title, subtitle, 1000)
        };

        results.AddRange(BuildViewNavigationResults(currentView, browseNotesLimit));

        if (notes.Count == 0)
        {
            results.Add(_resultFactory.CreateSectionResult(
                Localize.creta_plugin_note_empty_view_title(),
                Localize.creta_plugin_note_empty_view_subtitle(),
                990));
            return results;
        }

        results.AddRange(notes.Select(_resultFactory.CreateRecentNoteResult));
        return results;
    }

    private void AddSection(List<Result> results, string title, string subtitle, int score, IReadOnlyList<NoteItem> notes)
    {
        if (notes.Count == 0)
        {
            return;
        }

        results.Add(_resultFactory.CreateSectionResult(title, subtitle, score));
        results.AddRange(notes.Select(_resultFactory.CreateRecentNoteResult));
    }

    private List<Result> BuildShortcutResults(NoteViewStats stats)
    {
        return
        [
            new Result
            {
                Title = Localize.creta_plugin_note_shortcut_editor_title(),
                SubTitle = Localize.creta_plugin_note_shortcut_editor_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = 992,
                Action = _ => _openEditorForNewNote(string.Empty)
            },
            new Result
            {
                Title = Localize.creta_plugin_note_shortcut_manager_title(),
                SubTitle = Localize.creta_plugin_note_shortcut_manager_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = 991,
                Action = _ => _openNotesManager()
            },
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_all_title(),
                Localize.creta_plugin_note_shortcut_all_subtitle(stats.TotalNotesCount),
                NoteSpecialViewKeywords.All,
                990),
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_pinned_title(),
                Localize.creta_plugin_note_shortcut_pinned_subtitle(stats.PinnedNotesCount),
                NoteSpecialViewKeywords.Pinned,
                989),
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_today_title(),
                Localize.creta_plugin_note_shortcut_today_subtitle(stats.TodayNotesCount),
                NoteSpecialViewKeywords.Today,
                988),
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_week_title(),
                Localize.creta_plugin_note_shortcut_week_subtitle(stats.WeekNotesCount),
                NoteSpecialViewKeywords.Week,
                987),
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_recent_title(),
                Localize.creta_plugin_note_shortcut_recent_subtitle(stats.RecentNotesCount),
                NoteSpecialViewKeywords.Recent,
                986),
            _resultFactory.CreateViewJumpResult(
                Localize.creta_plugin_note_shortcut_archived_title(),
                Localize.creta_plugin_note_shortcut_archived_subtitle(stats.ArchivedNotesCount),
                NoteSpecialViewKeywords.Archived,
                985)
        ];
    }

    private List<Result> BuildViewNavigationResults(string currentView, int browseNotesLimit)
    {
        var results = new List<Result>
        {
            new()
            {
                Title = Localize.creta_plugin_note_navigation_home_title(),
                SubTitle = Localize.creta_plugin_note_navigation_home_subtitle(),
                IcoPath = Main.IcoPathValue,
                Score = 995,
                Action = _ =>
                {
                    Main.Context.API.ChangeQuery(_actionKeyword, true);
                    return false;
                }
            }
        };

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsAll,
            Localize.creta_plugin_note_shortcut_all_title(),
            Localize.creta_plugin_note_shortcut_all_subtitle(_repository.GetAllNotes(browseNotesLimit).Count),
            NoteSpecialViewKeywords.All,
            994);

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsPinned,
            Localize.creta_plugin_note_shortcut_pinned_title(),
            Localize.creta_plugin_note_shortcut_pinned_subtitle(_repository.GetPinnedNotes(browseNotesLimit).Count),
            NoteSpecialViewKeywords.Pinned,
            993);

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsToday,
            Localize.creta_plugin_note_shortcut_today_title(),
            Localize.creta_plugin_note_shortcut_today_subtitle(_repository.GetNotesCreatedOn(DateTime.Now, browseNotesLimit).Count),
            NoteSpecialViewKeywords.Today,
            992);

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsWeek,
            Localize.creta_plugin_note_shortcut_week_title(),
            Localize.creta_plugin_note_shortcut_week_subtitle(_repository.GetNotesCreatedThisWeek(browseNotesLimit).Count),
            NoteSpecialViewKeywords.Week,
            991);

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsRecent,
            Localize.creta_plugin_note_shortcut_recent_title(),
            Localize.creta_plugin_note_shortcut_recent_subtitle(_repository.GetRecentNotes(browseNotesLimit).Count),
            NoteSpecialViewKeywords.Recent,
            990);

        AddNavigationIfNeeded(results, currentView, NoteSpecialViewKeywords.IsArchived,
            Localize.creta_plugin_note_shortcut_archived_title(),
            Localize.creta_plugin_note_shortcut_archived_subtitle(_repository.GetArchivedNotes(browseNotesLimit).Count),
            NoteSpecialViewKeywords.Archived,
            989);

        return results;
    }

    private void AddNavigationIfNeeded(
        List<Result> results,
        string currentView,
        Func<string, bool> matcher,
        string title,
        string subtitle,
        string viewKeyword,
        int score)
    {
        if (!matcher(currentView))
        {
            results.Add(_resultFactory.CreateViewJumpResult(title, subtitle, viewKeyword, score));
        }
    }
}
