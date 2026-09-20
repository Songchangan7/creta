namespace Creta.Plugin.Note;

public sealed class NotionSyncResult
{
    public bool Succeeded { get; init; }

    public bool Skipped { get; init; }

    public string PageId { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public static NotionSyncResult Success(string pageId)
    {
        return new NotionSyncResult
        {
            Succeeded = true,
            PageId = pageId ?? string.Empty
        };
    }

    public static NotionSyncResult Skip()
    {
        return new NotionSyncResult
        {
            Skipped = true
        };
    }

    public static NotionSyncResult Fail(string errorMessage)
    {
        return new NotionSyncResult
        {
            ErrorMessage = errorMessage ?? string.Empty
        };
    }
}
