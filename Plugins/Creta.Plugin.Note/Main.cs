using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using Flow.Launcher.Plugin;
using Creta.Plugin.Note.Views;

namespace Creta.Plugin.Note;

public class Main : IPlugin, IPluginI18n, IContextMenu, ISettingProvider
{
    internal const string IcoPathValue = "Images/app.png";
    private const int RecentNotesLimit = 8;
    private const int BrowseNotesLimit = 20;
    private const int TagListLimit = 8;

    internal static PluginInitContext Context { get; private set; } = null!;

    internal static NoteRepository ActiveRepository { get; private set; } = null!;

    private NoteRepository _repository = null!;
    private NoteResultFactory _resultFactory = null!;
    private NoteContextMenuBuilder _contextMenuBuilder = null!;
    private NoteQueryResultBuilder _queryResultBuilder = null!;
    private Settings _settings = null!;
    private string _editingNoteId = string.Empty;
    private SettingsControl _settingsControl;
    private bool _selectNotesManagerOnSettingsOpen;

    public void Init(PluginInitContext context)
    {
        Context = context;
        _settings = context.API.LoadSettingJsonStorage<Settings>();
        _repository = new NoteRepository(
            context.CurrentPluginMetadata.PluginDirectory,
            context.CurrentPluginMetadata.PluginSettingsDirectoryPath,
            _settings.NotesFilePath);
        ActiveRepository = _repository;
        _repository.Load();
        _resultFactory = new NoteResultFactory(context.CurrentPluginMetadata.ActionKeyword, ActivateNote);
        _contextMenuBuilder = new NoteContextMenuBuilder();
        _queryResultBuilder = new NoteQueryResultBuilder(
            _repository,
            _resultFactory,
            GetNotesCountText,
            content => SaveNote(content),
            OpenEditorForNewNote,
            UpdateEditingNote,
            GetStoragePathText,
            ClearEditingState,
            OpenNotesManager,
            NoteClipboardImages.HasImage,
            SaveClipboardImage,
            context.CurrentPluginMetadata.ActionKeyword);
    }

    public Control CreateSettingPanel()
    {
        _settingsControl = new SettingsControl(_settings, _repository, ApplyStoragePathChange, GetStoragePathText);
        if (_selectNotesManagerOnSettingsOpen)
        {
            _settingsControl.SelectNotesManagerTab();
            _selectNotesManagerOnSettingsOpen = false;
        }

        return _settingsControl;
    }

    public List<Result> Query(Query query)
    {
        if (!string.IsNullOrWhiteSpace(_repository.LoadError))
        {
            return
            [
                new Result
                {
                    Title = Localize.creta_plugin_note_unavailable_title(),
                    SubTitle = _repository.LoadError,
                    IcoPath = IcoPathValue,
                    Score = 1000,
                    Action = _ => false
                }
            ];
        }

        return string.IsNullOrWhiteSpace(query.Search)
            ? BuildHomeResults()
            : BuildInputResults(query.Search);
    }

    public string GetTranslatedPluginTitle()
    {
        return Localize.creta_plugin_note_plugin_name();
    }

    public string GetTranslatedPluginDescription()
    {
        return Localize.creta_plugin_note_plugin_description();
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not NoteItem note)
        {
            return [];
        }

