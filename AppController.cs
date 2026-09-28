// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Interop;

namespace TradeValueOverlay;

public enum HotkeySlot { Scan = 1, Hide = 2, Calibrate = 3 }

public enum ToastKind { Info, Success, Error }

public sealed class AppController : IDisposable
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public ValueStore Values { get; } = new();
    public OcrService? Ocr { get; private set; }
    public string? OcrError { get; private set; }

    public TradeScan? LastScan { get; private set; }
    public string? LastError { get; private set; }
    public bool IsScanning { get; private set; }
    public bool IsCalibrating { get; private set; }
    public bool IsUpdatingValues { get; private set; }
    public double ValuesProgress { get; private set; }
    public string? ValuesError { get; private set; }
    public List<string> HotkeyErrors { get; } = new();

    public event Action? StateChanged;

    public event Action<string, ToastKind>? Toast;

    public void Notify(string message, ToastKind kind = ToastKind.Info) => Toast?.Invoke(message, kind);

    private readonly HotkeyManager _hotkeys = new();
    private readonly DiscordPresence _presence = new();
    private MainWindow _main = null!;
    private OverlayWindow _overlay = null!;

    public void Start()
    {
        Sounds.Enabled = Settings.SoundsEnabled && !Headless;
        Sounds.Volume = Sounds.VolumeFor(Settings.SoundVolume);
        ThemeManager.Apply(Settings.Accent, Settings.ThemeBase, Settings.OverlayOpacity);
        Values.Load();
        Values.Changed += RaiseChanged;

        try
        {
            Ocr = OcrService.Create();
            Log.Info($"OCR ready ({Ocr.LanguageName})");
        }
        catch (Exception ex)
        {
            OcrError = ex.Message;
            Log.Error("OCR unavailable", ex);
        }

        _overlay = new OverlayWindow(this);
        _main = new MainWindow(this);
        _main.Closed += (_, _) => Application.Current.Shutdown();
        Application.Current.MainWindow = _main;
        if (Headless)
        {
            _main.WindowStartupLocation = WindowStartupLocation.Manual;
            _main.Left = _main.Top = -30000;
            _main.ShowActivated = false;
            _main.Show();
            return;
        }
        _main.Show();

        RegisterHotkeys();
        ApplyPresence();

        if (Values.ItemCount == 0 || (Settings.AutoUpdateValues && Values.IsStale))
            _ = UpdateValuesAsync(silent: true);
    }

    public string GetHotkey(HotkeySlot slot) => slot switch
    {
        HotkeySlot.Scan => Settings.ScanHotkey,
        HotkeySlot.Hide => Settings.HideHotkey,
        _ => Settings.CalibrateHotkey,
    };

    private void StoreHotkey(HotkeySlot slot, string value)
    {
        switch (slot)
        {
            case HotkeySlot.Scan: Settings.ScanHotkey = value; break;
            case HotkeySlot.Hide: Settings.HideHotkey = value; break;
            default: Settings.CalibrateHotkey = value; break;
        }
    }

    private static string Label(HotkeySlot slot) => slot switch
    {
        HotkeySlot.Scan => "Scan trade",
        HotkeySlot.Hide => "Hide overlay",
        _ => "Calibrate",
    };

    public void RegisterHotkeys()
    {
        HotkeyErrors.Clear();
        foreach (var slot in Enum.GetValues<HotkeySlot>())
        {
            if (!RegisterOne(slot))
            {
                HotkeyErrors.Add($"{GetHotkey(slot)} ({Label(slot)}) is used by another app. Pick a different shortcut in Settings.");
                Log.Info($"Hotkey {GetHotkey(slot)} for {slot} is taken");
                Notify($"{GetHotkey(slot)} is already used by another app. Pick a different {Label(slot).ToLowerInvariant()} shortcut in Settings.", ToastKind.Error);
            }
        }
        RaiseChanged();
    }

    public void SuspendHotkeys() => _hotkeys.UnregisterAll();

    public void ResumeHotkeys() => RegisterHotkeys();

    public string? TrySetHotkey(HotkeySlot slot, string value)
    {
        if (!Hotkey.TryParse(value, out var parsed)) return "That key combination isn't supported.";

        foreach (var other in Enum.GetValues<HotkeySlot>().Where(s => s != slot))
        {
            if (Hotkey.TryParse(GetHotkey(other), out var o) && o == parsed)
                return $"{value} is already your {Label(other)} shortcut.";
        }

        var previous = GetHotkey(slot);
        StoreHotkey(slot, value);
        if (!RegisterOne(slot))
        {
            StoreHotkey(slot, previous);
            RegisterOne(slot);
            return $"{value} is already taken by another app.";
        }

        Settings.Save();
        RegisterHotkeys();
        return null;
    }

    private bool RegisterOne(HotkeySlot slot)
    {
        if (!Hotkey.TryParse(GetHotkey(slot), out var hk)) return false;
        Action action = slot switch
        {
            HotkeySlot.Scan => () => _ = ScanAsync(),
            HotkeySlot.Hide => () => _overlay.HideOverlay(),
            _ => Calibrate,
        };
        return _hotkeys.Register((int)slot, hk, action);
    }

    public async Task ScanAsync()
    {
        if (IsScanning || IsCalibrating) return;

        if (Ocr == null)
        {
            BringMainToFront();
            return;
        }
        if (!Settings.IsCalibrated)
        {
            Calibrate();
            return;
        }
        if (Values.ItemCount == 0)
        {
            _overlay.ShowError("No value list yet", "The value list is still downloading. Try again in a few seconds.");
            return;
        }

        IsScanning = true;
        LastError = null;
        Sounds.Play(Sfx.Scan);
        RaiseChanged();
        try
        {
            var notes = new List<string>();
            var (yours, theirs) = ResolveRegions(notes);

            Frame yourShot, theirShot;
            using (new CaptureShield(new Window[] { _overlay, _main }, yours, theirs))
            {
                yourShot = ScreenGrabber.Capture(yours);
                theirShot = ScreenGrabber.Capture(theirs);
            }

            var debug = Settings.SaveDebugCaptures ? new DebugSink() : null;
            var scanner = new TradeScanner(Ocr, Values.Matcher);
            var give = await scanner.ReadOfferAsync(yourShot, yours, debug, "your_offer");
            var get = await scanner.ReadOfferAsync(theirShot, theirs, debug, "their_offer");
            debug?.Flush();

            LastScan = new TradeScan { Yours = give, Theirs = get, Notes = notes };
            Log.Info($"Scan: {give.Items.Count} vs {get.Items.Count} items, {give.Unknown.Count + get.Unknown.Count} unknown labels");
            _overlay.ShowScan(LastScan);
            _presence.TradeChecked();
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed", ex);
            LastError = ex.Message;
            _overlay.ShowError("Scan failed", ex.Message);
            Notify($"Scan failed: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsScanning = false;
            RaiseChanged();
        }
    }

    private (PixelRect Yours, PixelRect Theirs) ResolveRegions(List<string> notes)
    {
        var yours = Settings.YourRegion!.Value;
        var theirs = Settings.TheirRegion!.Value;
        if (Settings.RobloxClientAtCalibration is not { } anchor || RobloxWindow.ClientArea() is not { } now)
            return (yours, theirs);

        if (now.Width != anchor.Width || now.Height != anchor.Height)
            notes.Add("The Roblox window changed size since you calibrated. If items look wrong, recalibrate.");

        double sx = now.Width / (double)anchor.Width, sy = now.Height / (double)anchor.Height;
        PixelRect Map(PixelRect r) => new(
            now.X + (int)Math.Round((r.X - anchor.X) * sx),
            now.Y + (int)Math.Round((r.Y - anchor.Y) * sy),
            Math.Max(1, (int)Math.Round(r.Width * sx)),
            Math.Max(1, (int)Math.Round(r.Height * sy)));

        return (Map(yours), Map(theirs));
    }

    public void Calibrate()
    {
        if (IsCalibrating || IsScanning) return;
        IsCalibrating = true;
        RaiseChanged();

        bool mainWasVisible = _main.IsVisible && _main.WindowState != WindowState.Minimized;
        try
        {
            var client = RobloxWindow.ClientArea();
            var monitor = client is { } c ? Monitors.FromRect(c) : Monitors.FromCursor();

            _overlay.HideOverlay(animate: false);
            if (mainWasVisible) _main.Hide();
            Native.WaitForCompositor();

            var screenshot = ScreenGrabber.Capture(monitor);
            var picker = new RegionPickerWindow(screenshot, monitor);
            if (picker.ShowDialog() == true && picker.Yours is { } y && picker.Theirs is { } t)
            {
                Settings.YourRegion = y;
                Settings.TheirRegion = t;
                Settings.RobloxClientAtCalibration = client;
                Settings.Save();
                Log.Info($"Calibrated: yours {y}, theirs {t}, roblox {client?.ToString() ?? "not found"}");
                Sounds.Play(Sfx.Saved);
                Notify("Capture regions saved. Press " + Settings.ScanHotkey + " during a trade.", ToastKind.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Calibration failed", ex);
            Notify($"Calibration failed: {ex.Message}", ToastKind.Error);
        }
        finally
        {
            if (mainWasVisible)
            {
                _main.Show();
                _main.Activate();
            }
            IsCalibrating = false;
            RaiseChanged();
        }
    }

    public async Task UpdateValuesAsync(bool silent = false)
    {
        if (IsUpdatingValues) return;
        IsUpdatingValues = true;
        ValuesError = null;
        ValuesProgress = 0;
        RaiseChanged();
        try
        {
            var progress = new Progress<double>(p =>
            {
                ValuesProgress = p;
                RaiseChanged();
            });
            var snapshot = await ValueFetcher.FetchAsync(progress, CancellationToken.None);
            Values.Apply(snapshot, persist: true);
            Log.Info($"Value list updated: {snapshot.Items.Count} rows");
            if (!silent) Notify($"Values updated. {Values.ItemCount:N0} items loaded.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Value update failed", ex);
            ValuesError = $"Couldn't update: {ex.Message}";
            if (!silent) Notify(ValuesError, ToastKind.Error);
        }
        finally
        {
            IsUpdatingValues = false;
            RaiseChanged();
        }
    }

    public void RememberVariant(ItemGroup group, ValueItem variant)
    {
        Settings.VariantChoices[group.Key] = variant.Id;
        Settings.SaveSoon();
    }

    public void ResetOverlayPosition()
    {
        Settings.OverlayX = Settings.OverlayY = null;
        Settings.Save();
    }

    public void ApplyLook()
    {
        ThemeManager.Apply(Settings.Accent, Settings.ThemeBase, Settings.OverlayOpacity);
        Sounds.Volume = Sounds.VolumeFor(Settings.SoundVolume);
        _overlay.ApplyLook();
        Settings.SaveSoon();
    }

    public void ApplyPresence() => _presence.SetEnabled(Settings.DiscordPresence);

    public void ShowOverlayPreview() => _overlay.ShowScan(LastScan, preview: LastScan == null);

    private void BringMainToFront()
    {
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Show();
        _main.Activate();
    }

    private void RaiseChanged() => StateChanged?.Invoke();

    public bool Headless { get; init; }

    internal void InjectScanForScreenshots(TradeScan scan)
    {
        LastScan = scan;
        _overlay.ShowScan(scan);
        RaiseChanged();
    }

    internal IEnumerable<(string Name, FrameworkElement Element, System.Windows.Media.Brush Background)> ScreenshotTargets()
    {
        var bg = (System.Windows.Media.Brush)_main.FindResource("BgBrush");
        foreach (var page in new[] { "trade", "values", "settings" })
        {
            _main.SelectPage(page);
            yield return ("main_" + page, (FrameworkElement)_main.Content, bg);
        }

        _main.SelectPage("settings");
        if (Anim.Descendants<System.Windows.Controls.ScrollViewer>(_main.SettingsPage).FirstOrDefault() is { } scroller)
        {
            scroller.ScrollToVerticalOffset(760);
            scroller.UpdateLayout();
            yield return ("main_settings_lower", (FrameworkElement)_main.Content, bg);
            scroller.ScrollToVerticalOffset(0);
        }

        ThemeManager.Apply("teal", "slate");
        _main.SelectPage("settings");
        yield return ("main_settings_themed", (FrameworkElement)_main.Content, (System.Windows.Media.Brush)_main.FindResource("BgBrush"));
        yield return ("overlay_themed", (FrameworkElement)_overlay.Content,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x5A, 0x48)));
        ThemeManager.Apply(Settings.Accent, Settings.ThemeBase, Settings.OverlayOpacity);
        yield return ("overlay", (FrameworkElement)_overlay.Content,
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x5A, 0x48)));
    }

    public void Dispose()
    {
        Settings.Save();
        _hotkeys.Dispose();
        _presence.Dispose();
    }

    private sealed class CaptureShield : IDisposable
    {
        private readonly List<IntPtr> _hidden = new();

        public CaptureShield(IEnumerable<Window> windows, params PixelRect[] regions)
        {
            foreach (var w in windows)
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) continue;
                if (Native.GetWindowBounds(hwnd) is not { } bounds || !regions.Any(r => r.IntersectsWith(bounds))) continue;

                Native.ShowWindow(hwnd, Native.SW_HIDE);
                _hidden.Add(hwnd);
            }
            if (_hidden.Count > 0) Native.WaitForCompositor();
        }

        public void Dispose()
        {
            foreach (var hwnd in _hidden) Native.ShowWindow(hwnd, Native.SW_SHOWNA);
        }
    }
}
