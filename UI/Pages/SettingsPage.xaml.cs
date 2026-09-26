// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace TradeValueOverlay;

public partial class SettingsPage : UserControl
{
    private readonly AppController _app;
    private bool _loading = true;

    public SettingsPage(AppController app)
    {
        _app = app;
        InitializeComponent();

        foreach (var box in new[] { ScanHotkeyBox, HideHotkeyBox, CalibrateHotkeyBox })
        {
            var slot = (HotkeySlot)box.Tag;
            box.Hotkey = _app.GetHotkey(slot);
            box.CaptureStarted += (_, _) => _app.SuspendHotkeys();
            box.CaptureEnded += (_, _) => _app.ResumeHotkeys();
            box.HotkeyChosen += (_, value) =>
            {
                var error = _app.TrySetHotkey(slot, value);
                box.Hotkey = _app.GetHotkey(slot);
                if (error != null) _app.Notify(error, ToastKind.Error);
                else
                {
                    Sounds.Play(Sfx.Saved);
                    _app.Notify($"Shortcut set to {value}.", ToastKind.Success);
                }
            };
        }

        var s = _app.Settings;
        DebugToggle.IsChecked = s.SaveDebugCaptures;
        SoundToggle.IsChecked = s.SoundsEnabled;
        AutoUpdateToggle.IsChecked = s.AutoUpdateValues;
        foreach (RadioButton rb in AutoHidePanel.Children)
            rb.IsChecked = int.Parse((string)rb.Tag) == s.AutoHideSeconds;
        if (AutoHidePanel.Children.OfType<RadioButton>().All(r => r.IsChecked != true))
            ((RadioButton)AutoHidePanel.Children[0]).IsChecked = true;
        AboutTitle.Text = $"MM2 Trade Overlay  {AppInfo.Version}";
        BuildAppearance();
        BuildChoices();
        CompactToggle.IsChecked = s.CompactOverlay;
        _loading = false;

        _app.StateChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var s = _app.Settings;
        RegionText.Text = s.IsCalibrated
            ? $"Your offer {s.YourRegion!.Value.Width}×{s.YourRegion.Value.Height} px  ·  their offer {s.TheirRegion!.Value.Width}×{s.TheirRegion.Value.Height} px"
            : "Not set up yet";
        CalibrateText.Text = s.IsCalibrated ? "Recalibrate" : "Set up";
        CalibrateButton.IsEnabled = _app.Ocr != null && !_app.IsCalibrating;
    }

    private void BuildAppearance()
    {
        var s = _app.Settings;
        foreach (var accent in ThemeManager.Accents)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "Accent",
                Background = new System.Windows.Media.SolidColorBrush(accent.Color),
                ToolTip = accent.Name,
                IsChecked = ThemeManager.AccentFor(s.Accent) == accent,
            };
            swatch.Checked += (_, _) =>
            {
                if (_loading) return;
                s.Accent = accent.Key;
                ApplyTheme();
            };
            AccentPanel.Children.Add(swatch);
        }

        foreach (var option in ThemeManager.Bases)
        {
            var segment = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "ThemeBase",
                Content = option.Name,
                IsChecked = ThemeManager.BaseFor(s.ThemeBase) == option,
            };
            segment.Checked += (_, _) =>
            {
                if (_loading) return;
                s.ThemeBase = option.Key;
                ApplyTheme();
            };
            BasePanel.Children.Add(segment);
        }
        AccentName.Text = ThemeManager.AccentFor(s.Accent).Name;
    }

    private void BuildChoices()
    {
        var s = _app.Settings;
        Segments(ScalePanel, new[] { ("90%", 90), ("100%", 100), ("115%", 115), ("130%", 130) }, s.OverlayScale, v =>
        {
            s.OverlayScale = v;
            _app.ApplyLook();
            _app.ShowOverlayPreview();
        });
        Segments(OpacityPanel, new[] { ("Solid", 100), ("90%", 90), ("80%", 80), ("70%", 70) }, s.OverlayOpacity, v =>
        {
            s.OverlayOpacity = v;
            _app.ApplyLook();
            _app.ShowOverlayPreview();
        });
        Segments(FairPanel, new[] { ("1%", 1), ("3%", 3), ("5%", 5), ("10%", 10) }, s.FairPercent, v =>
        {
            s.FairPercent = v;
            s.SaveSoon();
        });
        Segments(VolumePanel, new[] { ("Quiet", 0), ("Normal", 1), ("Loud", 2) }, s.SoundVolume, v =>
        {
            s.SoundVolume = v;
            _app.ApplyLook();
            Sounds.Play(Sfx.Win);
        });
    }

    private void Segments(StackPanel panel, (string Label, int Value)[] options, int current, Action<int> onPick)
    {
        if (options.All(o => o.Value != current)) current = options[1].Value;
        foreach (var (label, value) in options)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = panel.Name,
                Content = label,
                IsChecked = value == current,
            };
            rb.Checked += (_, _) =>
            {
                if (_loading) return;
                Sounds.Play(Sfx.Tap);
                onPick(value);
            };
            panel.Children.Add(rb);
        }
    }

    private void ApplyTheme()
    {
        _app.ApplyLook();
        AccentName.Text = ThemeManager.AccentFor(_app.Settings.Accent).Name;
        _app.Settings.SaveSoon();
        Sounds.Play(Sfx.Tap);
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _app.Settings;
        s.SaveDebugCaptures = DebugToggle.IsChecked == true;
        s.AutoUpdateValues = AutoUpdateToggle.IsChecked == true;
        s.SoundsEnabled = SoundToggle.IsChecked == true;
        bool compactChanged = s.CompactOverlay != (CompactToggle.IsChecked == true);
        s.CompactOverlay = CompactToggle.IsChecked == true;
        if (compactChanged)
        {
            _app.ApplyLook();
            _app.ShowOverlayPreview();
        }
        Sounds.Enabled = s.SoundsEnabled;
        Sounds.Play(Sfx.Tap);
        s.SaveSoon();
    }

    private void AutoHide_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.AutoHideSeconds = int.Parse((string)((RadioButton)sender).Tag);
        _app.Settings.SaveSoon();
        Sounds.Play(Sfx.Tap);
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e) => _app.Calibrate();

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _app.ResetOverlayPosition();
        _app.ShowOverlayPreview();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => _app.ShowOverlayPreview();

    private void OpenDebug_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DebugDir);

    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DataDir);

    private static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
