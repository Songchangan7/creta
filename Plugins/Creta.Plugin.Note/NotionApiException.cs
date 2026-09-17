using System;

namespace Creta.Plugin.Note;

public sealed class NotionApiException : Exception
{
    public NotionApiException(string message)
        : base(message)
    {
    }

    public NotionApiException(int statusCode, string message)
        : base(string.IsNullOrWhiteSpace(message) ? $"Notion API error ({statusCode})." : message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
