// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Text;

namespace TradeValueOverlay;

public sealed class ItemMatcher
{
    private readonly (string Key, ItemGroup Group)[] _names;
    private readonly Dictionary<string, ItemGroup> _exact;

    private static readonly HashSet<(char, char)> Confusable = BuildConfusables(
        "0o", "1l", "1i", "li", "5s", "8b", "6g", "9g", "2z", "uv", "ce", "nh", "nm", "rn", "vy", "4a", "7t", "ao", "ec");

    public ItemMatcher(IEnumerable<ItemGroup> groups)
    {
        _exact = new Dictionary<string, ItemGroup>();
        foreach (var g in groups) _exact.TryAdd(g.Key, g);
        _names = _exact.Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    public int Count => _names.Length;

    public IEnumerable<ItemGroup> AllGroups => _names.Select(n => n.Group);

    public IEnumerable<ItemGroup> NearNames(string key)
    {
        foreach (var (other, group) in _names)
        {
            if (other == key) continue;
            int extra = other.Length - key.Length;
            bool longer = extra is > 0 and <= 2 && other.StartsWith(key, StringComparison.Ordinal) && key.Length >= 3;
            bool shorter = extra is < 0 and >= -2 && key.StartsWith(other, StringComparison.Ordinal) && other.Length >= 4;
            bool slip = key.Length >= 6 && Math.Abs(extra) <= 1 && Distance(key, other) <= 1;
            if (longer || shorter || slip) yield return group;
        }
    }

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    public static double MaxCost(int length) => length switch
    {
        <= 3 => 0,
        <= 5 => 0.4,
        <= 7 => 1,
        _ => 1 + (length - 8) * 0.2,
    };

    public (ItemGroup? Group, double Cost) Match(string normalized)
    {
        if (normalized.Length == 0) return (null, double.MaxValue);
        if (_exact.TryGetValue(normalized, out var exact)) return (exact, 0);

        double limit = MaxCost(normalized.Length);
        if (limit <= 0) return (null, double.MaxValue);

        ItemGroup? best = null;
        double bestCost = double.MaxValue;
        foreach (var (key, group) in _names)
        {
            double allowed = Math.Min(limit, MaxCost(key.Length));
            if (allowed <= 0 || Math.Abs(key.Length - normalized.Length) > allowed) continue;

            double cost = Distance(normalized, key);
            if (cost <= allowed && cost < bestCost)
            {
                bestCost = cost;
                best = group;
            }
        }
        return (best, bestCost);
    }

    private static double Distance(string a, string b)
    {
        var d = new double[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                double sub = a[i - 1] == b[j - 1] ? 0 : Confusable.Contains((a[i - 1], b[j - 1])) ? 0.4 : 1;
                double best = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + sub);

                if (i >= 2 && Split.Contains((a[i - 2], a[i - 1], b[j - 1]))) best = Math.Min(best, d[i - 2, j - 1] + 0.4);
                if (j >= 2 && Split.Contains((b[j - 2], b[j - 1], a[i - 1]))) best = Math.Min(best, d[i - 1, j - 2] + 0.4);

                d[i, j] = best;
            }
        }
        return d[a.Length, b.Length];
    }

    private static readonly HashSet<(char, char, char)> Split = new()
    {
        ('r', 'n', 'm'), ('n', 'n', 'm'), ('i', 'n', 'm'), ('v', 'v', 'w'), ('c', 'l', 'd'), ('l', 'i', 'h'), ('i', 'i', 'u'),
    };

    private static HashSet<(char, char)> BuildConfusables(params string[] pairs)
    {
        var set = new HashSet<(char, char)>();
        foreach (var p in pairs)
        {
            set.Add((p[0], p[1]));
            set.Add((p[1], p[0]));
        }
        return set;
    }
}
