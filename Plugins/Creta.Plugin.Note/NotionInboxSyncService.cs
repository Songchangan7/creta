using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Creta.Plugin.Note;

public sealed class NotionInboxSyncService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly Settings _settings;
    private readonly NoteRepository _repository;
    private readonly NotionInboxClient _client;
    private readonly Action<string> _logWarn;
    private readonly Action<string, Exception> _logException;

    public NotionInboxSyncService(
        Settings settings,
        NoteRepository repository,
        NotionInboxClient client,
        Action<string> logWarn,
        Action<string, Exception> logException)
    {
        _settings = settings;
        _repository = repository;
        _client = client;
        _logWarn = logWarn;
        _logException = logException;
    }

    public static NotionInboxSyncService Create(
        Settings settings,
        NoteRepository repository,
        Action<string, Exception> log)
    {
        return Create(
            settings,
            repository,
            message => log?.Invoke(message, null),
            (message, exception) => log?.Invoke(message, exception));
    }

    public static NotionInboxSyncService Create(
        Settings settings,
        NoteRepository repository,
        Action<string> logWarn,
        Action<string, Exception> logException)
    {
        return new NotionInboxSyncService(
            settings,
            repository,
            new NotionInboxClient(new HttpClient { Timeout = RequestTimeout }),
            logWarn,
            logException);
    }

    public void Enqueue(NoteItem note)
    {
        _ = EnqueueAsync(note);
    }

    public Task EnqueueAsync(NoteItem note)
    {
        if (note is null || !_settings.CanSyncToNotion)
        {
            return Task.CompletedTask;
        }

        if (!_repository.SetNotionSyncState(note.Id, note.NotionPageId, true, out _))
        {
            return Task.CompletedTask;
        }

        return SyncNoteAsync(note.Id);
    }

    public void RetryPending()
    {
        _ = RetryPendingAsync();
    }

    public async Task RetryPendingAsync()
    {
        if (!_settings.CanSyncToNotion)
        {
            return;
        }

        foreach (var note in _repository.GetNotesPendingNotionSync())
        {
            await SyncNoteAsync(note.Id).ConfigureAwait(false);
        }
    }

    private async Task SyncNoteAsync(string noteId)
    {
        try
        {
            var note = await InvokeOnUiAsync(() => _repository.GetNoteById(noteId)).ConfigureAwait(false);
            if (note is null || !_settings.CanSyncToNotion)
            {
                return;
            }

            var title = NotionInboxMapper.BuildTitle(note.Content);
            var content = NotionInboxMapper.BuildPlainText(note.Content);
            var result = string.IsNullOrWhiteSpace(note.NotionPageId)
                ? await _client.CreateInboxItemAsync(
                    _settings.NotionIntegrationToken,
                    _settings.NotionDatabaseId,
                    title,
                    content).ConfigureAwait(false)
                : await _client.UpdateInboxItemAsync(
                    _settings.NotionIntegrationToken,
                    note.NotionPageId,
                    title,
                    content).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                _logWarn?.Invoke(result.ErrorMessage);
                return;
            }

            await InvokeOnUiAsync(() => _repository.SetNotionSyncState(noteId, result.PageId, false, out _)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logException?.Invoke("Failed to sync quick note to Notion inbox.", ex);
        }
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task;
    }
}
