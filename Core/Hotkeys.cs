// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows.Input;
using System.Windows.Interop;

namespace TradeValueOverlay;

public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var last = parts[^1];
        Key key;
        if (last.Length == 1 && char.IsDigit(last[0])) key = Key.D0 + (last[0] - '0');
        else if (!Enum.TryParse(last, true, out key)) return false;

        hotkey = new Hotkey(mods, key);
        return key != Key.None;
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.Return => "Enter",
        Key.Next => "PageDown",
        Key.Prior => "PageUp",
        _ => key.ToString(),
    };
}

public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _window;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotkeyManager()
    {
        _window = new HwndSource(new HwndSourceParameters("MM2TradeOverlay.Hotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    public bool Register(int id, Hotkey hotkey, Action onPressed)
    {
        Unregister(id);

        uint mods = Native.MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (!Native.RegisterHotKey(_window.Handle, id, mods, vk)) return false;

        _handlers[id] = onPressed;
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id)) Native.UnregisterHotKey(_window.Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
