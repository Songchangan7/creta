using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Creta.Plugin.Note.Views;

internal static class NoteClipboardImages
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp"
    };

    internal static bool HasImage()
    {
        try
        {
            if (Clipboard.ContainsData("PNG") || Clipboard.ContainsImage())
            {
                return true;
            }

            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            foreach (var path in Clipboard.GetFileDropList())
            {
                if (IsImagePath(path))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryRead(out IReadOnlyList<NoteImageWriteRequest> images, out string errorMessage)
    {
        images = [];
        errorMessage = string.Empty;

        try
        {
            if (TryReadFileDrop(out var fileImages, out errorMessage))
            {
                images = fileImages;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            if (TryReadClipboardBitmap(out var clipboardImage, out errorMessage))
            {
                images = [clipboardImage];
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    internal static bool TryReadFiles(IEnumerable<string> filePaths, out IReadOnlyList<NoteImageWriteRequest> images, out string errorMessage)
    {
        images = [];
        errorMessage = string.Empty;
        var results = new List<NoteImageWriteRequest>();

        foreach (var filePath in filePaths ?? [])
        {
            if (!IsImagePath(filePath))
            {
                continue;
            }

            if (!TryReadFile(filePath, out var request, out errorMessage))
            {
                return false;
            }

            results.Add(request);
        }

        if (results.Count == 0)
        {
            return false;
        }

        images = results;
        return true;
    }

    internal static BitmapSource CreateThumbnail(byte[] bytes, int decodeWidth = 160)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.DecodePixelWidth = decodeWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static BitmapSource CreateThumbnailFromFile(string path, int decodeWidth = 160)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = decodeWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static BitmapSource CreatePreview(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static BitmapSource CreatePreviewFromFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool TryReadFileDrop(out IReadOnlyList<NoteImageWriteRequest> images, out string errorMessage)
    {
        images = [];
        errorMessage = string.Empty;

        if (!Clipboard.ContainsFileDropList())
        {
            return false;
        }

        var paths = new List<string>();
        foreach (var path in Clipboard.GetFileDropList())
        {
            paths.Add(path);
        }

        return TryReadFiles(paths, out images, out errorMessage);
    }

    private static bool TryReadClipboardBitmap(out NoteImageWriteRequest request, out string errorMessage)
    {
        request = null;
        errorMessage = string.Empty;

        if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is Stream pngStream)
        {
            using var copy = new MemoryStream();
            if (pngStream.CanSeek)
            {
                pngStream.Position = 0;
            }

            pngStream.CopyTo(copy);
            var bytes = copy.ToArray();
            if (NoteAttachmentStorage.TryDetectImage(bytes, out _, out _, out errorMessage))
            {
                request = new NoteImageWriteRequest
                {
                    Bytes = bytes,
                    FileName = "screenshot.png",
                    Source = NoteAttachmentSources.Clipboard
                };
                return true;
            }

            return false;
        }

        if (!Clipboard.ContainsImage())
        {
            return false;
        }

        var image = Clipboard.GetImage();
        if (image is null)
        {
            errorMessage = "Image is required.";
            return false;
        }

        request = new NoteImageWriteRequest
        {
            Bytes = EncodePng(image),
            FileName = "screenshot.png",
            Source = NoteAttachmentSources.Clipboard
        };

        if (!NoteAttachmentStorage.TryDetectImage(request.Bytes, out _, out _, out errorMessage))
        {
            request = null;
            return false;
        }

        return true;
    }

    private static bool TryReadFile(string filePath, out NoteImageWriteRequest request, out string errorMessage)
    {
        request = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "Image is required.";
            return false;
        }

        var bytes = File.ReadAllBytes(filePath);
        if (!NoteAttachmentStorage.TryDetectImage(bytes, out _, out _, out errorMessage))
        {
            return false;
        }

        request = new NoteImageWriteRequest
        {
            Bytes = bytes,
            FileName = Path.GetFileName(filePath),
            Source = NoteAttachmentSources.File
        };
        return true;
    }

    private static bool IsImagePath(string filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
               ImageExtensions.Contains(Path.GetExtension(filePath));
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
