using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Creta.Plugin.Note;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Creta.Test.Plugins;

public class NotionInboxSyncTests
{
    private string _testRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "Creta.NotionInboxTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Test]
    public void GivenMultilineContentWhenBuildTitleThenUsesFirstLineAndTruncates()
    {
        ClassicAssert.AreEqual("随手记", NotionInboxMapper.BuildTitle("   "));
        ClassicAssert.AreEqual("明天确认发布节奏", NotionInboxMapper.BuildTitle("明天确认发布节奏\n补充细节"));
        ClassicAssert.AreEqual(new string('a', 80) + "…", NotionInboxMapper.BuildTitle(new string('a', 90)));
    }

    [Test]
    public void GivenLongContentWhenBuildPlainTextThenCapsLength()
    {
        var content = new string('b', NotionInboxMapper.PlainTextMaxLength + 50);

        ClassicAssert.AreEqual("short", NotionInboxMapper.BuildPlainText("  short  "));
        ClassicAssert.AreEqual(NotionInboxMapper.PlainTextMaxLength, NotionInboxMapper.BuildPlainText(content).Length);
    }

    [Test]
    public void GivenSettingsWhenCanSyncToNotionThenRequiresSwitchTokenAndDatabase()
    {
        var settings = new Settings
        {
            NotionSyncEnabled = true,
            NotionIntegrationToken = "secret",
            NotionDatabaseId = Settings.DefaultNotionDatabaseId
        };

        ClassicAssert.IsTrue(settings.CanSyncToNotion);

        settings.NotionSyncEnabled = false;
        ClassicAssert.IsFalse(settings.CanSyncToNotion);

        settings.NotionSyncEnabled = true;
        settings.NotionIntegrationToken = " ";
        ClassicAssert.IsFalse(settings.CanSyncToNotion);
    }

