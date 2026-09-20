using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Creta.Plugin.Note;

public static class NotionNoteMapper
{
    public const int TitleMaxLength = 100;
    public const int RichTextMaxLength = 2000;
    public const int MaxBodyBlocks = 100;

    public static string BuildTitle(NoteItem note)
    {
        var content = note?.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var firstLine = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        if (firstLine.Length <= TitleMaxLength)
        {
            return firstLine;
        }

        return firstLine[..TitleMaxLength];
    }

    public static IReadOnlyList<string> SplitBody(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var chunks = new List<string>();
        for (var index = 0; index < content.Length && chunks.Count < MaxBodyBlocks; index += RichTextMaxLength)
        {
            var length = Math.Min(RichTextMaxLength, content.Length - index);
            chunks.Add(content.Substring(index, length));
        }

        return chunks;
    }

    public static JsonObject BuildCreatePagePayload(NoteItem note, string databaseId, NotionDatabaseSchema schema)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.TitlePropertyName))
        {
            throw new ArgumentException("Title property name is required.", nameof(schema));
        }

        var apiDatabaseId = NotionDatabaseIdParser.ToApiId(databaseId);
        var properties = new JsonObject
        {
            [schema.TitlePropertyName] = new JsonObject
            {
                ["title"] = new JsonArray
                {
                    CreateTextObject(BuildTitle(note))
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(schema.TagsPropertyName) && note.Tags is { Count: > 0 })
        {
            var tags = new JsonArray();
            foreach (var tag in note.Tags.Where(static item => !string.IsNullOrWhiteSpace(item)))
            {
                tags.Add(new JsonObject { ["name"] = tag.Trim() });
            }

            if (tags.Count > 0)
            {
                properties[schema.TagsPropertyName] = new JsonObject
                {
                    ["multi_select"] = tags
                };
            }
        }

        var children = new JsonArray();
        foreach (var chunk in SplitBody(note.Content))
        {
            children.Add(new JsonObject
            {
                ["object"] = "block",
                ["type"] = "paragraph",
                ["paragraph"] = new JsonObject
                {
                    ["rich_text"] = new JsonArray
                    {
                        CreateTextObject(chunk)
                    }
                }
            });
        }

        return new JsonObject
        {
            ["parent"] = new JsonObject
            {
                ["database_id"] = apiDatabaseId
            },
            ["properties"] = properties,
            ["children"] = children
        };
    }

    private static JsonObject CreateTextObject(string content)
    {
        return new JsonObject
        {
            ["type"] = "text",
            ["text"] = new JsonObject
            {
                ["content"] = content ?? string.Empty
            }
        };
    }
}
