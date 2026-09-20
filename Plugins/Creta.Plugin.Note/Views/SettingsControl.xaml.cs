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
        Action saveSettings)
    {
        InitializeComponent();

        var storageTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_storage(),
            Content = new StorageSettingsControl(settings, applyAction, activePathProvider)
        };

        _notesTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_notes(),
            Content = new NotesManagerControl(repository)
        };

        var notionTab = new TabItem
        {
            Header = Localize.creta_plugin_note_settings_tab_notion(),
            Content = new NotionSettingsControl(settings, saveSettings)
        };

        SettingsTabs.Items.Add(storageTab);
        SettingsTabs.Items.Add(_notesTab);
        SettingsTabs.Items.Add(notionTab);
    }

    internal void SelectNotesManagerTab()
    {
        SettingsTabs.SelectedItem = _notesTab;
    }
}
