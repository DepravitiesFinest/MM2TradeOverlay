// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TradeValueOverlay;

public static class SelfTest
{
    private sealed record Card(string Name, int Qty, Color Rarity, string? Decoy = null);

    private sealed record Scene(string Title, string Font, double FontSize, double CardWidth, Card[] Cards, int PerRow = 4, string[]? Fakes = null);

    public static async Task<int> RunAsync(string[] args)
    {
        int outIdx = Array.IndexOf(args, "--out");
        var outDir = outIdx >= 0 && outIdx + 1 < args.Length ? args[outIdx + 1] : Path.Combine(AppContext.BaseDirectory, "selftest");
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        int failures = 0;

        try
        {
            if (args.Contains("--fetch"))
            {
                var snap = await ValueFetcher.FetchAsync(null, CancellationToken.None);
                File.WriteAllText(Path.Combine(outDir, "values.json"), JsonSerializer.Serialize(snap, ValueStore.Json));
                report.AppendLine($"FETCH: {snap.Items.Count} rows");
            }

            failures += ValueChecks(report);

            var store = new ValueStore();
            var fetched = Path.Combine(outDir, "values.json");
            if (File.Exists(fetched))
                store.Apply(JsonSerializer.Deserialize<ValueSnapshot>(File.ReadAllText(fetched), ValueStore.Json)!, persist: false);
            else
                store.Load();
            report.AppendLine($"VALUES: {store.ItemCount} unique names");
            if (store.Snapshot != null) ReportSnapshot(store.Snapshot, report);

            var ocr = OcrService.Create();
            report.AppendLine($"OCR: {ocr.LanguageName}, max dimension {OcrService.MaxDimension}");
            var scanner = new TradeScanner(ocr, store.Matcher);

            var screen = Monitors.FromCursor();
            var shot = ScreenGrabber.Capture(new PixelRect(screen.X, screen.Y, Math.Min(400, screen.Width), Math.Min(300, screen.Height)));
            int lit = 0;
            for (int i = 0; i < shot.Pixels.Length; i += 4) if (shot.Pixels[i] + shot.Pixels[i + 1] + shot.Pixels[i + 2] > 30) lit++;
            report.AppendLine($"CAPTURE: monitor {screen}, grabbed {shot.Width}x{shot.Height}, {100.0 * lit / (shot.Width * shot.Height):0}% non-black pixels");

            var scans = new List<OfferScan>();
            if (args.Contains("--ocr") || args.Contains("--screens"))
            {
                int n = 0;
                foreach (var scene in Scenes())
                {
                    n++;
                    var frame = Render(scene);
                    frame.SavePng(Path.Combine(outDir, $"scene{n}.png"));
                    var debug = new DebugSink(Path.Combine(outDir, $"scene{n}_debug"));
                    var result = await scanner.ReadOfferAsync(frame, new PixelRect(0, 0, frame.Width, frame.Height), debug, "offer");
                    debug.Flush();
                    scans.Add(result);

                    var expected = scene.Cards.Where(c => scene.Fakes?.Contains(c.Name) != true).Select(c => (Key: ItemMatcher.Normalize(c.Name), c.Qty)).OrderBy(x => x.Key).ToList();
                    var actual = result.Items.Select(i => (Key: i.Group.Key, i.Quantity)).OrderBy(x => x.Key).ToList();
                    bool pass = expected.SequenceEqual(actual);
                    if (!pass) failures++;

                    report.AppendLine($"{(pass ? "PASS" : "FAIL")}  scene {n}: {scene.Title}");
                    foreach (var c in scene.Cards)
                    {
                        var (g, _) = store.Matcher.Match(ItemMatcher.Normalize(c.Name));
                        report.AppendLine($"        expect {c.Name} x{c.Qty}{(g == null ? "   (!! not in value list)" : "")}");
                    }
                    foreach (var i in result.Items)
                        report.AppendLine($"        got    {i.Group.Name} x{i.Quantity}   <- \"{i.OcrText}\" cost {i.Cost:0.0}");
                    foreach (var u in result.Unknown)
                        report.AppendLine($"        ???    \"{u.Text}\"");
                }
            }

            if (args.Contains("--sweep"))
                await LookalikeSweep(store, report);

            if (args.Contains("--ocr"))
                failures += await LookalikeChecks(store, ArgValue(args, "--fixtures"), report);

            if (args.Contains("--ocr"))
                failures += await Fixtures(scanner, outDir, report, ArgValue(args, "--fixtures"));

            if (args.Contains("--screens"))
                await Screens(outDir, store, scans, report);
        }
        catch (Exception ex)
        {
            report.AppendLine("EXCEPTION " + ex);
            failures++;
        }

        report.AppendLine(failures == 0 ? "ALL OK" : $"{failures} FAILURE(S)");
        File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
        return failures == 0 ? 0 : 1;
    }

