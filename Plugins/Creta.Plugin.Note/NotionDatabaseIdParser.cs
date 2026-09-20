using System;
using System.Text.RegularExpressions;

namespace Creta.Plugin.Note;

public static class NotionDatabaseIdParser
{
    private static readonly Regex Hex32Regex = new("[0-9a-fA-F]{32}", RegexOptions.Compiled);

    public static bool TryParse(string input, out string databaseId)
    {
        databaseId = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (Guid.TryParse(trimmed, out var guid))
        {
            databaseId = guid.ToString("N");
            return true;
        }

        var path = trimmed;
        var separatorIndex = path.IndexOfAny(['?', '#']);
        if (separatorIndex >= 0)
        {
            path = path[..separatorIndex];
        }

        Match lastMatch = null;
        foreach (Match match in Hex32Regex.Matches(path))
        {
            lastMatch = match;
        }

        if (lastMatch is not { Success: true })
        {
            return false;
        }

        databaseId = lastMatch.Value.ToLowerInvariant();
        return true;
    }

    public static string ToApiId(string databaseId)
    {
        if (!TryParse(databaseId, out var normalized))
        {
            throw new ArgumentException("Database id is not a valid Notion UUID.", nameof(databaseId));
        }

        return Guid.ParseExact(normalized, "N").ToString("D");
    }
}
