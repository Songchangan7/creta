using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Creta.Plugin.Note.Views;

public partial class NoteEditorWindow
{
    private readonly ObservableCollection<NoteEditorAttachmentItem> _attachments = [];
    private readonly HashSet<string> _removedAttachmentIds = new(StringComparer.OrdinalIgnoreCase);

    public string EditedContent => EditorTextBox.Text;

    public IReadOnlyList<NoteImageWriteRequest> PendingImages =>
        _attachments
            .Where(item => !item.IsExisting && item.Bytes is { Length: > 0 })
            .Select(item => new NoteImageWriteRequest
            {
                Bytes = item.Bytes,
                FileName = item.FileName,
                Source = item.Source
            })
            .ToList();

    public IReadOnlyList<string> RemovedAttachmentIds => _removedAttachmentIds.ToList();

    public bool HasAttachments => _attachments.Count > 0;

    public NoteEditorWindow(string title, string subtitle, string confirmText, string initialContent)
        : this(title, subtitle, confirmText, initialContent, existingAttachments: null, notesDirectory: null)
    {
    }

    public NoteEditorWindow(
        string title,
        string subtitle,
        string confirmText,
        string initialContent,
        IReadOnlyList<NoteAttachment> existingAttachments,
        string notesDirectory)
    {
        InitializeComponent();

        EditorTitle.Text = title;
        EditorSubtitle.Text = subtitle;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = Localize.creta_plugin_note_editor_cancel();
        EditorTextBox.Text = initialContent ?? string.Empty;
        AttachmentList.ItemsSource = _attachments;

        LoadExistingAttachments(existingAttachments, notesDirectory);
        DataObject.AddPastingHandler(EditorTextBox, OnTextBoxPasting);
        PreviewKeyDown += OnWindowPreviewKeyDown;
        DragOver += OnEditorDragOver;
        Drop += OnEditorDrop;

        Loaded += (_, _) =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
        };
    }

    private void ConfirmEdit(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EditorTextBox.Text) && !HasAttachments)
        {
            Main.Context.API.ShowMsgBox(
                Localize.creta_plugin_note_editor_empty_warning(),
                Localize.creta_plugin_note_editor_window_title());
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelEdit(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnTextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        var addedImage = TryAddClipboardImages();
        var hasText = e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) ||
                      e.SourceDataObject.GetDataPresent(DataFormats.Text);
        if (addedImage && !hasText)
        {
            e.CancelCommand();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (ReferenceEquals(Keyboard.FocusedElement, EditorTextBox))
        {
            return;
        }

        if (TryAddClipboardImages())
        {
            e.Handled = true;
        }
    }

    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        if (NoteClipboardImages.TryReadFiles(files, out var images, out var errorMessage))
        {
            AddImages(images);
        }
        else
        {
            ShowAttachmentError(errorMessage);
        }
    }

    private void PreviewAttachment(object sender, MouseButtonEventArgs e)
    {
        if (GetAttachmentItem(sender) is { } item)
        {
            ShowPreview(item);
        }
    }

    private void PreviewAttachmentMenu(object sender, RoutedEventArgs e)
    {
        if (GetAttachmentItem(sender) is { } item)
        {
            ShowPreview(item);
        }
    }

    private void DeleteAttachmentButton(object sender, RoutedEventArgs e)
    {
        if (GetAttachmentItem(sender) is { } item)
        {
            RemoveAttachment(item);
        }
    }

    private void DeleteAttachmentMenu(object sender, RoutedEventArgs e)
    {
        if (GetAttachmentItem(sender) is { } item)
        {
            RemoveAttachment(item);
        }
    }

    private void SaveAttachmentAs(object sender, RoutedEventArgs e)
    {
        if (GetAttachmentItem(sender) is not { } item)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Localize.creta_plugin_note_editor_attachment_save_as(),
            FileName = item.FileName,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            if (item.Bytes is { Length: > 0 })
            {
                File.WriteAllBytes(dialog.FileName, item.Bytes);
            }
            else if (!string.IsNullOrWhiteSpace(item.FullPath) && File.Exists(item.FullPath))
            {
                File.Copy(item.FullPath, dialog.FileName, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Main.Context.API.ShowMsgBox(ex.Message, Localize.creta_plugin_note_editor_window_title());
        }
    }

    private void LoadExistingAttachments(IReadOnlyList<NoteAttachment> existingAttachments, string notesDirectory)
    {
        if (existingAttachments is null || existingAttachments.Count == 0)
        {
            return;
        }

        foreach (var attachment in existingAttachments)
        {
            var fullPath = string.IsNullOrWhiteSpace(notesDirectory) || string.IsNullOrWhiteSpace(attachment.RelativePath)
                ? string.Empty
                : NoteAttachmentStorage.ResolveFullPath(notesDirectory, attachment.RelativePath);
            ImageSource thumbnail = null;
            if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
            {
                try
                {
                    thumbnail = NoteClipboardImages.CreateThumbnailFromFile(fullPath);
                }
                catch
                {
                    thumbnail = null;
                }
            }

            _attachments.Add(new NoteEditorAttachmentItem
            {
                Id = attachment.Id,
                IsExisting = true,
                FileName = attachment.FileName,
                FullPath = fullPath,
                Source = attachment.Source,
                Thumbnail = thumbnail
            });
        }
    }

    private bool TryAddClipboardImages()
    {
        if (!NoteClipboardImages.TryRead(out var images, out var errorMessage))
        {
            ShowAttachmentError(errorMessage);
            return false;
        }

        return AddImages(images);
    }

    private bool AddImages(IReadOnlyList<NoteImageWriteRequest> images)
    {
        if (images is null || images.Count == 0)
        {
            return false;
        }

        if (_attachments.Count + images.Count > NoteAttachmentConstraints.MaxAttachmentsPerNote)
        {
            Main.Context.API.ShowMsgBox(
                Localize.creta_plugin_note_editor_attachment_limit(),
                Localize.creta_plugin_note_editor_window_title());
            return false;
        }

        foreach (var image in images)
        {
            ImageSource thumbnail = null;
            try
            {
                thumbnail = NoteClipboardImages.CreateThumbnail(image.Bytes);
            }
            catch
            {
                thumbnail = null;
            }

            _attachments.Add(new NoteEditorAttachmentItem
            {
                Id = Guid.NewGuid().ToString("N"),
                IsExisting = false,
                FileName = image.FileName,
                Bytes = image.Bytes,
                Source = image.Source,
                Thumbnail = thumbnail
            });
        }

        return true;
    }

    private void RemoveAttachment(NoteEditorAttachmentItem item)
    {
        if (!_attachments.Remove(item))
        {
            return;
        }

        if (item.IsExisting)
        {
            _removedAttachmentIds.Add(item.Id);
        }
    }

    private void ShowPreview(NoteEditorAttachmentItem item)
    {
        ImageSource image = null;
        try
        {
            if (item.Bytes is { Length: > 0 })
            {
                image = NoteClipboardImages.CreatePreview(item.Bytes);
            }
            else if (!string.IsNullOrWhiteSpace(item.FullPath) && File.Exists(item.FullPath))
            {
                image = NoteClipboardImages.CreatePreviewFromFile(item.FullPath);
            }
        }
        catch (Exception ex)
        {
            Main.Context.API.ShowMsgBox(ex.Message, Localize.creta_plugin_note_editor_window_title());
            return;
        }

        if (image is null)
        {
            return;
        }

        var preview = new Window
        {
            Title = Localize.creta_plugin_note_editor_attachment_preview(),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background,
            Width = Math.Min(960, SystemParameters.WorkArea.Width * 0.8),
            Height = Math.Min(720, SystemParameters.WorkArea.Height * 0.8),
            Content = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(16)
            }
        };
        preview.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                preview.Close();
            }
        };
        preview.MouseLeftButtonUp += (_, _) => preview.Close();
        preview.ShowDialog();
    }

    private void ShowAttachmentError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return;
        }

        var message = errorMessage switch
        {
            "Image exceeds the 10 MB limit." => Localize.creta_plugin_note_editor_attachment_too_large(),
            "Unsupported image format." => Localize.creta_plugin_note_editor_attachment_invalid(),
            _ => errorMessage
        };
        Main.Context.API.ShowMsgBox(message, Localize.creta_plugin_note_editor_window_title());
    }

    private static NoteEditorAttachmentItem GetAttachmentItem(object sender)
    {
        return sender switch
        {
            FrameworkElement { DataContext: NoteEditorAttachmentItem item } => item,
            _ => null
        };
    }
}
