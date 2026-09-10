using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note;

public class Settings : BaseModel
{
    public const string DefaultNotionDatabaseId = "91957543-f7e8-4789-af86-ae73752ea367";

    private string _notesFilePath = string.Empty;
    private bool _notionSyncEnabled;
    private string _notionIntegrationToken = string.Empty;
    private string _notionDatabaseId = DefaultNotionDatabaseId;

    public string NotesFilePath
    {
        get => _notesFilePath;
        set
        {
            if (_notesFilePath != value)
            {
                _notesFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    public bool NotionSyncEnabled
    {
        get => _notionSyncEnabled;
        set
        {
            if (_notionSyncEnabled != value)
            {
                _notionSyncEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string NotionIntegrationToken
    {
        get => _notionIntegrationToken;
        set
        {
            var next = value ?? string.Empty;
            if (_notionIntegrationToken != next)
            {
                _notionIntegrationToken = next;
                OnPropertyChanged();
            }
        }
    }

    public string NotionDatabaseId
    {
        get => _notionDatabaseId;
        set
        {
            var next = value?.Trim() ?? string.Empty;
            if (_notionDatabaseId != next)
            {
                _notionDatabaseId = next;
                OnPropertyChanged();
            }
        }
    }

    public bool CanSyncToNotion =>
        NotionSyncEnabled &&
        !string.IsNullOrWhiteSpace(NotionIntegrationToken) &&
        !string.IsNullOrWhiteSpace(NotionDatabaseId);
}
