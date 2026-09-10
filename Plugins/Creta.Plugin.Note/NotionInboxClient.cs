using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Creta.Plugin.Note;

public sealed class NotionInboxClient
{
    public const string ApiVersion = "2022-06-28";
    private const string PagesEndpoint = "https://api.notion.com/v1/pages";

    private readonly HttpClient _httpClient;

    public NotionInboxClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task<NotionInboxUpsertResult> CreateInboxItemAsync(
        string token,
        string databaseId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var body = BuildCreatePayload(databaseId, title, content);
        return SendAsync(HttpMethod.Post, PagesEndpoint, token, body, cancellationToken);
    }

    public Task<NotionInboxUpsertResult> UpdateInboxItemAsync(
        string token,
        string pageId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var normalizedPageId = NotionInboxMapper.NormalizePageId(pageId);
        var body = BuildUpdatePayload(title, content);
        return SendAsync(HttpMethod.Patch, $"{PagesEndpoint}/{normalizedPageId}", token, body, cancellationToken);
    }

    internal static string BuildCreatePayload(string databaseId, string title, string content)
    {
        var properties = BuildContentProperties(title, content);
        properties["状态"] = BuildSelectProperty(NotionInboxMapper.DefaultStatus);
        properties["去向"] = BuildSelectProperty(NotionInboxMapper.DefaultDestination);

        var payload = new JsonObject
        {
            ["parent"] = new JsonObject
            {
                ["database_id"] = NotionInboxMapper.NormalizeDatabaseId(databaseId)
            },
            ["properties"] = properties
        };

        return payload.ToJsonString();
    }

    internal static string BuildUpdatePayload(string title, string content)
    {
        var payload = new JsonObject
        {
            ["properties"] = BuildContentProperties(title, content)
        };

        return payload.ToJsonString();
    }

    private async Task<NotionInboxUpsertResult> SendAsync(
        HttpMethod method,
        string url,
        string token,
        string json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token?.Trim());
        request.Headers.TryAddWithoutValidation("Notion-Version", ApiVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return NotionInboxUpsertResult.Fail(ReadErrorMessage(responseBody, (int)response.StatusCode));
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
        if (!document.RootElement.TryGetProperty("id", out var idElement))
        {
            return NotionInboxUpsertResult.Fail("Notion response did not include a page id.");
        }

        var pageId = idElement.GetString();
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return NotionInboxUpsertResult.Fail("Notion response did not include a page id.");
        }

        return NotionInboxUpsertResult.Ok(pageId);
    }

    private static JsonObject BuildContentProperties(string title, string content)
    {
        return new JsonObject
        {
            ["标题"] = new JsonObject
            {
                ["title"] = new JsonArray { BuildTextObject(title) }
            },
            ["原文"] = new JsonObject
            {
                ["rich_text"] = new JsonArray { BuildTextObject(content) }
            }
        };
    }

    private static JsonObject BuildSelectProperty(string name)
    {
        return new JsonObject
        {
            ["select"] = new JsonObject
            {
                ["name"] = name
            }
        };
    }

    private static JsonObject BuildTextObject(string text)
    {
        return new JsonObject
        {
            ["text"] = new JsonObject
            {
                ["content"] = text ?? string.Empty
            }
        };
    }

    private static string ReadErrorMessage(string responseBody, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return $"Notion API {statusCode}: {message}";
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic status message.
        }

        return $"Notion API {statusCode}";
    }
}

public sealed class NotionInboxUpsertResult
{
    public bool Succeeded { get; init; }

    public string PageId { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public static NotionInboxUpsertResult Ok(string pageId) => new()
    {
        Succeeded = true,
        PageId = pageId
    };

    public static NotionInboxUpsertResult Fail(string errorMessage) => new()
    {
        Succeeded = false,
        ErrorMessage = errorMessage
    };
}