    [Test]
    public async Task GivenCreateRequestWhenSendThenPostsInboxDefaultsAndHeaders()
    {
        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.OK, """{"id":"page-created"}""")
        };
        var client = new NotionInboxClient(new HttpClient(handler));

        var result = await client.CreateInboxItemAsync(
            " ntn_token ",
            Settings.DefaultNotionDatabaseId,
            "标题",
            "原文内容");

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual("page-created", result.PageId);
        ClassicAssert.AreEqual(1, handler.Requests.Count);

        var request = handler.Requests[0];
        ClassicAssert.AreEqual(HttpMethod.Post, request.Method);
        ClassicAssert.AreEqual("https://api.notion.com/v1/pages", request.Url);
        ClassicAssert.AreEqual("Bearer ntn_token", request.Authorization);
        ClassicAssert.AreEqual(NotionInboxClient.ApiVersion, request.NotionVersion);

        var payload = JsonNode.Parse(request.Body)!.AsObject();
        ClassicAssert.AreEqual(Settings.DefaultNotionDatabaseId, payload["parent"]!["database_id"]!.GetValue<string>());
        ClassicAssert.AreEqual("标题", ReadTitle(payload));
        ClassicAssert.AreEqual("原文内容", ReadPlainText(payload));
        ClassicAssert.AreEqual(NotionInboxMapper.DefaultStatus, payload["properties"]!["状态"]!["select"]!["name"]!.GetValue<string>());
        ClassicAssert.AreEqual(NotionInboxMapper.DefaultDestination, payload["properties"]!["去向"]!["select"]!["name"]!.GetValue<string>());
    }

    [Test]
    public async Task GivenUpdateRequestWhenSendThenPatchesTitleAndContentOnly()
    {
        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.OK, """{"id":"page-updated"}""")
        };
        var client = new NotionInboxClient(new HttpClient(handler));

        var result = await client.UpdateInboxItemAsync("token", " page-updated ", "新标题", "新原文");

        ClassicAssert.IsTrue(result.Succeeded);
        var request = handler.Requests.Single();
        ClassicAssert.AreEqual(HttpMethod.Patch, request.Method);
        ClassicAssert.AreEqual("https://api.notion.com/v1/pages/page-updated", request.Url);

        var payload = JsonNode.Parse(request.Body)!.AsObject();
        ClassicAssert.IsNull(payload["parent"]);
        ClassicAssert.IsNull(payload["properties"]!["状态"]);
        ClassicAssert.IsNull(payload["properties"]!["去向"]);
        ClassicAssert.AreEqual("新标题", ReadTitle(payload));
        ClassicAssert.AreEqual("新原文", ReadPlainText(payload));
    }

    [Test]
    public async Task GivenApiErrorWhenCreateThenReturnsFailureMessage()
    {
        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.Unauthorized, """{"message":"Invalid token"}""")
        };
        var client = new NotionInboxClient(new HttpClient(handler));

        var result = await client.CreateInboxItemAsync("bad", Settings.DefaultNotionDatabaseId, "t", "c");

        ClassicAssert.IsFalse(result.Succeeded);
        ClassicAssert.IsTrue(result.ErrorMessage.Contains("401"));
        ClassicAssert.IsTrue(result.ErrorMessage.Contains("Invalid token"));
    }

    [Test]
    public void GivenNotionSyncStateWhenPersistThenReloadsPageIdAndPending()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("sync me", out var savedNote, out _));
        ClassicAssert.IsFalse(File.ReadAllText(repository.NotesFilePath).Contains("NotionPageId"));
        ClassicAssert.IsTrue(repository.SetNotionSyncState(savedNote.Id, "notion-page-1", true, out _));

        var json = File.ReadAllText(repository.NotesFilePath);
        ClassicAssert.IsTrue(json.Contains("notion-page-1"));
        ClassicAssert.IsTrue(json.Contains("NotionSyncPending"));

        repository.Reload();
        var reloaded = repository.GetNoteById(savedNote.Id);
        ClassicAssert.AreEqual("notion-page-1", reloaded.NotionPageId);
        ClassicAssert.IsTrue(reloaded.NotionSyncPending);
        ClassicAssert.AreEqual(1, repository.GetNotesPendingNotionSync().Count);
        ClassicAssert.IsFalse(File.ReadAllText(repository.NotesFilePath).Contains("\"NotionPageId\": \"\""));
    }

    [Test]
    public void GivenLegacyNotesJsonWhenLoadThenNotionFieldsStayEmpty()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "legacy-note",
                "Content": "old note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var note = repository.GetNoteById("legacy-note");
        ClassicAssert.IsTrue(string.IsNullOrEmpty(note.NotionPageId));
        ClassicAssert.IsFalse(note.NotionSyncPending);
        ClassicAssert.AreEqual(0, repository.GetNotesPendingNotionSync().Count);
        ClassicAssert.IsFalse(File.ReadAllText(repository.NotesFilePath).Contains("NotionPageId"));
    }

    [Test]
    public async Task GivenEnabledSyncWhenEnqueueNewNoteThenCreatesInboxItem()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("明天确认发布节奏", out var savedNote, out _));

        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.OK, """{"id":"created-page"}""")
        };
        var service = CreateService(repository, handler, enabled: true);

        await service.EnqueueAsync(savedNote);

        ClassicAssert.AreEqual(1, handler.Requests.Count);
        ClassicAssert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        var synced = repository.GetNoteById(savedNote.Id);
        ClassicAssert.AreEqual("created-page", synced.NotionPageId);
        ClassicAssert.IsFalse(synced.NotionSyncPending);
    }

    [Test]
    public async Task GivenExistingPageIdWhenEnqueueThenPatchesWithoutResettingSelects()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("原文", out var savedNote, out _));
        ClassicAssert.IsTrue(repository.SetNotionSyncState(savedNote.Id, "existing-page", false, out _));
        savedNote.NotionPageId = "existing-page";

        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.OK, """{"id":"existing-page"}""")
        };
        var service = CreateService(repository, handler, enabled: true);

        await service.EnqueueAsync(savedNote);

        var request = handler.Requests.Single();
        ClassicAssert.AreEqual(HttpMethod.Patch, request.Method);
        var payload = JsonNode.Parse(request.Body)!.AsObject();
        ClassicAssert.IsNull(payload["properties"]!["状态"]);
        ClassicAssert.IsNull(payload["properties"]!["去向"]);
        ClassicAssert.IsFalse(repository.GetNoteById(savedNote.Id).NotionSyncPending);
    }

    [Test]
    public async Task GivenDisabledSyncWhenEnqueueThenDoesNotCallApi()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("skip", out var savedNote, out _));
        var handler = new RecordingHandler();
        var service = CreateService(repository, handler, enabled: false);

        await service.EnqueueAsync(savedNote);

        ClassicAssert.AreEqual(0, handler.Requests.Count);
        ClassicAssert.IsFalse(repository.GetNoteById(savedNote.Id).NotionSyncPending);
    }

    [Test]
    public async Task GivenPendingNoteWhenRetryThenCreatesInboxItem()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("pending note", out var savedNote, out _));
        ClassicAssert.IsTrue(repository.SetNotionSyncState(savedNote.Id, string.Empty, true, out _));

        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.OK, """{"id":"retried-page"}""")
        };
        var service = CreateService(repository, handler, enabled: true);

        await service.RetryPendingAsync();

        ClassicAssert.AreEqual(1, handler.Requests.Count);
        ClassicAssert.AreEqual("retried-page", repository.GetNoteById(savedNote.Id).NotionPageId);
        ClassicAssert.IsFalse(repository.GetNoteById(savedNote.Id).NotionSyncPending);
    }

    [Test]
    public async Task GivenApiFailureWhenEnqueueThenKeepsPendingForRetry()
    {
        var repository = CreateRepository();
        ClassicAssert.IsTrue(repository.SaveNote("failing note", out var savedNote, out _));
        var handler = new RecordingHandler
        {
            Responder = (_, _) => JsonResponse(HttpStatusCode.BadGateway, """{"message":"unavailable"}""")
        };
        var service = CreateService(repository, handler, enabled: true);

        await service.EnqueueAsync(savedNote);

        var note = repository.GetNoteById(savedNote.Id);
        ClassicAssert.IsTrue(string.IsNullOrEmpty(note.NotionPageId));
        ClassicAssert.IsTrue(note.NotionSyncPending);
    }

    private NoteRepository CreateRepository()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();
        return repository;
    }

    private static NotionInboxSyncService CreateService(
        NoteRepository repository,
        RecordingHandler handler,
        bool enabled)
    {
        var settings = new Settings
        {
            NotionSyncEnabled = enabled,
            NotionIntegrationToken = enabled ? "token" : string.Empty,
            NotionDatabaseId = Settings.DefaultNotionDatabaseId
        };

        return new NotionInboxSyncService(
            settings,
            repository,
            new NotionInboxClient(new HttpClient(handler)),
            _ => { },
            (_, _) => { });
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string ReadTitle(JsonObject payload)
    {
        return payload["properties"]!["标题"]!["title"]![0]!["text"]!["content"]!.GetValue<string>();
    }

    private static string ReadPlainText(JsonObject payload)
    {
        return payload["properties"]!["原文"]!["rich_text"]![0]!["text"]!["content"]!.GetValue<string>();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, string, HttpResponseMessage> Responder { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            request.Headers.TryGetValues("Notion-Version", out var versionValues);
            Requests.Add(new CapturedRequest
            {
                Method = request.Method,
                Url = request.RequestUri?.ToString() ?? string.Empty,
                Body = body,
                Authorization = request.Headers.Authorization?.ToString() ?? string.Empty,
                NotionVersion = versionValues?.FirstOrDefault() ?? string.Empty
            });

            return Responder?.Invoke(request, body)
                ?? JsonResponse(HttpStatusCode.OK, """{"id":"page-1"}""");
        }
    }

    private sealed class CapturedRequest
    {
        public HttpMethod Method { get; init; }

        public string Url { get; init; } = string.Empty;

        public string Body { get; init; } = string.Empty;

        public string Authorization { get; init; } = string.Empty;

        public string NotionVersion { get; init; } = string.Empty;
    }
}
