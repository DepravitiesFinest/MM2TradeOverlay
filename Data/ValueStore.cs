// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradeValueOverlay;

public sealed class ValueItem
{
    public string Name { get; set; } = "";

    public string? Variant { get; set; }

    public double? Amount { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ValueUnit Unit { get; set; }

    public string ValueText { get; set; } = "";

    public string? Origin { get; set; }
    public string? Icon { get; set; }

    public double? Demand { get; set; }

    public double? RarityScore { get; set; }

    public string? Stability { get; set; }

    public string? Range { get; set; }

    public string? Category { get; set; }

    [JsonIgnore] public bool IsUnvalued => Unit == ValueUnit.Value && Amount == 0;

    [JsonIgnore] public ValueUnit EffectiveUnit => IsUnvalued ? ValueUnit.Common : Unit;
    [JsonIgnore] public double? EffectiveAmount => IsUnvalued ? 1 : Amount;

    [JsonIgnore] public double? Value => EffectiveAmount is { } a ? ValueMath.ToValue(EffectiveUnit, a) : null;

    [JsonIgnore] public bool IsTier => EffectiveUnit != ValueUnit.Value;

    [JsonIgnore] public string Id => $"{Name}|{Variant}|{Origin}|{ValueText}";
}

public sealed class ValueSnapshot
{
    public const int CurrentSchema = 3;

    public int Schema { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset FetchedAt { get; set; }
    public List<ValueItem> Items { get; set; } = new();
}

public sealed class ItemGroup(string key, string name, List<ValueItem> variants)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public IReadOnlyList<ValueItem> Variants { get; } = variants;
}

public sealed class ValueStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ValueSnapshot? Snapshot { get; private set; }
    public ItemMatcher Matcher { get; private set; } = new(Array.Empty<ItemGroup>());
    public IReadOnlyList<ItemGroup> Groups { get; private set; } = Array.Empty<ItemGroup>();
    public int ItemCount => Matcher.Count;
    public bool IsStale => Snapshot == null || DateTimeOffset.Now - Snapshot.FetchedAt > TimeSpan.FromDays(3);

    public event Action? Changed;

    public void Load()
    {
        var candidates = new[] { TryReadFile(AppPaths.ValuesFile), TryReadEmbedded() };
        var pick = candidates
            .Where(s => s is { Items.Count: > 0, Schema: ValueSnapshot.CurrentSchema })
            .OrderByDescending(s => s!.FetchedAt)
            .FirstOrDefault();
        if (pick != null) Apply(pick, persist: false);
    }

    public void Apply(ValueSnapshot snapshot, bool persist)
    {
        var groups = snapshot.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => ItemMatcher.Normalize(i.Name))
            .Where(g => g.Key.Length > 0)
            .Select(g =>
            {
                var variants = g
                    .OrderBy(v => v.Value ?? double.MaxValue)
                    .ThenBy(v => v.Variant)
                    .ThenBy(v => v.Origin)
                    .ToList();
                var name = g.Select(v => v.Name).OrderByDescending(n => n.Count(char.IsUpper)).First();
                return new ItemGroup(g.Key, name, variants);
            })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Groups = groups;
        Matcher = new ItemMatcher(WithInGameAliases(groups));
        Snapshot = snapshot;

        if (persist)
        {
            try
            {
                AppPaths.WriteAllTextAtomic(AppPaths.ValuesFile, JsonSerializer.Serialize(snapshot, Json));
            }
            catch (Exception ex)
            {
                Log.Error("Could not save value list", ex);
            }
        }

        Changed?.Invoke();
    }

    private static readonly string[] WeaponSuffixes = { " Gun", " Knife" };

    private static List<ItemGroup> WithInGameAliases(List<ItemGroup> groups)
    {
        var byKey = groups.ToDictionary(g => g.Key);
        var extra = new Dictionary<string, (string Name, List<ValueItem> Variants)>();

        foreach (var g in groups)
        {
            var suffix = WeaponSuffixes.FirstOrDefault(s => g.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (suffix == null) continue;
            var baseName = g.Name[..^suffix.Length].Trim();
            var baseKey = ItemMatcher.Normalize(baseName);
            if (baseKey.Length < 3) continue;

            if (!extra.TryGetValue(baseKey, out var entry))
            {
                entry = byKey.TryGetValue(baseKey, out var existing)
                    ? (existing.Name, existing.Variants.ToList())
                    : (baseName, new List<ValueItem>());
                extra[baseKey] = entry;
            }
            entry.Variants.AddRange(g.Variants);
        }

        return groups
            .Where(g => !extra.ContainsKey(g.Key))
            .Concat(extra.Select(e => new ItemGroup(e.Key, e.Value.Name,
                e.Value.Variants.OrderBy(v => v.Value ?? double.MaxValue).ThenBy(v => v.Name).ToList())))
            .ToList();
    }

    private static ValueSnapshot? TryReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ValueSnapshot>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex)
        {
            Log.Error($"Ignoring unreadable value list at {path}", ex);
            return null;
        }
    }

    private static ValueSnapshot? TryReadEmbedded()
    {
        try
        {
            using var stream = typeof(ValueStore).Assembly.GetManifestResourceStream("values.json");
            return stream == null ? null : JsonSerializer.Deserialize<ValueSnapshot>(stream, Json);
        }
        catch (Exception ex)
        {
            Log.Error("Built-in value list is unreadable", ex);
            return null;
        }
    }
}