    private static int ValueChecks(StringBuilder report)
    {
        int fails = 0;
        void Check(string what, bool ok)
        {
            if (!ok) fails++;
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  value: {what}");
        }

        (double?, ValueUnit) P(string s) => ValueFetcher.ParseValue(s);
        Check("\"45\" is 45 value", P("45") == (45, ValueUnit.Value));
        Check("\"0.15\" is 0.15 value", P("0.15") == (0.15, ValueUnit.Value));
        Check("\"1,250\" is 1250 value", P("1,250") == (1250, ValueUnit.Value));
        Check("\"4 (T1) Legend\" is 4 Legendaries", P("4 (T1) Legend") == (4, ValueUnit.Legendary));
        Check("\"4 X (T1) Legend\" is 4 Legendaries", P("4 X (T1) Legend") == (4, ValueUnit.Legendary));
        Check("\"3 (T1) Rare\" is 3 Rares", P("3 (T1) Rare") == (3, ValueUnit.Rare));
        Check("\"2 (T1) Uncom\" is 2 Uncommons", P("2 (T1) Uncom") == (2, ValueUnit.Uncommon));
        Check("\"1 (T1) Comm\" is 1 Common", P("1 (T1) Comm") == (1, ValueUnit.Common));
        Check("unknown text is no value, not a guess", P("N/A").Item1 == null && P("2 (T2) Legend").Item1 == null && P("3 (T1) Mythic").Item1 == null);

        bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        Check("6 C = 1 U", Near(ValueMath.ToValue(ValueUnit.Common, 6), ValueMath.ToValue(ValueUnit.Uncommon, 1)));
        Check("6 U = 1 R", Near(ValueMath.ToValue(ValueUnit.Uncommon, 6), ValueMath.ToValue(ValueUnit.Rare, 1)));
        Check("6 R = 1 L", Near(ValueMath.ToValue(ValueUnit.Rare, 6), ValueMath.ToValue(ValueUnit.Legendary, 1)));
        Check("5 L = 1 value", Near(ValueMath.ToValue(ValueUnit.Legendary, 5), 1));
        Check("site: 8 × 1R shows \"1L 2R\"", ValueMath.Format(ValueMath.ToValue(ValueUnit.Rare, 8)) == "1L 2R");
        Check("site: 2 × 3R shows \"1L\"", ValueMath.Format(ValueMath.ToValue(ValueUnit.Rare, 6)) == "1L");
        Check("site: 6480 C = 6 value", ValueMath.Format(ValueMath.ToValue(ValueUnit.Common, 6480)) == "6");
        Check("site: 50 L = 10 value", ValueMath.Format(ValueMath.ToValue(ValueUnit.Legendary, 50)) == "10");
        Check("7 + 2 L shows \"7 + 2L\"", ValueMath.Format(7 + ValueMath.ToValue(ValueUnit.Legendary, 2)) == "7 + 2L");
        Check("big totals round: 130.975 shows \"131\"", ValueMath.Format(130.975) == "131");
        Check("a 4L item is worth less than a 1-value item", ValueMath.ToValue(ValueUnit.Legendary, 4) < 1);
        var unpriced = new ValueItem { Name = "Cherries", Amount = 0, Unit = ValueUnit.Value, ValueText = "0" };
        Check("\"0\" (not priced yet) counts as 1 Common, not nothing", unpriced.IsUnvalued && Near(unpriced.Value!.Value, ValueMath.ToValue(ValueUnit.Common, 1)));
        Check("two unpriced items show as ~2C", Fmt.Worth(unpriced, 2) == "~2C");
        Check("signed format", ValueMath.FormatSigned(-ValueMath.ToValue(ValueUnit.Legendary, 3)) == "−3L");

        const string html = "<p><img src=../img/pred.png width=45> &nbsp; <font color=black size=+1><span>Pre<b>d</b>ator (knife) - Value: 4 (T1) Legend<br></font>" +
                            "<font color=red> &nbsp; Origin: Knife Box 3</font></span></p><p><img src=../img/bw.png> <span>Batwing - Value: 45<br></font><font color=red> Origin: Halloween 2018</font></span></p>";
        var rows = ValueFetcher.ParsePage(html).ToList();
        Check("page parse: 2 rows", rows.Count == 2);
        if (rows.Count == 2)
        {
            Check("page parse: \"Predator (knife)\" → name Predator, variant Knife",
                rows[0].Name == "Predator" && rows[0].Variant == "Knife");
            Check("page parse: tier value kept as 4 L (0.8), not 4",
                rows[0].Amount == 4 && rows[0].Unit == ValueUnit.Legendary && Near(rows[0].Value!.Value, 0.8));
            Check("page parse: icon URL resolved", rows[0].Icon == "https://www.mm2values.com/img/pred.png");
            Check("page parse: plain value", rows[1].Amount == 45 && rows[1].Unit == ValueUnit.Value && rows[1].Origin == "Halloween 2018");
        }
        var heart = ValueFetcher.ParsePage("<p><img src=../img/heartp.png> <span><3 (<b>h</b>eart) - Value: 10<br></font><font color=red> Origin: 400 coins Valentines limited</font></span></p>").ToList();
        Check("\"<3 (heart)\" parses as one item, not \"art)\" junk", heart.Count == 1 && heart[0].Name == "<3" && heart[0].Variant == "Heart");

        const string cat =
            "<div class=stackable><img src='img/TravGun.png' width=100><b>Travelers Gun</b><br> Value: 5,150<br>Range: N/A <br>Demand: 5 - Rarity: 4<br>Stability: Stable<hr> <input>" +
            "<div class=stackable><img src='img/chromaluger.png'><b>Chroma Luger</b> Value: 48<br>Range: N/A <br>Demand: 2 - Rarity: 2<br>Stability: Fluctuating<hr>" +
            "<div class=stackable><img src='img/PredatorKnifeUp.png'><b>Predator (knife)</b><br> Value: 4 X (T1) Legend<br>Range: 3-5 <br>Demand: 2.5 - Rarity: 3<br>Stability: N/A<hr>";
        var entries = ValueFetcher.ParseCategoryPage(cat, "Godly").ToList();
        Check("category parse: 3 items", entries.Count == 3);
        if (entries.Count == 3)
        {
            Check("category parse: demand, rarity, stability", entries[0].Name == "Travelers Gun" && entries[0].Demand == 5 && entries[0].Rarity == 4 && entries[0].Stability == "Stable");
            Check("category parse: chroma page inline value", entries[1].Name == "Chroma Luger" && entries[1].ValueText == "48" && entries[1].Stability == "Fluctuating");
            Check("category parse: variant, range, N/A stability", entries[2].Name == "Predator" && entries[2].Variant == "Knife" && entries[2].Range == "3-5" && entries[2].Stability == null && entries[2].Demand == 2.5);

            var list = new List<ValueItem>
            {
                new() { Name = "Travelers Gun", ValueText = "5150", Amount = 5150, Icon = "https://www.mm2values.com/img/TravGun.png" },
                new() { Name = "Predator", Variant = "Knife", ValueText = "4 (T1) Legend", Amount = 4, Unit = ValueUnit.Legendary },
            };
            int added = ValueFetcher.Enrich(list, entries);
            Check("join: demand lands on the right items", list[0].Demand == 5 && list[1].Demand == 2.5 && list[1].Range == "3-5");
            Check("join: items only on category pages are added", added == 1 && list.Any(i => i.Name == "Chroma Luger" && i.Demand == 2));
        }
        return fails;
    }

