using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Creta.Plugin.Note;

namespace Creta.Test.Plugins;

public class NotionSyncTests
{
    private const string DatabaseId = "11111111111111111111111111111111";
    private const string DatabaseApiId = "11111111-1111-1111-1111-111111111111";
    private const string PageId = "22222222-2222-2222-2222-222222222222";

    [Test]
    public void GivenUuidAndUrlWhenParseThenExtractsDatabaseId()
    {
        ClassicAssert.IsTrue(NotionDatabaseIdParser.TryParse("11111111-1111-1111-1111-111111111111", out var dashed));
        ClassicAssert.AreEqual(DatabaseId, dashed);

        ClassicAssert.IsTrue(NotionDatabaseIdParser.TryParse(DatabaseId, out var hex));
        ClassicAssert.AreEqual(DatabaseId, hex);

        ClassicAssert.IsTrue(NotionDatabaseIdParser.TryParse(
            "https://www.notion.so/workspace/Inbox-11111111111111111111111111111111?v=abcd",
            out var fromUrl));
        ClassicAssert.AreEqual(DatabaseId, fromUrl);

        ClassicAssert.AreEqual(DatabaseApiId, NotionDatabaseIdParser.ToApiId(DatabaseId));
    }

    [Test]
    public void GivenInvalidInputWhenParseThenReturnsFalse()
    {
        ClassicAssert.IsFalse(NotionDatabaseIdParser.TryParse(" ", out _));
        ClassicAssert.IsFalse(NotionDatabaseIdParser.TryParse("not-a-database-id", out _));
        ClassicAssert.IsFalse(NotionDatabaseIdParser.TryParse("https://www.notion.so/workspace/notes", out _));
    }

    [Test]
    public void GivenLongContentWhenMapThenTruncatesTitleAndSplitsBody()
    {
        var title = new string('a', NotionNoteMapper.TitleMaxLength + 20);
        var body = new string('b', NotionNoteMapper.RichTextMaxLength + 50);
        var note = new NoteItem
        {
            Content = $"{title}\n{body}",
            Tags = ["idea", "work"]
        };

        var mappedTitle = NotionNoteMapper.BuildTitle(note);
        ClassicAssert.AreEqual(NotionNoteMapper.TitleMaxLength, mappedTitle.Length);
        ClassicAssert.IsTrue(mappedTitle.StartsWith('a'));

        var chunks = NotionNoteMapper.SplitBody(note.Content);
        ClassicAssert.AreEqual(2, chunks.Count);
        ClassicAssert.AreEqual(NotionNoteMapper.RichTextMaxLength, chunks[0].Length);
        ClassicAssert.AreEqual(note.Content.Length - NotionNoteMapper.RichTextMaxLength, chunks[1].Length);

        ClassicAssert.IsTrue(NotionDatabaseSchema.TryParse(CreateDatabaseJson("Name", "Tags"), out var schema, out _));
        var payload = NotionNoteMapper.BuildCreatePagePayload(note, DatabaseId, schema).ToJsonString();
        ClassicAssert.IsTrue(payload.Contains("\"database_id\":\"11111111-1111-1111-1111-111111111111\""));
        ClassicAssert.IsTrue(payload.Contains("\"Name\""));
        ClassicAssert.IsTrue(payload.Contains("\"Tags\""));
        ClassicAssert.IsTrue(payload.Contains("\"idea\""));
        ClassicAssert.IsTrue(payload.Contains("\"work\""));
    }

    [Test]
    public void GivenChineseTagPropertyWhenParseSchemaThenMapsTags()
    {
        ClassicAssert.IsTrue(NotionDatabaseSchema.TryParse(CreateDatabaseJson("标题", "标签"), out var schema, out _));
        ClassicAssert.AreEqual("Inbox", schema.Title);
        ClassicAssert.AreEqual("标题", schema.TitlePropertyName);
        ClassicAssert.AreEqual("标签", schema.TagsPropertyName);
    }

    [Test]
    public async Task GivenDisabledSettingsWhenSyncThenDoesNotSendRequestAsync()
    {
        var handler = new StubHandler();
        using var client = new NotionPagesClient(NotionPagesClient.CreateHttpClient(handler));
        var settings = new Settings
        {
            NotionSyncEnabled = false,
            NotionToken = "secret_token",
            NotionDatabaseId = DatabaseId
        };
        var service = new NotionSyncService(() => settings, client);

        var result = await service.SyncAsync(new NoteItem { Content = "hello" });

        ClassicAssert.IsTrue(result.Skipped);
        ClassicAssert.AreEqual(0, handler.Requests.Count);
        ClassicAssert.IsFalse(service.ShouldSync(new NoteItem { Content = "hello" }));
    }

