using System;
using System.Collections.Generic;
using System.Linq;

namespace Creta.Plugin.LocalPromptSearch;

internal sealed record PromptSearchSplit(string TypedContent, List<string> Tags);

internal sealed record PromptSaveInput(
    string Title,
    List<string> Tags,
    List<string> Keywords,
    bool ReplaceTitle);

internal static class PromptSaveParser
{
    internal const int MinContentLength = 2;
    internal const int AutoTitleMaxLength = 24;

    internal static PromptSearchSplit SplitSearch(string search)
    {
        var tags = new List<string>();
        var contentParts = new List<string>();

        var terms = (search ?? string.Empty).Split(
            [' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var term in terms)
        {
            if (term.StartsWith('#') && term.Length > 1)
            {
                var tag = term[1..].Trim();
                if (tag.Length > 0 && !ContainsIgnoreCase(tags, tag))
                {
                    tags.Add(tag);
                }
            }
            else
            {
                contentParts.Add(term);
            }
        }

        return new PromptSearchSplit(string.Join(" ", contentParts), tags);
    }

    internal static PromptSaveInput Parse(string search, string content, bool replaceTitle)
    {
        var split = SplitSearch(search);
        var title = CreateTitleFromContent(content);
        var keywords = title
            .Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(split.Tags)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new PromptSaveInput(title, split.Tags, keywords, replaceTitle);
    }

    internal static string CreateTitleFromContent(string content)
    {
        var firstLine = (content ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "未命名 Prompt";
        }

        return firstLine.Length <= AutoTitleMaxLength
            ? firstLine
            : firstLine[..AutoTitleMaxLength].TrimEnd() + "…";
    }

    internal static bool IsContentValid(string content, out string error)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "没有可保存的文本。请在 c 后面输入 Prompt，或先复制后再输入 c。";
            return false;
        }

        if (content.Trim().Length < MinContentLength)
        {
            error = $"Prompt 正文太短（至少 {MinContentLength} 个字符）。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static string Preview(string content, int maxLength = 80)
    {
        var normalized = string.Join(" ", (content ?? string.Empty).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "…";
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> items, string value) =>
        items.Any(item => string.Equals(item, value, StringComparison.CurrentCultureIgnoreCase));
}
