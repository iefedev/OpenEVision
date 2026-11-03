namespace OpenEVision.Device;

/// <summary>
/// Everything that is specific to one keyboard model: USB ids, the vendor HID interface, LED and key-table geometry,
/// writable regions and the on-screen layout. The protocol itself (HidKeyboard) is shared by EVision controllers.
/// </summary>
/// <param name="Name">Shown in the UI and in error messages.</param>
/// <param name="Vid">USB vendor id.</param>
/// <param name="Pid">USB product id.</param>
/// <param name="UsagePage">HID usage page of the vendor interface that accepts the 64-byte command reports.</param>
/// <param name="LedCount">Number of entries in the custom (flash) color table, 3 bytes each.</param>
/// <param name="Columns">Width of the custom color matrix: custom LED index = row * Columns + slot.</param>
/// <param name="KeyTableOffset">Config offset of the active key-assignment table.</param>
/// <param name="KeyTableEntries">Entries in the key table (3 bytes each); the live (RAM) LED order uses the same indices.</param>
/// <param name="ConfigSize">Bytes of config memory read for a backup.</param>
/// <param name="CustomSize">Bytes of custom color memory read for a backup.</param>
/// <param name="LayoutResource">Embedded JSON with the keycaps drawn on screen.</param>
/// <param name="CustomLedRows">Key code at each custom matrix position (row by row, null = unused slot).</param>
public sealed record DeviceProfile(
    string Name, ushort Vid, ushort Pid, ushort UsagePage,
    int LedCount, int Columns, int KeyTableOffset, int KeyTableEntries, int ConfigSize, int CustomSize,
    string LayoutResource, string?[][] CustomLedRows)
{
    /// <summary>"320F:505B"</summary>
    public string UsbId => $"{Vid:X4}:{Pid:X4}";

    /// <summary>Config bytes below the key table: the hardware effect header.</summary>
    public int HeaderSize => KeyTableOffset;
}

/// <summary>
/// Known keyboard models. The app runs with one active profile; to support another EVision model, add its profile here
/// (ids, geometry, layout file and LED matrix) and make <see cref="Active"/> select it.
/// </summary>
public static class DeviceProfiles
{
    /// <summary>
    /// Full-size ISO keyboard with a Turkish-Q layout, 126 LEDs. The custom LED matrix is OpenRGB's EVision default
    /// 21-column matrix, verified on this model.
    /// </summary>
    public static readonly DeviceProfile EVision505B = new(
        Name: "EVision 320F:505B", Vid: 0x320F, Pid: 0x505B, UsagePage: 0xFF1C,
        LedCount: 126, Columns: 21, KeyTableOffset: 0xC0, KeyTableEntries: 128, ConfigSize: 0x1000, CustomSize: 0x200,
        LayoutResource: "layout-505b-iso-tr.json",
        CustomLedRows:
        [
            ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", null, "PrintScreen", "ScrollLock", "Pause"],
            ["Backquote", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal",
             "Backspace", "Insert", "Home", "PageUp", "NumLock", "NumpadDivide", "NumpadMultiply", "NumpadSubtract"],
            ["Tab", "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight", null,
             "Delete", "End", "PageDown", "Numpad7", "Numpad8", "Numpad9", "NumpadAdd"],
            ["CapsLock", "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Backslash", "Enter",
             null, null, null, "Numpad4", "Numpad5", "Numpad6"],
            ["ShiftLeft", "IntlBackslash", "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash", null, "ShiftRight",
             null, "ArrowUp", null, "Numpad1", "Numpad2", "Numpad3", "NumpadEnter"],
            ["ControlLeft", "MetaLeft", "AltLeft", "Space", "AltRight", "Fn", "ContextMenu", null, "ControlRight", null, null, null, null, null,
             "ArrowLeft", "ArrowDown", "ArrowRight", null, "Numpad0", "NumpadDecimal"],
        ]);

    public static readonly IReadOnlyList<DeviceProfile> All = [EVision505B];

    public static DeviceProfile Active => EVision505B;

    /// <summary>Shown when no keyboard matching a known profile is connected.</summary>
    public static string NotFoundMessage =>
        $"No supported keyboard found. OpenEVision supports EVision-controller keyboards that report USB ID {Active.UsbId} " +
        $"(vendor HID interface 0x{Active.UsagePage:X4}). Check that the keyboard is plugged in and that no other RGB software has it open exclusively.";
}
