using System;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

public partial class SettingsControl : UserControl
{
    private readonly TabItem _notesTab;

    public SettingsControl(
        Settings settings,
        NoteRepository repository,
        Func<NoteStorageChangeResult> applyAction,
        Func<string> activePathProvider,
        Action saveNotionSettings)
    {
        InitializeComponent();

        var storageTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_storage(),
            Content = new StorageSettingsControl(settings, applyAction, activePathProvider)
        };

        var notionTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_notion(),
            Content = new NotionSettingsControl(settings, saveNotionSettings)
        };

        _notesTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_notes(),
            Content = new NotesManagerControl(repository)
        };

        SettingsTabs.Items.Add(storageTab);
        SettingsTabs.Items.Add(notionTab);
        SettingsTabs.Items.Add(_notesTab);
    }

    internal void SelectNotesManagerTab()
    {
        SettingsTabs.SelectedItem = _notesTab;
    }
}