    private static void ReportSnapshot(ValueSnapshot snap, StringBuilder report)
    {
        var byUnit = snap.Items.GroupBy(i => i.Amount == null ? "no value" : i.Unit.ToString())
            .Select(g => $"{g.Key} {g.Count()}");
        report.AppendLine("UNITS: " + string.Join(", ", byUnit));
        report.AppendLine($"DEMAND: {snap.Items.Count(i => i.Demand != null)} of {snap.Items.Count} items rated");
        var wrong = snap.Items.Where(i => i.ValueText.Contains("(T") && i.Unit == ValueUnit.Value).ToList();
        report.AppendLine(wrong.Count == 0 ? "PASS  value: every \"(T1)\" row parsed as a tier unit"
                                           : $"FAIL  value: {wrong.Count} tier rows parsed as plain value, e.g. {wrong[0].Name}");
    }

    private static readonly Color Godly = Color.FromRgb(0xE0, 0x3B, 0x6A);
    private static readonly Color Chroma = Color.FromRgb(0x8E, 0x4B, 0xE8);
    private static readonly Color Legendary = Color.FromRgb(0xE8, 0xB4, 0x2C);
    private static readonly Color Rare = Color.FromRgb(0x3A, 0x8D, 0xE8);
    private static readonly Color Common = Color.FromRgb(0x6B, 0x72, 0x80);

