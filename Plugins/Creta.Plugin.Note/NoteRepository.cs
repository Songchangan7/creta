using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Creta.Plugin.Note;

public sealed class NoteRepository
{
    private const string NotesFileName = "notes.json";
    private const string SampleNotesFileName = "notes.sample.json";
    private static readonly Regex TagTokenRegex = new(@"(^|[\s])#(?<tag>[^\s#]+)", RegexOptions.Compiled);
    private static readonly Regex RepeatedSpacesRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex SpacesAroundNewLineRegex = new(@"[ \t]*(\r\n|\r|\n)[ \t]*", RegexOptions.Compiled);

    private readonly string _pluginDirectory;
    private readonly string _defaultStorageDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private List<NoteItem> _notes = [];
    private string _customNotesFilePath;

    public string CustomNotesFilePath => _customNotesFilePath;

    public string NotesFilePath => ResolveNotesFilePath();

    public string LoadError { get; private set; } = string.Empty;

    public NoteRepository(string pluginDirectory, string storageDirectory)
        : this(pluginDirectory, storageDirectory, string.Empty)
    {
    }

    public NoteRepository(string pluginDirectory, string storageDirectory, string customNotesFilePath)
    {
        _pluginDirectory = pluginDirectory;
        _defaultStorageDirectory = storageDirectory;
        _customNotesFilePath = customNotesFilePath?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<NoteItem> Notes => _notes;

    public IReadOnlyList<NoteItem> GetRecentNotes(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => !note.IsArchived)
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

  public IReadOnlyList<NoteItem> GetAllNotes()
    {
        return CloneNotes(_notes);
    }

    public IReadOnlyList<NoteItem> GetAllNotes(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => !note.IsArchived)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<NoteItem> GetPinnedNotes(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => note.IsPinned && !note.IsArchived)
            .OrderByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<NoteItem> GetArchivedNotes(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => note.IsArchived)
            .OrderByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<KeyValuePair<string, int>> GetTopTags(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => !note.IsArchived)
            .SelectMany(note => note.Tags)
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(group => group.Value)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<NoteItem> GetNotesCreatedOn(DateTime date, int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        var target = date.Date;
        return _notes
            .Where(note => !note.IsArchived)
            .Where(note => note.CreatedAt.ToLocalTime().Date == target)
            .OrderByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<NoteItem> GetNotesCreatedThisWeek(int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        var (weekStart, weekEndExclusive) = GetCurrentWeekRangeLocal();
        return _notes
            .Where(note => !note.IsArchived)
            .Where(note =>
            {
                var created = note.CreatedAt.ToLocalTime();
                return created >= weekStart && created < weekEndExclusive;
            })
            .OrderByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    public static (DateTime WeekStart, DateTime WeekEndExclusive) GetCurrentWeekRangeLocal(DateTime? referenceLocalTime = null)
    {
        var reference = (referenceLocalTime ?? DateTime.Now).Date;
        var firstDayOfWeek = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var dayOffset = (7 + (reference.DayOfWeek - firstDayOfWeek)) % 7;
        var weekStart = reference.AddDays(-dayOffset);
        return (weekStart, weekStart.AddDays(7));
    }

    public IReadOnlyList<NoteItem> GetNotesByTag(string tag, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(tag) || maxCount <= 0)
        {
            return [];
        }

        var normalizedTag = NormalizeTag(tag);
        return _notes
            .Where(note => !note.IsArchived)
            .Where(note => note.Tags.Any(existingTag =>
                string.Equals(existingTag, normalizedTag, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    public IReadOnlyList<NoteItem> SearchNotes(string search, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(search) || maxCount <= 0)
        {
            return [];
        }

        return _notes
            .Where(note => !note.IsArchived)
            .Select(note => NoteSearchScorer.Match(note, search))
            .Where(match => match is not null)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Note.UpdatedAt)
            .Take(maxCount)
            .Select(match => match.Note)
            .ToList();
    }

    public NoteStorageChangeResult UpdateStoragePath(string customNotesFilePath)
    {
        var nextCustomNotesFilePath = customNotesFilePath?.Trim() ?? string.Empty;
        var previousCustomNotesFilePath = _customNotesFilePath;
        var previousPath = NotesFilePath;
        var nextPath = ResolveNotesFilePath(nextCustomNotesFilePath);

        if (PathsEqual(previousPath, nextPath))
        {
            _customNotesFilePath = nextCustomNotesFilePath;
            Load();

            return new NoteStorageChangeResult
            {
                Succeeded = string.IsNullOrWhiteSpace(LoadError),
                PathChanged = false,
                PreviousPath = previousPath,
                CurrentPath = NotesFilePath,
                CurrentNoteCount = _notes.Count,
                ErrorMessage = LoadError
            };
        }

        try
        {
            var sourceNotes = CloneNotes(_notes);
            var targetNotes = LoadNotesFromPath(nextPath, initializeIfMissing: false);
            var mergedNotes = MergeNotes(targetNotes, sourceNotes);

            PersistNotesToPath(nextPath, mergedNotes);

            _customNotesFilePath = nextCustomNotesFilePath;
            _notes = mergedNotes;
            LoadError = string.Empty;

            return new NoteStorageChangeResult
            {
                Succeeded = true,
                PathChanged = true,
                NotesMerged = targetNotes.Count > 0,
                PreviousPath = previousPath,
                CurrentPath = NotesFilePath,
                MigratedNoteCount = sourceNotes.Count,
                ExistingTargetNoteCount = targetNotes.Count,
                CurrentNoteCount = _notes.Count
            };
        }
        catch (Exception ex)
        {
            _customNotesFilePath = previousCustomNotesFilePath;
            LoadError = $"Failed to migrate notes: {ex.Message}";

            return new NoteStorageChangeResult
            {
                Succeeded = false,
                PathChanged = false,
                PreviousPath = previousPath,
                CurrentPath = previousPath,
                CurrentNoteCount = _notes.Count,
                ErrorMessage = LoadError
            };
        }
    }

    public void Load()
    {
        LoadError = string.Empty;
        _notes = [];

        try
        {
            _notes = LoadNotesFromPath(NotesFilePath, initializeIfMissing: true);
            _notes = SortNotes(_notes);
        }
        catch (Exception ex)
        {
            LoadError = $"Failed to load notes: {ex.Message}";
            _notes = [];
        }
    }

    public void Reload()
    {
        Load();
    }

    public NoteImportResult ImportTextNotes(IReadOnlyList<string> rawContents, bool skipDuplicates)
    {
        if (rawContents is null || rawContents.Count == 0)
        {
            return new NoteImportResult { Succeeded = true };
        }

        var imported = 0;
        var skippedDuplicate = 0;
        var skippedEmpty = 0;
        var existingContents = new HashSet<string>(
            _notes.Select(note => note.Content.Trim()),
            StringComparer.Ordinal);

        try
        {
            foreach (var raw in rawContents)
            {
                var normalized = NoteTextImportParser.NormalizeChunk(raw);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    skippedEmpty++;
                    continue;
                }

                var trimmed = normalized.Trim();
                if (skipDuplicates && existingContents.Contains(trimmed))
                {
                    skippedDuplicate++;
                    continue;
                }

                var parsedContent = ParseContent(trimmed);
                if (string.IsNullOrWhiteSpace(parsedContent.Content))
                {
                    skippedEmpty++;
                    continue;
                }

                var now = DateTime.UtcNow;
                var note = Normalize(new NoteItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Content = parsedContent.Content,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsPinned = false,
                    IsArchived = false,
                    Tags = parsedContent.Tags,
                    Source = NoteSources.ImportText
                });

                _notes.Insert(0, note);
                existingContents.Add(parsedContent.Content.Trim());
                imported++;
            }

            if (imported > 0)
            {
                SortNotesInPlace();
                PersistNotes();
                LoadError = string.Empty;
            }

            return new NoteImportResult
            {
                Succeeded = true,
                ImportedCount = imported,
                SkippedDuplicateCount = skippedDuplicate,
                SkippedEmptyCount = skippedEmpty
            };
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to import notes: {ex.Message}";
            LoadError = errorMessage;
            return new NoteImportResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage,
                ImportedCount = imported,
                SkippedDuplicateCount = skippedDuplicate,
                SkippedEmptyCount = skippedEmpty
            };
        }
    }

    public NoteImportResult ImportJsonNotes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new NoteImportResult
            {
                Succeeded = false,
                ErrorMessage = "Notes file not found."
            };
        }

