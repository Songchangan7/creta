using Flow.Launcher.Plugin;
using System.Collections.Generic;

namespace Creta.Plugin.LocalPromptSearch;

public class Settings : BaseModel
{
    private string _promptFilePath = string.Empty;

    public string PromptFilePath
    {
        get => _promptFilePath;
        set
        {
            if (_promptFilePath != value)
            {
                _promptFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _pasteAfterCopy = true;

    public bool PasteAfterCopy
    {
        get => _pasteAfterCopy;
        set
        {
            if (_pasteAfterCopy != value)
            {
                _pasteAfterCopy = value;
                OnPropertyChanged();
            }
        }
    }

    public List<string> RecentPromptIds { get; set; } = [];
}
