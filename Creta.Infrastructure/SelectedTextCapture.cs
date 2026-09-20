using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Creta.Infrastructure.Logger;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WindowsInput;
using WindowsInput.Native;

namespace Creta.Infrastructure;

/// <summary>
/// Captures selected text from the current foreground window before Creta takes focus.
/// </summary>
public static class SelectedTextCapture
{
    private const int EsPassword = 0x0020;
    private const int ModifierReleaseTimeoutMs = 200;
    private const int ClipboardCopyDelayMs = 50;
    private static readonly string ClassName = nameof(SelectedTextCapture);
    private static readonly InputSimulator InputSimulator = new();

    public static string LastText { get; private set; } = string.Empty;

    public static async Task<string> CaptureAsync(bool allowClipboardFallback = true)
    {
        try
        {
            var captured = TryCaptureWithoutClipboard(out var hasTextPattern, out var hwnd);
            if (string.IsNullOrEmpty(captured) &&
                allowClipboardFallback &&
                !hasTextPattern &&
                ShouldUseClipboardFallback(hwnd))
            {
                captured = await TryClipboardCopyAsync();
            }

            LastText = SelectedTextFormatter.ForQuery(captured);
            return LastText;
        }
        catch (System.Exception e)
        {
            Log.Error(ClassName, $"Failed to capture selected text: {e.Message}");
            LastText = string.Empty;
            return string.Empty;
        }
    }

    private static string TryCaptureWithoutClipboard(out bool hasTextPattern, out HWND hwnd)
    {
        hasTextPattern = false;
        if (!TryGetFocusedControl(out hwnd) || hwnd.IsNull)
        {
            return string.Empty;
        }

        if (IsPasswordControl(hwnd))
        {
            return string.Empty;
        }

        var nativeText = TryNativeEditSelection(hwnd);
        if (!string.IsNullOrEmpty(nativeText))
        {
            return nativeText;
        }

        var automationText = TryAutomationSelection(hwnd, out hasTextPattern);
        return automationText ?? string.Empty;
    }

    private static bool TryGetFocusedControl(out HWND hwnd)
    {
        hwnd = HWND.Null;
        var info = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
        };

        if (!PInvoke.GetGUIThreadInfo(0, ref info))
        {
            var foreground = PInvoke.GetForegroundWindow();
            if (foreground.IsNull)
            {
                return false;
            }

            hwnd = foreground;
            return true;
        }

        hwnd = info.hwndFocus.IsNull ? info.hwndCaret : info.hwndFocus;
        if (hwnd.IsNull)
        {
            hwnd = info.hwndActive;
        }