        try
        {
            var incoming = LoadNotesFromPath(filePath, initializeIfMissing: false);
            foreach (var note in incoming)
            {
                if (string.IsNullOrWhiteSpace(note.Source))
                {
                    note.Source = NoteSources.ImportJson;
                }
            }

            var imported = 0;
            var updated = 0;
            var existingById = _notes.ToDictionary(note => note.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var note in incoming)
            {
                if (!existingById.TryGetValue(note.Id, out var existing))
                {
                    imported++;
                    continue;
                }

                if (note.UpdatedAt >= existing.UpdatedAt)
                {
                    updated++;
                }
            }

            _notes = MergeNotes(_notes, incoming);
            PersistNotes();
            LoadError = string.Empty;

            return new NoteImportResult
            {
                Succeeded = true,
                ImportedCount = imported,
                UpdatedCount = updated
            };
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to import notes: {ex.Message}";
            LoadError = errorMessage;
            return new NoteImportResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage
            };
        }
    }

    public bool SaveNote(string content, out NoteItem savedNote, out string errorMessage, string source = NoteSources.Launcher)
    {
        savedNote = null;
        errorMessage = string.Empty;

        var trimmedContent = content?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            errorMessage = "Note content cannot be empty.";
            return false;
        }

        var parsedContent = ParseContent(trimmedContent);
        if (string.IsNullOrWhiteSpace(parsedContent.Content))
        {
            errorMessage = "Note content cannot be empty.";
            return false;
        }

        try
        {
            var now = DateTime.UtcNow;
            var note = Normalize(new NoteItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Content = parsedContent.Content,
                CreatedAt = now,
                UpdatedAt = now,
                IsPinned = false,
                IsArchived = false,
                Tags = parsedContent.Tags,
                Source = NormalizeSource(source)
            });

            _notes.Insert(0, note);
            PersistNotes();
            savedNote = note;
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to save note: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool UpdateNote(string noteId, string content, out NoteItem updatedNote, out string errorMessage)
    {
        updatedNote = null;
        errorMessage = string.Empty;

        var trimmedContent = content?.Trim();
        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            errorMessage = "Note content cannot be empty.";
            return false;
        }

        var parsedContent = ParseContent(trimmedContent);
        if (string.IsNullOrWhiteSpace(parsedContent.Content))
        {
            errorMessage = "Note content cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.Content = parsedContent.Content;
            note.Tags = MergeTags(note.Tags, parsedContent.Tags);
            note.UpdatedAt = DateTime.UtcNow;
            SortNotesInPlace();
            PersistNotes();
            updatedNote = note;
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update note: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public NoteItem GetNoteById(string noteId)
    {
        if (string.IsNullOrWhiteSpace(noteId))
        {
            return null;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        return note is null ? null : CloneAndNormalize(note);
    }

    public IReadOnlyList<NoteItem> GetNotesPendingNotionSync()
    {
        return _notes
            .Where(note => note.NotionSyncPending)
            .Select(CloneAndNormalize)
            .ToList();
    }

    public bool SetNotionSyncState(string noteId, string notionPageId, bool pending, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.NotionPageId = string.IsNullOrWhiteSpace(notionPageId) ? null : notionPageId.Trim();
            note.NotionSyncPending = pending;
            PersistNotes();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update Notion sync state: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool DeleteNote(string noteId, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            _notes.Remove(note);
            PersistNotes();
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to delete note: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool SetPinned(string noteId, bool isPinned, out NoteItem updatedNote, out string errorMessage)
    {
        updatedNote = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.IsPinned = isPinned && !note.IsArchived;
            note.UpdatedAt = DateTime.UtcNow;
            SortNotesInPlace();
            PersistNotes();
            updatedNote = note;
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update note pin state: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool SetTags(string noteId, IReadOnlyList<string> tags, out NoteItem updatedNote, out string errorMessage)
    {
        updatedNote = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.Tags = NormalizeTagList(tags);
            note.UpdatedAt = DateTime.UtcNow;
            SortNotesInPlace();
            PersistNotes();
            updatedNote = note;
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update note tags: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool SetArchived(string noteId, bool isArchived, out NoteItem updatedNote, out string errorMessage)
    {
        updatedNote = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.IsArchived = isArchived;
            if (isArchived)
            {
                note.IsPinned = false;
            }

            note.UpdatedAt = DateTime.UtcNow;
            SortNotesInPlace();
            PersistNotes();
            updatedNote = note;
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update note archive state: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    public bool RecordLastViewed(string noteId, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(noteId))
        {
            errorMessage = "Note id cannot be empty.";
            return false;
        }

        var note = _notes.FirstOrDefault(x => string.Equals(x.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            errorMessage = "Note not found.";
            return false;
        }

        try
        {
            note.LastViewedAt = DateTime.UtcNow;
            PersistNotes();
            LoadError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to update note view state: {ex.Message}";
            LoadError = errorMessage;
            return false;
        }
    }

    private void InitializeNotesFile(string notesFilePath)
    {
        var samplePath = Path.Combine(_pluginDirectory, SampleNotesFileName);
        if (File.Exists(samplePath))
        {
            File.Copy(samplePath, notesFilePath, overwrite: false);
        }
        else
        {
            File.WriteAllText(notesFilePath, "[]");
        }
    }

    private void PersistNotes()
    {
        PersistNotesToPath(NotesFilePath, _notes);
    }

    private string ResolveNotesFilePath()
    {
        return ResolveNotesFilePath(_customNotesFilePath);
    }

    private string ResolveNotesFilePath(string customNotesFilePath)
    {
        if (!string.IsNullOrWhiteSpace(customNotesFilePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(customNotesFilePath));
        }

        return Path.Combine(_defaultStorageDirectory, NotesFileName);
    }

    private string GetNotesDirectoryPath()
    {
        return Path.GetDirectoryName(NotesFilePath) ?? _defaultStorageDirectory;
    }

    private void SortNotesInPlace()
    {
        _notes = SortNotes(_notes);
    }

    private static List<NoteItem> SortNotes(IEnumerable<NoteItem> notes)
    {
        return notes
            .OrderByDescending(note => !note.IsArchived)
            .ThenByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAt)
            .ToList();
    }

    private List<NoteItem> LoadNotesFromPath(string notesFilePath, bool initializeIfMissing)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(notesFilePath) ?? _defaultStorageDirectory);

        if (!File.Exists(notesFilePath))
        {
            if (!initializeIfMissing)
            {
                return [];
            }

            InitializeNotesFile(notesFilePath);
        }

        var json = File.ReadAllText(notesFilePath);
        var notes = JsonSerializer.Deserialize<List<NoteItem>>(json, _jsonOptions) ?? [];
        return notes
            .Where(note => !string.IsNullOrWhiteSpace(note.Content))
            .Select(CloneAndNormalize)
            .ToList();
    }

    private void PersistNotesToPath(string notesFilePath, IReadOnlyCollection<NoteItem> notes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(notesFilePath) ?? _defaultStorageDirectory);

        if (!File.Exists(notesFilePath))
        {
            InitializeNotesFile(notesFilePath);
        }

        var json = JsonSerializer.Serialize(SortNotes(CloneNotes(notes)), _jsonOptions);
        File.WriteAllText(notesFilePath, json);
    }

    private static List<NoteItem> MergeNotes(IEnumerable<NoteItem> existingTargetNotes, IEnumerable<NoteItem> sourceNotes)
    {
        var merged = new Dictionary<string, NoteItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in existingTargetNotes)
        {
            merged[note.Id] = CloneAndNormalize(note);
        }

        foreach (var note in sourceNotes)
        {
            var candidate = CloneAndNormalize(note);
            if (!merged.TryGetValue(candidate.Id, out var existing) ||
                candidate.UpdatedAt >= existing.UpdatedAt)
            {
                merged[candidate.Id] = candidate;
            }
        }

        return SortNotes(merged.Values);
    }

    private static List<NoteItem> CloneNotes(IEnumerable<NoteItem> notes)
    {
        return notes.Select(CloneAndNormalize).ToList();
    }

    private static NoteItem CloneAndNormalize(NoteItem note)
    {
        return Normalize(new NoteItem
        {
            Id = note.Id,
            Content = note.Content,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt,
            IsPinned = note.IsPinned,
            IsArchived = note.IsArchived,
            Tags = note.Tags?.ToList() ?? [],
            Source = note.Source,
            NotionPageId = note.NotionPageId,
            NotionSyncPending = note.NotionSyncPending,
            LastViewedAt = note.LastViewedAt
        });
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static NoteItem Normalize(NoteItem note)
    {
        note.Id ??= string.Empty;
        note.Content ??= string.Empty;
        note.Tags ??= [];
        note.Source = NormalizeSource(note.Source);
        note.NotionPageId = string.IsNullOrWhiteSpace(note.NotionPageId) ? null : note.NotionPageId.Trim();

        if (string.IsNullOrWhiteSpace(note.Id))
        {
            note.Id = Guid.NewGuid().ToString("N");
        }

        if (note.CreatedAt == default)
        {
            note.CreatedAt = DateTime.UtcNow;
        }

        if (note.UpdatedAt == default)
        {
            note.UpdatedAt = note.CreatedAt;
        }

        var parsedContent = ParseContent(note.Content);
        note.Content = parsedContent.Content;
        note.Tags = MergeTags(note.Tags, parsedContent.Tags);

        return note;
    }

    private static string NormalizeSource(string source)
    {
        return source?.Trim() ?? string.Empty;
    }

    private static List<string> ExtractTags(string content)
    {
        return TagTokenRegex.Matches(content)
            .Select(match => NormalizeTag(match.Groups["tag"].Value))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ParsedContent ParseContent(string content)
    {
        return new ParsedContent
        {
            Content = StripTagsFromContent(content),
            Tags = ExtractTags(content)
        };
    }

    private static string StripTagsFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var withoutTags = TagTokenRegex.Replace(content, static match => match.Groups[1].Value);
        withoutTags = RepeatedSpacesRegex.Replace(withoutTags, " ");
        withoutTags = SpacesAroundNewLineRegex.Replace(withoutTags, "$1");
        return withoutTags.Trim();
    }

    private static string NormalizeTag(string tag)
    {
        return tag?.Trim().TrimStart('#').ToLowerInvariant() ?? string.Empty;
    }

    private static List<string> NormalizeTagList(IEnumerable<string> tags)
    {
        return tags?
            .Select(NormalizeTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    private static List<string> MergeTags(IEnumerable<string> existingTags, IEnumerable<string> additionalTags)
    {
        return NormalizeTagList((existingTags ?? []).Concat(additionalTags ?? []));
    }

    public static string BuildEditableContent(NoteItem note)
    {
        if (note is null)
        {
            return string.Empty;
        }

        var content = note.Content?.Trim() ?? string.Empty;
        if (note.Tags is null || note.Tags.Count == 0)
        {
            return content;
        }

        var tagText = string.Join(" ", note.Tags.Select(tag => $"#{tag}"));
        return string.IsNullOrWhiteSpace(content) ? tagText : $"{content} {tagText}";
    }

    private sealed class ParsedContent
    {
        public string Content { get; init; } = string.Empty;

        public List<string> Tags { get; init; } = [];
    }
}
