using System.Text;

namespace Creta.Infrastructure;

public static class SelectedTextFormatter
{
    public const int MaxQueryLength = 2000;

    public static string ForQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = CollapseWhitespace(text.Trim());
        if (collapsed.Length <= MaxQueryLength)
        {
            return collapsed;
        }

        return collapsed[..MaxQueryLength].TrimEnd();
    }

    public static string BuildNoteQuery(string actionKeyword, string selectedText)
    {
        if (string.IsNullOrWhiteSpace(actionKeyword))
        {
            return string.Empty;
        }

        var formatted = ForQuery(selectedText);
        return string.IsNullOrEmpty(formatted)
            ? string.Empty
            : $"{actionKeyword} {formatted}";
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasWhitespace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }
}
