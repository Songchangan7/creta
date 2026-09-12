using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

public partial class NotesManagerControl : UserControl
{
    private readonly NoteRepository _repository;
    private readonly NotesManagerViewModel _viewModel;

    public NotesManagerControl(NoteRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        _viewModel = new NotesManagerViewModel();
        DataContext = _viewModel;
        InitializeStatusFilterComboBox();
        ReloadNotes();
    }

    private void InitializeStatusFilterComboBox()
    {
        StatusFilterComboBox.Items.Clear();
        StatusFilterComboBox.Items.Add(CreateStatusFilterItem(
            NotesManagerStatusFilter.All,
            Localize.creta_plugin_note_settings_notes_filter_all()));
        StatusFilterComboBox.Items.Add(CreateStatusFilterItem(
            NotesManagerStatusFilter.Active,
            Localize.creta_plugin_note_settings_notes_filter_active()));
        StatusFilterComboBox.Items.Add(CreateStatusFilterItem(
            NotesManagerStatusFilter.Pinned,
            Localize.creta_plugin_note_settings_notes_filter_pinned()));
        StatusFilterComboBox.Items.Add(CreateStatusFilterItem(
            NotesManagerStatusFilter.Archived,
            Localize.creta_plugin_note_settings_notes_filter_archived()));
        StatusFilterComboBox.SelectedIndex = 0;
    }

    private static ComboBoxItem CreateStatusFilterItem(NotesManagerStatusFilter filter, string label)
    {
        return new ComboBoxItem
        {
            Content = label,
            Tag = filter
        };
    }

