using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Creta.Plugin.Note;

public sealed class NoteItem
{
    public string Id { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsPinned { get; set; }

    public bool IsArchived { get; set; }

    public List<string> Tags { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Source { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastViewedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NoteAttachment> Attachments { get; set; }

    [JsonIgnore]
    public bool HasAttachments => Attachments is { Count: > 0 };
}
