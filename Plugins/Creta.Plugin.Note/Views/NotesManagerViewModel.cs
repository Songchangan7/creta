using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

internal sealed class NotesManagerViewModel : BaseModel
{
    private const int PageSize = 50;

    private readonly ObservableCollection<NoteManagerRowViewModel> _visibleNotes = [];
    private List<NoteManagerRowViewModel> _sourceRows = [];
    private List<NoteManagerRowViewModel> _filteredRows = [];
    private string _summaryText = string.Empty;
    private string _loadErrorText = string.Empty;
    private string _pageInfoText = string.Empty;
    private string _searchText = string.Empty;
    private bool _hasLoadError;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalNoteCount;
    private int _selectedCount;
    private NoteManagerRowViewModel _selectedRow;
    private NotesManagerStatusFilter _statusFilter = NotesManagerStatusFilter.All;
    private NotesManagerSortColumn _sortColumn = NotesManagerSortColumn.UpdatedAt;
    private bool _sortDescending = true;

    internal NotesManagerViewModel()
    {
        VisibleNotes = new ReadOnlyObservableCollection<NoteManagerRowViewModel>(_visibleNotes);
    }

    internal ReadOnlyObservableCollection<NoteManagerRowViewModel> VisibleNotes { get; }

    internal string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    internal string LoadErrorText
    {
        get => _loadErrorText;
        private set => SetProperty(ref _loadErrorText, value);
    }

    internal string PageInfoText
    {
        get => _pageInfoText;
        private set => SetProperty(ref _pageInfoText, value);
    }

