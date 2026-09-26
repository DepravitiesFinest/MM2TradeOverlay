// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TradeValueOverlay;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}

public static class Fmt
{
    public static string DemandWord(double d) => d switch
    {
        >= 10 => "collector",
        >= 6 => "very high",
        >= 4 => "high",
        >= 2.5 => "good",
        >= 1.5 => "fair",
        >= 0.5 => "low",
        _ => "none",
    };

    public static string DemandNumber(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);

    public static string? DemandLine(ValueItem v)
    {
        if (v.Demand is not { } d) return null;
        var line = $"Demand {DemandNumber(d)}";
        if (v.Stability is { } st && !st.Equals("Stable", StringComparison.OrdinalIgnoreCase))
            line += ", " + st.ToLowerInvariant();
        return line;
    }

    public static string Subtitle(ValueItem v) => v.IsUnvalued
        ? string.Join("  ·  ", new[] { "Not priced yet", DemandLine(v) }.Where(x => x != null))
        : string.Join("  ·  ", new[] { DemandLine(v), v.Variant, v.Origin ?? v.Category }.Where(x => !string.IsNullOrEmpty(x)));

    public static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 60) return $"{(int)d.TotalDays} days ago";
        return t.LocalDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    public static string Worth(ValueItem v, int quantity = 1) => v.EffectiveAmount is not { } a ? "N/A"
        : v.IsTier ? (v.IsUnvalued ? "~" : "") + (a * quantity).ToString("0", CultureInfo.InvariantCulture) + ValueMath.Letter(v.EffectiveUnit)
        : ValueMath.FormatNumber(a * quantity);

    public static string WorthExplained(ValueItem v, int quantity = 1)
    {
        if (v.IsUnvalued)
            return $"mm2values.com lists this item at 0, meaning it hasn't been priced yet. " +
                   $"It's counted as {quantity} Common{(quantity == 1 ? "" : "s")}, the smallest unit, so it still adds to the total.";
        if (v.EffectiveAmount is not { } a) return $"No usable value listed (\"{v.ValueText}\").";
        if (!v.IsTier) return $"Worth {ValueMath.FormatNumber(a * quantity)} value.";
        double n = a * quantity;
        return $"Worth {n:0} × Tier-1 {ValueMath.Name(v.Unit)} ≈ {ValueMath.ToValue(v.Unit, n).ToString("0.###", CultureInfo.InvariantCulture)} value.\n" +
               "(6 Common = 1 Uncommon, 6 Uncommon = 1 Rare, 6 Rare = 1 Legendary, 5 Legendary = 1 value)";
    }
}

public enum Verdict { None, Win, Fair, Loss }

public sealed class ItemRowVM : Observable
{
    private readonly AppController? _app;
    private readonly Action _changed;
    private int _variant;
    private ImageSource? _icon;

    public ItemRowVM(ItemGroup group, int quantity, AppController? app, Action changed, ValueItem? matched = null, double matchMargin = 0)
    {
        Group = group;
        _quantity = quantity;
        DetectedQuantity = quantity;
        _app = app;
        _changed = changed;

        if (matched != null && group.Variants.ToList().IndexOf(matched) is var m and >= 0)
        {
            _variant = m;
            MatchedByPicture = matchMargin >= ArtMatcher.ConfidentMargin;
        }
        else if (app != null && app.Settings.VariantChoices.TryGetValue(group.Key, out var id))
            _variant = Math.Max(0, group.Variants.ToList().FindIndex(v => v.Id == id));

        CycleVariant = new RelayCommand(Cycle);
        MoreCommand = new RelayCommand(() => SetQuantity(_quantity + 1));
        LessCommand = new RelayCommand(() => SetQuantity(_quantity - 1));
        LoadIcon();
    }

    public ItemGroup Group { get; }
    private int _quantity;

    public int Quantity => _quantity;

    public int DetectedQuantity { get; }
    public bool IsAdjusted => _quantity != DetectedQuantity;
    public ValueItem Variant => Group.Variants[_variant];

    public string Name => Variant.Name;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string QuantityText => $"×{Quantity}";
    public bool ShowQuantity => Quantity > 1 || IsAdjusted;
    public string QuantityTooltip => "Click to add one, right-click to remove one." +
        (IsAdjusted ? $" The scan read x{DetectedQuantity}." : "");
    public string Subtitle =>
        Fmt.Subtitle(Variant);

