using Creta.Core.Plugin;
using Creta.Infrastructure;

namespace Creta.Helper;

internal static class NoteSelectionQuery
{
    internal const string NotePluginId = "7FA14D42B7884FD7A8F86E1B1CC95E41";

    internal static string TryBuild(string selectedText)
    {
        var plugin = PluginManager.GetPluginForId(NotePluginId);
        if (plugin?.Metadata is null || plugin.Metadata.Disabled)
        {
            return string.Empty;
        }

        return SelectedTextFormatter.BuildNoteQuery(plugin.Metadata.ActionKeyword, selectedText);
    }
}