    private static IEnumerable<Scene> Scenes()
    {
        yield return new Scene("four slots, bold font", "Segoe UI Black", 15, 118, new[]
        {
            new Card("Chroma Luger", 1, Chroma), new Card("Seer", 2, Godly), new Card("Batwing", 1, Godly), new Card("Chroma Shark", 1, Chroma),
        });
        yield return new Scene("long names that wrap onto two lines", "Arial Black", 14, 104, new[]
        {
            new Card("Chroma DeathShard", 1, Chroma), new Card("Chroma Gingerblade", 1, Chroma), new Card("Chroma Fang", 3, Chroma),
        });
        yield return new Scene("small text, yellow card", "Segoe UI Semibold", 12, 96, new[]
        {
            new Card("Chroma Laser", 1, Chroma), new Card("Chroma Seer", 1, Legendary), new Card("Chroma Tides", 1, Rare), new Card("Batwing", 1, Common),
        });
        yield return new Scene("rounded game-style font", "Comic Sans MS", 15, 118, new[]
        {
            new Card("Chroma Slasher", 1, Chroma), new Card("Chroma Heat", 2, Legendary), new Card("Chroma Saw", 1, Rare),
        });
        yield return new Scene("two rows, badge under a name, plus a fake item", "Segoe UI Black", 14, 110, new[]
        {
            new Card("Chroma Luger", 1, Chroma), new Card("Batwing", 1, Godly), new Card("Seer", 1, Godly),
            new Card("Totally Fake Blade", 1, Common), new Card("Chroma Fang", 4, Chroma), new Card("Chroma Saw", 1, Rare),
        }, PerRow: 3, Fakes: new[] { "Totally Fake Blade" });
        yield return new Scene("cheap tier-valued items", "Segoe UI Black", 15, 118, new[]
        {
            new Card("Emerald", 1, Legendary), new Card("Fade", 2, Rare), new Card("Galaxy", 1, Rare), new Card("Camo", 3, Common),
        });
        yield return new Scene("no phantom quantities from stray text", "Segoe UI Black", 15, 118, new[]
        {
            new Card("Batwing", 1, Godly, Decoy: "x3"), new Card("Seer", 2, Godly), new Card("Chroma Luger", 1, Chroma, Decoy: "%2"),
        });
    }

    private static readonly Dictionary<string, OfferScan> FixtureScans = new();