        return _contextMenuBuilder.Build(
            note,
            OpenEditorForExistingNote,
            CopyNoteToClipboard,
            UseNoteAsQuery,
            ToggleArchived,
            TogglePinned,
            BeginEdit,
            DeleteNote);
    }

    private bool OpenNotesManager()
    {
        _selectNotesManagerOnSettingsOpen = true;
        var opened = Context.API.OpenPluginSettingsWindow(Context.CurrentPluginMetadata.ID);
        if (!opened)
        {
            _selectNotesManagerOnSettingsOpen = false;
            return false;
        }

        if (_settingsControl is not null)
        {
            _settingsControl.SelectNotesManagerTab();
            _selectNotesManagerOnSettingsOpen = false;
        }
        else
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                SelectNotesManagerTabIfPending,
                DispatcherPriority.Loaded);
        }

        return false;
    }

    private void SelectNotesManagerTabIfPending()
    {
        if (!_selectNotesManagerOnSettingsOpen)
        {
            return;
        }

        _settingsControl?.SelectNotesManagerTab();
        _selectNotesManagerOnSettingsOpen = false;
    }

    private List<Result> BuildHomeResults()
    {
        return _queryResultBuilder.BuildHomeResults(RecentNotesLimit, BrowseNotesLimit, TagListLimit);
    }

    private List<Result> BuildInputResults(string rawSearch)
    {
        var trimmedContent = rawSearch.Trim();
        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            return BuildHomeResults();
        }

        if (!string.IsNullOrWhiteSpace(_editingNoteId))
        {
            return _queryResultBuilder.BuildEditingResults(trimmedContent);
        }

        var specialView = NoteSpecialViewResolver.Resolve(trimmedContent, _repository, BrowseNotesLimit);
        if (specialView is not null)
        {
            return _queryResultBuilder.BuildNoteViewResults(specialView.Title, specialView.Subtitle, specialView.Notes, specialView.Key, BrowseNotesLimit);
        }

        var matches = BuildSearchMatches(trimmedContent);
        return _queryResultBuilder.BuildSearchAndSaveResults(trimmedContent, matches);
    }

    private bool SaveNote(string content, string source = NoteSources.Launcher)
    {
        if (_repository.SaveNote(content, out var savedNote, out var errorMessage, source))
        {
            ClearEditingState();
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_saved_title(),
                NotePresentation.BuildNoteDisplayTitle(savedNote),
                false,
                2200,
                true);
            return false;
        }

        Context.API.ShowMainWindowNotification(
            Localize.creta_plugin_note_error_save_title(),
            errorMessage,
            true,
            4000);
        return false;
    }

    private bool UpdateEditingNote(string content)
    {
        var editingNoteId = _editingNoteId;
        if (string.IsNullOrWhiteSpace(editingNoteId))
        {
            Context.API.ShowMsgError(
                Localize.creta_plugin_note_error_update_title(),
                Localize.creta_plugin_note_error_no_edit_session());
            return false;
        }

        if (_repository.UpdateNote(editingNoteId, content, out var updatedNote, out var errorMessage))
        {
            ClearEditingState();
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_updated_title(),
                NotePresentation.BuildNoteDisplayTitle(updatedNote),
                false,
                2200,
                true);
            return false;
        }

        Context.API.ShowMainWindowNotification(
            Localize.creta_plugin_note_error_update_title(),
            errorMessage,
            true,
            4000);
        return false;
    }

    private List<NoteSearchMatch> BuildSearchMatches(string search)
    {
        return _repository.Notes
            .Where(note => !note.IsArchived)
            .Select(note => NoteSearchScorer.Match(note, search))
            .Where(match => match is not null)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Note.UpdatedAt)
            .Take(BrowseNotesLimit)
            .ToList();
    }

    private bool ActivateNote(NoteItem note)
    {
        if (note.HasAttachments && string.IsNullOrWhiteSpace(note.Content))
        {
            return OpenEditorForExistingNote(note);
        }

        return CopyNoteToClipboard(note);
    }

    private bool SaveClipboardImage()
    {
        if (!NoteClipboardImages.TryRead(out var images, out var readError) || images.Count == 0)
        {
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_error_save_title(),
                string.IsNullOrWhiteSpace(readError)
                    ? Localize.creta_plugin_note_editor_attachment_invalid()
                    : readError,
                true,
                4000);
            return false;
        }

        if (_repository.SaveNoteWithImages(
            string.Empty,
            images,
            out var savedNote,
            out var errorMessage,
            NoteSources.Launcher))
        {
            ClearEditingState();
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_saved_title(),
                NotePresentation.BuildNoteDisplayTitle(savedNote),
                false,
                2200,
                true);
            return false;
        }

        Context.API.ShowMainWindowNotification(
            Localize.creta_plugin_note_error_save_title(),
            errorMessage,
            true,
            4000);
        return false;
    }

    internal static bool CopyNoteToClipboardStatic(NoteItem note)
    {
        try
        {
            ActiveRepository?.RecordLastViewed(note.Id, out _);
            if (string.IsNullOrWhiteSpace(note.Content) && note.HasAttachments && ActiveRepository is not null)
            {
                var imagePath = ActiveRepository.GetAttachmentFullPath(note.Attachments[0]);
                if (File.Exists(imagePath))
                {
                    var image = NoteClipboardImages.CreatePreviewFromFile(imagePath);
                    System.Windows.Clipboard.SetImage(image);
                    Context.API.ShowMsg(
                        Localize.creta_plugin_note_copied_title(),
                        NotePresentation.BuildNoteDisplayTitle(note),
                        IcoPathValue);
                    return true;
                }
            }

            Context.API.CopyToClipboard(note.Content, showDefaultNotification: false);
            Context.API.ShowMsg(
                Localize.creta_plugin_note_copied_title(),
                NotePresentation.BuildNoteDisplayTitle(note),
                IcoPathValue);
            return true;
        }
        catch (Exception ex)
        {
            Context.API.ShowMsgError(Localize.creta_plugin_note_error_copy_title(), ex.Message);
            return false;
        }
    }

    private bool CopyNoteToClipboard(NoteItem note)
    {
        return CopyNoteToClipboardStatic(note);
    }

    private bool UseNoteAsQuery(NoteItem note)
    {
        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {note.Content}", true);
        return false;
    }

    private bool TogglePinned(NoteItem note)
    {
        if (_repository.SetPinned(note.Id, !note.IsPinned, out var updatedNote, out var errorMessage))
        {
            Context.API.ShowMsg(
                updatedNote.IsPinned
                    ? Localize.creta_plugin_note_pinned_title()
                    : Localize.creta_plugin_note_unpinned_title(),
                NotePresentation.BuildNoteDisplayTitle(updatedNote),
                IcoPathValue);
            Context.API.ReQuery();
            return false;
        }

        Context.API.ShowMsgError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    private bool ToggleArchived(NoteItem note)
    {
        if (_repository.SetArchived(note.Id, !note.IsArchived, out var updatedNote, out var errorMessage))
        {
            Context.API.ShowMsg(
                updatedNote.IsArchived
                    ? Localize.creta_plugin_note_archived_title()
                    : Localize.creta_plugin_note_unarchived_title(),
                NotePresentation.BuildNoteDisplayTitle(updatedNote),
                IcoPathValue);
            Context.API.ReQuery();
            return false;
        }

        Context.API.ShowMsgError(Localize.creta_plugin_note_error_update_title(), errorMessage);
        return false;
    }

    private bool DeleteNote(NoteItem note)
    {
        var message = Localize.creta_plugin_note_delete_confirm_message(
            Environment.NewLine,
            NotePresentation.BuildNoteDisplayTitle(note));
        var result = Context.API.ShowMsgBox(
            message,
            Localize.creta_plugin_note_delete_confirm_caption(),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return false;
        }

        if (_repository.DeleteNote(note.Id, out var errorMessage))
        {
            if (string.Equals(_editingNoteId, note.Id, StringComparison.OrdinalIgnoreCase))
            {
                ClearEditingState();
            }

            Context.API.ShowMsg(
                Localize.creta_plugin_note_deleted_title(),
                NotePresentation.BuildNoteDisplayTitle(note),
                IcoPathValue);
            Context.API.ReQuery();
            return false;
        }

        Context.API.ShowMsgError(Localize.creta_plugin_note_error_delete_title(), errorMessage);
        return false;
    }

    private bool BeginEdit(NoteItem note)
    {
        _editingNoteId = note.Id;
        var editableContent = NoteRepository.BuildEditableContent(note);
        Context.API.ShowMsg(
            Localize.creta_plugin_note_edit_mode_title(),
            Localize.creta_plugin_note_edit_mode_subtitle(NotePresentation.BuildNoteDisplayTitle(note)),
            IcoPathValue);
        Context.API.BackToQueryResults();
        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {editableContent}", true);
        return false;
    }

    private bool OpenEditorForNewNote(string initialContent)
    {
        var window = new NoteEditorWindow(
            Localize.creta_plugin_note_editor_new_title(),
            Localize.creta_plugin_note_editor_new_subtitle(),
            Localize.creta_plugin_note_editor_save_new(),
            initialContent);

        if (window.ShowDialog() != true)
        {
            return false;
        }

        if (_repository.SaveNoteWithImages(
            window.EditedContent,
            window.PendingImages,
            out var savedNote,
            out var errorMessage,
            NoteSources.Editor))
        {
            ClearEditingState();
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_saved_title(),
                NotePresentation.BuildNoteDisplayTitle(savedNote),
                false,
                2200,
                true);
            return false;
        }

        Context.API.ShowMainWindowNotification(
            Localize.creta_plugin_note_error_save_title(),
            errorMessage,
            true,
            4000);
        return false;
    }

    private bool OpenEditorForExistingNote(NoteItem note)
    {
        _repository.RecordLastViewed(note.Id, out _);
        var window = new NoteEditorWindow(
            Localize.creta_plugin_note_editor_edit_title(),
            Localize.creta_plugin_note_editor_edit_subtitle(),
            Localize.creta_plugin_note_editor_save_edit(),
            NoteRepository.BuildEditableContent(note),
            note.Attachments,
            _repository.NotesDirectoryPath);

        if (window.ShowDialog() != true)
        {
            return false;
        }

        if (_repository.UpdateNoteWithAttachments(
            note.Id,
            window.EditedContent,
            window.PendingImages,
            window.RemovedAttachmentIds,
            out var updatedNote,
            out var errorMessage))
        {
            ClearEditingState();
            Context.API.ShowMainWindowNotification(
                Localize.creta_plugin_note_updated_title(),
                NotePresentation.BuildNoteDisplayTitle(updatedNote),
                false,
                2200,
                true);
            Context.API.ReQuery();
            return false;
        }

        Context.API.ShowMainWindowNotification(
            Localize.creta_plugin_note_error_update_title(),
            errorMessage,
            true,
            4000);
        return false;
    }

    private void ClearEditingState()
    {
        _editingNoteId = string.Empty;
    }

    private string GetNotesCountText()
    {
        var count = _repository.GetAllNotes(BrowseNotesLimit).Count;
        return count == 1
            ? Localize.creta_plugin_note_count_single()
            : Localize.creta_plugin_note_count_multiple(count);
    }

    private string GetStoragePathText()
    {
        return _repository.NotesFilePath;
    }

    private NoteStorageChangeResult ApplyStoragePathChange()
    {
        var result = _repository.UpdateStoragePath(_settings.NotesFilePath);
        _settings.NotesFilePath = _repository.CustomNotesFilePath;

        Context.API.SaveSettingJsonStorage<Settings>();
        Context.API.ReQuery();
        return result;
    }
}