    public bool MatchedByPicture { get; private set; }

    public bool NeedsChoice => IsAmbiguous && !MatchedByPicture && !_userPicked;
    private bool _userPicked;
    public double? Total => Variant.Value * Quantity;
    public bool HasValue => Variant.Value.HasValue;

    public ValueUnit Unit => Variant.EffectiveUnit;
    public bool IsTier => Variant.IsTier && HasValue;
    public string ValueText => Fmt.Worth(Variant, Quantity);
    public string ValueTooltip => Fmt.WorthExplained(Variant, Quantity);

    public bool IsAmbiguous => Group.Variants.Count > 1;
    public string VariantBadge => $"{_variant + 1}/{Group.Variants.Count}";
    public string VariantTooltip =>
        (MatchedByPicture ? "Picked by comparing the card's picture with each item's.\n"
         : _userPicked ? ""
         : "The pictures were too alike to be sure, so check this one.\n") +
        $"{Group.Variants.Count} items show up as \"{Name}\" in game. Click to switch:\n" +
        string.Join("\n", Group.Variants.Select((v, i) =>
            $"{v.Name}{(v.Variant != null ? " (" + v.Variant + ")" : "")}, {v.Origin ?? v.Category ?? "unknown origin"}: {Fmt.Worth(v)}" +
            (i == _variant ? "   (shown)" : "")));

    public ImageSource? Icon
    {
        get => _icon;
        private set { _icon = value; Raise(); Raise(nameof(HasIcon)); }
    }

    public bool HasIcon => _icon != null;

    public ICommand CycleVariant { get; }
    public ICommand MoreCommand { get; }
    public ICommand LessCommand { get; }

    private void SetQuantity(int value)
    {
        value = Math.Clamp(value, 1, 999);
        if (value == _quantity) return;
        _quantity = value;
        Sounds.Play(Sfx.Tap);
        RaiseAll();
        _changed();
    }

    private void Cycle()
    {
        _variant = (_variant + 1) % Group.Variants.Count;
        _userPicked = true;
        _app?.RememberVariant(Group, Variant);
        Sounds.Play(Sfx.Tap);
        RaiseAll();
        LoadIcon();
        _changed();
    }

    private async void LoadIcon() => Icon = await IconCache.GetAsync(Variant.Icon);
}

public sealed class OfferVM : Observable
{
    public OfferVM(OfferScan? scan, AppController? app, Action changed, bool isGive)
    {
        IsGive = isGive;
        if (scan == null) return;
        int slot = 0;
        foreach (var g in scan.Items.GroupBy(i => i.Group.Variants.Count == 1 ? i.Group.Key
                                                 : i.Variant != null ? i.Group.Key + "|" + i.Variant.Id
                                                 : i.Group.Key + "|slot" + slot++))
            Rows.Add(new ItemRowVM(g.First().Group, g.Sum(i => i.Quantity), app, changed, g.First().Variant, g.First().VariantMargin));
        Unknown = scan.Unknown.Select(u => u.Text).ToList();
    }

    public bool IsGive { get; }
    public string Title => IsGive ? "You give" : "You get";
    public ObservableCollection<ItemRowVM> Rows { get; } = new();
    public List<string> Unknown { get; } = new();

    public double Total => Rows.Sum(r => r.Total ?? 0);

    public double? Demand
    {
        get
        {
            var rated = Rows.Where(r => r.Variant.Demand != null && (r.Total ?? 0) > 0).ToList();
            double weight = rated.Sum(r => r.Total!.Value);
            return weight > 0 ? rated.Sum(r => r.Variant.Demand!.Value * r.Total!.Value) / weight : null;
        }
    }

    public string SummaryText => Demand is { } d ? $"{CountText}  ·  demand {Fmt.DemandNumber(d)}" : CountText;
    public string TotalText => ValueMath.Format(Total);
    public int ItemCount => Rows.Sum(r => r.Quantity);
    public string CountText => ItemCount == 1 ? "1 item" : $"{ItemCount} items";
    public bool IsEmpty => Rows.Count == 0;

    public void Refresh() => RaiseAll();
}

public sealed class TradeVM : Observable
{
    private readonly List<string> _notes;
    private readonly int _fairPercent = 3;