    private static async Task<int> Fixtures(TradeScanner scanner, string outDir, StringBuilder report, string? dirOverride)
    {
        var dir = dirOverride ?? FindFixtureDir();
        if (dir == null)
        {
            report.AppendLine("FIXTURES: none found");
            return 0;
        }

        int fails = 0;
        foreach (var png in Directory.GetFiles(dir, "*.png").OrderBy(f => f))
        {
            var expectFile = Path.ChangeExtension(png, ".txt");
            if (!File.Exists(expectFile)) continue;

            var expected = File.ReadAllLines(expectFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(l, @"^(.*?)\s+x(\d+)$");
                    return m.Success ? (Key: ItemMatcher.Normalize(m.Groups[1].Value), Qty: int.Parse(m.Groups[2].Value)) : (Key: ItemMatcher.Normalize(l), Qty: 1);
                })
                .OrderBy(x => x.Key).ToList();

            using var fs = File.OpenRead(png);
            var frame = Frame.FromBitmapSource(BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            var name = Path.GetFileNameWithoutExtension(png);
            var debug = new DebugSink(Path.Combine(outDir, "fixture_" + name));
            var result = await scanner.ReadOfferAsync(frame, new PixelRect(0, 0, frame.Width, frame.Height), debug, "offer");
            debug.Flush();
            FixtureScans[name] = result;

            bool Asked(DetectedItem i) => i.Variant != null && i.VariantMargin < ArtMatcher.ConfidentMargin && i.Group.Variants.Count > 1;
            HashSet<string> Accepts(DetectedItem i) => Asked(i)
                ? i.Group.Variants.Select(v => ItemMatcher.Normalize(v.Name)).ToHashSet()
                : new HashSet<string> { ItemMatcher.Normalize(i.Variant?.Name ?? i.Group.Name) };

            var remaining = result.Items.ToList();
            bool pass = true;
            foreach (var (key, qty) in expected)
            {
                var hit = remaining.FirstOrDefault(i => Accepts(i).Contains(key) && i.Quantity == qty);
                if (hit == null) { pass = false; break; }
                remaining.Remove(hit);
            }
            pass &= remaining.Count == 0;
            if (!pass) fails++;

            report.AppendLine($"{(pass ? "PASS" : "FAIL")}  fixture {name}");
            foreach (var i in result.Items)
                report.AppendLine($"        got    {i.Variant?.Name ?? i.Group.Name} x{i.Quantity}   <- \"{i.OcrText}\"" +
                                  (Asked(i) ? $"   (asks: {string.Join(" / ", i.Group.Variants.Select(v => v.Name).Distinct())})" : ""));
            var expectedMerged = expected.GroupBy(e => e.Key).Select(g => (Key: g.Key, Qty: g.Sum(e => e.Qty))).OrderBy(x => x.Key).ToList();
            foreach (var u in result.Unknown) report.AppendLine($"        ???    \"{u.Text}\"");
            if (!pass) report.AppendLine("        expect " + string.Join(", ", expectedMerged.Select(e => $"{e.Key} x{e.Qty}")));
        }
        return fails;
    }