        return !hwnd.IsNull;
    }

    private static bool ShouldUseClipboardFallback(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return false;
        }

        var className = GetClassName(hwnd);
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        if (className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        return className.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("ApplicationFrame", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasswordControl(HWND hwnd)
    {
        var style = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        return (style & EsPassword) != 0;
    }

    private static unsafe string TryNativeEditSelection(HWND hwnd)
    {
        var className = GetClassName(hwnd);
        if (!IsNativeEditableClass(className))
        {
            return string.Empty;
        }

        var selection = PInvoke.SendMessage(hwnd, PInvoke.EM_GETSEL, 0, 0);
        var start = (int)(selection.Value & 0xFFFF);
        var end = (int)((selection.Value >> 16) & 0xFFFF);
        if (end <= start)
        {
            return string.Empty;
        }

        var length = (int)PInvoke.SendMessage(hwnd, PInvoke.WM_GETTEXTLENGTH, 0, 0);
        if (length <= 0)
        {
            return string.Empty;
        }

        var bufferLength = length + 1;
        var buffer = new char[bufferLength];
        int copied;
        fixed (char* pBuffer = buffer)
        {
            copied = (int)PInvoke.SendMessage(hwnd, PInvoke.WM_GETTEXT, (nuint)bufferLength, (nint)pBuffer);
        }

        if (copied <= 0)
        {
            return string.Empty;
        }

        end = Math.Min(end, copied);
        start = Math.Min(start, end);
        return new string(buffer, start, end - start);
    }

    private static string TryAutomationSelection(HWND hwnd, out bool hasTextPattern)
    {
        hasTextPattern = false;
        AutomationElement element;
        try
        {
            element = AutomationElement.FromHandle(hwnd);
        }
        catch
        {
            return string.Empty;
        }

        if (element is null)
        {
            return string.Empty;
        }

        try
        {
            if (element.Current.IsPassword)
            {
                return string.Empty;
            }

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
            {
                return string.Empty;
            }

            hasTextPattern = true;
            var ranges = textPattern.GetSelection();
            if (ranges is null || ranges.Length == 0)
            {
                return string.Empty;
            }

            var selected = ranges[0].GetText(SelectedTextFormatter.MaxQueryLength + 1);
            return string.IsNullOrWhiteSpace(selected) ? string.Empty : selected;
        }
        catch (System.Exception e)
        {
            Log.Debug(ClassName, $"UI Automation selection failed: {e.Message}");
            hasTextPattern = false;
            return string.Empty;
        }
    }

    private static async Task<string> TryClipboardCopyAsync()
    {
        await WaitForModifiersReleasedAsync();

        string previousText = null;
        IDataObject previousData = null;
        try
        {
            previousData = Clipboard.GetDataObject();
            if (Clipboard.ContainsText())
            {
                previousText = Clipboard.GetText();
            }
        }
        catch (System.Exception e)
        {
            Log.Debug(ClassName, $"Failed to read clipboard before copy: {e.Message}");
        }

        try
        {
            InputSimulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
        }
        catch (System.Exception e)
        {
            Log.Debug(ClassName, $"Failed to send Ctrl+C: {e.Message}");
            return string.Empty;
        }

        await Task.Delay(ClipboardCopyDelayMs);

        string copied = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                copied = Clipboard.GetText();
            }
        }
        catch (System.Exception e)
        {
            Log.Debug(ClassName, $"Failed to read clipboard after copy: {e.Message}");
        }

        RestoreClipboard(previousData, previousText);

        if (string.IsNullOrWhiteSpace(copied) || copied == previousText)
        {
            return string.Empty;
        }

        return copied;
    }

    private static void RestoreClipboard(IDataObject previousData, string previousText)
    {
        try
        {
            if (previousData is not null)
            {
                Clipboard.SetDataObject(previousData, true);
                return;
            }

            if (previousText is not null)
            {
                Clipboard.SetText(previousText);
                return;
            }

            Clipboard.Clear();
        }
        catch (System.Exception e)
        {
            Log.Debug(ClassName, $"Failed to restore clipboard: {e.Message}");
        }
    }

    private static async Task WaitForModifiersReleasedAsync()
    {
        var waited = 0;
        while (waited < ModifierReleaseTimeoutMs && AnyCaptureModifierDown())
        {
            await Task.Delay(10);
            waited += 10;
        }
    }

    private static bool AnyCaptureModifierDown()
    {
        return IsDown(VIRTUAL_KEY.VK_MENU) ||
               IsDown(VIRTUAL_KEY.VK_SPACE) ||
               IsDown(VIRTUAL_KEY.VK_CONTROL) ||
               IsDown(VIRTUAL_KEY.VK_SHIFT) ||
               IsDown(VIRTUAL_KEY.VK_LWIN) ||
               IsDown(VIRTUAL_KEY.VK_RWIN);
    }

    private static bool IsDown(VIRTUAL_KEY key)
    {
        return (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private static unsafe string GetClassName(HWND hwnd)
    {
        const int capacity = 256;
        Span<char> buffer = stackalloc char[capacity];
        int length;
        fixed (char* pBuffer = buffer)
        {
            length = PInvoke.GetClassName(hwnd, pBuffer, capacity);
        }

        return length <= 0 ? string.Empty : buffer[..length].ToString();
    }

    private static bool IsNativeEditableClass(string className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("RICHEDIT60W", StringComparison.OrdinalIgnoreCase);
    }
}
