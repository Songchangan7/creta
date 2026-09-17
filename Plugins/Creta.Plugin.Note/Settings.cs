using Flow.Launcher.Plugin;

namespace Creta.Plugin.Note;

public class Settings : BaseModel
{
    private string _notesFilePath = string.Empty;
    private bool _notionSyncEnabled;
    private string _notionToken = string.Empty;
    private string _notionDatabaseId = string.Empty;

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

    public string NotionToken
    {
        get => _notionToken;
        set
        {
            var normalized = value ?? string.Empty;
            if (_notionToken != normalized)
            {
                _notionToken = normalized;
                OnPropertyChanged();
            }
        }
    }

    public string NotionDatabaseId
    {
        get => _notionDatabaseId;
        set
        {
            var normalized = value ?? string.Empty;
            if (_notionDatabaseId != normalized)
            {
                _notionDatabaseId = normalized;
                OnPropertyChanged();
            }
        }
    }
}
