// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

namespace TradeValueOverlay;

public static class ImageOps
{
    public static Frame Scale(Frame src, double scale)
    {
        if (Math.Abs(scale - 1) < 0.01) return src;

        int sw = src.Width, sh = src.Height;
        int w = Math.Max(1, (int)Math.Round(sw * scale));
        int h = Math.Max(1, (int)Math.Round(sh * scale));
        var s = src.Pixels;
        var d = new byte[w * h * 4];

        Parallel.For(0, h, y =>
        {
            double fy = (y + 0.5) / scale - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(fy), 0, sh - 1);
            int y1 = Math.Min(y0 + 1, sh - 1);
            double ty = Math.Clamp(fy - y0, 0, 1);

            for (int x = 0; x < w; x++)
            {
                double fx = (x + 0.5) / scale - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(fx), 0, sw - 1);
                int x1 = Math.Min(x0 + 1, sw - 1);
                double tx = Math.Clamp(fx - x0, 0, 1);

                int a = (y0 * sw + x0) * 4, b = (y0 * sw + x1) * 4, c = (y1 * sw + x0) * 4, e = (y1 * sw + x1) * 4;
                int o = (y * w + x) * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    double top = s[a + ch] + (s[b + ch] - s[a + ch]) * tx;
                    double bottom = s[c + ch] + (s[e + ch] - s[c + ch]) * tx;
                    d[o + ch] = (byte)(top + (bottom - top) * ty + 0.5);
                }
            }
        });

        return new Frame(w, h, d);
    }

    public static Frame WhiteTextMask(Frame src)
    {
        var s = src.Pixels;
        var d = new byte[s.Length];
        for (int i = 0; i < s.Length; i += 4)
        {
            int min = Math.Min(s[i], Math.Min(s[i + 1], s[i + 2]));
            double t = Math.Clamp((min - 140) / 70.0, 0, 1);
            byte v = (byte)(255 * (1 - t));
            d[i] = d[i + 1] = d[i + 2] = v;
            d[i + 3] = 255;
        }
        return new Frame(src.Width, src.Height, d);
    }
}
