namespace Creta.Plugin.Note;

public sealed class NoteImageWriteRequest
{
    public byte[] Bytes { get; set; } = [];

    public string FileName { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}