    public TradeVM(TradeScan? scan, AppController? app, bool preview = false)
    {
        _fairPercent = app?.Settings.FairPercent ?? 3;
        Give = new OfferVM(scan?.Yours, app, Recalculate, isGive: true);
        Get = new OfferVM(scan?.Theirs, app, Recalculate, isGive: false);
        Time = (scan?.Time ?? DateTime.Now).ToString("HH:mm");
        IsPreview = preview;
        _notes = scan?.Notes ?? new List<string>();
        Recalculate();
    }

    public TradeVM(string title, string message)
    {
        Give = new OfferVM(null, null, () => { }, isGive: true);
        Get = new OfferVM(null, null, () => { }, isGive: false);
        Time = DateTime.Now.ToString("HH:mm");
        ErrorTitle = title;
        ErrorMessage = message;
        _notes = new List<string>();
    }

    public OfferVM Give { get; }
    public OfferVM Get { get; }
    public string Time { get; }
    public bool IsPreview { get; }

    public string? ErrorTitle { get; }
    public string? ErrorMessage { get; }
    public bool IsError => ErrorTitle != null;
    public bool HasItems => !Give.IsEmpty || !Get.IsEmpty;
    public bool ShowResult => !IsError && HasItems;
    public bool ShowEmpty => !IsError && !HasItems;

    public Verdict Verdict { get; private set; }
    public double Diff { get; private set; }

    public string Headline { get; private set; } = "";

    public string Subline { get; private set; } = "";

    public string DemandNote { get; private set; } = "";
    public bool HasDemandNote => DemandNote.Length > 0;

    public bool DemandNoteIsGood { get; private set; }

    public double MeterPosition { get; private set; }

    public List<string> Warnings { get; private set; } = new();
    public bool HasWarnings => Warnings.Count > 0;
    public string WarningText => string.Join("\n", Warnings);

    public event Action? Recalculated;

    private void Recalculate()
    {
        double give = Give.Total, get = Get.Total, diff = get - give;
        double tolerance = Math.Max(1e-6, Math.Max(give, get) * _fairPercent / 100.0);

        if (!HasItems) Verdict = Verdict.None;
        else if (Math.Abs(diff) <= tolerance) Verdict = Verdict.Fair;
        else Verdict = diff > 0 ? Verdict.Win : Verdict.Loss;

        Diff = diff;
        string amount = ValueMath.Format(diff);
        Headline = Verdict switch
        {
            Verdict.Win => $"You come out {amount} ahead",
            Verdict.Loss => $"You're overpaying by {amount}",
            Verdict.Fair when Math.Abs(diff) < 1e-6 => "Dead even",
            Verdict.Fair => $"Fair trade, {amount} apart",
            _ => "",
        };
        Subline = Verdict switch
        {
            Verdict.Win when give <= 0 => "You aren't giving anything up.",
            Verdict.Win when diff / give >= 10 => $"Your whole side is only worth {ValueMath.Format(give)}.",
            Verdict.Win => $"They're giving {diff / give * 100:0}% more than you are.",
            Verdict.Loss when get <= 0 => "Nothing has been added on their side yet.",
            Verdict.Loss when -diff / get >= 10 => $"Their whole side is only worth {ValueMath.Format(get)}.",
            Verdict.Loss => $"You're giving {-diff / get * 100:0}% more than you get back.",
            Verdict.Fair => $"Both sides are within {_fairPercent}% of each other.",
            _ => "",
        };

        double ratio = Math.Max(give, get) > 0 ? diff / Math.Max(give, get) : 0;
        MeterPosition = Math.Sign(ratio) * Math.Sqrt(Math.Abs(ratio));

        var warnings = new List<string>(_notes);
        var unknown = Give.Unknown.Concat(Get.Unknown).ToList();
        if (unknown.Count > 0)
            warnings.Add($"Couldn't identify {Quote(unknown)}. Check {(unknown.Count == 1 ? "it" : "them")} by hand.");
        var unvalued = Give.Rows.Concat(Get.Rows).Where(r => r.Variant.IsUnvalued).Select(r => r.Name).ToList();
        if (unvalued.Count > 0)
            warnings.Add($"{Quote(unvalued)} {(unvalued.Count == 1 ? "isn't" : "aren't")} priced on mm2values yet, so {(unvalued.Count == 1 ? "it counts" : "they count")} as one Common each.");
        var noValue = Give.Rows.Concat(Get.Rows).Where(r => !r.HasValue).Select(r => r.Name).ToList();
        if (noValue.Count > 0)
            warnings.Add($"No listed value for {Quote(noValue)}.");
        var unsure = Give.Rows.Concat(Get.Rows).Where(r => r.NeedsChoice).Select(r => r.Name).ToList();
        if (unsure.Count > 0)
            warnings.Add($"{Quote(unsure)} could be more than one item, and the card's picture couldn't settle it. " +
                         "Click the amber badge to pick the right one.");
        Warnings = warnings;

        DemandNote = "";
        if (Verdict != Verdict.None && Give.Demand is { } dg && Get.Demand is { } dt && Math.Abs(dg - dt) >= 1.5)
        {
            DemandNoteIsGood = dt > dg;
            DemandNote = DemandNoteIsGood
                ? $"Their side is in higher demand ({Fmt.DemandNumber(dt)} vs {Fmt.DemandNumber(dg)}), so it should be easier to trade on."
                : $"Your side is in higher demand ({Fmt.DemandNumber(dg)} vs {Fmt.DemandNumber(dt)}). What you get may be harder to trade on.";
        }

        Give.Refresh();
        Get.Refresh();
        RaiseAll();
        Recalculated?.Invoke();
    }

