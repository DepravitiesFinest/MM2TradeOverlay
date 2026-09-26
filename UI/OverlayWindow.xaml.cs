// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TradeValueOverlay;

public partial class OverlayWindow : Window
{
    private readonly AppController _app;
    private readonly DispatcherTimer _autoHide = new();
    private bool _hiding;

    public OverlayWindow(AppController app)
    {
        _app = app;
        InitializeComponent();

        SourceInitialized += (_, _) =>
            Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

        Header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            if (Native.GetWindowBounds(new WindowInteropHelper(this).Handle) is { } r)
            {
                _app.Settings.OverlayX = r.X;
                _app.Settings.OverlayY = r.Y;
                _app.Settings.SaveSoon();
            }
        };

        _autoHide.Tick += (_, _) => HideOverlay();
        MouseEnter += (_, _) => _autoHide.Stop();
        MouseLeave += (_, _) => RestartAutoHide();
    }

    public void ApplyLook()
    {
        double scale = Math.Clamp(_app.Settings.OverlayScale, 70, 160) / 100.0;
        Root.LayoutTransform = Math.Abs(scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(scale, scale);
        Width = 384 * scale;
        ItemsList.Visibility = _app.Settings.CompactOverlay ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowScan(TradeScan? scan, bool preview = false)
    {
        ApplyLook();
        var vm = preview && scan == null ? new TradeVM(SampleScan(), _app, preview: true) : new TradeVM(scan, _app);
        DataContext = vm;
        UpdateFooter(preview);
        PlaceAndShow();

        Anim.Pop(Hero, delayMs: 40);
        Anim.StaggerRows(ResultPanel, startMs: 110, stepMs: 26);
        if (!preview)
        {
            Sounds.Play(vm.Verdict switch
            {
                Verdict.Win => Sfx.Win,
                Verdict.Loss => Sfx.Loss,
                Verdict.Fair => Sfx.Fair,
                _ => Sfx.Error,
            });
        }
    }

    public void ShowError(string title, string message)
    {
        DataContext = new TradeVM(title, message);
        UpdateFooter(false);
        PlaceAndShow();
        Sounds.Play(Sfx.Error);
    }

    public void HideOverlay(bool animate = true)
    {
        _autoHide.Stop();
        if (!IsVisible || _hiding) return;

        if (!animate)
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }

        _hiding = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) =>
        {
            _hiding = false;
            BeginAnimation(OpacityProperty, null);
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateFooter(bool preview)
    {
        Footer.Children.Clear();
        if (preview)
        {
            Footer.Children.Add(FooterText("Preview with sample items  ·  drag the header to move it"));
            return;
        }
        Footer.Children.Add(HotkeyBox.BuildKeycaps(_app.Settings.ScanHotkey, 10.5));
        Footer.Children.Add(FooterText("rescan", 4));
        Footer.Children.Add(HotkeyBox.BuildKeycaps(_app.Settings.HideHotkey, 10.5));
        Footer.Children.Add(FooterText("hide", 4));
    }

    private static TextBlock FooterText(string text, double left = 0) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextFaintBrush"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(left, 0, 14, 0),
    };

    private void PlaceAndShow()
    {
        _hiding = false;
        BeginAnimation(OpacityProperty, null);
        bool wasVisible = IsVisible;
        if (!wasVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (Native.GetWindowBounds(hwnd) is { } size)
        {
            var (x, y) = ChoosePosition(size);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        if (!wasVisible)
        {
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = 1; };
            BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            Opacity = 1;
        }

        RestartAutoHide();
    }

    private (int X, int Y) ChoosePosition(PixelRect size)
    {
        if (_app.Headless) return (-30000, -30000);

        var s = _app.Settings;
        if (s.OverlayX is int sx && s.OverlayY is int sy && Monitors.IsVisible(new PixelRect(sx, sy, size.Width, size.Height)))
            return (sx, sy);

        var work = RobloxWindow.ClientArea() is { } game ? Monitors.FromRect(game, workArea: true) : Monitors.FromCursor(workArea: true);
        return (work.Right - size.Width - 8, work.Y + Math.Max(0, (work.Height - size.Height) / 2));
    }

    private void RestartAutoHide()
    {
        _autoHide.Stop();
        if (_app.Settings.AutoHideSeconds > 0 && IsVisible)
        {
            _autoHide.Interval = TimeSpan.FromSeconds(_app.Settings.AutoHideSeconds);
            _autoHide.Start();
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = _app.ScanAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private TradeScan SampleScan()
    {
        var blank = new Frame(1, 1, new byte[4]);
        OfferScan Offer(params (string Name, int Qty)[] items)
        {
            var list = new List<DetectedItem>();
            foreach (var (name, qty) in items)
            {
                var (group, _) = _app.Values.Matcher.Match(ItemMatcher.Normalize(name));
                if (group != null) list.Add(new DetectedItem { Group = group, Bounds = Rect.Empty, Quantity = qty });
            }
            return new OfferScan { Capture = blank, ScreenRegion = default, Items = list };
        }

        return new TradeScan
        {
            Yours = Offer(("Chroma Luger", 1), ("Seer", 2)),
            Theirs = Offer(("Chroma Shark", 1), ("Chroma Fang", 1), ("Batwing", 1)),
        };
    }
}