    private static async Task<int> LookalikeChecks(ValueStore store, string? fixtureDir, StringBuilder report)
    {
        var dir = Path.Combine(fixtureDir ?? FindFixtureDir() ?? "", "lookalikes");
        if (!Directory.Exists(dir)) return 0;

        int fails = 0;
        foreach (var png in Directory.GetFiles(dir, "*.png").OrderBy(p => p))
        {
            var expectLine = File.ReadAllLines(Path.ChangeExtension(png, ".txt")).First(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'));
            var parts = expectLine.Split('|', StringSplitOptions.TrimEntries);
            var label = Path.GetFileNameWithoutExtension(png).Split("_card")[0];

            var (group, _) = store.Matcher.Match(ItemMatcher.Normalize(label));
            using var fs = File.OpenRead(png);
            var card = Frame.FromBitmapSource(BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            var mine = ArtMatcher.Describe(card);
            if (group == null || mine == null)
            {
                fails++;
                report.AppendLine($"FAIL  look-alike {Path.GetFileName(png)}: {(group == null ? "label not found" : "art unusable")}");
                continue;
            }

            var scores = new List<(ValueItem V, double S)>();
            foreach (var v in group.Variants)
                if (await IconCache.GetFrameAsync(v.Icon) is { } pic && ArtMatcher.Describe(pic, sitePicture: true) is { } theirs)
                    scores.Add((v, ArtMatcher.Similarity(mine, theirs)));
            scores.Sort((a, b) => b.S.CompareTo(a.S));

            bool pass = scores.Count > 0 && scores[0].V.Name == parts[0] && (parts.Length < 2 || scores[0].V.Origin == parts[1]);
            if (!pass) fails++;
            report.AppendLine($"{(pass ? "PASS" : "FAIL")}  look-alike {Path.GetFileName(png)} (label \"{group.Name}\", {group.Variants.Count} candidates), expect {expectLine}");
            foreach (var (v, sc) in scores) report.AppendLine($"        {sc:0.000}  {v.Name}, {v.Origin ?? v.Category}");
        }
        return fails;
    }

    private static async Task LookalikeSweep(ValueStore store, StringBuilder report)
    {
        var groups = store.Matcher.AllGroups.Where(g => g.Variants.Count > 1).ToList();
        var sigs = new Dictionary<ValueItem, ArtMatcher.Signature?>();
        foreach (var v in groups.SelectMany(g => g.Variants).Distinct())
            sigs[v] = await IconCache.GetFrameAsync(v.Icon) is { } pic ? ArtMatcher.Describe(pic, sitePicture: true) : null;

        int total = 0, right = 0;
        var misses = new List<string>();
        var rightMargins = new List<double>();
        var wrongMargins = new List<double>();
        foreach (var g in groups)
        {
            foreach (var v in g.Variants)
            {
                if (await IconCache.GetFrameAsync(v.Icon) is not { } pic) continue;
                if (ArtMatcher.Describe(AsGameCard(pic)) is not { } card) continue;
                total++;
                var ranked = g.Variants.Where(c => sigs[c] != null)
                    .Select(c => (c, score: ArtMatcher.Similarity(card, sigs[c]!)))
                    .OrderByDescending(x => x.score).ToList();
                var best = ranked[0];
                double margin = ranked.Count > 1 ? best.score - ranked[1].score : 1;
                if (best.c == v) { right++; rightMargins.Add(margin); }
                else
                {
                    wrongMargins.Add(margin);
                    misses.Add($"{v.Name} ({v.Origin}) taken for {best.c.Name} ({best.c.Origin}), margin {margin:0.000}");
                }
            }
        }
        report.AppendLine($"SWEEP: {right}/{total} look-alike items picked correctly from their group ({100.0 * right / Math.Max(1, total):0}%)");
        foreach (var m in misses) report.AppendLine("        miss  " + m);
        foreach (var t in new[] { 0.01, 0.02, 0.03, 0.04, 0.05, 0.06, 0.08 })
            report.AppendLine($"        at margin {t:0.00}: {rightMargins.Count(m => m >= t)} right picks confident, " +
                              $"{wrongMargins.Count(m => m >= t)} wrong picks confident, {rightMargins.Count(m => m < t) + wrongMargins.Count(m => m < t)} left for you to choose");
    }

    private static Frame AsGameCard(Frame pic)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), null, new Rect(0, 0, 72, 72));
            dc.DrawImage(pic.ToBitmapSource(), new Rect(4, 4, 64, 64));
        }
        var bmp = new RenderTargetBitmap(72, 72, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return Frame.FromBitmapSource(bmp);
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? FindFixtureDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "Tests", "Fixtures");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static Frame Render(Scene scene)
    {
        const double pad = 14, gap = 10, cardHeight = 128, header = 30;
        int cols = Math.Min(scene.PerRow, scene.Cards.Length), rows = (scene.Cards.Length + scene.PerRow - 1) / scene.PerRow;
        double width = pad * 2 + cols * scene.CardWidth + (cols - 1) * gap;
        double height = pad * 2 + header + rows * cardHeight + (rows - 1) * gap;
        var typeface = new Typeface(new FontFamily(scene.Font), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x33)), null, new Rect(0, 0, width, height));
            var title = Text("Your Offer", typeface, 14, Brushes.White, 400);
            dc.DrawText(title, new Point(pad, pad));

            for (int i = 0; i < scene.Cards.Length; i++)
            {
                var c = scene.Cards[i];
                double x = pad + (i % scene.PerRow) * (scene.CardWidth + gap), y = pad + header + (i / scene.PerRow) * (cardHeight + gap);
                var rect = new Rect(x, y, scene.CardWidth, cardHeight);
                var fill = new LinearGradientBrush(c.Rarity, Darken(c.Rarity), 90);
                dc.DrawRoundedRectangle(fill, new Pen(Brushes.Black, 2), rect, 8, 8);

                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), null,
                    new Point(x + scene.CardWidth / 2, y + 42), 26, 20);

                if (c.Decoy != null)
                    Outlined(dc, Text(c.Decoy, typeface, scene.FontSize, Brushes.White, 60), new Point(x + 6, y + 50));

                if (c.Qty > 1)
                    Outlined(dc, Text($"x{c.Qty}", typeface, scene.FontSize, Brushes.White, 60), new Point(x + scene.CardWidth - 30, y + 6));

                var name = Text(c.Name, typeface, scene.FontSize, Brushes.White, scene.CardWidth - 10);
                name.TextAlignment = TextAlignment.Center;
                Outlined(dc, name, new Point(x + 5, y + cardHeight - 12 - name.Height));
            }
        }

        var bmp = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return Frame.FromBitmapSource(bmp);
    }

    private static FormattedText Text(string s, Typeface tf, double size, Brush brush, double maxWidth) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush, 1.0) { MaxTextWidth = maxWidth };

    private static void Outlined(DrawingContext dc, FormattedText text, Point origin)
    {
        var geometry = text.BuildGeometry(origin);
        dc.DrawGeometry(null, new Pen(Brushes.Black, 3) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawGeometry(Brushes.White, null, geometry);
    }

    private static Color Darken(Color c) => Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55));

    private static async Task Screens(string outDir, ValueStore store, List<OfferScan> scans, StringBuilder report)
    {
        var app = new AppController { Headless = true };
        app.Start();
        await Task.Delay(300);

        var scan = new TradeScan
        {
            Yours = FixtureScans.GetValueOrDefault("real_your_offer_icewing") ?? FixtureScans.GetValueOrDefault("real_your_offer_x2") ?? scans.ElementAtOrDefault(0) ?? throw new InvalidOperationException("no scans"),
            Theirs = FixtureScans.GetValueOrDefault("real_their_offer_elitey_misread") ?? FixtureScans.GetValueOrDefault("real_their_offer_x2") ?? scans.ElementAtOrDefault(5) ?? scans[0],
        };
        app.InjectScanForScreenshots(scan);
        await Task.Delay(1500);

        foreach (var (name, element, bg) in app.ScreenshotTargets())
        {
            await Task.Delay(450);
            SaveElement(element, bg, Path.Combine(outDir, name + ".png"));
            report.AppendLine($"SCREEN: {name}.png");
        }

        var (carrot, _) = store.Matcher.Match("carrot");
        if (carrot != null && carrot.Variants.FirstOrDefault(v => v.Name == "Carrot Gun") is { } gun
                           && carrot.Variants.FirstOrDefault(v => v.Name == "Carrot Knife") is { } knife)
        {
            var blank = new Frame(1, 1, new byte[4]);
            var theirs = new OfferScan
            {
                Capture = blank, ScreenRegion = default,
                Items = new()
                {
                    new DetectedItem { Group = carrot, Bounds = new Rect(0, 0, 10, 10), Variant = gun, VariantMargin = 0.15 },
                    new DetectedItem { Group = carrot, Bounds = new Rect(20, 0, 10, 10), Variant = knife, VariantMargin = 0.01 },
                },
            };
            app.InjectScanForScreenshots(new TradeScan { Yours = scan.Yours, Theirs = theirs });
            await Task.Delay(1200);
            var overlay = app.ScreenshotTargets().Last();
            SaveElement(overlay.Element, overlay.Background, Path.Combine(outDir, "overlay_lookalikes.png"));
            report.AppendLine("SCREEN: overlay_lookalikes.png");
        }
    }

    private static void SaveElement(FrameworkElement element, Brush background, string path)
    {
        element.UpdateLayout();
        double w = element.ActualWidth, h = element.ActualHeight;
        const double scale = 1.5;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
