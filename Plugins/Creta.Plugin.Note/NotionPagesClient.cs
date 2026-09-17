using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Creta.Plugin.Note;

public sealed class NotionPagesClient : IDisposable
{
    public const string ApiVersion = "2022-06-28";
    private static readonly Uri ApiBaseAddress = new("https://api.notion.com/");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public NotionPagesClient()
        : this(CreateHttpClient(), true)
    {
    }

    public NotionPagesClient(HttpClient httpClient)
        : this(httpClient, false)
    {
    }

    private NotionPagesClient(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = ApiBaseAddress;
        }
    }

    public static HttpClient CreateHttpClient(HttpMessageHandler handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = ApiBaseAddress;
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    public async Task<NotionDatabaseSchema> RetrieveDatabaseAsync(
        string token,
        string databaseId,
        CancellationToken cancellationToken = default)
    {
        var apiId = NotionDatabaseIdParser.ToApiId(databaseId);
        using var request = CreateRequest(HttpMethod.Get, $"v1/databases/{apiId}", token);
        var json = await SendAsync(request, cancellationToken);
        if (!NotionDatabaseSchema.TryParse(json, out var schema, out var errorMessage))
        {
            throw new NotionApiException(errorMessage);
        }

        return schema;
    }

    public async Task<string> CreatePageAsync(
        string token,
        string databaseId,
        NoteItem note,
        NotionDatabaseSchema schema,
        CancellationToken cancellationToken = default)
    {
        var payload = NotionNoteMapper.BuildCreatePagePayload(note, databaseId, schema);
        using var request = CreateRequest(HttpMethod.Post, "v1/pages", token);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var json = await SendAsync(request, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("id", out var idValue) &&
            !string.IsNullOrWhiteSpace(idValue.GetString()))
        {
            return idValue.GetString();
        }

        throw new NotionApiException("Notion page was created without an id.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token?.Trim() ?? string.Empty);
        request.Headers.TryAddWithoutValidation("Notion-Version", ApiVersion);
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return json;
        }

        var message = TryReadErrorMessage(json) ?? $"Notion API error ({(int)response.StatusCode}).";
        throw new NotionApiException((int)response.StatusCode, message);
    }

    private static string TryReadErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return NotionDatabaseSchema.ReadErrorMessage(document.RootElement, null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
