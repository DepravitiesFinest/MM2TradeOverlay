// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace TradeValueOverlay;

public static class ValueFetcher
{
    public const string SourceName = "mm2values.com";
    public const string SearchUrl = "https://www.mm2values.com/search2.php?term=";

    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex Row = new(@"^(?<name>.+?)\s*-\s*Value:\s*(?<value>.*?)\s*\|\s*Origin:\s*(?<origin>.*)$", RegexOptions.Compiled);

    private static readonly Regex ValueFormat = new(
        @"^(?<num>\d[\d,]*(?:\.\d+)?|\.\d+)\s*(?:[x×]\s*)?(?:\(\s*T(?<tier>\d+)\s*\)\s*(?<rarity>[a-z]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Bracketed = new(@"^(?<base>.+?)\s*\((?<variant>[^)]+)\)$", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"<img[^>]*src=['""]?(?<src>[^'"" >]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex LineBreak = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex LooseAngle = new(@"<(?![a-zA-Z/!])", RegexOptions.Compiled);

    private static string StripTags(string html) => Tags.Replace(LooseAngle.Replace(html, "&lt;"), "");

    public const string CategoryUrl = "https://www.mm2values.com/?p=";

    public static readonly (string Page, string Name)[] Categories =
    {
        ("ancient", "Ancient"), ("unique", "Unique"), ("chroma", "Chroma"), ("godly", "Godly"),
        ("legend", "Legendary"), ("rare", "Rare"), ("uncommon", "Uncommon"), ("common", "Common"),
        ("vintage", "Vintage"), ("pets", "Pet"), ("misc", "Misc"),
    };

    public static async Task<ValueSnapshot> FetchAsync(IProgress<double>? progress, CancellationToken ct)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        var urls = letters.Select(l => SearchUrl + l).Concat(Categories.Select(c => CategoryUrl + c.Page)).ToArray();
        var pages = new string[urls.Length];
        int done = 0;
        using var gate = new SemaphoreSlim(4);

