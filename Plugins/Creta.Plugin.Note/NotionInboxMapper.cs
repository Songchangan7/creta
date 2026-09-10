using System;

namespace Creta.Plugin.Note;

public static class NotionInboxMapper
{
    public const int TitleMaxLength = 80;
    public const int PlainTextMaxLength = 2000;
    public const string DefaultTitle = "随手记";
    public const string DefaultStatus = "待处理";
    public const string DefaultDestination = "未分类";

    public static string BuildTitle(string content)
    {
        var text = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultTitle;
        }

        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return DefaultTitle;
        }

        return firstLine.Length <= TitleMaxLength
            ? firstLine
            : firstLine[..TitleMaxLength].TrimEnd() + "…";
    }

    public static string BuildPlainText(string content)
    {
        var text = content?.Trim() ?? string.Empty;
        if (text.Length <= PlainTextMaxLength)
        {
            return text;
        }

        return text[..PlainTextMaxLength];
    }

    public static string NormalizeDatabaseId(string databaseId)
    {
        return databaseId?.Trim() ?? string.Empty;
    }

    public static string NormalizePageId(string pageId)
    {
        return pageId?.Trim() ?? string.Empty;
    }
}
