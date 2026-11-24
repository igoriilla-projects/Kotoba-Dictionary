using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Kotoba.Core;

namespace Kotoba.Windows;

internal sealed class GlobalDictionaryShortcut : IDisposable
{
    internal const int HotkeyMessage = 0x0312;
    internal const int FirstRegistrationId = 1;
    private const uint NoRepeat = 0x4000;
    private readonly HwndSource source;
    private readonly Action invoke;
    private DictionaryShortcut? registered;
    private int activeId;

    internal GlobalDictionaryShortcut(HwndSource source, Action invoke)
    {
        this.source = source;
        this.invoke = invoke;
        source.AddHook(HandleMessage);
    }

    internal void Change(DictionaryShortcut? next, Action persist)
    {
        if (next == registered)
        {
            persist();
            return;
        }

        int nextId = 0;
        if (next is { } shortcut)
        {
            nextId = activeId == FirstRegistrationId ? 2 : FirstRegistrationId;
            if (!RegisterHotKey(source.Handle, nextId, shortcut.Modifiers | NoRepeat, shortcut.VirtualKey))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            persist();
        }
        catch
        {
            Unregister(nextId);
            throw;
        }

        Unregister(activeId);
        activeId = nextId;
        registered = next;
    }

    private IntPtr HandleMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        uint keys = unchecked((uint)lParam.ToInt64());
        if (message == HotkeyMessage && registered is { } shortcut && wParam.ToInt64() == activeId &&
            (keys & 0xf) == shortcut.Modifiers && (keys >> 16) == shortcut.VirtualKey)
        {
            handled = true;
            invoke();
        }
        return IntPtr.Zero;
    }

    private void Unregister(int id)
    {
        if (id != 0 && !source.IsDisposed)
            UnregisterHotKey(source.Handle, id);
    }

    public void Dispose()
    {
        Unregister(activeId);
        activeId = 0;
        registered = null;
        if (!source.IsDisposed)
            source.RemoveHook(HandleMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
