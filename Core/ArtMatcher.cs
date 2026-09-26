// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;

namespace TradeValueOverlay;

public static class ArtMatcher
{
    private const int Grid = 16;

    public const double ConfidentMargin = 0.03;
    private const int LayoutGrid = 8;
    private const int HueBins = 18, GreyBins = 4;

    public sealed class Signature
    {
        public required float[] Colours { get; init; }
        public required float[] Shape { get; init; }
        public required float[] ShapeMirrored { get; init; }

        public required float[] Layout { get; init; }
        public required float[] LayoutMirrored { get; init; }

        public required float[] Structure { get; init; }
        public required float[] StructureMirrored { get; init; }

        public bool IsCardShot { get; init; }

        public bool Transparent { get; init; }
    }

    public static Signature? Describe(Frame f, bool sitePicture = false)
    {
        int w = f.Width, h = f.Height;
        if (w < 8 || h < 8) return null;
        var px = f.Pixels;

        bool transparent = CountTransparent(px) > w * h / 10;
        var background = transparent ? null : FloodBackground(f);

        int lastRow = h;
        if (sitePicture && !transparent && IsDarkCard(f)) lastRow = (int)(h * 0.74);

        var item = new bool[w * h];
        int minX = w, minY = h, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < lastRow; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                byte b = px[o], g = px[o + 1], r = px[o + 2], a = px[o + 3];
                bool on = transparent ? a > 128 : !background![y * w + x];
                if (on && Math.Min(r, Math.Min(g, b)) > 225) on = false;
                if (on && g > 190 && r < 100 && b < 100) on = false;
                if (!on) continue;

                item[y * w + x] = true;
                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (count < 40) return null;

        var colours = new float[HueBins + GreyBins];
        for (int i = 0; i < item.Length; i++)
        {
            if (!item[i]) continue;
            int o = i * 4;
            var (hue, sat, val) = Hsv(px[o + 2], px[o + 1], px[o]);
            if (sat < 0.22 || val < 0.12) colours[HueBins + Math.Min(GreyBins - 1, (int)(val * GreyBins))]++;
            else colours[Math.Min(HueBins - 1, (int)(hue / 360 * HueBins))]++;
        }
        for (int i = 0; i < colours.Length; i++) colours[i] /= count;

        int bw = maxX - minX + 1, bh = maxY - minY + 1, side = Math.Max(bw, bh);
        int ox = minX - (side - bw) / 2, oy = minY - (side - bh) / 2;
        var shape = new float[Grid * Grid];
        var cells = new int[Grid * Grid];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int sx = ox + x, sy = oy + y;
                int cell = (y * Grid / side) * Grid + x * Grid / side;
                cells[cell]++;
                if (sx >= 0 && sy >= 0 && sx < w && sy < h && item[sy * w + sx]) shape[cell]++;
            }
        }
        for (int i = 0; i < shape.Length; i++) shape[i] = cells[i] > 0 ? shape[i] / cells[i] : 0;

        var sums = new double[LayoutGrid * LayoutGrid * 3];
        var hits = new int[LayoutGrid * LayoutGrid];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int sx = ox + x, sy = oy + y;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h || !item[sy * w + sx]) continue;
                int cell = (y * LayoutGrid / side) * LayoutGrid + x * LayoutGrid / side;
                int o = (sy * w + sx) * 4;
                sums[cell * 3] += px[o + 2]; sums[cell * 3 + 1] += px[o + 1]; sums[cell * 3 + 2] += px[o];
                hits[cell]++;
            }
        }
        var layout = new float[LayoutGrid * LayoutGrid * 3];
        for (int c = 0; c < hits.Length; c++)
            for (int k = 0; k < 3; k++)
                layout[c * 3 + k] = hits[c] > 2 ? (float)(sums[c * 3 + k] / hits[c]) : float.NaN;
        var layoutMirrored = new float[layout.Length];
        for (int y = 0; y < LayoutGrid; y++)
            for (int x = 0; x < LayoutGrid; x++)
                for (int k = 0; k < 3; k++)
                    layoutMirrored[(y * LayoutGrid + x) * 3 + k] = layout[(y * LayoutGrid + (LayoutGrid - 1 - x)) * 3 + k];

        var mirrored = new float[shape.Length];
        for (int y = 0; y < Grid; y++)
            for (int x = 0; x < Grid; x++)
                mirrored[y * Grid + x] = shape[y * Grid + (Grid - 1 - x)];

        bool cardShot = sitePicture && !transparent && lastRow < h;
        var area = cardShot
            ? new Int32Rect((int)(w * 0.10), (int)(h * 0.08), (int)(w * 0.80), (int)(h * 0.62))
            : new Int32Rect(0, 0, w, h);
        var (structure, structureMirrored) = StructureOf(f, area);

        return new Signature
        {
            Colours = colours, Shape = shape, ShapeMirrored = mirrored, Layout = layout, LayoutMirrored = layoutMirrored,
            Structure = structure, StructureMirrored = structureMirrored, IsCardShot = cardShot, Transparent = transparent,
        };
    }

    public static string Explain(Signature a, Signature b)
    {
        double colour = 0;
        for (int i = 0; i < a.Colours.Length; i++) colour += Math.Min(a.Colours[i], b.Colours[i]);
        double outline = 0;
        for (int i = 0; i < a.Shape.Length; i++) outline += Math.Abs(a.Shape[i] - b.Shape[i]);
        double corr = 0;
        for (int i = 0; i < a.Structure.Length; i++) corr += a.Structure[i] * b.Structure[i];
        return $"colour {colour:0.00} outline {1 - outline / a.Shape.Length:0.00} overlay {(corr / a.Structure.Length + 1) / 2:0.00} cardshot {b.IsCardShot} transparent {b.Transparent}";
    }

    public static double Similarity(Signature a, Signature b)
    {
        double Layout(float[] l)
        {
            double total = 0;
            int n = 0;
            for (int c = 0; c < l.Length; c += 3)
            {
                if (float.IsNaN(a.Layout[c]) || float.IsNaN(l[c])) continue;
                double d = Math.Sqrt(Sq(a.Layout[c] - l[c]) + Sq(a.Layout[c + 1] - l[c + 1]) + Sq(a.Layout[c + 2] - l[c + 2]));
                total += Math.Min(1, d / 160);
                n++;
            }
            return n == 0 ? 0 : 1 - total / n;
        }
        static double Sq(double v) => v * v;

        double colour = 0;
        for (int i = 0; i < a.Colours.Length; i++) colour += Math.Min(a.Colours[i], b.Colours[i]);

        double Outline(float[] s)
        {
            double diff = 0;
            for (int i = 0; i < s.Length; i++) diff += Math.Abs(a.Shape[i] - s[i]);
            return 1 - diff / s.Length;
        }
        double straight = 0.5 * Outline(b.Shape) + 0.5 * Layout(b.Layout);
        double flipped = 0.5 * Outline(b.ShapeMirrored) + 0.5 * Layout(b.LayoutMirrored);
        double score = 0.35 * colour + 0.65 * Math.Max(straight, flipped);
        if (!b.IsCardShot) return score;

        double Correlate(float[] s)
        {
            double sum = 0;
            for (int i = 0; i < s.Length; i++) sum += a.Structure[i] * s[i];
            return sum / s.Length;
        }
        double overlay = (Math.Max(Correlate(b.Structure), Correlate(b.StructureMirrored)) + 1) / 2;
        return 0.5 * score + 0.5 * overlay;
    }

    private const int StructureGrid = 20;

    private static (float[] Straight, float[] Mirrored) StructureOf(Frame f, Int32Rect area)
    {
        var grid = new double[StructureGrid * StructureGrid];
        var hits = new int[grid.Length];
        for (int y = 0; y < area.Height; y++)
        {
            for (int x = 0; x < area.Width; x++)
            {
                int o = ((area.Y + y) * f.Width + area.X + x) * 4;
                int cell = (y * StructureGrid / area.Height) * StructureGrid + x * StructureGrid / area.Width;
                grid[cell] += (f.Pixels[o] + f.Pixels[o + 1] + f.Pixels[o + 2]) / 3.0;
                hits[cell]++;
            }
        }
        for (int i = 0; i < grid.Length; i++) grid[i] = hits[i] > 0 ? grid[i] / hits[i] : 0;
        double mean = grid.Average(), spread = Math.Sqrt(grid.Average(v => (v - mean) * (v - mean)));
        var straight = grid.Select(v => (float)(spread > 1e-6 ? (v - mean) / spread : 0)).ToArray();
        var mirrored = new float[straight.Length];
        for (int y = 0; y < StructureGrid; y++)
            for (int x = 0; x < StructureGrid; x++)
                mirrored[y * StructureGrid + x] = straight[y * StructureGrid + (StructureGrid - 1 - x)];
        return (straight, mirrored);
    }

    private static bool IsDarkCard(Frame f)
    {
        long sum = 0;
        int n = 0;
        for (int x = 0; x < f.Width; x += 2)
        {
            foreach (int y in new[] { 1, f.Height - 2 })
            {
                int o = (y * f.Width + x) * 4;
                sum += (f.Pixels[o] + f.Pixels[o + 1] + f.Pixels[o + 2]) / 3;
                n++;
            }
        }
        return n > 0 && sum / n < 110;
    }

    private static int CountTransparent(byte[] px)
    {
        int n = 0;
        for (int i = 3; i < px.Length; i += 4) if (px[i] < 128) n++;
        return n;
    }

    private static bool[] FloodBackground(Frame f)
    {
        int w = f.Width, h = f.Height;
        var px = f.Pixels;
        var bg = new bool[w * h];
        var queue = new Queue<int>();

        void Seed(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (bg[i]) return;
            bg[i] = true;
            queue.Enqueue(i);
        }

        foreach (int inset in new[] { 0, 3, 6 })
        {
            if (inset * 2 >= Math.Min(w, h)) break;
            for (int x = inset; x < w - inset; x++) { Seed(x, inset); Seed(x, h - 1 - inset); }
            for (int y = inset; y < h - inset; y++) { Seed(inset, y); Seed(w - 1 - inset, y); }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w, o = i * 4;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int n = ny * w + nx;
                if (bg[n]) continue;
                int p = n * 4;
                int step = Math.Abs(px[p] - px[o]) + Math.Abs(px[p + 1] - px[o + 1]) + Math.Abs(px[p + 2] - px[o + 2]);
                if (step > 14) continue;
                bg[n] = true;
                queue.Enqueue(n);
            }
        }
        return bg;
    }

    private static (double H, double S, double V) Hsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double hue = d == 0 ? 0
            : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : d / max, max);
    }
}
