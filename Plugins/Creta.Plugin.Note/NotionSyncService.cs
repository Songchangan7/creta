using System;
using System.Threading;
using System.Threading.Tasks;

namespace Creta.Plugin.Note;

public sealed class NotionSyncService
{
    private readonly Func<Settings> _settingsProvider;
    private readonly NotionPagesClient _client;

    public NotionSyncService(Func<Settings> settingsProvider, NotionPagesClient client)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public bool ShouldSync(NoteItem note)
    {
        if (note is null || string.IsNullOrWhiteSpace(note.Content) || !string.IsNullOrWhiteSpace(note.NotionPageId))
        {
            return false;
        }

        var settings = _settingsProvider();
        return settings is not null &&
               settings.NotionSyncEnabled &&
               !string.IsNullOrWhiteSpace(settings.NotionToken) &&
               NotionDatabaseIdParser.TryParse(settings.NotionDatabaseId, out _);
    }

    public async Task<NotionSyncResult> SyncAsync(NoteItem note, CancellationToken cancellationToken = default)
    {
        if (!ShouldSync(note))
        {
            return NotionSyncResult.Skip();
        }

        var settings = _settingsProvider();
        if (!NotionDatabaseIdParser.TryParse(settings.NotionDatabaseId, out var databaseId))
        {
            return NotionSyncResult.Fail("Notion database id is invalid.");
        }

        try
        {
            var schema = await _client.RetrieveDatabaseAsync(settings.NotionToken, databaseId, cancellationToken);
            var pageId = await _client.CreatePageAsync(settings.NotionToken, databaseId, note, schema, cancellationToken);
            if (string.IsNullOrWhiteSpace(pageId))
            {
                return NotionSyncResult.Fail("Notion page was created without an id.");
            }

            return NotionSyncResult.Success(pageId);
        }
        catch (Exception ex)
        {
            return NotionSyncResult.Fail(ex.Message);
        }
    }
}