    private static string Quote(List<string> items)
    {
        var shown = items.Take(3).Select(s => $"\"{s}\"");
        return string.Join(", ", shown) + (items.Count > 3 ? $" +{items.Count - 3} more" : "");
    }
}

public sealed class ValueRowVM : Observable
{
    private ImageSource? _icon;
    private bool _iconRequested;

    public ValueRowVM(ItemGroup group, ValueItem item)
    {
        Group = group;
        Item = item;
        SearchKey = ItemMatcher.Normalize(group.Name + " " + item.Variant + " " + item.Origin);
    }

    public ItemGroup Group { get; }
    public ValueItem Item { get; }
    public string SearchKey { get; }

    public string Name => Group.Name;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Subtitle => Fmt.Subtitle(Item);
    public double SortDemand => Item.Demand ?? -1;
    public ValueUnit Unit => Item.EffectiveUnit;
    public bool IsTier => Item.IsTier && Item.Amount.HasValue;
    public string ValueText => Fmt.Worth(Item);
    public string ValueTooltip => Fmt.WorthExplained(Item);
    public double SortValue => Item.Value ?? -1;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                LoadIcon();
            }
            return _icon;
        }
    }

    public bool HasIcon => _icon != null;

    private async void LoadIcon()
    {
        _icon = await IconCache.GetAsync(Item.Icon);
        Raise(nameof(Icon));
        Raise(nameof(HasIcon));
    }
}

public sealed class ValuesVM : Observable
{
    private readonly List<ValueRowVM> _all = new();
    private string _search = "";
    private ValueUnit? _unit;
    private string _sort = "value";

    public ValuesVM()
    {
        View = CollectionViewSource.GetDefaultView(_all);
        View.Filter = o => o is ValueRowVM r && Matches(r);
    }

    public ICollectionView View { get; }
    public int VisibleCount => View.Cast<object>().Count();

    public string Search
    {
        get => _search;
        set { _search = value; Refresh(); }
    }

    public string SortBy
    {
        get => _sort;
        set { _sort = value; ApplySort(); Refresh(); }
    }

    private void ApplySort()
    {
        Comparison<ValueRowVM> byName = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        _all.Sort(_sort switch
        {
            "demand" => (a, b) => b.SortDemand.CompareTo(a.SortDemand) is var c && c != 0 ? c : b.SortValue.CompareTo(a.SortValue),
            "name" => byName,
            _ => (a, b) => b.SortValue.CompareTo(a.SortValue) is var c && c != 0 ? c : byName(a, b),
        });
    }

    public ValueUnit? UnitFilter
    {
        get => _unit;
        set { _unit = value; Refresh(); }
    }

    public void Load(ValueStore store)
    {
        _all.Clear();
        foreach (var g in store.Groups)
            foreach (var v in g.Variants)
                _all.Add(new ValueRowVM(g, v));
        ApplySort();
        Refresh();
    }

    private bool Matches(ValueRowVM r)
    {
        if (_unit is { } u && r.Unit != u) return false;
        if (_search.Length == 0) return true;
        var q = ItemMatcher.Normalize(_search);
        return q.Length == 0 || r.SearchKey.Contains(q, StringComparison.Ordinal);
    }

    private void Refresh()
    {
        View.Refresh();
        Raise(nameof(VisibleCount));
    }
}
