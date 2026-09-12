using System;

namespace Creta.Plugin.Note;

public sealed class NoteAttachment
{
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string MimeType { get; set; } = "image/png";

    public int? Width { get; set; }

    public int? Height { get; set; }

    public long? ByteSize { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Source { get; set; } = string.Empty;
}
