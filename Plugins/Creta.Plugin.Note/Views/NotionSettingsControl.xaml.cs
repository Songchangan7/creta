using System;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

public partial class NotionSettingsControl : UserControl
{
    private readonly Settings _settings;
    private readonly Action _saveAction;

    public NotionSettingsControl(Settings settings, Action saveAction)
    {
        InitializeComponent();
        _settings = settings;
        _saveAction = saveAction;

        EnableSyncCheckBox.IsChecked = settings.NotionSyncEnabled;
        TokenBox.Password = settings.NotionIntegrationToken;
        DatabaseIdBox.Text = string.IsNullOrWhiteSpace(settings.NotionDatabaseId)
            ? Settings.DefaultNotionDatabaseId
            : settings.NotionDatabaseId;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.NotionSyncEnabled = EnableSyncCheckBox.IsChecked == true;
        _settings.NotionIntegrationToken = TokenBox.Password ?? string.Empty;
        _settings.NotionDatabaseId = string.IsNullOrWhiteSpace(DatabaseIdBox.Text)
            ? Settings.DefaultNotionDatabaseId
            : DatabaseIdBox.Text.Trim();

        _saveAction();

        if (_settings.CanSyncToNotion)
        {
            Main.Context.API.ShowMsg(
                Localize.creta_plugin_note_settings_notion_saved_title(),
                Localize.creta_plugin_note_settings_notion_saved_ready());
            return;
        }

        Main.Context.API.ShowMsg(
            Localize.creta_plugin_note_settings_notion_saved_title(),
            Localize.creta_plugin_note_settings_notion_saved_incomplete());
    }
}
