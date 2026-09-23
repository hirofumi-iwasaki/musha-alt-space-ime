using System.Runtime.InteropServices;
using MushaAltSpaceIme.Core;

namespace MushaAltSpaceIme;

internal readonly record struct InputTarget(nint Window, uint Thread, nint Layout, nint Focus)
{
    internal static InputTarget? Capture()
    {
        nint window = NativeMethods.GetForegroundWindow();
        if (window == 0) return null;
        uint thread = NativeMethods.GetWindowThreadProcessId(window, out _);
        if (thread == 0) return null;
        var info = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        if (!NativeMethods.GetGUIThreadInfo(thread, ref info)) return null;
        return new(window, thread, NativeMethods.GetKeyboardLayout(thread), info.Focus);
    }
}

internal readonly record struct ToggleShortcut(InputTarget Target, ushort VirtualKey, ushort ScanCode, bool Extended);

internal sealed class ShortcutSender
{
    internal static ToggleShortcut? Resolve(InputTarget? target)
    {
        if (target is not { } value || value.Layout == 0) return null;
        short mapping = NativeMethods.VkKeyScanExW('`', value.Layout);
        // A shifted grave accent is not the unmodified grave key in Alt+`.
        // Never substitute JIS half/full-width or another IME-specific key.
        if (mapping == -1 || ((ushort)mapping >> 8) != 0) return null;
        ushort key = (ushort)(mapping & 0xFF);
        uint scan = NativeMethods.MapVirtualKeyExW(key, 4 /* MAPVK_VK_TO_VSC_EX */, value.Layout);
        if (scan == 0 || (scan >> 8) is not (0 or 0xE0)) return null;
        return new(value, key, (ushort)(scan & 0xFF), (scan >> 8) == 0xE0);
    }

    internal bool SendToggle(ToggleShortcut shortcut)
    {
        if (InputTarget.Capture() != shortcut.Target) return false;
        KeyEvent down = new(shortcut.VirtualKey, shortcut.ScanCode, false, shortcut.Extended);
        return Send([
            new(0xA4, 0x38, false, false),
            down,
            down with { IsUp = true },
            new(0xA4, 0x38, true, false)
        ]);
    }

    internal bool Send(IReadOnlyList<KeyEvent> events)
    {
        if (events.Count == 0) return true;
        NativeMethods.Input[] inputs = events.Select(ToNative).ToArray();
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent == inputs.Length) return true;

        // Only recover presses accepted in this incomplete batch; never retry a toggle.
        var held = new Dictionary<ushort, KeyEvent>();
        for (int i = 0; i < Math.Min(sent, (uint)events.Count); i++)
        {
            KeyEvent key = events[i];
            if (key.IsUp) held.Remove(key.VirtualKey);
            else held[key.VirtualKey] = key;
        }
        NativeMethods.Input[] releases = held.Values.Reverse()
            .Select(key => ToNative(key with { IsUp = true })).ToArray();
        if (releases.Length > 0)
            NativeMethods.SendInput((uint)releases.Length, releases, Marshal.SizeOf<NativeMethods.Input>());
        return false;
    }

    internal bool SendPointer(IReadOnlyList<KeyEvent> prefix, NativeMethods.MouseData data, uint flags)
    {
        int left = NativeMethods.GetSystemMetrics(76), top = NativeMethods.GetSystemMetrics(77);
        int width = NativeMethods.GetSystemMetrics(78), height = NativeMethods.GetSystemMetrics(79);
        if (width <= 0 || height <= 0) return false;
        var inputs = new NativeMethods.Input[prefix.Count + 1];
        for (int i = 0; i < prefix.Count; i++) inputs[i] = ToNative(prefix[i]);
        inputs[^1] = new NativeMethods.Input
        {
            Type = 0,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = Normalize(data.Position.X, left, width),
                    Y = Normalize(data.Position.Y, top, height),
                    MouseData = flags is 0x0800 or 0x1000
                        ? unchecked((uint)(short)(data.Data >> 16))
                        : flags is 0x0080 or 0x0100 ? data.Data >> 16 : 0,
                    // Replay at the observed position, including across virtual monitors.
                    Flags = flags | 0x8000 /* ABSOLUTE */ | 0x4000 /* VIRTUALDESK */ | 0x0001 /* MOVE */,
                    ExtraInfo = NativeMethods.InputTag
                }
            }
        };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent == inputs.Length) return true;
        if (sent > 0)
            Send(prefix.Take((int)Math.Min(sent, (uint)prefix.Count)).Reverse()
                .Where(key => !key.IsUp).Select(key => key with { IsUp = true }).ToArray());
        return false;
    }

    private static int Normalize(int position, int origin, int extent) =>
        (int)Math.Clamp(((long)position - origin) * 65536 / extent + 32768 / extent, 0, 65535);

    private static NativeMethods.Input ToNative(KeyEvent key) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput
            {
                VirtualKey = key.ScanCode == 0 ? key.VirtualKey : (ushort)0,
                ScanCode = key.ScanCode,
                Flags = (key.ScanCode != 0 ? NativeMethods.KeyEventScanCode : 0)
                    | (key.IsUp ? NativeMethods.KeyEventUp : 0)
                    | (key.IsExtended ? NativeMethods.KeyEventExtended : 0),
                ExtraInfo = NativeMethods.InputTag
            }
        }
    };
}