    internal string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_searchText == normalized)
            {
                return;
            }

            _searchText = normalized;
            OnPropertyChanged(nameof(SearchText));
            ApplyFilters(resetPage: true);
        }
    }

    internal NotesManagerStatusFilter StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (_statusFilter == value)
            {
                return;
            }

            _statusFilter = value;
            OnPropertyChanged(nameof(StatusFilter));
            ApplyFilters(resetPage: true);
        }
    }

    internal bool HasLoadError
    {
        get => _hasLoadError;
        private set => SetProperty(ref _hasLoadError, value);
    }

    internal bool CanGoPrevious => _currentPage > 1;

    internal bool CanGoNext => _currentPage < _totalPages;

    internal bool HasNotes => _filteredRows.Count > 0;

    internal bool HasAnyNotes => _sourceRows.Count > 0;

    internal bool HasAnySelection => _selectedCount > 0;

    internal bool HasSingleSelection => _selectedCount == 1;

    internal bool HasSelection => HasSingleSelection;

    internal NoteManagerRowViewModel SelectedRow
    {
        get => _selectedRow;
        private set
        {
            if (ReferenceEquals(_selectedRow, value))
            {
                return;
            }

            _selectedRow = value;
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    internal void LoadFromRepository(NoteRepository repository)
    {
        var allNotes = repository.GetAllNotes();
        var archivedCount = allNotes.Count(note => note.IsArchived);
        _totalNoteCount = allNotes.Count;
        SummaryText = Localize.creta_plugin_note_settings_notes_summary(
            allNotes.Count,
            archivedCount,
            allNotes.Count - archivedCount);
        LoadErrorText = repository.LoadError;
        HasLoadError = !string.IsNullOrWhiteSpace(repository.LoadError);
        _sourceRows = allNotes.Select(note => new NoteManagerRowViewModel(note)).ToList();
        UpdateSelection([]);
        OnPropertyChanged(nameof(HasAnyNotes));
        ApplyFilters(resetPage: false);
    }

    internal void UpdateSelection(IReadOnlyList<NoteManagerRowViewModel> selectedRows)
    {
        _selectedCount = selectedRows?.Count ?? 0;
        SelectedRow = _selectedCount == 1 ? selectedRows[0] : null;
        OnPropertyChanged(nameof(HasAnySelection));
        OnPropertyChanged(nameof(HasSingleSelection));
    }

    internal void ToggleSort(NotesManagerSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = column is NotesManagerSortColumn.UpdatedAt or NotesManagerSortColumn.CreatedAt;
        }

        ApplyFilters(resetPage: false);
    }

    internal void GoToPreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        _currentPage--;
        RefreshVisiblePage();
    }

    internal void GoToNextPage()
    {
        if (!CanGoNext)
        {
            return;
        }

        _currentPage++;
        RefreshVisiblePage();
    }

    internal IReadOnlyList<NoteItem> GetFilteredNoteItems()
    {
        return _filteredRows.Select(row => row.Note).ToList();
    }

    internal IReadOnlyList<NoteItem> GetAllNoteItems()
    {
        return _sourceRows.Select(row => row.Note).ToList();
    }

    private void ApplyFilters(bool resetPage)
    {
        var filtered = _sourceRows.AsEnumerable();

        filtered = StatusFilter switch
        {
            NotesManagerStatusFilter.Active => filtered.Where(row => !row.Note.IsArchived),
            NotesManagerStatusFilter.Pinned => filtered.Where(row => row.Note.IsPinned && !row.Note.IsArchived),
            NotesManagerStatusFilter.Archived => filtered.Where(row => row.Note.IsArchived),
            _ => filtered
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            filtered = filtered.Where(row => MatchesSearch(row.Note, term));
        }

        _filteredRows = SortRows(filtered).ToList();
        _totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Count / (double)PageSize));

        if (resetPage)
        {
            _currentPage = 1;
        }
        else
        {
            _currentPage = Math.Min(_currentPage, _totalPages);
        }

        if (_currentPage < 1)
        {
            _currentPage = 1;
        }

        RefreshVisiblePage();
    }

    private IEnumerable<NoteManagerRowViewModel> SortRows(IEnumerable<NoteManagerRowViewModel> rows)
    {
        return _sortColumn switch
        {
            NotesManagerSortColumn.Content => _sortDescending
                ? rows.OrderByDescending(row => row.Note.Content, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(row => row.Note.Content, StringComparer.OrdinalIgnoreCase),
            NotesManagerSortColumn.Attachments => _sortDescending
                ? rows.OrderByDescending(row => row.Note.Attachments?.Count ?? 0)
                : rows.OrderBy(row => row.Note.Attachments?.Count ?? 0),
            NotesManagerSortColumn.Tags => _sortDescending
                ? rows.OrderByDescending(row => row.TagsText, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(row => row.TagsText, StringComparer.OrdinalIgnoreCase),
            NotesManagerSortColumn.CreatedAt => _sortDescending
                ? rows.OrderByDescending(row => row.Note.CreatedAt)
                : rows.OrderBy(row => row.Note.CreatedAt),
            NotesManagerSortColumn.IsPinned => _sortDescending
                ? rows.OrderByDescending(row => row.Note.IsPinned)
                : rows.OrderBy(row => row.Note.IsPinned),
            NotesManagerSortColumn.IsArchived => _sortDescending
                ? rows.OrderByDescending(row => row.Note.IsArchived)
                : rows.OrderBy(row => row.Note.IsArchived),
            _ => _sortDescending
                ? rows.OrderByDescending(row => row.Note.UpdatedAt)
                : rows.OrderBy(row => row.Note.UpdatedAt)
        };
    }

    private void RefreshVisiblePage()
    {
        _visibleNotes.Clear();

        if (_filteredRows.Count == 0)
        {
            PageInfoText = HasActiveFilters()
                ? Localize.creta_plugin_note_settings_notes_filter_empty(_totalNoteCount)
                : Localize.creta_plugin_note_settings_notes_empty();
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(HasNotes));
            return;
        }

        var skip = (_currentPage - 1) * PageSize;
        foreach (var row in _filteredRows.Skip(skip).Take(PageSize))
        {
            _visibleNotes.Add(row);
        }

        PageInfoText = HasActiveFilters()
            ? Localize.creta_plugin_note_settings_notes_page_info_filtered(
                _currentPage,
                _totalPages,
                _filteredRows.Count,
                _totalNoteCount)
            : Localize.creta_plugin_note_settings_notes_page_info(
                _currentPage,
                _totalPages,
                _filteredRows.Count);
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(HasNotes));
    }

    private bool HasActiveFilters()
    {
        return StatusFilter != NotesManagerStatusFilter.All || !string.IsNullOrWhiteSpace(SearchText);
    }

    private static bool MatchesSearch(NoteItem note, string term)
    {
        if (note.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (note.HasAttachments &&
            (term.Contains("图", StringComparison.OrdinalIgnoreCase) ||
             term.Contains("image", StringComparison.OrdinalIgnoreCase) ||
             note.Attachments.Any(attachment =>
                 !string.IsNullOrWhiteSpace(attachment.FileName) &&
                 attachment.FileName.Contains(term, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        var normalizedTerm = term.Trim().TrimStart('#');
        return note.Tags.Any(tag =>
            tag.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase) ||
            tag.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
