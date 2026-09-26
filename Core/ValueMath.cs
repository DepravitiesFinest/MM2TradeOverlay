// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Globalization;

namespace TradeValueOverlay;

public enum ValueUnit { Value, Legendary, Rare, Uncommon, Common }

public static class ValueMath
{
    public const int TierStep = 6;
    public const int LegendariesPerValue = 5;

    public static double PerUnit(ValueUnit unit) => unit switch
    {
        ValueUnit.Legendary => 1.0 / LegendariesPerValue,
        ValueUnit.Rare => 1.0 / (LegendariesPerValue * TierStep),
        ValueUnit.Uncommon => 1.0 / (LegendariesPerValue * TierStep * TierStep),
        ValueUnit.Common => 1.0 / (LegendariesPerValue * TierStep * TierStep * TierStep),
        _ => 1.0,
    };

    public static double ToValue(ValueUnit unit, double amount) => amount * PerUnit(unit);

    public static string Letter(ValueUnit unit) => unit switch
    {
        ValueUnit.Legendary => "L",
        ValueUnit.Rare => "R",
        ValueUnit.Uncommon => "U",
        ValueUnit.Common => "C",
        _ => "",
    };

    public static string Name(ValueUnit unit) => unit switch
    {
        ValueUnit.Legendary => "Legendary",
        ValueUnit.Rare => "Rare",
        ValueUnit.Uncommon => "Uncommon",
        ValueUnit.Common => "Common",
        _ => "Value",
    };

    public static ValueUnit? ParseRarity(string text) => text.Trim().ToLowerInvariant() switch
    {
        "legend" or "legendary" or "l" => ValueUnit.Legendary,
        "rare" or "r" => ValueUnit.Rare,
        "uncom" or "uncommon" or "u" => ValueUnit.Uncommon,
        "comm" or "common" or "c" => ValueUnit.Common,
        _ => null,
    };

    public static string Format(double value)
    {
        value = Math.Abs(value);
        if (value >= 10) return FormatNumber(Math.Round(value));
        var parts = Breakdown(value).Where(p => p.Amount > 0).Take(2).ToList();
        if (parts.Count == 0) return "0";

        return string.Join(" ", parts.Select((p, i) =>
            p.Unit == ValueUnit.Value
                ? FormatNumber(p.Amount)
                : (i > 0 && parts[0].Unit == ValueUnit.Value ? "+ " : "") +
                  p.Amount.ToString("0", CultureInfo.InvariantCulture) + Letter(p.Unit)));
    }

    public static string FormatSigned(double value) =>
        Math.Abs(value) < 1e-9 ? "0" : (value > 0 ? "+" : "−") + Format(value);

    public static string FormatNumber(double v)
    {
        double a = Math.Abs(v);
        if (a >= 1_000_000) return (v / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        return v.ToString(a >= 100 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
    }

    public static List<(ValueUnit Unit, double Amount)> Breakdown(double value)
    {
        const double commonsPerValue = LegendariesPerValue * TierStep * TierStep * TierStep;
        double whole = Math.Floor(value + 1e-9);
        double fraction = value - whole;

        long commons = (long)Math.Round(fraction * commonsPerValue);
        if (Math.Abs(commons / commonsPerValue - fraction) > 1e-6)
            return new() { (ValueUnit.Value, Math.Round(value, 2)) };

        long c = commons % TierStep; commons /= TierStep;
        long u = commons % TierStep; commons /= TierStep;
        long r = commons % TierStep; commons /= TierStep;
        long l = commons;

        return new()
        {
            (ValueUnit.Value, whole),
            (ValueUnit.Legendary, l),
            (ValueUnit.Rare, r),
            (ValueUnit.Uncommon, u),
            (ValueUnit.Common, c),
        };
    }
}
