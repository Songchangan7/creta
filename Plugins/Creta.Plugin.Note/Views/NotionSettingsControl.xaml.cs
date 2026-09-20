using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note.Views;

public partial class NotionSettingsControl : UserControl
{
    private readonly Action _saveAction;
    private readonly SettingsViewModel _viewModel;
    private bool _updatingPassword;

    public NotionSettingsControl(Settings settings, Action saveAction)
    {
        InitializeComponent();
        _saveAction = saveAction;
        _viewModel = new SettingsViewModel(settings);
        DataContext = _viewModel;
        Loaded += NotionSettingsControl_OnLoaded;
    }

    private void NotionSettingsControl_OnLoaded(object sender, RoutedEventArgs e)
    {
        _updatingPassword = true;
        TokenBox.Password = _viewModel.NotionToken ?? string.Empty;
        _updatingPassword = false;
    }

    private void TokenBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingPassword)
        {
            return;
        }

        _viewModel.NotionToken = TokenBox.Password;
    }

    private async void TestButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyTokenFromPasswordBox();
        if (!_viewModel.CanTestConnection(out var errorMessage))
        {
            _viewModel.StatusText = errorMessage;
            return;
        }

        _viewModel.StatusText = Localize.creta_plugin_note_settings_notion_testing();
        var token = _viewModel.NotionToken;
        var databaseId = _viewModel.NotionDatabaseId;
        try
        {
            var result = await Task.Run(async () =>
            {
                using var client = new NotionPagesClient();
                var schema = await client.RetrieveDatabaseAsync(token, databaseId);
                return schema;
            });

            var title = string.IsNullOrWhiteSpace(result.Title)
                ? result.TitlePropertyName
                : result.Title;
            _viewModel.StatusText = Localize.creta_plugin_note_settings_notion_test_success(title);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = Localize.creta_plugin_note_settings_notion_test_failed(ex.Message);
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyTokenFromPasswordBox();
        _saveAction();
        Main.Context.API.ShowMsg(
            Localize.creta_plugin_note_settings_notion_saved_title(),
            Localize.creta_plugin_note_settings_notion_saved_subtitle());
        _viewModel.StatusText = Localize.creta_plugin_note_settings_notion_saved_subtitle();
    }

    private void ApplyTokenFromPasswordBox()
    {
        _viewModel.NotionToken = TokenBox.Password;
    }

    private sealed class SettingsViewModel : BaseModel
    {
        private string _statusText = string.Empty;

        public SettingsViewModel(Settings settings)
        {
            Settings = settings;
        }

        public Settings Settings { get; }

        public bool NotionSyncEnabled
        {
            get => Settings.NotionSyncEnabled;
            set
            {
                if (Settings.NotionSyncEnabled != value)
                {
                    Settings.NotionSyncEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NotionToken
        {
            get => Settings.NotionToken;
            set => Settings.NotionToken = value;
        }

        public string NotionDatabaseId
        {
            get => Settings.NotionDatabaseId;
            set
            {
                if (Settings.NotionDatabaseId != value)
                {
                    Settings.NotionDatabaseId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanTestConnection(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(NotionToken))
            {
                errorMessage = Localize.creta_plugin_note_settings_notion_token_required();
                return false;
            }

            if (!NotionDatabaseIdParser.TryParse(NotionDatabaseId, out _))
            {
                errorMessage = Localize.creta_plugin_note_settings_notion_database_invalid();
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
