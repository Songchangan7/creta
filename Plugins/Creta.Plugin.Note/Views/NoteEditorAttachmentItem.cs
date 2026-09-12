using System.Windows.Media;

namespace Creta.Plugin.Note.Views;

internal sealed class NoteEditorAttachmentItem
{
    public string Id { get; init; } = string.Empty;

    public bool IsExisting { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public byte[] Bytes { get; init; }

    public string Source { get; init; } = string.Empty;

    public ImageSource Thumbnail { get; init; }
}
