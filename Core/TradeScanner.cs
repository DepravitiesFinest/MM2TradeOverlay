// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace TradeValueOverlay;

public sealed class DetectedItem
{
    public required ItemGroup Group { get; init; }
    public required Rect Bounds { get; init; }
    public string OcrText { get; init; } = "";
    public double Cost { get; init; }
    public int Quantity { get; set; } = 1;

    public string? QuantityNote { get; set; }

    public double LineHeight { get; init; }

    public ValueItem? Variant { get; set; }

    public double VariantMargin { get; set; }
}

public sealed record UnknownText(string Text, Rect Bounds, IReadOnlyList<WordBox>? Words = null);

public sealed class OfferScan
{
    public required Frame Capture { get; init; }
    public required PixelRect ScreenRegion { get; init; }
    public List<DetectedItem> Items { get; init; } = new();
    public List<UnknownText> Unknown { get; init; } = new();
}

public sealed class TradeScan
{
    public required OfferScan Yours { get; init; }
    public required OfferScan Theirs { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public List<string> Notes { get; init; } = new();
}

public sealed class TradeScanner(OcrService ocr, ItemMatcher matcher)
{
    private static readonly HashSet<string> UiWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "your", "their", "offer", "offers", "trade", "trading", "accept", "accepted", "decline", "ready", "confirm",
        "cancel", "items", "item", "value", "values", "total", "add", "remove", "you", "they", "waiting", "for",
        "the", "to", "and", "of", "is", "x", "s", "vs", "with", "inventory", "search", "weapons", "pets", "all",
        "other", "player", "has", "please", "wait", "before", "accepting",
    };

    private static readonly Regex Quantity = new(@"^(?:[x×X*€%¥]\s*(\d{1,3})|(\d{1,3})\s*[x×X])$", RegexOptions.IgnoreCase);

    public async Task<OfferScan> ReadOfferAsync(Frame capture, PixelRect region, DebugSink? debug = null, string label = "")
    {
        double scale = ChooseScale(capture);
        var enlarged = ImageOps.Scale(capture, scale);
        var mask = ImageOps.WhiteTextMask(enlarged);

        var rawLines = await ocr.RecognizeAsync(enlarged);
        var maskLines = await ocr.RecognizeAsync(mask);

        var raw = await Task.Run(() => Parse(rawLines, scale));
        var masked = await Task.Run(() => Parse(maskLines, scale));

        var tags = await ReadSkippedTagsAsync(mask, rawLines.Concat(maskLines), scale, debug);

        Trace.Clear();
        ArtCrops.Clear();
        var merged = Merge(raw, masked, tags);

        var artLog = await ResolveLookalikesAsync(capture, merged.Items);

        if (debug != null)
        {
            debug.SaveFrame($"{label}_capture", capture);
            debug.SaveFrame($"{label}_mask", mask);
            debug.Text($"== {label}: region {region}, scale {scale:0.00}");
            debug.Text($"-- raw pass (score {raw.Score:0.0})");
            foreach (var l in rawLines) debug.Text("   " + string.Join(" ", l.Words.Select(w => w.Text)));
            debug.Text($"-- mask pass (score {masked.Score:0.0})");
            foreach (var l in maskLines) debug.Text("   " + string.Join(" ", l.Words.Select(w => w.Text)));
            debug.Text($"-- tag pass: {string.Join(", ", tags.Select(t => "x" + t.Qty))}");
            foreach (var t in Trace) debug.Text("   quantity: " + t);
            foreach (var t in artLog) debug.Text("   picture: " + t);
            for (int k = 0; k < ArtCrops.Count; k++) debug.SaveFrame($"{label}_art{k}_{ItemMatcher.Normalize(ArtCrops[k].Name)}", ArtCrops[k].Art);
            debug.Text("-- result");
            foreach (var i in merged.Items) debug.Text($"   ✓ {i.Group.Name} x{i.Quantity}  (read \"{i.OcrText}\", cost {i.Cost:0.0}{(i.QuantityNote != null ? ", " + i.QuantityNote : "")})");
            foreach (var u in merged.Unknown) debug.Text($"   ? \"{u.Text}\"");
        }

        return new OfferScan { Capture = capture, ScreenRegion = region, Items = merged.Items, Unknown = merged.Unknown };
    }

