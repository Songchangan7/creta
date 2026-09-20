using System;
using System.Linq;
using System.Text.Json;

namespace Creta.Plugin.Note;

public sealed class NotionDatabaseSchema
{
    private static readonly string[] TagPropertyNames = ["Tags", "tags", "Tag", "标签"];

    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string TitlePropertyName { get; init; } = string.Empty;

    public string TagsPropertyName { get; init; }

    public static bool TryParse(string json, out NotionDatabaseSchema schema, out string errorMessage)
    {
        schema = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "Notion database response was empty.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("object", out var objectValue) &&
                string.Equals(objectValue.GetString(), "error", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = ReadErrorMessage(root, "Notion database request failed.");
                return false;
            }

            if (!root.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Notion database has no properties.";
                return false;
            }

            string titlePropertyName = null;
            string tagsPropertyName = null;
            foreach (var property in properties.EnumerateObject())
            {
                var type = property.Value.TryGetProperty("type", out var typeValue)
                    ? typeValue.GetString()
                    : string.Empty;
                if (titlePropertyName is null &&
                    string.Equals(type, "title", StringComparison.OrdinalIgnoreCase))
                {
                    titlePropertyName = property.Name;
                }

                if (tagsPropertyName is null &&
                    string.Equals(type, "multi_select", StringComparison.OrdinalIgnoreCase) &&
                    TagPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    tagsPropertyName = property.Name;
                }
            }

            if (string.IsNullOrWhiteSpace(titlePropertyName))
            {
                errorMessage = "Notion database is missing a title property.";
                return false;
            }

            schema = new NotionDatabaseSchema
            {
                Id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty,
                Title = ReadPlainText(root, "title"),
                TitlePropertyName = titlePropertyName,
                TagsPropertyName = tagsPropertyName
            };
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"Failed to parse Notion database: {ex.Message}";
            return false;
        }
    }

    internal static string ReadErrorMessage(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("message", out var message) &&
            !string.IsNullOrWhiteSpace(message.GetString()))
        {
            return message.GetString();
        }

        return fallback;
    }

    private static string ReadPlainText(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var title) || title.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in title.EnumerateArray())
        {
            if (item.TryGetProperty("plain_text", out var plainText) &&
                !string.IsNullOrWhiteSpace(plainText.GetString()))
            {
                return plainText.GetString();
            }
        }

        return string.Empty;
    }
}
