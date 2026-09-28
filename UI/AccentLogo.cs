// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TradeValueOverlay;

public static class AccentLogo
{
    private const double SourceSaturation = 0.85;
    private const double SourceBrightness = 0.9;

    private static readonly Dictionary<Color, BitmapSource> Cache = new();
    private static byte[]? _source;
    private static List<(int Offset, double S, double V)>? _redPixels;
    private static int _width, _height;

    public static BitmapSource For(Color accent)
    {
        if (Cache.TryGetValue(accent, out var cached)) return cached;
        EnsureSource();

        var (hue, saturation, brightness) = ColorPicker.ToHsv(accent, 0);
        var pixels = (byte[])_source!.Clone();
        foreach (var (offset, s, v) in _redPixels!)
        {
            var c = ColorPicker.FromHsv(hue,
                Math.Min(1, s * saturation / SourceSaturation),
                Math.Min(1, v * brightness / SourceBrightness));
            pixels[offset] = c.B;
            pixels[offset + 1] = c.G;
            pixels[offset + 2] = c.R;
        }

        var bitmap = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgra32, null, pixels, _width * 4);
        bitmap.Freeze();
        if (Cache.Count >= 24) Cache.Clear();
        Cache[accent] = bitmap;
        return bitmap;
    }

    private static void EnsureSource()
    {
        if (_source != null) return;
        var decoded = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        var bgra = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        _width = bgra.PixelWidth;
        _height = bgra.PixelHeight;
        _source = new byte[_width * _height * 4];
        bgra.CopyPixels(_source, _width * 4, 0);

        _redPixels = new();
        for (int i = 0; i < _source.Length; i += 4)
        {
            var (h, s, v) = ColorPicker.ToHsv(Color.FromRgb(_source[i + 2], _source[i + 1], _source[i]), 0);
            bool reddish = h >= 290 || h <= 35;
            if (reddish && s > 0.1 && v > 0.15) _redPixels.Add((i, s, v));
        }
    }
}