    private static double ChooseScale(Frame f)
    {
        int longest = Math.Max(f.Width, f.Height);
        double scale = Math.Clamp(2000.0 / longest, 1.0, 3.0);
        double cap = (OcrService.MaxDimension - 1.0) / longest;
        return Math.Min(scale, cap);
    }

    private sealed record ParseResult(List<DetectedItem> Items, List<UnknownText> Unknown, List<(int Qty, Rect Bounds)> Quantities, double Score);

    private readonly record struct Segment(int From, int To, ItemGroup? Group, double Cost);

    private sealed class Chunk
    {
        public List<WordBox> Words { get; } = new();
        public double LineHeight { get; init; }
        public List<Segment> Segments { get; set; } = new();
        public double Cost { get; set; }
        public bool Consumed { get; set; }
        public Rect Bounds => Union(Words.Select(w => w.Bounds));
        public bool HasUnknown => Segments.Any(s => s.Group == null && ItemMatcher.Normalize(Words[s.From].Text).Length > 0);
    }

    private ParseResult Parse(List<OcrTextLine> lines, double scale)
    {
        var chunks = new List<Chunk>();
        var quantities = new List<(int Qty, Rect Bounds)>();

        foreach (var line in lines)
        {
            var words = line.Words
                .Select(w => new WordBox(w.Text, new Rect(w.Bounds.X / scale, w.Bounds.Y / scale, w.Bounds.Width / scale, w.Bounds.Height / scale)))
                .OrderBy(w => w.Bounds.X)
                .ToList();
            if (words.Count == 0) continue;

            double height = Median(words.Select(w => w.Bounds.Height));
            var current = new Chunk { LineHeight = height };

            void Flush()
            {
                if (current.Words.Count > 0) chunks.Add(current);
                current = new Chunk { LineHeight = height };
            }

            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];

                if (TryQuantity(w.Text, out int q))
                {
                    quantities.Add((q, w.Bounds));
                    Flush();
                    continue;
                }
                if (w.Text is "x" or "X" or "×" && i + 1 < words.Count && int.TryParse(words[i + 1].Text, out q) && q is > 0 and < 1000)
                {
                    quantities.Add((q, Union(new[] { w.Bounds, words[i + 1].Bounds })));
                    i++;
                    Flush();
                    continue;
                }

                if (current.Words.Count > 0 && w.Bounds.Left - current.Words[^1].Bounds.Right > height * 0.9)
                    Flush();

                current.Words.Add(w);
            }
            Flush();
        }

        foreach (var c in chunks) SegmentChunk(c);
        RejoinWrappedNames(chunks);

        var items = new List<DetectedItem>();
        var unknown = new List<UnknownText>();
        foreach (var c in chunks.Where(c => !c.Consumed)) Emit(c, items, unknown);

        AttachBadges(items, unknown);

        AttachQuantities(items, quantities);

        items = items
            .OrderBy(i => Math.Round(i.Bounds.Top / Math.Max(8, i.Bounds.Height * 2)))
            .ThenBy(i => i.Bounds.Left)
            .ToList();

        double score = items.Sum(i => i.Group.Key.Length - i.Cost) - 0.3 * unknown.Sum(u => u.Text.Length);
        return new ParseResult(items, unknown, quantities, score);
    }

    private void SegmentChunk(Chunk chunk)
    {
        var norms = chunk.Words.Select(w => ItemMatcher.Normalize(w.Text)).ToArray();
        int n = norms.Length;
        var best = new double[n + 1];
        var back = new Segment[n + 1];
        Array.Fill(best, double.MaxValue);
        best[0] = 0;

        for (int i = 0; i < n; i++)
        {
            if (best[i] == double.MaxValue) continue;

            double skip = norms[i].Length + 0.2;
            if (best[i] + skip < best[i + 1])
            {
                best[i + 1] = best[i] + skip;
                back[i + 1] = new Segment(i, i + 1, null, skip);
            }

            var text = new StringBuilder();
            for (int j = i + 1; j <= Math.Min(n, i + 5); j++)
            {
                text.Append(norms[j - 1]);
                if (text.Length == 0) continue;
                var (group, cost) = matcher.Match(text.ToString());
                if (group == null) continue;

                double total = best[i] + cost + 0.5;
                if (total < best[j])
                {
                    best[j] = total;
                    back[j] = new Segment(i, j, group, cost);
                }
            }
        }

        var segments = new List<Segment>();
        for (int k = n; k > 0; k = back[k].From) segments.Add(back[k]);
        segments.Reverse();
        chunk.Segments = segments;
        chunk.Cost = best[n];
    }

    private void RejoinWrappedNames(List<Chunk> chunks)
    {
        var ordered = chunks.OrderBy(c => c.Bounds.Top).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var a = ordered[i];
            if (a.Consumed) continue;

            for (int j = 0; j < ordered.Count; j++)
            {
                var b = ordered[j];
                if (b == a || b.Consumed || !(a.HasUnknown || b.HasUnknown)) continue;

                Rect ra = a.Bounds, rb = b.Bounds;
                double h = Math.Max(a.LineHeight, 1);
                bool below = rb.Top > ra.Top + h * 0.5 && rb.Top - ra.Bottom < h * 1.0;
                double overlap = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left);
                if (!below || overlap < Math.Min(ra.Width, rb.Width) * 0.3) continue;

                var joined = new Chunk { LineHeight = a.LineHeight };
                joined.Words.AddRange(a.Words);
                joined.Words.AddRange(b.Words);
                SegmentChunk(joined);

                if (joined.Cost < a.Cost + b.Cost - 0.01)
                {
                    int idx = chunks.IndexOf(a);
                    chunks[idx] = joined;
                    ordered[i] = joined;
                    a.Consumed = true;
                    b.Consumed = true;
                    joined.Consumed = false;
                    a = joined;
                }
            }
        }
    }

    private static void Emit(Chunk c, List<DetectedItem> items, List<UnknownText> unknown)
    {
        var pending = new List<WordBox>();

        void FlushUnknown()
        {
            if (pending.Count == 0) return;
            var text = string.Join(" ", pending.Select(w => w.Text)).Trim();
            bool allUi = pending.All(w => UiWords.Contains(ItemMatcher.Normalize(w.Text)) || ItemMatcher.Normalize(w.Text).Length == 0);
            if (LooksLikeAName(pending) && !allUi) unknown.Add(new UnknownText(text, Union(pending.Select(w => w.Bounds)), pending.ToList()));
            pending.Clear();
        }

        foreach (var s in c.Segments)
        {
            var words = c.Words.GetRange(s.From, s.To - s.From);
            if (s.Group == null)
            {
                pending.AddRange(words);
                continue;
            }
            FlushUnknown();
            items.Add(new DetectedItem
            {
                Group = s.Group,
                Bounds = Union(words.Select(w => w.Bounds)),
                LineHeight = c.LineHeight,
                OcrText = string.Join(" ", words.Select(w => w.Text)),
                Cost = s.Cost,
            });
        }
        FlushUnknown();
    }

    private void AttachBadges(List<DetectedItem> items, List<UnknownText> unknown)
    {
        for (int u = unknown.Count - 1; u >= 0; u--)
        {
            var words = unknown[u].Words;
            if (words == null) continue;

            var used = new HashSet<WordBox>();
            var candidates = words.Select(w => new[] { w })
                .Concat(words.Zip(words.Skip(1), (a, b) => new[] { a, b }))
                .OrderByDescending(c => c.Length);

            foreach (var group in candidates)
            {
                if (group.Any(used.Contains)) continue;
                var prefix = string.Concat(group.Select(w => ItemMatcher.Normalize(w.Text)));
                if (prefix.Length < 3) continue;
                var box = Union(group.Select(w => w.Bounds));

                var below = items
                    .Select((item, index) => (item, index))
                    .Where(x =>
                    {
                        var b = x.item.Bounds;
                        double h = Math.Max(b.Height, box.Height);
                        double cx = box.X + box.Width / 2;
                        return cx >= b.Left - h && cx <= b.Right + h
                            && b.Top >= box.Top + box.Height * 0.3
                            && b.Top - box.Bottom <= h * 2.5;
                    })
                    .OrderBy(x => x.item.Bounds.Top - box.Bottom)
                    .FirstOrDefault();
                if (below.item == null) continue;

                var (combined, cost) = matcher.Match(prefix + below.item.Group.Key);
                if (combined == null || combined == below.item.Group || cost > 2) continue;

                items[below.index] = new DetectedItem
                {
                    Group = combined,
                    Bounds = Rect.Union(box, below.item.Bounds),
                    OcrText = string.Join(" ", group.Select(w => w.Text)) + " " + below.item.OcrText,
                    Cost = below.item.Cost + cost,
                    Quantity = below.item.Quantity,
                    LineHeight = below.item.LineHeight,
                };
                foreach (var w in group) used.Add(w);
            }

            if (used.Count == 0) continue;
            var left = words.Where(w => !used.Contains(w)).ToList();
            var text = string.Join(" ", left.Select(w => w.Text)).Trim();
            if (LooksLikeAName(left))
                unknown[u] = new UnknownText(text, Union(left.Select(w => w.Bounds)), left);
            else
                unknown.RemoveAt(u);
        }
    }

    private static void AttachQuantities(List<DetectedItem> items, IEnumerable<(int Qty, Rect Bounds)> quantities, List<string>? trace = null)
    {
        foreach (var (qty, r) in quantities)
        {
            var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            DetectedItem? best = null;
            double bestScore = double.MaxValue;

            foreach (var item in items)
            {
                var b = item.Bounds;
                double h = item.LineHeight > 0 ? item.LineHeight : b.Height;
                if (h <= 0) continue;
                bool badgeSized = r.Height >= h * 0.5 && r.Height <= h * 5;
                double rise = b.Top - r.Bottom;
                bool above = rise >= h * 0.5 && rise <= h * 10;
                double nameCentre = b.X + b.Width / 2;
                bool rightHalf = center.X >= nameCentre - b.Width * 0.1 && center.X <= b.Right + h * 3;
                if (!badgeSized || !above || !rightHalf)
                {
                    trace?.Add($"x{qty} at ({r.X:0},{r.Y:0}) {r.Width:0}x{r.Height:0} not for {item.Group.Name}: " +
                               $"{(badgeSized ? "" : "wrong size ")}{(above ? "" : $"not above (rise {rise:0}, line {h:0}) ")}{(rightHalf ? "" : "not on the right ")}");
                    continue;
                }

                double score = rise + Math.Abs(center.X - b.Right);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            if (best != null && qty > best.Quantity)
            {
                best.Quantity = qty;
                best.QuantityNote = $"x{qty} badge at ({r.X:0}, {r.Y:0})";
            }
        }
    }

    private List<string> Trace { get; } = new();

    private List<(string Name, Frame Art)> ArtCrops { get; } = new();

    private async Task<List<string>> ResolveLookalikesAsync(Frame capture, List<DetectedItem> items)
    {
        var log = new List<string>();
        double slot = EstimateSlotWidth(items, capture.Width);
        capture = LiftBanners(capture, log);

        for (int n = 0; n < items.Count; n++)
        {
            var item = items[n];
            if (item.Bounds.Width <= 0) continue;
            var near = matcher.NearNames(item.Group.Key)
                .Where(g => item.Cost > 0 || g.Key.Length >= item.Group.Key.Length)
                .ToList();
            if (item.Group.Variants.Count < 2 && near.Count == 0) continue;

            var art = CardArt(capture, item.Bounds, slot);
            if (art != null) ArtCrops.Add((item.Group.Name, art));
            var mine = art == null ? null : ArtMatcher.Describe(art);
            if (mine == null)
            {
                log.Add($"{item.Group.Name}: card art not usable");
                continue;
            }

            var scores = new List<(ItemGroup Group, ValueItem Variant, double Score)>();
            foreach (var group in near.Prepend(item.Group))
            {
                foreach (var v in group.Variants)
                {
                    if (await IconCache.GetFrameAsync(v.Icon) is { } picture && ArtMatcher.Describe(picture, sitePicture: true) is { } theirs)
                    {
                        scores.Add((group, v, ArtMatcher.Similarity(mine, theirs)));
                        log.Add($"   {v.Name} ({v.Origin}): {ArtMatcher.Explain(mine, theirs)}");
                    }
                }
            }
            if (scores.Count == 0)
            {
                log.Add($"{item.Group.Name}: no pictures to compare (offline?)");
                continue;
            }

            scores.Sort((a, b) => b.Score.CompareTo(a.Score));
            var best = scores[0];
            double margin = scores.Count > 1 ? best.Score - scores[1].Score : 1;
            log.Add($"{item.Group.Name}: " + string.Join(", ", scores.Select(x => $"{x.Variant.Name} {x.Variant.Origin} {x.Score:0.000}")));

            if (best.Group != item.Group)
            {
                if (margin >= ArtMatcher.ConfidentMargin * 2)
                {
                    items[n] = item = new DetectedItem
                    {
                        Group = best.Group,
                        Bounds = item.Bounds,
                        OcrText = item.OcrText,
                        Cost = item.Cost,
                        Quantity = item.Quantity,
                        QuantityNote = item.QuantityNote,
                        LineHeight = item.LineHeight,
                    };
                    log.Add($"   read as \"{items[n].OcrText}\" but the picture is {best.Variant.Name}; using that");
                }
                else
                {
                    log.Add("   picture leans to another item but not clearly; keeping the text match and asking");
                }
            }

            bool confirmed = best.Group == item.Group && margin >= ArtMatcher.ConfidentMargin;
            if (near.Count > 0 && !confirmed && best.Group == item.Group || near.Count > 0 && best.Group != item.Group)
            {
                var own = item.Group;
                var choices = own.Variants.Concat(near.SelectMany(g => g.Variants)).Distinct().ToList();
                var ownBest = scores.Where(s => s.Group == own).Select(s => s.Variant).FirstOrDefault() ?? own.Variants[0];
                items[n] = new DetectedItem
                {
                    Group = new ItemGroup(own.Key, own.Name, choices),
                    Bounds = item.Bounds,
                    OcrText = item.OcrText,
                    Cost = item.Cost,
                    Quantity = item.Quantity,
                    QuantityNote = item.QuantityNote,
                    LineHeight = item.LineHeight,
                    Variant = ownBest,
                    VariantMargin = 0,
                };
                log.Add($"   could also be {string.Join(" or ", near.Select(g => g.Name))}; asking");
                continue;
            }

            if (item.Group.Variants.Count > 1)
            {
                var own = scores.Where(s => s.Group == item.Group).ToList();
                item.Variant = own[0].Variant;
                item.VariantMargin = own.Count > 1 ? own[0].Score - own[1].Score : 1;
            }
        }
        return log;
    }

    private static Frame LiftBanners(Frame capture, List<string> log)
    {
        int w = capture.Width, h = capture.Height;
        var px = capture.Pixels;
        var rowMedian = new int[h];
        var row = new int[w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                row[x] = (px[o] + px[o + 1] + px[o + 2]) / 3;
            }
            Array.Sort(row);
            rowMedian[y] = row[w / 2];
        }
        var sorted = rowMedian.OrderBy(v => v).ToArray();
        int typical = sorted[h / 2];
        if (typical < 12) return capture;

        Frame? lifted = null;
        for (int y = 0; y < h;)
        {
            if (rowMedian[y] > typical * 0.7) { y++; continue; }
            int start = y;
            while (y < h && (rowMedian[y] <= typical * 0.7 ||
                             Enumerable.Range(y + 1, 3).Any(k => k < h && rowMedian[k] <= typical * 0.7))) y++;
            int length = y - start;
            if (length < 8 || length > h * 0.4 || start == 0 || y == h) continue;

            double band = rowMedian.Skip(start).Take(length).OrderBy(v => v).ElementAt(length / 2);
            double gain = Math.Clamp(typical / Math.Max(band, 1), 1, 4);
            lifted ??= new Frame(w, h, (byte[])px.Clone());
            var d = lifted.Pixels;
            for (int yy = start; yy < y; yy++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (yy * w + x) * 4;
                    for (int c = 0; c < 3; c++) d[o + c] = (byte)Math.Min(255, d[o + c] * gain);
                }
            }
            log.Add($"banner across rows {start}-{y - 1}, brightened x{gain:0.0} for picture matching");
        }
        return lifted ?? capture;
    }

    private static Frame? CardArt(Frame capture, Rect label, double slot)
    {
        double cx = label.X + label.Width / 2;
        var r = new Rect(cx - slot * 0.40, label.Top - slot * 0.90, slot * 0.80, slot * 0.80);
        int x0 = (int)Math.Max(0, r.X), y0 = (int)Math.Max(0, r.Y);
        int x1 = (int)Math.Min(capture.Width, r.Right), y1 = (int)Math.Min(capture.Height, r.Bottom);
        if (x1 - x0 < 12 || y1 - y0 < 12) return null;
        return Crop(capture, new Int32Rect(x0, y0, x1 - x0, y1 - y0), pad: 0);
    }

    private static double EstimateSlotWidth(List<DetectedItem> items, int captureWidth)
    {
        var gaps = new List<double>();
        for (int a = 0; a < items.Count; a++)
        {
            double ca = items[a].Bounds.X + items[a].Bounds.Width / 2, best = double.MaxValue;
            for (int b = 0; b < items.Count; b++)
            {
                if (a == b || Math.Abs(items[a].Bounds.Y - items[b].Bounds.Y) > Math.Max(items[a].Bounds.Height, 1)) continue;
                double dx = Math.Abs(ca - (items[b].Bounds.X + items[b].Bounds.Width / 2));
                if (dx > 1 && dx < best) best = dx;
            }
            if (best < captureWidth * 0.45) gaps.Add(best);
        }
        return gaps.Count > 0 ? Median(gaps) : captureWidth / 4.0;
    }

    private ParseResult Merge(ParseResult a, ParseResult b, List<(int Qty, Rect Bounds)> extraTags)
    {
        var (primary, secondary) = a.Score >= b.Score ? (a, b) : (b, a);
        var items = primary.Items.ToList();
        var unknown = primary.Unknown.ToList();

        foreach (var s in secondary.Items)
        {
            var twin = items.FirstOrDefault(p => Overlaps(p.Bounds, s.Bounds));
            if (twin != null)
            {
                if (s.Group.Key.Length > twin.Group.Key.Length && s.Group.Key.EndsWith(twin.Group.Key, StringComparison.Ordinal))
                {
                    s.Quantity = Math.Max(s.Quantity, twin.Quantity);
                    items[items.IndexOf(twin)] = s;
                    unknown.RemoveAll(u => Overlaps(u.Bounds, s.Bounds));
                }
                continue;
            }
            if (s.Cost > 1) continue;

            items.Add(s);
            unknown.RemoveAll(u => Overlaps(u.Bounds, s.Bounds));
        }

        AttachQuantities(items, primary.Quantities.Concat(secondary.Quantities).Concat(extraTags), Trace);

        items = items
            .OrderBy(i => Math.Round(i.Bounds.Top / Math.Max(8, i.Bounds.Height * 2)))
            .ThenBy(i => i.Bounds.Left)
            .ToList();
        return new ParseResult(items, unknown, primary.Quantities, primary.Score);
    }

    private async Task<List<(int Qty, Rect Bounds)>> ReadSkippedTagsAsync(Frame mask, IEnumerable<OcrTextLine> lines, double scale, DebugSink? debug)
    {
        var words = lines.SelectMany(l => l.Words).ToList();
        if (words.Count == 0) return new();
        double textHeight = Median(words.Select(w => w.Bounds.Height));

        var blobs = await Task.Run(() => FindUnreadBlobs(mask, words.Select(w => w.Bounds), textHeight));
        var found = new List<(int, Rect)>();
        if (blobs.Count == 0) return found;

        var anchorWord = words
            .Where(w => w.Text.Count(char.IsLetter) >= 4 && Math.Abs(w.Bounds.Height - textHeight) < textHeight * 0.4)
            .OrderByDescending(w => w.Text.Length)
            .FirstOrDefault();
        if (anchorWord == null) return found;
        var anchor = Crop(mask, ToInt(anchorWord.Bounds), pad: 4);

        foreach (var blob in blobs.Take(12))
        {
            var tag = Crop(mask, blob, pad: 4);
            var composite = SideBySide(anchor, tag, gap: (int)(textHeight * 0.8), margin: (int)textHeight);
            var result = await ocr.RecognizeAsync(composite.Image);
            var text = string.Concat(result
                .SelectMany(l => l.Words)
                .Where(w => w.Bounds.X + w.Bounds.Width / 2 >= composite.SecondX)
                .Select(w => w.Text));
            debug?.Text($"   unread blob {blob.Width}x{blob.Height} at ({blob.X},{blob.Y}) read as \"{text}\"");
            if (TryQuantity(text, out int q))
                found.Add((q, new Rect(blob.X / scale, blob.Y / scale, blob.Width / scale, blob.Height / scale)));
        }
        return found;
    }

    private static (Frame Image, int SecondX) SideBySide(Frame a, Frame b, int gap, int margin)
    {
        int w = margin * 2 + a.Width + gap + b.Width;
        int h = margin * 2 + Math.Max(a.Height, b.Height);
        var px = new byte[w * h * 4];
        Array.Fill(px, (byte)255);

        void Blit(Frame src, int ox)
        {
            int oy = (h - src.Height) / 2;
            for (int y = 0; y < src.Height; y++)
                Buffer.BlockCopy(src.Pixels, y * src.Width * 4, px, ((oy + y) * w + ox) * 4, src.Width * 4);
        }

        Blit(a, margin);
        int secondX = margin + a.Width + gap;
        Blit(b, secondX);
        return (new Frame(w, h, px), secondX);
    }

    private static Int32Rect ToInt(Rect r) =>
        new((int)Math.Floor(r.X), (int)Math.Floor(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height));

    private static List<Int32Rect> FindUnreadBlobs(Frame mask, IEnumerable<Rect> readWords, double textHeight)
    {
        const int step = 2;
        int gw = mask.Width / step, gh = mask.Height / step;
        var dark = new bool[gw * gh];
        var px = mask.Pixels;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                dark[y * gw + x] = px[((y * step) * mask.Width + x * step) * 4] < 60;

        foreach (var r in readWords)
        {
            int x0 = Math.Max(0, (int)((r.Left - 4) / step)), x1 = Math.Min(gw - 1, (int)((r.Right + 4) / step));
            int y0 = Math.Max(0, (int)((r.Top - 4) / step)), y1 = Math.Min(gh - 1, (int)((r.Bottom + 4) / step));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    dark[y * gw + x] = false;
        }

        var components = new List<Int32Rect>();
        var seen = new bool[dark.Length];
        var stack = new Stack<int>();
        for (int i = 0; i < dark.Length; i++)
        {
            if (!dark[i] || seen[i]) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0, count = 0;
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop(), cx = p % gw, cy = p / gw;
                count++;
                minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                        int n = ny * gw + nx;
                        if (dark[n] && !seen[n]) { seen[n] = true; stack.Push(n); }
                    }
            }
            if (count >= 4) components.Add(new Int32Rect(minX * step, minY * step, (maxX - minX + 1) * step, (maxY - minY + 1) * step));
        }

        components.Sort((a, b) => a.X.CompareTo(b.X));
        var words = new List<Int32Rect>();
        foreach (var c in components)
        {
            int idx = words.FindIndex(w =>
                c.X - (w.X + w.Width) < textHeight * 0.6 && c.X >= w.X &&
                Math.Min(w.Y + w.Height, c.Y + c.Height) - Math.Max(w.Y, c.Y) > Math.Min(w.Height, c.Height) * 0.3);
            if (idx < 0)
            {
                words.Add(c);
                continue;
            }
            var w = words[idx];
            int x0 = Math.Min(w.X, c.X), y0 = Math.Min(w.Y, c.Y);
            int x1 = Math.Max(w.X + w.Width, c.X + c.Width), y1 = Math.Max(w.Y + w.Height, c.Y + c.Height);
            words[idx] = new Int32Rect(x0, y0, x1 - x0, y1 - y0);
        }

        return words
            .Where(w => w.Height >= textHeight * 0.5 && w.Height <= textHeight * 1.6 && w.Width <= textHeight * 3.5 && w.Width >= textHeight * 0.6)
            .ToList();
    }

    private static Frame Crop(Frame src, Int32Rect r, int pad)
    {
        int x0 = Math.Max(0, r.X - pad), y0 = Math.Max(0, r.Y - pad);
        int x1 = Math.Min(src.Width, r.X + r.Width + pad), y1 = Math.Min(src.Height, r.Y + r.Height + pad);
        int w = x1 - x0, h = y1 - y0;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(src.Pixels, ((y0 + y) * src.Width + x0) * 4, px, y * w * 4, w * 4);
        return new Frame(w, h, px);
    }

    private static bool LooksLikeAName(IEnumerable<WordBox> words) =>
        words.Any(w => w.Text.Count(char.IsLetter) >= 4);

    private static bool TryQuantity(string text, out int qty)
    {
        qty = 0;
        var m = Quantity.Match(text.Trim());
        if (!m.Success) return false;
        qty = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return qty is > 0 and < 1000;
    }

    private static bool Overlaps(Rect a, Rect b)
    {
        var i = a;
        i.Intersect(b);
        if (i.IsEmpty) return false;
        double smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return smaller > 0 && i.Width * i.Height / smaller > 0.3;
    }

    private static Rect Union(IEnumerable<Rect> rects)
    {
        Rect? acc = null;
        foreach (var r in rects)
        {
            if (acc is { } a) { a.Union(r); acc = a; }
            else acc = r;
        }
        return acc ?? Rect.Empty;
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v[v.Length / 2];
    }
}

public sealed class DebugSink
{
    private readonly StringBuilder _log = new();
    public string Folder { get; }

    public DebugSink(string? folder = null)
    {
        Folder = folder ?? Path.Combine(AppPaths.DebugDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(Folder);
    }

    public void SaveFrame(string name, Frame f) => f.SavePng(Path.Combine(Folder, name + ".png"));
    public void Text(string line) => _log.AppendLine(line);
    public void Flush() => File.WriteAllText(Path.Combine(Folder, "ocr.txt"), _log.ToString());
}
