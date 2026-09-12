using System;
using System.IO;
using System.Linq;

namespace Creta.Plugin.Note;

internal static class NoteAttachmentStorage
{
    internal const string DirectoryName = "attachments";

    internal static string GetRootDirectory(string notesDirectory)
    {
        return Path.Combine(notesDirectory, DirectoryName);
    }

    internal static string GetNoteDirectory(string notesDirectory, string noteId)
    {
        return Path.Combine(GetRootDirectory(notesDirectory), SanitizeNoteId(noteId));
    }

    internal static bool TryDetectImage(byte[] imageBytes, out string mimeType, out string extension, out string errorMessage)
    {
        mimeType = string.Empty;
        extension = string.Empty;
        errorMessage = string.Empty;

        if (imageBytes is null || imageBytes.Length == 0)
        {
            errorMessage = "Image is required.";
            return false;
        }

        if (imageBytes.Length > NoteAttachmentConstraints.MaxByteSize)
        {
            errorMessage = "Image exceeds the 10 MB limit.";
            return false;
        }

        if (HasPngSignature(imageBytes))
        {
            mimeType = "image/png";
            extension = ".png";
            return true;
        }

        if (HasJpegSignature(imageBytes))
        {
            mimeType = "image/jpeg";
            extension = ".jpg";
            return true;
        }

        if (HasGifSignature(imageBytes))
        {
            mimeType = "image/gif";
            extension = ".gif";
            return true;
        }

        if (HasBmpSignature(imageBytes))
        {
            mimeType = "image/bmp";
            extension = ".bmp";
            return true;
        }

        errorMessage = "Unsupported image format.";
        return false;
    }

    internal static NoteAttachment WriteImage(
        string notesDirectory,
        string noteId,
        byte[] imageBytes,
        string fileName,
        string source)
    {
        if (!TryDetectImage(imageBytes, out var mimeType, out var extension, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        var attachmentId = Guid.NewGuid().ToString("N");
        var relativePath = $"{DirectoryName}/{SanitizeNoteId(noteId)}/{attachmentId}{extension}";
        var fullPath = ResolveFullPath(notesDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = fullPath + ".tmp";
        File.WriteAllBytes(tempPath, imageBytes);
        File.Move(tempPath, fullPath, overwrite: true);

        return new NoteAttachment
        {
            Id = attachmentId,
            FileName = BuildDisplayName(fileName, extension),
            RelativePath = relativePath,
            MimeType = mimeType,
            ByteSize = imageBytes.Length,
            CreatedAt = DateTime.UtcNow,
            Source = source?.Trim() ?? string.Empty
        };
    }

    internal static void DeleteNoteDirectory(string notesDirectory, string noteId)
    {
        TryDeleteDirectory(GetNoteDirectory(notesDirectory, noteId));
    }

    internal static void DeleteFile(string notesDirectory, string relativePath)
    {
        var fullPath = ResolveFullPath(notesDirectory, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) &&
            Directory.Exists(directory) &&
            Directory.EnumerateFileSystemEntries(directory).Any() == false)
        {
            TryDeleteDirectory(directory);
        }
    }

    internal static void CopyNoteDirectory(string sourceNotesDirectory, string destinationNotesDirectory, string noteId, bool replaceExisting)
    {
        var sourceDirectory = GetNoteDirectory(sourceNotesDirectory, noteId);
        var destinationDirectory = GetNoteDirectory(destinationNotesDirectory, noteId);

        if (!Directory.Exists(sourceDirectory))
        {
            if (replaceExisting)
            {
                TryDeleteDirectory(destinationDirectory);
            }

            return;
        }

        if (replaceExisting)
        {
            TryDeleteDirectory(destinationDirectory);
        }

        CopyDirectory(sourceDirectory, destinationDirectory);
    }

    internal static void CopyNoteDirectoryIfSourceExists(
        string sourceNotesDirectory,
        string destinationNotesDirectory,
        string noteId)
    {
        var sourceDirectory = GetNoteDirectory(sourceNotesDirectory, noteId);
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        CopyNoteDirectory(sourceNotesDirectory, destinationNotesDirectory, noteId, replaceExisting: true);
    }

    internal static string ResolveFullPath(string notesDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Attachment path is empty.");
        }

        var root = Path.GetFullPath(notesDirectory);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attachment path is outside the notes directory.");
        }

        return combined;
    }

    private static string BuildDisplayName(string fileName, string extension)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "screenshot" + extension;
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(withoutExtension))
        {
            return "screenshot" + extension;
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            withoutExtension = withoutExtension.Replace(invalidChar, '_');
        }

        return withoutExtension + extension;
    }

    private static string SanitizeNoteId(string noteId)
    {
        var name = Path.GetFileName(noteId?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new InvalidOperationException("Invalid note id.");
        }

        return name;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private static bool HasPngSignature(byte[] data)
    {
        return data.Length >= 8 &&
               data[0] == 0x89 &&
               data[1] == 0x50 &&
               data[2] == 0x4E &&
               data[3] == 0x47 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }

    private static bool HasJpegSignature(byte[] data)
    {
        return data.Length >= 3 &&
               data[0] == 0xFF &&
               data[1] == 0xD8 &&
               data[2] == 0xFF;
    }

    private static bool HasGifSignature(byte[] data)
    {
        return data.Length >= 6 &&
               data[0] == (byte)'G' &&
               data[1] == (byte)'I' &&
               data[2] == (byte)'F' &&
               data[3] == (byte)'8' &&
               (data[4] == (byte)'7' || data[4] == (byte)'9') &&
               data[5] == (byte)'a';
    }

    private static bool HasBmpSignature(byte[] data)
    {
        return data.Length >= 2 &&
               data[0] == (byte)'B' &&
               data[1] == (byte)'M';
    }
}
