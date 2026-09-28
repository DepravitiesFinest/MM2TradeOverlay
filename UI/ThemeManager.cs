// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Globalization;
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
        new("graphite", "Graphite", C("#111111"), C("#181818"), C("#1F1F1F"), C("#282828"), C("#242424"), C("#363636"), C("#F7141414")),
        new("forest", "Forest", C("#09110D"), C("#0F1914"), C("#15211B"), C("#1C2B23"), C("#1A2720"), C("#2A3C32"), C("#F70D1611")),
        new("plum", "Plum", C("#110C15"), C("#18111E"), C("#1F1726"), C("#281E31"), C("#251B2D"), C("#382B44"), C("#F715101B")),
        new("mocha", "Mocha", C("#13100D"), C("#1A1612"), C("#211C17"), C("#2A241E"), C("#27211B"), C("#3A3129"), C("#F718140F")),
    };

    public const string MatchAccentKey = "accent";

    public static AccentOption AccentFor(string? key) => Accents.FirstOrDefault(a => a.Key == key) ?? Accents[0];
    public static BaseOption BaseFor(string? key) => Bases.FirstOrDefault(b => b.Key == key) ?? Bases[0];

    public static BaseOption ResolveBase(string? baseKey, string? accent)
    {
        if (baseKey != MatchAccentKey) return BaseFor(baseKey);
        var tint = ResolveAccent(accent).Color;
        var d = Bases[0];
        return new(MatchAccentKey, "Match accent",
            Mix(d.Bg, tint, 0.05), Mix(d.Surface, tint, 0.07), Mix(d.Surface2, tint, 0.08), Mix(d.Surface3, tint, 0.1),
            Mix(d.Line, tint, 0.09), Mix(d.Strong, tint, 0.14), Mix(d.Overlay, tint, 0.07));
    }

    public static bool IsCustom(string? accent) => accent?.StartsWith('#') == true && TryParseHex(accent, out _);

    public static (Color Color, Color Hot) ResolveAccent(string? accent)
    {
        if (accent?.StartsWith('#') == true && TryParseHex(accent, out var custom))
            return (custom, Mix(custom, Colors.White, 0.22));
        var preset = AccentFor(accent);
        return (preset.Color, preset.Hot);
    }

    public static string DescribeAccent(string? accent) =>
        IsCustom(accent) ? "Custom, " + accent!.ToUpperInvariant() : AccentFor(accent).Name;

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static void Apply(string? accent, string? baseKey, int overlayOpacity = 100)
    {
        var res = Application.Current.Resources;
        var (color, hot) = ResolveAccent(accent);
        var b = ResolveBase(baseKey, accent);

        Set(res, "AccentBrush", color);
        Set(res, "AccentHotBrush", hot);
        Set(res, "AccentSoftBrush", Color.FromArgb(0x24, color.R, color.G, color.B));
        var gradient = new LinearGradientBrush(Mix(color, hot, 0.55), Darken(color, 0.9), 90);
        gradient.Freeze();
        res["AccentGradientBrush"] = gradient;
        res["LogoImage"] = AccentLogo.For(color);

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
