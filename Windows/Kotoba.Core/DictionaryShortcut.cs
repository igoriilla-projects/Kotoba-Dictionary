namespace Kotoba.Core;

public readonly record struct DictionaryShortcut(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 1, Control = 2, Shift = 4, Windows = 8;
    public static readonly DictionaryShortcut Default = new(Control | Alt, 'J');

    public bool IsValid() =>
        (Modifiers & ~(Control | Alt | Shift | Windows)) == 0 &&
        (Modifiers & (Control | Alt | Windows)) != 0 &&
        SupportsKey(VirtualKey);

    public static bool SupportsKey(uint key) =>
        key is 0x20 or >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5a or
            >= 0x70 and <= 0x87 and not 0x7b;
}
