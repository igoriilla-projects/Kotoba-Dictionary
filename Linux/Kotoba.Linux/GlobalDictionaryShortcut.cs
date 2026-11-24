using System.Runtime.InteropServices;
using Avalonia.Threading;
using Kotoba.Core;

namespace Kotoba.Linux;

internal sealed class GlobalDictionaryShortcut : IDisposable
{
    private const string X11 = "libX11.so.6";
    private const string X11Xcb = "libX11-xcb.so.1";
    private const string Xcb = "libxcb.so.1";
    private const string Libc = "libc.so.6";
    private const byte KeyPress = 2, KeyRelease = 3, GrabModeAsync = 1;
    private const ushort ShiftMask = 1, LockMask = 2, ControlMask = 4;
    private readonly IntPtr display;
    private readonly IntPtr connection;
    private readonly uint root;
    private readonly Action invoke;
    private readonly DispatcherTimer timer;
    private HashSet<Grab> registered = [];
    private (byte Code, uint Time)? released;
    private byte pressed;

    private readonly record struct Grab(byte Code, ushort Modifiers);

    internal static bool UsesWayland =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland" ||
        (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") != "x11" &&
         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));

    internal GlobalDictionaryShortcut(Action invoke)
    {
        this.invoke = invoke;
        display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            throw new InvalidOperationException("Не удалось подключиться к X11.");
        try
        {
            connection = XGetXCBConnection(display);
            root = (uint)XDefaultRootWindow(display);
            XSetEventQueueOwner(display, 1); // XCBOwnsEventQueue
        }
        catch
        {
            XCloseDisplay(display);
            throw;
        }
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) => Poll();
    }

    internal void Change(DictionaryShortcut? next, Action persist)
    {
        var target = next is { } shortcut ? Resolve(shortcut) : [];
        var claimed = new List<Grab>();
        try
        {
            foreach (var grab in target.Except(registered))
            {
                uint request = xcb_grab_key_checked(connection, 0, root, grab.Modifiers, grab.Code,
                                                    GrabModeAsync, GrabModeAsync);
                IntPtr error = xcb_request_check(connection, request);
                if (error != IntPtr.Zero)
                {
                    free(error);
                    throw new InvalidOperationException("Сочетание недоступно в X11. Выберите другое.");
                }
                if (xcb_connection_has_error(connection) != 0)
                    throw new InvalidOperationException("Соединение с X11 прервано.");
                claimed.Add(grab);
            }
            persist();
        }
        catch
        {
            Ungrab(claimed);
            throw;
        }
        Ungrab(registered.Except(target));
        registered = target;
        pressed = 0;
        released = null;
        timer.IsEnabled = registered.Count > 0;
    }

    private HashSet<Grab> Resolve(DictionaryShortcut shortcut)
    {
        if (!shortcut.IsValid())
            throw new ArgumentException("Добавьте Ctrl, Alt или Super и выберите клавишу.");
        uint key = shortcut.VirtualKey;
        uint symbol = key switch
        {
            >= 'A' and <= 'Z' => key + ('a' - 'A'),
            >= 0x70 => key - 0x70 + 0xffbe, // XK_F1
            _ => key
        };
        byte code = XKeysymToKeycode(display, symbol);
        if (code == 0)
            throw new InvalidOperationException("Клавиша недоступна в текущей раскладке X11.");
        ushort alt = ModifierMask(0xffe9, 0xffea); // XK_Alt_L, XK_Alt_R
        ushort super = ModifierMask(0xffeb, 0xffec); // XK_Super_L, XK_Super_R
        ushort locks = (ushort)(LockMask | ModifierMask(0xff7f, 0xff14)); // Num Lock, Scroll Lock
        uint mods = shortcut.Modifiers;
        ushort required = 0;
        if ((mods & DictionaryShortcut.Control) != 0) required |= ControlMask;
        if ((mods & DictionaryShortcut.Shift) != 0) required |= ShiftMask;
        var altMasks = ModifierOptions(mods, DictionaryShortcut.Alt, alt);
        var superMasks = ModifierOptions(mods, DictionaryShortcut.Windows, super);
        var target = new HashSet<Grab>();
        foreach (ushort a in altMasks)
        foreach (ushort s in superMasks)
        {
            ushort modifiers = (ushort)(required | a | s);
            // A modifier may share its bit with another key. Exclude required bits from the lock variants.
            int optional = locks & ~modifiers;
            for (int subset = optional; ; subset = (subset - 1) & optional)
            {
                target.Add(new Grab(code, (ushort)(modifiers | subset)));
                if (subset == 0) break;
            }
        }
        return target;
    }

    private static ushort[] ModifierOptions(uint chosen, uint flag, ushort mask)
    {
        if ((chosen & flag) == 0)
            return [0];
        if (mask == 0)
            throw new InvalidOperationException("Выбранный модификатор недоступен в текущей раскладке X11.");
        return Enumerable.Range(0, 8).Select(bit => (ushort)(1 << bit)).Where(bit => (bit & mask) != 0).ToArray();
    }

    private ushort ModifierMask(uint left, uint right)
    {
        byte first = XKeysymToKeycode(display, left);
        byte second = XKeysymToKeycode(display, right);
        IntPtr mapping = XGetModifierMapping(display);
        if (mapping == IntPtr.Zero)
            throw new InvalidOperationException("Не удалось прочитать модификаторы X11.");
        try
        {
            int slots = Marshal.ReadInt32(mapping);
            IntPtr keys = Marshal.ReadIntPtr(mapping, IntPtr.Size);
            ushort result = 0;
            for (int index = 0; index < 8 * slots; index++)
            {
                byte code = Marshal.ReadByte(keys, index);
                if (code != 0 && (code == first || code == second))
                    result |= (ushort)(1 << (index / slots));
            }
            return result;
        }
        finally { XFreeModifiermap(mapping); }
    }

    private void Poll()
    {
        IntPtr native;
        while ((native = xcb_poll_for_event(connection)) != IntPtr.Zero)
        {
            byte type = (byte)(Marshal.ReadByte(native) & 0x7f);
            byte code = Marshal.ReadByte(native, 1);
            uint time = unchecked((uint)Marshal.ReadInt32(native, 4));
            ushort state = (ushort)(Marshal.ReadInt16(native, 28) & 0xff);
            free(native);
            if (type == KeyRelease && code == pressed)
                released = (code, time);
            if (type != KeyPress)
                continue;
            // X11 can report autorepeat as a Release/Press pair with the same server time.
            if (released is { } last && last == (code, time))
            {
                released = null;
                continue;
            }
            if (released is not null)
                pressed = 0;
            released = null;
            if (pressed != code && registered.Contains(new Grab(code, state)))
            {
                pressed = code;
                invoke();
            }
        }
    }

    private void Ungrab(IEnumerable<Grab> grabs)
    {
        foreach (var grab in grabs)
            xcb_ungrab_key(connection, grab.Code, root, grab.Modifiers);
        xcb_flush(connection);
    }

    public void Dispose()
    {
        timer.Stop();
        XCloseDisplay(display);
    }

    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr display, nuint symbol);
    [DllImport(X11)] private static extern IntPtr XGetModifierMapping(IntPtr display);
    [DllImport(X11)] private static extern int XFreeModifiermap(IntPtr mapping);
    [DllImport(X11Xcb)] private static extern IntPtr XGetXCBConnection(IntPtr display);
    [DllImport(X11Xcb)] private static extern void XSetEventQueueOwner(IntPtr display, int owner);
    [DllImport(Xcb)] private static extern int xcb_connection_has_error(IntPtr connection);
    [DllImport(Xcb)] private static extern uint xcb_grab_key_checked(IntPtr connection, byte ownerEvents, uint root,
        ushort modifiers, byte key, byte pointerMode, byte keyboardMode);
    [DllImport(Xcb)] private static extern IntPtr xcb_request_check(IntPtr connection, uint cookie);
    [DllImport(Xcb)] private static extern uint xcb_ungrab_key(IntPtr connection, byte key, uint root, ushort modifiers);
    [DllImport(Xcb)] private static extern int xcb_flush(IntPtr connection);
    [DllImport(Xcb)] private static extern IntPtr xcb_poll_for_event(IntPtr connection);
    [DllImport(Libc)] private static extern void free(IntPtr memory);
}