        await Task.WhenAll(urls.Select(async (url, i) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                pages[i] = await GetWithRetry(url, ct);
                progress?.Report(Interlocked.Increment(ref done) / (double)urls.Length);
            }
            finally
            {
                gate.Release();
            }
        }));

        var items = Merge(pages.Take(letters.Length).SelectMany(ParsePage));
        if (items.Count < 100)
            throw new InvalidOperationException($"Only found {items.Count} items. {SourceName} may have changed its page layout.");

        var entries = pages.Skip(letters.Length)
            .SelectMany((html, i) => ParseCategoryPage(html, Categories[i].Name))
            .ToList();
        int added = Enrich(items, entries);
        Log.Info($"Category pages: {entries.Count} entries, {items.Count(i => i.Demand != null)} items have demand, {added} added");
        items = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        int unreadable = items.Count(i => i.Amount == null);
        if (unreadable > 0)
            Log.Info($"{unreadable} values used an unknown format: " +
                     string.Join(", ", items.Where(i => i.Amount == null).Take(5).Select(i => $"{i.Name} = \"{i.ValueText}\"")));

        return new ValueSnapshot
        {
            Schema = ValueSnapshot.CurrentSchema,
            Source = SourceName,
            FetchedAt = DateTimeOffset.Now,
            Items = items,
        };
    }

    internal sealed record CategoryEntry(
        string Name, string? Variant, string ValueText, double? Demand, double? Rarity,
        string? Stability, string? Range, string? Icon, string Category);

    private static readonly Regex Stackable = new(@"<div class=stackable>(?<body>.*?)<hr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    internal static IEnumerable<CategoryEntry> ParseCategoryPage(string html, string category)
    {
        var baseUri = new Uri(CategoryUrl);
        foreach (Match block in Stackable.Matches(html))
        {
            var raw = block.Groups["body"].Value;
            var text = WebUtility.HtmlDecode(StripTags(LineBreak.Replace(raw, "\n")));
            var lines = text.Split('\n').Select(l => Spaces.Replace(l, " ").Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) continue;

            var name = Regex.Replace(lines[0], @"\s*Value:.*$", "").Trim();
            if (name.Length == 0) continue;
            string? variant = null;
            var bracket = Bracketed.Match(name);
            if (bracket.Success)
            {
                name = bracket.Groups["base"].Value.Trim();
                variant = Capitalize(bracket.Groups["variant"].Value.Trim());
            }

            var flat = string.Join("\n", lines);
            string? Field(string label) =>
                Regex.Match(flat, label + @":\s*(.+?)\s*(?:\n|$| - )") is { Success: true } m ? m.Groups[1].Value.Trim() : null;
            static double? Number(string? s) =>
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            static string? Rated(string? s) => s is null or "N/A" or "" ? null : s;

            string? icon = null;
            var img = Image.Match(raw);
            if (img.Success && Uri.TryCreate(baseUri, img.Groups["src"].Value, out var iconUri)) icon = iconUri.ToString();

            yield return new CategoryEntry(name, variant, Field("Value") ?? "", Number(Field("Demand")), Number(Field("Rarity")),
                Rated(Field("Stability")), Rated(Field("Range")), icon, category);
        }
    }

    internal static int Enrich(List<ValueItem> items, List<CategoryEntry> entries)
    {
        static string ImageKey(string? url) =>
            url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? Path.GetFileName(u.AbsolutePath).ToLowerInvariant() : "";
        static string NameKey(string name, string? variant) => ItemMatcher.Normalize(name) + "|" + (variant ?? "").ToLowerInvariant();

        var byImage = entries.Where(e => e.Icon != null).ToLookup(e => ImageKey(e.Icon));
        var byName = entries.ToLookup(e => NameKey(e.Name, e.Variant));
        var used = new HashSet<CategoryEntry>();

        foreach (var item in items)
        {
            var key = NameKey(item.Name, item.Variant);
            var candidates = byImage[ImageKey(item.Icon)].Where(e => NameKey(e.Name, e.Variant) == key && !used.Contains(e)).ToList();
            if (candidates.Count == 0) candidates = byName[key].Where(e => !used.Contains(e)).ToList();
            if (candidates.Count == 0) continue;

            var parsed = ParseValue(item.ValueText);
            var match = candidates.FirstOrDefault(e => ParseValue(e.ValueText) == parsed) ?? candidates[0];
            used.Add(match);
            item.Demand = match.Demand;
            item.RarityScore = match.Rarity;
            item.Stability = match.Stability;
            item.Range = match.Range;
            item.Category = match.Category;
        }

        var known = new HashSet<string>(items.Select(i => NameKey(i.Name, i.Variant)));
        int added = 0;
        foreach (var e in entries.Where(e => !used.Contains(e) && !known.Contains(NameKey(e.Name, e.Variant))))
        {
            var (amount, unit) = ParseValue(e.ValueText);
            if (amount == null) continue;
            items.Add(new ValueItem
            {
                Name = e.Name, Variant = e.Variant, ValueText = e.ValueText, Amount = amount, Unit = unit, Icon = e.Icon,
                Demand = e.Demand, RarityScore = e.Rarity, Stability = e.Stability, Range = e.Range, Category = e.Category,
            });
            known.Add(NameKey(e.Name, e.Variant));
            added++;
        }
        return added;
    }

    internal static IEnumerable<ValueItem> ParsePage(string html)
    {
        var baseUri = new Uri(SearchUrl);
        foreach (var part in Regex.Split(html, "<p>", RegexOptions.IgnoreCase))
        {
            var text = LineBreak.Replace(part, " | ");
            text = WebUtility.HtmlDecode(StripTags(text));
            text = Spaces.Replace(text, " ").Trim();

            var m = Row.Match(text);
            if (!m.Success) continue;

            var item = new ValueItem
            {
                ValueText = m.Groups["value"].Value.Trim(),
                Origin = NullIfEmpty(m.Groups["origin"].Value.Trim()),
            };

            var name = m.Groups["name"].Value.Trim();
            var bracket = Bracketed.Match(name);
            if (bracket.Success)
            {
                item.Name = bracket.Groups["base"].Value.Trim();
                item.Variant = Capitalize(bracket.Groups["variant"].Value.Trim());
            }
            else
            {
                item.Name = name;
            }

            (item.Amount, item.Unit) = ParseValue(item.ValueText);

            var img = Image.Match(part);
            if (img.Success && Uri.TryCreate(baseUri, img.Groups["src"].Value, out var iconUri)) item.Icon = iconUri.ToString();

            yield return item;
        }
    }

    public static (double? Amount, ValueUnit Unit) ParseValue(string text)
    {
        var m = ValueFormat.Match(text.Trim());
        if (!m.Success) return (null, ValueUnit.Value);

        if (!double.TryParse(m.Groups["num"].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return (null, ValueUnit.Value);

        if (!m.Groups["tier"].Success) return (amount, ValueUnit.Value);

        if (m.Groups["tier"].Value != "1") return (null, ValueUnit.Value);
        return ValueMath.ParseRarity(m.Groups["rarity"].Value) is { } unit ? (amount, unit) : (null, ValueUnit.Value);
    }

    private static List<ValueItem> Merge(IEnumerable<ValueItem> rows)
    {
        var merged = new Dictionary<string, ValueItem>();
        foreach (var row in rows)
        {
            var key = $"{row.Name.ToLowerInvariant()}|{row.Variant?.ToLowerInvariant()}|{row.Origin}|{row.ValueText}";
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = row;
                continue;
            }
            existing.Name = KeepCapitals(existing.Name, row.Name);
            existing.Icon ??= row.Icon;
        }
        return merged.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string KeepCapitals(string a, string b)
    {
        if (a.Length != b.Length) return a;
        var chars = a.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsUpper(b[i])) chars[i] = b[i];
        return new string(chars);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static async Task<string> GetWithRetry(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await Http.GetStringAsync(url, ct);
            }
            catch (Exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                await Task.Delay(700 * attempt, ct);
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MM2TradeOverlay/{AppInfo.Version} (+open source trade calculator)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