    [Test]
    public async Task GivenEnabledSettingsWhenSyncThenCreatesPageAndReturnsIdAsync()
    {
        var handler = new StubHandler
        {
            Responder = request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/v1/databases/", StringComparison.Ordinal))
                {
                    return JsonResponse(HttpStatusCode.OK, CreateDatabaseJson("Name", "Tags"));
                }

                return JsonResponse(HttpStatusCode.OK, $$"""{ "id": "{{PageId}}", "object": "page" }""");
            }
        };
        using var client = new NotionPagesClient(NotionPagesClient.CreateHttpClient(handler));
        var settings = new Settings
        {
            NotionSyncEnabled = true,
            NotionToken = "secret_token",
            NotionDatabaseId = $"https://www.notion.so/{DatabaseId}?v=1"
        };
        var service = new NotionSyncService(() => settings, client);
        var note = new NoteItem
        {
            Content = "hello notion #idea",
            Tags = ["idea"]
        };

        var result = await service.SyncAsync(note);

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual(PageId, result.PageId);
        ClassicAssert.AreEqual(2, handler.Requests.Count);
        ClassicAssert.IsTrue(handler.Requests[0].Path.Contains($"/v1/databases/{DatabaseApiId}"));
        ClassicAssert.AreEqual("/v1/pages", handler.Requests[1].Path);
        ClassicAssert.IsTrue(handler.Requests[1].Body.Contains("hello notion"));
        ClassicAssert.IsTrue(handler.Requests[1].Body.Contains("idea"));
        ClassicAssert.AreEqual("Bearer secret_token", handler.Requests[0].Authorization);
        ClassicAssert.AreEqual(NotionPagesClient.ApiVersion, handler.Requests[0].NotionVersion);
    }

    [Test]
    public async Task GivenUnauthorizedWhenSyncThenReturnsReadableErrorAsync()
    {
        var handler = new StubHandler
        {
            Responder = _ => JsonResponse(HttpStatusCode.Unauthorized, """{ "object": "error", "message": "API token is invalid." }""")
        };
        using var client = new NotionPagesClient(NotionPagesClient.CreateHttpClient(handler));
        var service = new NotionSyncService(() => new Settings
        {
            NotionSyncEnabled = true,
            NotionToken = "bad",
            NotionDatabaseId = DatabaseId
        }, client);

        var result = await service.SyncAsync(new NoteItem { Content = "hello" });

        ClassicAssert.IsFalse(result.Succeeded);
        ClassicAssert.IsTrue(result.ErrorMessage.Contains("API token is invalid."));
    }

    [Test]
    public async Task GivenMissingDatabaseWhenSyncThenReturnsReadableErrorAsync()
    {
        var handler = new StubHandler
        {
            Responder = _ => JsonResponse(
                HttpStatusCode.NotFound,
                """{ "object": "error", "message": "Could not find database with ID: 11111111-1111-1111-1111-111111111111." }""")
        };
        using var client = new NotionPagesClient(NotionPagesClient.CreateHttpClient(handler));
        var service = new NotionSyncService(() => new Settings
        {
            NotionSyncEnabled = true,
            NotionToken = "secret_token",
            NotionDatabaseId = DatabaseId
        }, client);

        var result = await service.SyncAsync(new NoteItem { Content = "hello" });

        ClassicAssert.IsFalse(result.Succeeded);
        ClassicAssert.IsTrue(result.ErrorMessage.Contains("Could not find database"));
    }

    [Test]
    public void GivenEmptyContentWhenShouldSyncThenReturnsFalse()
    {
        var service = new NotionSyncService(
            () => new Settings
            {
                NotionSyncEnabled = true,
                NotionToken = "secret_token",
                NotionDatabaseId = DatabaseId
            },
            new NotionPagesClient(NotionPagesClient.CreateHttpClient(new StubHandler())));

        ClassicAssert.IsFalse(service.ShouldSync(new NoteItem { Content = " " }));
        ClassicAssert.IsFalse(service.ShouldSync(new NoteItem
        {
            Content = "already synced",
            NotionPageId = PageId
        }));
    }

    private static string CreateDatabaseJson(string titlePropertyName, string tagsPropertyName)
    {
        return $$"""
            {
              "object": "database",
              "id": "{{DatabaseApiId}}",
              "title": [{ "plain_text": "Inbox" }],
              "properties": {
                "{{titlePropertyName}}": { "id": "title", "type": "title", "title": {} },
                "{{tagsPropertyName}}": { "id": "tags", "type": "multi_select", "multi_select": { "options": [] } }
              }
            }
            """;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("Notion-Version", out var versions)
                    ? string.Join(',', versions)
                    : string.Empty));
            return Responder(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string Body,
        string Authorization,
        string NotionVersion);
}
