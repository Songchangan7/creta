using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Creta.Plugin.Note;

namespace Creta.Test.Plugins;

public class NoteRepositoryTests
{
    private string _testRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "Creta.NoteTests", Guid.NewGuid().ToString("N"));
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
    public void GivenNoNotesFileWhenLoadThenCreatesNotesFileFromSample()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"),
            """
            [
              {
                "Id": "sample",
                "Content": "hello",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);

        repository.Load();

        ClassicAssert.IsTrue(File.Exists(Path.Combine(storageDirectory, "notes.json")));
        ClassicAssert.AreEqual(string.Empty, repository.LoadError);
        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("hello", repository.Notes[0].Content);
    }

    [Test]
    public void GivenInvalidJsonWhenLoadThenReturnsLoadError()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"), "{invalid json");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);

        repository.Load();

        ClassicAssert.IsNotEmpty(repository.LoadError);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
    }

    [Test]
    public void GivenValidContentWhenSaveNoteThenPersistsNewNote()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SaveNote("  new idea  ", out var savedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsNotNull(savedNote);
        ClassicAssert.AreEqual("new idea", savedNote.Content);
        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("new idea", repository.Notes[0].Content);
        ClassicAssert.IsTrue(File.ReadAllText(repository.NotesFilePath).Contains("new idea"));
    }

    [Test]
    public void GivenChineseContentWhenSaveNoteThenPersistsReadableChineseJson()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        const string expectedContent = "前端语境下，gutter是什么";
        var result = repository.SaveNote(expectedContent, out var savedNote, out var errorMessage);
        var json = File.ReadAllText(repository.NotesFilePath);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsNotNull(savedNote);
        ClassicAssert.AreEqual(expectedContent, savedNote.Content);
        ClassicAssert.IsTrue(json.Contains(expectedContent));
        ClassicAssert.IsFalse(json.Contains("\\u524D"));
    }

    [Test]
    public void GivenWhitespaceContentWhenSaveNoteThenReturnsFalse()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SaveNote("   ", out var savedNote, out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(savedNote);
        ClassicAssert.AreEqual("Note content cannot be empty.", errorMessage);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
    }

    [Test]
    public void GivenPinnedAndRecentNotesWhenGetRecentNotesThenPinnedArePrioritized()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "old-pinned",
                "Content": "pinned note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": true
              },
              {
                "Id": "new-unpinned",
                "Content": "new note",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": false
              },
              {
                "Id": "older-unpinned",
                "Content": "older note",
                "CreatedAt": "2026-06-15T00:00:00Z",
                "UpdatedAt": "2026-06-15T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);

        repository.Load();
        var recentNotes = repository.GetRecentNotes(2);

        ClassicAssert.AreEqual(2, recentNotes.Count);
        ClassicAssert.AreEqual("pinned note", recentNotes[0].Content);
        ClassicAssert.AreEqual("new note", recentNotes[1].Content);
    }

    [Test]
    public void GivenPinnedAndUnpinnedNotesWhenLoadThenPinnedAreSortedFirst()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "unpinned-newer",
                "Content": "newer unpinned",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": false
              },
              {
                "Id": "pinned-older",
                "Content": "older pinned",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": true
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);

        repository.Load();

        ClassicAssert.AreEqual(2, repository.Notes.Count);
        ClassicAssert.AreEqual("older pinned", repository.Notes[0].Content);
        ClassicAssert.AreEqual("newer unpinned", repository.Notes[1].Content);
    }

    [Test]
    public void GivenExistingNoteWhenUpdateNoteThenReplacesContentAndPersists()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "before",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.UpdateNote("note-1", " after ", out var updatedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsNotNull(updatedNote);
        ClassicAssert.AreEqual("after", updatedNote.Content);
        ClassicAssert.IsTrue(File.ReadAllText(repository.NotesFilePath).Contains("after"));
    }

    [Test]
    public void GivenExistingNoteWhenDeleteNoteThenRemovesIt()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "to delete",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.DeleteNote("note-1", out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
        ClassicAssert.IsFalse(File.ReadAllText(repository.NotesFilePath).Contains("to delete"));
    }

    [Test]
    public void GivenExistingNoteWhenSetPinnedThenMovesItToPinnedState()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "pin me",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SetPinned("note-1", true, out var updatedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsNotNull(updatedNote);
        ClassicAssert.IsTrue(updatedNote.IsPinned);
        ClassicAssert.IsTrue(File.ReadAllText(repository.NotesFilePath).Contains("\"IsPinned\": true"));
    }

    [Test]
    public void GivenSearchTextWhenSearchNotesThenReturnsMatchingNotes()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "build quick note plugin",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              },
              {
                "Id": "note-2",
                "Content": "something else",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": true
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var matches = repository.SearchNotes("quick", 10);

        ClassicAssert.AreEqual(1, matches.Count);
        ClassicAssert.AreEqual("build quick note plugin", matches[0].Content);
    }

    [Test]
    public void GivenSearchNotesWhenExactAndPinnedMatchesExistThenRankingMatchesScorerRules()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "contains-pinned",
                "Content": "daily quick note summary",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": true
              },
              {
                "Id": "exact",
                "Content": "quick note",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var matches = repository.SearchNotes("quick note", 10);

        ClassicAssert.AreEqual(2, matches.Count);
        ClassicAssert.AreEqual("quick note", matches[0].Content);
        ClassicAssert.AreEqual("daily quick note summary", matches[1].Content);
    }

    [Test]
    public void GivenDateWhenGetNotesCreatedOnThenReturnsSameDayNotes()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "today note",
                "CreatedAt": "2026-06-16T12:00:00Z",
                "UpdatedAt": "2026-06-16T12:00:00Z",
                "IsPinned": false
              },
              {
                "Id": "note-2",
                "Content": "other day",
                "CreatedAt": "2026-06-15T12:00:00Z",
                "UpdatedAt": "2026-06-15T12:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var notes = repository.GetNotesCreatedOn(new DateTime(2026, 6, 16), 10);

        ClassicAssert.AreEqual(1, notes.Count);
        ClassicAssert.AreEqual("today note", notes[0].Content);
    }

    [Test]
    public void GivenDifferentMatchStrengthsWhenScoringThenExactMatchRanksHighest()
    {
        var exact = new NoteItem
        {
            Id = "exact",
            Content = "quick note",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };
        var prefix = new NoteItem
        {
            Id = "prefix",
            Content = "quick note plugin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };
        var contains = new NoteItem
        {
            Id = "contains",
            Content = "build a quick note plugin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        var exactMatch = NoteSearchScorer.Match(exact, "quick note");
        var prefixMatch = NoteSearchScorer.Match(prefix, "quick note");
        var containsMatch = NoteSearchScorer.Match(contains, "quick note");

        ClassicAssert.IsNotNull(exactMatch);
        ClassicAssert.IsNotNull(prefixMatch);
        ClassicAssert.IsNotNull(containsMatch);
        ClassicAssert.Greater(exactMatch!.Score, prefixMatch!.Score);
        ClassicAssert.Greater(prefixMatch.Score, containsMatch!.Score);
    }

    [Test]
    public void GivenPinnedAndUnpinnedSameMatchWhenScoringThenPinnedRanksHigher()
    {
        var pinned = new NoteItem
        {
            Id = "pinned",
            Content = "project roadmap",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = true
        };
        var normal = new NoteItem
        {
            Id = "normal",
            Content = "project roadmap",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        var pinnedMatch = NoteSearchScorer.Match(pinned, "project roadmap");
        var normalMatch = NoteSearchScorer.Match(normal, "project roadmap");

        ClassicAssert.IsNotNull(pinnedMatch);
        ClassicAssert.IsNotNull(normalMatch);
        ClassicAssert.Greater(pinnedMatch!.Score, normalMatch!.Score);
    }

    [Test]
    public void GivenExactOrPrefixMatchesWhenScoringThenTheyAreMarkedHighSimilarity()
    {
        var exact = new NoteItem
        {
            Id = "exact",
            Content = "weekly sync",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };
        var prefix = new NoteItem
        {
            Id = "prefix",
            Content = "weekly sync notes",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };
        var broad = new NoteItem
        {
            Id = "broad",
            Content = "notes for the weekly product sync meeting",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        var exactMatch = NoteSearchScorer.Match(exact, "weekly sync");
        var prefixMatch = NoteSearchScorer.Match(prefix, "weekly sync");
        var broadMatch = NoteSearchScorer.Match(broad, "sync");

        ClassicAssert.IsTrue(exactMatch!.IsHighSimilarity);
        ClassicAssert.IsTrue(prefixMatch!.IsHighSimilarity);
        ClassicAssert.IsFalse(broadMatch!.IsExactMatch);
    }

    [Test]
    public void GivenAllNotesWhenGetAllNotesThenReturnsCurrentSortedOrder()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "regular note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              },
              {
                "Id": "note-2",
                "Content": "pinned note",
                "CreatedAt": "2026-06-15T00:00:00Z",
                "UpdatedAt": "2026-06-15T00:00:00Z",
                "IsPinned": true
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var notes = repository.GetAllNotes(10);

        ClassicAssert.AreEqual(2, notes.Count);
        ClassicAssert.AreEqual("pinned note", notes[0].Content);
        ClassicAssert.AreEqual("regular note", notes[1].Content);
    }

    [Test]
    public void GivenTaggedNoteWhenSaveNoteThenStripsTagsFromContentAndExtractsTags()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SaveNote("ship plugin #flow #note", out var savedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("ship plugin", savedNote.Content);
        ClassicAssert.AreEqual(2, savedNote.Tags.Count);
        ClassicAssert.IsTrue(savedNote.Tags.Contains("flow"));
        ClassicAssert.IsTrue(savedNote.Tags.Contains("note"));
        ClassicAssert.IsFalse(File.ReadAllText(repository.NotesFilePath).Contains("#flow"));
    }

    [Test]
    public void GivenArchivedNoteWhenGetRecentNotesThenItIsExcluded()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "archived note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "IsArchived": true,
                "Tags": []
              },
              {
                "Id": "note-2",
                "Content": "active note",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var notes = repository.GetRecentNotes(10);

        ClassicAssert.AreEqual(1, notes.Count);
        ClassicAssert.AreEqual("active note", notes[0].Content);
    }

    [Test]
    public void GivenArchivedStateWhenSetArchivedThenMovesNoteBetweenViews()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "archive me #work",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": true,
                "IsArchived": false,
                "Tags": [ "work" ]
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SetArchived("note-1", true, out var updatedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsTrue(updatedNote.IsArchived);
        ClassicAssert.IsFalse(updatedNote.IsPinned);
        ClassicAssert.AreEqual(0, repository.GetRecentNotes(10).Count);
        ClassicAssert.AreEqual(1, repository.GetArchivedNotes(10).Count);
    }

    [Test]
    public void GivenTagFilterWhenGetNotesByTagThenReturnsMatchingActiveNotes()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "active work note #work",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": [ "work" ]
              },
              {
                "Id": "note-2",
                "Content": "archived work note #work",
                "CreatedAt": "2026-06-15T00:00:00Z",
                "UpdatedAt": "2026-06-15T00:00:00Z",
                "IsPinned": false,
                "IsArchived": true,
                "Tags": [ "work" ]
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var notes = repository.GetNotesByTag("work", 10);

        ClassicAssert.AreEqual(1, notes.Count);
        ClassicAssert.AreEqual("active work note", notes[0].Content);
    }

    [Test]
    public void GivenTaggedContentWhenUpdateNoteThenStripsTagsFromContentAndPersistsTagsOnly()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "before",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.UpdateNote("note-1", "question body #frontend", out var updatedNote, out var errorMessage);
        var json = File.ReadAllText(repository.NotesFilePath);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("question body", updatedNote.Content);
        ClassicAssert.AreEqual(1, updatedNote.Tags.Count);
        ClassicAssert.IsTrue(updatedNote.Tags.Contains("frontend"));
        ClassicAssert.IsFalse(json.Contains("#frontend"));
        ClassicAssert.IsTrue(json.Contains("\"Tags\": ["));
    }

    [Test]
    public void GivenLegacyTaggedContentWhenLoadThenNormalizesContentAndKeepsTags()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "问题：前端语境下，如何定义出外层和内层 #前端",
                "CreatedAt": "2026-06-23T13:00:36.7454032Z",
                "UpdatedAt": "2026-06-23T13:00:36.7454032Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": [ "前端" ]
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);

        repository.Load();

        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("问题：前端语境下，如何定义出外层和内层", repository.Notes[0].Content);
        ClassicAssert.AreEqual(1, repository.Notes[0].Tags.Count);
        ClassicAssert.AreEqual("前端", repository.Notes[0].Tags[0]);
    }

    [Test]
    public void GivenExistingTaggedNoteWhenBuildEditableContentThenAppendsTagsBackForEditing()
    {
        var note = new NoteItem
        {
            Id = "note-1",
            Content = "问题：前端语境下，如何定义出外层和内层",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tags = [ "前端", "布局" ]
        };

        var editableContent = NoteRepository.BuildEditableContent(note);

        ClassicAssert.AreEqual("问题：前端语境下，如何定义出外层和内层 #前端 #布局", editableContent);
    }

    [Test]
    public void GivenStoragePathChangesWhenUpdateStoragePathThenMigratesExistingNotesToNewPath()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        var customDirectory = Path.Combine(_testRoot, "custom");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "migrate me",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.UpdateStoragePath(Path.Combine(customDirectory, "notes.json"));

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.IsTrue(result.PathChanged);
        ClassicAssert.IsFalse(result.NotesMerged);
        ClassicAssert.AreEqual(1, result.MigratedNoteCount);
        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("migrate me", repository.Notes[0].Content);
        ClassicAssert.IsTrue(File.Exists(Path.Combine(customDirectory, "notes.json")));
        ClassicAssert.IsTrue(File.ReadAllText(Path.Combine(customDirectory, "notes.json")).Contains("migrate me"));
    }

    [Test]
    public void GivenTargetPathHasNotesWhenUpdateStoragePathThenMergesSourceAndTargetNotes()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        var customDirectory = Path.Combine(_testRoot, "custom");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        Directory.CreateDirectory(customDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "source-note",
                "Content": "source note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);
        File.WriteAllText(Path.Combine(customDirectory, "notes.json"),
            """
            [
              {
                "Id": "target-note",
                "Content": "target note",
                "CreatedAt": "2026-06-17T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": true
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.UpdateStoragePath(Path.Combine(customDirectory, "notes.json"));

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.IsTrue(result.PathChanged);
        ClassicAssert.IsTrue(result.NotesMerged);
        ClassicAssert.AreEqual(1, result.MigratedNoteCount);
        ClassicAssert.AreEqual(1, result.ExistingTargetNoteCount);
        ClassicAssert.AreEqual(2, repository.Notes.Count);
        ClassicAssert.IsTrue(File.ReadAllText(Path.Combine(customDirectory, "notes.json")).Contains("source note"));
        ClassicAssert.IsTrue(File.ReadAllText(Path.Combine(customDirectory, "notes.json")).Contains("target note"));
    }

    [Test]
    public void GivenStoragePathUnchangedWhenUpdateStoragePathThenDoesNotMigrateAgain()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "stay here",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.UpdateStoragePath(string.Empty);

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.IsFalse(result.PathChanged);
        ClassicAssert.AreEqual(1, result.CurrentNoteCount);
        ClassicAssert.AreEqual("stay here", repository.Notes[0].Content);
        ClassicAssert.IsTrue(File.ReadAllText(Path.Combine(storageDirectory, "notes.json")).Contains("stay here"));
    }

    [Test]
    public void GivenMixedNotesWhenGetAllNotesWithoutLimitThenReturnsAllIncludingArchived()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "active note",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              },
              {
                "Id": "note-2",
                "Content": "archived note",
                "CreatedAt": "2026-06-15T00:00:00Z",
                "UpdatedAt": "2026-06-15T00:00:00Z",
                "IsPinned": false,
                "IsArchived": true,
                "Tags": []
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var limitedNotes = repository.GetAllNotes(10);
        var allNotes = repository.GetAllNotes();

        ClassicAssert.AreEqual(1, limitedNotes.Count);
        ClassicAssert.AreEqual("active note", limitedNotes[0].Content);
        ClassicAssert.AreEqual(2, allNotes.Count);
        ClassicAssert.AreEqual("active note", allNotes[0].Content);
        ClassicAssert.AreEqual("archived note", allNotes[1].Content);
    }

    [Test]
    public void GivenExternalFileChangeWhenReloadThenRefreshesInMemoryNotes()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "before reload",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "after reload",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-17T00:00:00Z",
                "IsPinned": false
              }
            ]
            """);

        repository.Reload();

        ClassicAssert.AreEqual(string.Empty, repository.LoadError);
        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("after reload", repository.Notes[0].Content);
    }

    [Test]
    public void GivenExistingNoteWhenSetTagsThenUpdatesTagsWithoutChangingContent()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "keep this body",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "Tags": [ "old" ]
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SetTags("note-1", [ "#Work ", "work", "idea" ], out var updatedNote, out var errorMessage);
        var json = File.ReadAllText(repository.NotesFilePath);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("keep this body", updatedNote.Content);
        ClassicAssert.AreEqual(2, updatedNote.Tags.Count);
        ClassicAssert.IsTrue(updatedNote.Tags.Contains("work"));
        ClassicAssert.IsTrue(updatedNote.Tags.Contains("idea"));
        ClassicAssert.IsFalse(json.Contains("#Work"));
        ClassicAssert.IsTrue(json.Contains("\"work\""));
    }

    [Test]
    public void GivenTagsSetIndependentlyWhenUpdateNoteWithoutHashTagsThenPreservesExistingTags()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "note-1",
                "Content": "draft",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "Tags": []
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        ClassicAssert.IsTrue(repository.SetTags("note-1", [ "planning" ], out _, out _));

        var result = repository.UpdateNote("note-1", "draft updated", out var updatedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("draft updated", updatedNote.Content);
        ClassicAssert.AreEqual(1, updatedNote.Tags.Count);
        ClassicAssert.IsTrue(updatedNote.Tags.Contains("planning"));
    }

    [Test]
    public void GivenUnknownNoteWhenSetTagsThenReturnsError()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var result = repository.SetTags("missing", [ "work" ], out var updatedNote, out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(updatedNote);
        ClassicAssert.AreEqual("Note not found.", errorMessage);
    }

    [Test]
    public void GivenWeekRangeWhenGetNotesCreatedThisWeekThenReturnsNotesInCurrentWeekOnly()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);

        var (weekStart, _) = NoteRepository.GetCurrentWeekRangeLocal();
        var inWeekCreatedAt = weekStart.AddDays(1).AddHours(9).ToUniversalTime();
        var outWeekCreatedAt = weekStart.AddDays(-1).AddHours(9).ToUniversalTime();

        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            $$"""
            [
              {
                "Id": "note-in-week",
                "Content": "inside week",
                "CreatedAt": "{{inWeekCreatedAt:O}}",
                "UpdatedAt": "{{inWeekCreatedAt:O}}",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              },
              {
                "Id": "note-last-week",
                "Content": "outside week",
                "CreatedAt": "{{outWeekCreatedAt:O}}",
                "UpdatedAt": "{{outWeekCreatedAt:O}}",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              }
            ]
            """);

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        var notes = repository.GetNotesCreatedThisWeek(10);

        ClassicAssert.AreEqual(1, notes.Count);
        ClassicAssert.AreEqual("inside week", notes[0].Content);
    }

    [Test]
    public void GivenExportedTextFormatWhenSplitThenStripsMetadataAndSplitsBySeparator()
    {
        var content = """
            创建时间 2026-06-16 10:00
            更新时间 2026-06-16 10:00
            标签 #work
            first note

            ----------------------------------------

            创建时间 2026-06-16 11:00
            更新时间 2026-06-16 11:00
            标签
            second note
            """;

        var chunks = NoteTextImportParser.Split(content, NotesTextImportSplitMode.DashSeparator);

        ClassicAssert.AreEqual(2, chunks.Count);
        ClassicAssert.AreEqual("first note", chunks[0]);
        ClassicAssert.AreEqual("second note", chunks[1]);
    }

    [Test]
    public void GivenMarkdownExportFormatWhenSplitThenStripsMetadata()
    {
        var content = """
            ## 1. Title one

            - **创建时间** 2026-06-16 10:00
            - **更新时间** 2026-06-16 10:00
            - **标签** #idea

            markdown body

            ## 2. Title two

            - **Created** 2026-06-16 11:00
            - **Updated** 2026-06-16 11:00
            - **Tags** #work

            another body
            """;

        var chunks = NoteTextImportParser.Split(content, NotesTextImportSplitMode.MarkdownHeading);

        ClassicAssert.AreEqual(2, chunks.Count);
        ClassicAssert.AreEqual("markdown body", chunks[0]);
        ClassicAssert.AreEqual("another body", chunks[1]);
    }

    [Test]
    public void GivenDuplicateContentWhenImportTextNotesWithSkipThenSkipsDuplicate()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();
        ClassicAssert.IsTrue(repository.SaveNote("existing note", out _, out _));

        var result = repository.ImportTextNotes(["existing note", "new note"], skipDuplicates: true);

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual(1, result.ImportedCount);
        ClassicAssert.AreEqual(1, result.SkippedDuplicateCount);
        ClassicAssert.AreEqual(2, repository.GetAllNotes().Count);
        ClassicAssert.IsTrue(repository.GetAllNotes().Any(note => note.Content == "new note"));
    }

    [Test]
    public void GivenJsonFileWhenImportJsonNotesThenMergesById()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();
        ClassicAssert.IsTrue(repository.SaveNote("local note", out _, out _));

        var importPath = Path.Combine(_testRoot, "import.json");
        File.WriteAllText(importPath,
            """
            [
              {
                "Id": "imported-note",
                "Content": "from json",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "IsArchived": false,
                "Tags": []
              }
            ]
            """);

        var result = repository.ImportJsonNotes(importPath);

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual(1, result.ImportedCount);
        ClassicAssert.AreEqual(2, repository.GetAllNotes().Count);
        ClassicAssert.IsTrue(repository.GetAllNotes().Any(note => note.Content == "from json"));
    }

    [Test]
    public void GivenLegacyNotesWithoutExtensionFieldsWhenLoadThenUsesDefaults()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        File.WriteAllText(Path.Combine(storageDirectory, "notes.json"),
            """
            [
              {
                "Id": "legacy-note",
                "Content": "legacy content",
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

        var note = repository.GetAllNotes().Single();
        ClassicAssert.AreEqual(string.Empty, note.Source);
        ClassicAssert.IsNull(note.LastViewedAt);
    }

    [Test]
    public void GivenSaveNoteWithSourceWhenPersistThenSourceIsWritten()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();

        ClassicAssert.IsTrue(repository.SaveNote("editor note", out _, out _, NoteSources.Editor));

        repository.Reload();
        var note = repository.GetAllNotes().Single();
        ClassicAssert.AreEqual(NoteSources.Editor, note.Source);
    }

    [Test]
    public void GivenRecordLastViewedWhenCalledThenUpdatesTimestampWithoutChangingUpdatedAt()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();
        ClassicAssert.IsTrue(repository.SaveNote("viewed note", out var savedNote, out _));

        var updatedAt = savedNote.UpdatedAt;
        ClassicAssert.IsTrue(repository.RecordLastViewed(savedNote.Id, out _));

        repository.Reload();
        var note = repository.GetAllNotes().Single();
        ClassicAssert.IsNotNull(note.LastViewedAt);
        ClassicAssert.AreEqual(updatedAt, note.UpdatedAt);
    }

    [Test]
    public void GivenImageBytesWhenSaveImageNoteThenPersistsReferenceAndFile()
    {
        var repository = CreateEmptyRepository();
        var imageBytes = CreateMinimalPng();

        var result = repository.SaveImageNote(
            imageBytes,
            "shot.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsNotNull(savedNote);
        ClassicAssert.AreEqual(string.Empty, savedNote.Content);
        ClassicAssert.AreEqual(1, savedNote.Attachments.Count);
        ClassicAssert.AreEqual("shot.png", savedNote.Attachments[0].FileName);
        ClassicAssert.AreEqual("image/png", savedNote.Attachments[0].MimeType);
        ClassicAssert.AreEqual(NoteAttachmentSources.Clipboard, savedNote.Attachments[0].Source);
        ClassicAssert.IsTrue(File.Exists(GetAttachmentFullPath(repository, savedNote.Attachments[0])));

        var json = File.ReadAllText(repository.NotesFilePath);
        ClassicAssert.IsTrue(json.Contains("Attachments"));
        ClassicAssert.IsTrue(json.Contains(savedNote.Attachments[0].RelativePath));
        ClassicAssert.IsFalse(json.Contains("iVBORw0KGgo"));
    }

    [Test]
    public void GivenImageOnlyNoteWhenLoadThenKeepsEmptyContentNote()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));

        repository.Reload();

        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual(savedNote.Id, repository.Notes[0].Id);
        ClassicAssert.AreEqual(string.Empty, repository.Notes[0].Content);
        ClassicAssert.AreEqual(1, repository.Notes[0].Attachments.Count);
        ClassicAssert.IsTrue(File.Exists(GetAttachmentFullPath(repository, repository.Notes[0].Attachments[0])));
    }

    [Test]
    public void GivenTextNoteWhenPersistThenOmitsAttachmentsField()
    {
        var repository = CreateEmptyRepository();

        ClassicAssert.IsTrue(repository.SaveNote("plain text", out _, out _));

        var json = File.ReadAllText(repository.NotesFilePath);
        ClassicAssert.IsFalse(json.Contains("Attachments"));
    }

    [Test]
    public void GivenImageNoteWhenDeleteNoteThenRemovesAttachmentDirectory()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            "caption",
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));
        var attachmentDirectory = Path.GetDirectoryName(GetAttachmentFullPath(repository, savedNote.Attachments[0]));

        ClassicAssert.IsTrue(repository.DeleteNote(savedNote.Id, out var errorMessage));

        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
        ClassicAssert.IsFalse(Directory.Exists(attachmentDirectory));
    }

    [Test]
    public void GivenTextNoteWithImageWhenDeleteAttachmentThenKeepsNoteAndRemovesFile()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveNote("keep this", out var savedNote, out _));
        ClassicAssert.IsTrue(repository.AddImageAttachment(
            savedNote.Id,
            CreateMinimalPng(),
            "extra.png",
            NoteAttachmentSources.File,
            out var attachment,
            out _));
        var fullPath = GetAttachmentFullPath(repository, attachment);

        ClassicAssert.IsTrue(repository.DeleteAttachment(savedNote.Id, attachment.Id, out var errorMessage));
        repository.Reload();

        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual(1, repository.Notes.Count);
        ClassicAssert.AreEqual("keep this", repository.Notes[0].Content);
        ClassicAssert.AreEqual(0, repository.Notes[0].Attachments.Count);
        ClassicAssert.IsFalse(File.Exists(fullPath));
    }

    [Test]
    public void GivenImageOnlyNoteWhenDeleteLastAttachmentThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));

        var result = repository.DeleteAttachment(savedNote.Id, savedNote.Attachments[0].Id, out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.AreEqual("Cannot remove the last attachment from a note without text.", errorMessage);
        ClassicAssert.AreEqual(1, repository.Notes[0].Attachments.Count);
        ClassicAssert.IsTrue(File.Exists(GetAttachmentFullPath(repository, savedNote.Attachments[0])));
    }

    [Test]
    public void GivenImageNoteWhenUpdateNoteWithEmptyContentThenKeepsNote()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            "caption",
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));

        var result = repository.UpdateNote(savedNote.Id, "   ", out var updatedNote, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual(string.Empty, updatedNote.Content);
        ClassicAssert.AreEqual(1, updatedNote.Attachments.Count);
    }

    [Test]
    public void GivenTextNoteWhenUpdateNoteWithEmptyContentThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveNote("body", out var savedNote, out _));

        var result = repository.UpdateNote(savedNote.Id, "   ", out var updatedNote, out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(updatedNote);
        ClassicAssert.AreEqual("Note content cannot be empty.", errorMessage);
        ClassicAssert.AreEqual("body", repository.Notes[0].Content);
    }

    [Test]
    public void GivenTooManyAttachmentsWhenAddImageAttachmentThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveNote("gallery", out var savedNote, out _));

        for (var index = 0; index < NoteAttachmentConstraints.MaxAttachmentsPerNote; index++)
        {
            ClassicAssert.IsTrue(repository.AddImageAttachment(
                savedNote.Id,
                CreateMinimalPng(),
                $"shot-{index}.png",
                NoteAttachmentSources.Clipboard,
                out _,
                out _));
        }

        var result = repository.AddImageAttachment(
            savedNote.Id,
            CreateMinimalPng(),
            "overflow.png",
            NoteAttachmentSources.Clipboard,
            out var attachment,
            out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(attachment);
        ClassicAssert.AreEqual("A note can have at most 5 attachments.", errorMessage);
        ClassicAssert.AreEqual(NoteAttachmentConstraints.MaxAttachmentsPerNote, repository.Notes[0].Attachments.Count);
    }

    [Test]
    public void GivenUnsupportedBytesWhenSaveImageNoteThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();

        var result = repository.SaveImageNote(
            [1, 2, 3, 4],
            "file.bin",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(savedNote);
        ClassicAssert.AreEqual("Unsupported image format.", errorMessage);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
    }

    [Test]
    public void GivenOversizedImageWhenSaveImageNoteThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();
        var oversized = new byte[NoteAttachmentConstraints.MaxByteSize + 1];
        oversized[0] = 0x89;
        oversized[1] = 0x50;
        oversized[2] = 0x4E;
        oversized[3] = 0x47;
        oversized[4] = 0x0D;
        oversized[5] = 0x0A;
        oversized[6] = 0x1A;
        oversized[7] = 0x0A;

        var result = repository.SaveImageNote(
            oversized,
            "huge.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(savedNote);
        ClassicAssert.AreEqual("Image exceeds the 10 MB limit.", errorMessage);
        ClassicAssert.AreEqual(0, repository.Notes.Count);
    }

    [Test]
    public void GivenImageNoteWhenUpdateStoragePathThenMigratesAttachmentFiles()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            "migrate image",
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));
        var originalPath = GetAttachmentFullPath(repository, savedNote.Attachments[0]);
        var customDirectory = Path.Combine(_testRoot, "custom");

        var result = repository.UpdateStoragePath(Path.Combine(customDirectory, "notes.json"));
        var migratedPath = Path.Combine(customDirectory, savedNote.Attachments[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.IsTrue(result.PathChanged);
        ClassicAssert.IsTrue(File.Exists(migratedPath));
        ClassicAssert.IsTrue(File.Exists(originalPath));
        ClassicAssert.AreEqual(1, repository.Notes[0].Attachments.Count);
    }

    [Test]
    public void GivenConflictingImageNotesWhenUpdateStoragePathThenKeepsNewerSourceAttachments()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        var customDirectory = Path.Combine(_testRoot, "custom");
        Directory.CreateDirectory(pluginDirectory);
        Directory.CreateDirectory(storageDirectory);
        Directory.CreateDirectory(customDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var sourceRepository = new NoteRepository(pluginDirectory, storageDirectory);
        sourceRepository.Load();
        ClassicAssert.IsTrue(sourceRepository.SaveNote("shared", out var sourceNote, out _));
        ClassicAssert.IsTrue(sourceRepository.AddImageAttachment(
            sourceNote.Id,
            CreateMinimalPng(),
            "source.png",
            NoteAttachmentSources.Clipboard,
            out var sourceAttachment,
            out _));

        var targetRepository = new NoteRepository(pluginDirectory, customDirectory, Path.Combine(customDirectory, "notes.json"));
        targetRepository.Load();
        ClassicAssert.IsTrue(targetRepository.SaveNote("shared", out _, out _));
        File.WriteAllText(
            Path.Combine(customDirectory, "notes.json"),
            $$"""
            [
              {
                "Id": "{{sourceNote.Id}}",
                "Content": "older target",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "Attachments": [
                  {
                    "Id": "old-attachment",
                    "FileName": "target.png",
                    "RelativePath": "attachments/{{sourceNote.Id}}/old-attachment.png",
                    "MimeType": "image/png",
                    "ByteSize": 1,
                    "CreatedAt": "2026-06-16T00:00:00Z",
                    "Source": "file"
                  }
                ]
              }
            ]
            """);
        var targetAttachmentDirectory = Path.Combine(customDirectory, "attachments", sourceNote.Id);
        Directory.CreateDirectory(targetAttachmentDirectory);
        File.WriteAllText(Path.Combine(targetAttachmentDirectory, "old-attachment.png"), "old");

        var result = sourceRepository.UpdateStoragePath(Path.Combine(customDirectory, "notes.json"));
        var migratedPath = Path.Combine(customDirectory, sourceAttachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.IsTrue(result.NotesMerged);
        ClassicAssert.AreEqual("shared", sourceRepository.Notes.Single(note => note.Id == sourceNote.Id).Content);
        ClassicAssert.AreEqual("source.png", sourceRepository.Notes.Single(note => note.Id == sourceNote.Id).Attachments[0].FileName);
        ClassicAssert.IsTrue(File.Exists(migratedPath));
        ClassicAssert.IsFalse(File.Exists(Path.Combine(targetAttachmentDirectory, "old-attachment.png")));
    }

    [Test]
    public void GivenImagesWhenSaveNoteWithImagesThenPersistsAllAttachments()
    {
        var repository = CreateEmptyRepository();
        var images = new[]
        {
            new NoteImageWriteRequest
            {
                Bytes = CreateMinimalPng(),
                FileName = "one.png",
                Source = NoteAttachmentSources.Clipboard
            },
            new NoteImageWriteRequest
            {
                Bytes = CreateMinimalPng(),
                FileName = "two.png",
                Source = NoteAttachmentSources.Clipboard
            }
        };

        var result = repository.SaveNoteWithImages(
            "two shots",
            images,
            out var savedNote,
            out var errorMessage,
            NoteSources.Editor);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("two shots", savedNote.Content);
        ClassicAssert.AreEqual(2, savedNote.Attachments.Count);
        ClassicAssert.AreEqual(NoteSources.Editor, savedNote.Source);
        ClassicAssert.IsTrue(File.Exists(repository.GetAttachmentFullPath(savedNote.Attachments[0])));
        ClassicAssert.IsTrue(File.Exists(repository.GetAttachmentFullPath(savedNote.Attachments[1])));
    }

    [Test]
    public void GivenExistingNoteWhenUpdateNoteWithAttachmentsThenAddsAndRemovesFiles()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveNote("caption", out var savedNote, out _));
        ClassicAssert.IsTrue(repository.AddImageAttachment(
            savedNote.Id,
            CreateMinimalPng(),
            "old.png",
            NoteAttachmentSources.Clipboard,
            out var existing,
            out _));
        var existingPath = repository.GetAttachmentFullPath(existing);

        var result = repository.UpdateNoteWithAttachments(
            savedNote.Id,
            "updated",
            [
                new NoteImageWriteRequest
                {
                    Bytes = CreateMinimalPng(),
                    FileName = "new.png",
                    Source = NoteAttachmentSources.File
                }
            ],
            [existing.Id],
            out var updatedNote,
            out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.AreEqual("updated", updatedNote.Content);
        ClassicAssert.AreEqual(1, updatedNote.Attachments.Count);
        ClassicAssert.AreEqual("new.png", updatedNote.Attachments[0].FileName);
        ClassicAssert.IsFalse(File.Exists(existingPath));
        ClassicAssert.IsTrue(File.Exists(repository.GetAttachmentFullPath(updatedNote.Attachments[0])));
    }

    [Test]
    public void GivenImageOnlyNoteWhenUpdateNoteWithAttachmentsClearsAllThenReturnsFalse()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));

        var result = repository.UpdateNoteWithAttachments(
            savedNote.Id,
            "   ",
            [],
            [savedNote.Attachments[0].Id],
            out var updatedNote,
            out var errorMessage);

        ClassicAssert.IsFalse(result);
        ClassicAssert.IsNull(updatedNote);
        ClassicAssert.AreEqual("Note content cannot be empty.", errorMessage);
        ClassicAssert.AreEqual(1, repository.Notes[0].Attachments.Count);
        ClassicAssert.IsTrue(File.Exists(repository.GetAttachmentFullPath(savedNote.Attachments[0])));
    }

    [Test]
    public void GivenImageNoteWhenExportFullBackupThenZipContainsJsonAndAttachment()
    {
        var repository = CreateEmptyRepository();
        ClassicAssert.IsTrue(repository.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            "caption",
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));
        var zipPath = Path.Combine(_testRoot, "full-backup.zip");

        var result = repository.ExportFullBackup(zipPath, out var errorMessage);

        ClassicAssert.IsTrue(result);
        ClassicAssert.AreEqual(string.Empty, errorMessage);
        ClassicAssert.IsTrue(File.Exists(zipPath));
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
        ClassicAssert.IsTrue(zip.Entries.Any(entry => entry.FullName == "notes.json"));
        ClassicAssert.IsTrue(zip.Entries.Any(entry =>
            entry.FullName.Replace('\\', '/') == savedNote.Attachments[0].RelativePath));
    }

    [Test]
    public void GivenJsonWithSiblingAttachmentsWhenImportJsonNotesThenCopiesFiles()
    {
        var repository = CreateEmptyRepository();
        var importDirectory = Path.Combine(_testRoot, "import-lib");
        var attachmentDirectory = Path.Combine(importDirectory, "attachments", "imported-image");
        Directory.CreateDirectory(attachmentDirectory);
        File.WriteAllBytes(Path.Combine(attachmentDirectory, "shot.png"), CreateMinimalPng());
        File.WriteAllText(
            Path.Combine(importDirectory, "notes.json"),
            """
            [
              {
                "Id": "imported-image",
                "Content": "",
                "CreatedAt": "2026-06-16T00:00:00Z",
                "UpdatedAt": "2026-06-16T00:00:00Z",
                "IsPinned": false,
                "Attachments": [
                  {
                    "Id": "shot",
                    "FileName": "shot.png",
                    "RelativePath": "attachments/imported-image/shot.png",
                    "MimeType": "image/png",
                    "ByteSize": 70,
                    "CreatedAt": "2026-06-16T00:00:00Z",
                    "Source": "import"
                  }
                ]
              }
            ]
            """);

        var result = repository.ImportJsonNotes(Path.Combine(importDirectory, "notes.json"));

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual(1, result.ImportedCount);
        var imported = repository.Notes.Single(note => note.Id == "imported-image");
        ClassicAssert.AreEqual(1, imported.Attachments.Count);
        ClassicAssert.IsTrue(File.Exists(repository.GetAttachmentFullPath(imported.Attachments[0])));
    }

    [Test]
    public void GivenFullBackupZipWhenImportFullBackupThenRestoresAttachments()
    {
        var source = CreateEmptyRepository();
        ClassicAssert.IsTrue(source.SaveImageNote(
            CreateMinimalPng(),
            "clip.png",
            string.Empty,
            NoteAttachmentSources.Clipboard,
            out var savedNote,
            out _));
        var zipPath = Path.Combine(_testRoot, "restore.zip");
        ClassicAssert.IsTrue(source.ExportFullBackup(zipPath, out _));

        var targetPlugin = Path.Combine(_testRoot, "target-plugin");
        var targetStorage = Path.Combine(_testRoot, "target-storage");
        Directory.CreateDirectory(targetPlugin);
        File.WriteAllText(Path.Combine(targetPlugin, "notes.sample.json"), "[]");
        var target = new NoteRepository(targetPlugin, targetStorage);
        target.Load();

        var result = target.ImportFullBackup(zipPath);

        ClassicAssert.IsTrue(result.Succeeded);
        ClassicAssert.AreEqual(1, result.ImportedCount);
        var restored = target.Notes.Single(note => note.Id == savedNote.Id);
        ClassicAssert.AreEqual(1, restored.Attachments.Count);
        ClassicAssert.IsTrue(File.Exists(target.GetAttachmentFullPath(restored.Attachments[0])));
    }

    private NoteRepository CreateEmptyRepository()
    {
        var pluginDirectory = Path.Combine(_testRoot, "plugin");
        var storageDirectory = Path.Combine(_testRoot, "storage");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(pluginDirectory, "notes.sample.json"), "[]");

        var repository = new NoteRepository(pluginDirectory, storageDirectory);
        repository.Load();
        return repository;
    }

    private static string GetAttachmentFullPath(NoteRepository repository, NoteAttachment attachment)
    {
        var notesDirectory = Path.GetDirectoryName(repository.NotesFilePath);
        return Path.Combine(notesDirectory!, attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static byte[] CreateMinimalPng()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }
}