    private void StatusFilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusFilterComboBox.SelectedItem is not ComboBoxItem { Tag: NotesManagerStatusFilter filter })
        {
            return;
        }

        _viewModel.StatusFilter = filter;
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReloadNotes();

        if (!string.IsNullOrWhiteSpace(_repository.LoadError))
        {
            Main.Context.API.ShowMsgError(
                Localize.creta_plugin_note_settings_notes_reload_failed_title(),
                _repository.LoadError);
            return;
        }

        Main.Context.API.ShowMsg(
            Localize.creta_plugin_note_settings_notes_reloaded_title(),
            _viewModel.SummaryText);
    }

    private void NotesListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.UpdateSelection(GetSelectedRows());
    }

    private void GridViewColumnHeader_OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader headerClicked ||
            headerClicked.Role == GridViewColumnHeaderRole.Padding ||
            NotesListView.View is not GridView gridView)
        {
            return;
        }

        var columnIndex = gridView.Columns.IndexOf(headerClicked.Column);
        if (columnIndex < 0 || columnIndex >= 7)
        {
            return;
        }

        _viewModel.ToggleSort(columnIndex switch
        {
            0 => NotesManagerSortColumn.Content,
            1 => NotesManagerSortColumn.Attachments,
            2 => NotesManagerSortColumn.Tags,
            3 => NotesManagerSortColumn.CreatedAt,
            4 => NotesManagerSortColumn.UpdatedAt,
            5 => NotesManagerSortColumn.IsPinned,
            6 => NotesManagerSortColumn.IsArchived,
            _ => NotesManagerSortColumn.UpdatedAt
        });
    }

    private void EditSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is not null)
        {
            EditNote(_viewModel.SelectedRow);
        }
    }

    private void DeleteSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedNotes = GetSelectedNotes();
        if (selectedNotes.Count == 0)
        {
            return;
        }

        if (NotesManagerOperations.TryDeleteNotes(_repository, selectedNotes))
        {
            ReloadNotes();
        }
    }

    private void EditTagsSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is not null)
        {
            EditTags(_viewModel.SelectedRow);
        }
    }

    private void BatchArchiveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedNotes = GetSelectedNotes();
        if (selectedNotes.Count == 0)
        {
            return;
        }

        if (NotesManagerOperations.TryArchiveNotes(_repository, selectedNotes))
        {
            ReloadNotes();
        }
    }

    private void BatchAddTagsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedNotes = GetSelectedNotes();
        if (selectedNotes.Count == 0)
        {
            return;
        }

        if (NotesManagerOperations.TryBatchAddTags(_repository, selectedNotes))
        {
            ReloadNotes();
        }
    }

    private void ExportTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(GetSelectedNotes(), isMarkdown: false, ExportScope.Selected);
    }

    private void ExportMarkdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(GetSelectedNotes(), isMarkdown: true, ExportScope.Selected);
    }

    private void ExportFilteredTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(_viewModel.GetFilteredNoteItems(), isMarkdown: false, ExportScope.Filtered);
    }

    private void ExportFilteredMarkdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(_viewModel.GetFilteredNoteItems(), isMarkdown: true, ExportScope.Filtered);
    }

    private void ExportAllTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(_viewModel.GetAllNoteItems(), isMarkdown: false, ExportScope.All);
    }

    private void ExportAllMarkdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExportNotes(_viewModel.GetAllNoteItems(), isMarkdown: true, ExportScope.All);
    }

    private void BackupJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        _repository.Reload();
        NotesManagerExporter.TryExportJsonBackup(_repository.NotesFilePath, _repository.HasAnyAttachments());
    }

    private void BackupFullButton_OnClick(object sender, RoutedEventArgs e)
    {
        _repository.Reload();
        NotesManagerExporter.TryExportFullBackup(_repository);
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (NotesManagerImporter.TryImport(_repository))
        {
            ReloadNotes();
        }
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        NotesManagerOperations.TryOpenNotesFileFolder(_repository);
    }

    private void EditTagsRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteManagerRowViewModel row })
        {
            EditTags(row);
        }
    }

    private void EditRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteManagerRowViewModel row })
        {
            EditNote(row);
        }
    }

    private void DeleteRowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteManagerRowViewModel row })
        {
            DeleteNote(row);
        }
    }

    private void NotesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindRowFromSource(e.OriginalSource as DependencyObject) is { } row)
        {
            EditNote(row);
        }
    }

    private void PinnedCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: NoteManagerRowViewModel row, IsChecked: var isChecked })
        {
            return;
        }

        NotesManagerOperations.TrySetPinned(_repository, row.Note, isChecked == true);
        ReloadNotes();
    }

    private void ArchivedCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: NoteManagerRowViewModel row, IsChecked: var isChecked })
        {
            return;
        }

        NotesManagerOperations.TrySetArchived(_repository, row.Note, isChecked == true);
        ReloadNotes();
    }

    private void PreviousPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.GoToPreviousPage();
    }

    private void NextPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.GoToNextPage();
    }

    private void NotesListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ListView listView || listView.View is not GridView gridView || gridView.Columns.Count < 8)
        {
            return;
        }

        var workingWidth = listView.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        if (workingWidth <= 0)
        {
            return;
        }

        gridView.Columns[0].Width = workingWidth * 0.22;
        gridView.Columns[1].Width = workingWidth * 0.06;
        gridView.Columns[2].Width = workingWidth * 0.16;
        gridView.Columns[3].Width = workingWidth * 0.10;
        gridView.Columns[4].Width = workingWidth * 0.10;
        gridView.Columns[5].Width = workingWidth * 0.06;
        gridView.Columns[6].Width = workingWidth * 0.06;
        gridView.Columns[7].Width = workingWidth * 0.24;
    }

    private void ExportNotes(IReadOnlyList<NoteItem> notes, bool isMarkdown, ExportScope scope)
    {
        if (notes.Count == 0)
        {
            return;
        }

        var defaultFileName = scope switch
        {
            ExportScope.Filtered => isMarkdown ? "quick-notes-filtered.md" : "quick-notes-filtered.txt",
            ExportScope.All => isMarkdown ? "quick-notes-all.md" : "quick-notes-all.txt",
            _ => isMarkdown ? "quick-notes-selected.md" : "quick-notes-selected.txt"
        };
        var dialogTitle = scope switch
        {
            ExportScope.Filtered => Localize.creta_plugin_note_settings_notes_export_dialog_title_filtered(),
            ExportScope.All => Localize.creta_plugin_note_settings_notes_export_dialog_title_all(),
            _ => Localize.creta_plugin_note_settings_notes_export_dialog_title()
        };
        var filter = isMarkdown
            ? Localize.creta_plugin_note_settings_notes_export_filter_md()
            : Localize.creta_plugin_note_settings_notes_export_filter_txt();

        NotesManagerExporter.TryExportNotes(
            notes,
            dialogTitle,
            defaultFileName,
            filter,
            exportedNotes => isMarkdown
                ? NotesManagerExporter.BuildMarkdownExport(exportedNotes)
                : NotesManagerExporter.BuildTextExport(exportedNotes));
    }

    private enum ExportScope
    {
        Selected,
        Filtered,
        All
    }

    private void EditTags(NoteManagerRowViewModel row)
    {
        if (NotesManagerOperations.TryEditTags(_repository, row.Note))
        {
            ReloadNotes();
        }
    }

    private void EditNote(NoteManagerRowViewModel row)
    {
        if (NotesManagerOperations.TryEditNote(_repository, row.Note))
        {
            ReloadNotes();
        }
    }

    private void DeleteNote(NoteManagerRowViewModel row)
    {
        if (NotesManagerOperations.TryDeleteNote(_repository, row.Note))
        {
            ReloadNotes();
        }
    }

    private void ReloadNotes()
    {
        _repository.Reload();
        _viewModel.LoadFromRepository(_repository);
        NotesListView.SelectedItems.Clear();
    }

    private List<NoteManagerRowViewModel> GetSelectedRows()
    {
        return NotesListView.SelectedItems.Cast<NoteManagerRowViewModel>().ToList();
    }

    private List<NoteItem> GetSelectedNotes()
    {
        return GetSelectedRows().Select(row => row.Note).ToList();
    }

    private static NoteManagerRowViewModel FindRowFromSource(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: NoteManagerRowViewModel row })
            {
                return row;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
