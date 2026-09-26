// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Media;

namespace TradeValueOverlay;

public sealed record AccentOption(string Key, string Name, Color Color, Color Hot);

public sealed record BaseOption(string Key, string Name, Color Bg, Color Surface, Color Surface2, Color Surface3, Color Line, Color Strong, Color Overlay);

public static class ThemeManager
{
    public static readonly AccentOption[] Accents =
    {
        new("violet", "Violet", C("#7B6CF6"), C("#9A8FFF")),
        new("blue", "Blue", C("#3B82F6"), C("#60A5FA")),
        new("teal", "Teal", C("#14B8A6"), C("#2DD4BF")),
        new("green", "Green", C("#22C55E"), C("#4ADE80")),
        new("rose", "Rose", C("#F43F5E"), C("#FB7185")),
        new("amber", "Amber", C("#F59E0B"), C("#FBBF24")),
    };

    public static readonly BaseOption[] Bases =
    {
        new("dark", "Dark", C("#0A0B0E"), C("#111318"), C("#171A20"), C("#1F232B"), C("#1E2129"), C("#2C313C"), C("#F70F1115")),
        new("midnight", "Midnight", C("#000000"), C("#0A0A0C"), C("#111114"), C("#19191D"), C("#17171B"), C("#26262C"), C("#F7050506")),
        new("slate", "Slate", C("#0F1624"), C("#162033"), C("#1C283D"), C("#26344C"), C("#223047"), C("#34445E"), C("#F7121B2B")),
    };

    public static AccentOption AccentFor(string? key) => Accents.FirstOrDefault(a => a.Key == key) ?? Accents[0];
    public static BaseOption BaseFor(string? key) => Bases.FirstOrDefault(b => b.Key == key) ?? Bases[0];

    public static void Apply(string? accentKey, string? baseKey, int overlayOpacity = 100)
    {
        var res = Application.Current.Resources;
        var a = AccentFor(accentKey);
        var b = BaseFor(baseKey);

        Set(res, "AccentBrush", a.Color);
        Set(res, "AccentHotBrush", a.Hot);
        Set(res, "AccentSoftBrush", Color.FromArgb(0x24, a.Color.R, a.Color.G, a.Color.B));
        var gradient = new LinearGradientBrush(Mix(a.Color, a.Hot, 0.55), Darken(a.Color, 0.9), 90);
        gradient.Freeze();
        res["AccentGradientBrush"] = gradient;

        Set(res, "BgBrush", b.Bg);
        Set(res, "SurfaceBrush", b.Surface);
        Set(res, "Surface2Brush", b.Surface2);
        Set(res, "Surface3Brush", b.Surface3);
        Set(res, "LineBrush", b.Line);
        Set(res, "BorderStrongBrush", b.Strong);
        byte alpha = (byte)Math.Round(Math.Clamp(overlayOpacity, 50, 100) / 100.0 * 250);
        Set(res, "OverlayBgBrush", Color.FromArgb(alpha, b.Overlay.R, b.Overlay.G, b.Overlay.B));
    }

    private static void Set(ResourceDictionary res, string key, Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        res[key] = brush;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Mix(Color x, Color y, double t) => Color.FromRgb(
        (byte)(x.R + (y.R - x.R) * t), (byte)(x.G + (y.G - x.G) * t), (byte)(x.B + (y.B - x.B) * t));

    private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
}
