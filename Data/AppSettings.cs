// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace TradeValueOverlay;

public sealed class AppSettings
{
    public string ScanHotkey { get; set; } = "Alt+T";
    public string HideHotkey { get; set; } = "Alt+Shift+T";
    public string CalibrateHotkey { get; set; } = "Ctrl+Alt+C";

    public PixelRect? YourRegion { get; set; }
    public PixelRect? TheirRegion { get; set; }

    public PixelRect? RobloxClientAtCalibration { get; set; }

    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }

    public int AutoHideSeconds { get; set; }
    public bool AutoUpdateValues { get; set; } = true;
    public bool SaveDebugCaptures { get; set; }
    public bool SoundsEnabled { get; set; } = true;
    public string Accent { get; set; } = "violet";
    public string? CustomAccent { get; set; }
    public string ThemeBase { get; set; } = "dark";

    public int OverlayScale { get; set; } = 100;

    public int OverlayOpacity { get; set; } = 100;

    public bool CompactOverlay { get; set; }

    public int FairPercent { get; set; } = 3;

    public int SoundVolume { get; set; } = 1;

    public bool DiscordPresence { get; set; } = true;

    public Dictionary<string, string> VariantChoices { get; set; } = new();

    [JsonIgnore]
    public bool IsCalibrated => YourRegion is { IsEmpty: false } && TheirRegion is { IsEmpty: false };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private DispatcherTimer? _saveTimer;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Settings file was unreadable, starting fresh", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        _saveTimer?.Stop();
        try
        {
            AppPaths.WriteAllTextAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public void SaveSoon()
    {
        _saveTimer ??= CreateTimer();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        t.Tick += (_, _) => Save();
        return t;
    }
}
