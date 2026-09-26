// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TradeValueOverlay;

public partial class MainWindow : Window
{
    private readonly AppController _app;
    private readonly Dictionary<RadioButton, FrameworkElement> _pages;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private FrameworkElement? _current;

    public TradePage TradePage { get; }
    public ValuesPage ValuesPage { get; }
    public SettingsPage SettingsPage { get; }

    public MainWindow(AppController app)
    {
        _app = app;
        InitializeComponent();

        VersionText.Text = "v" + AppInfo.Version;
        SourceInitialized += (_, _) => Native.ApplyModernWindowFrame(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        TradePage = new TradePage(app);
        ValuesPage = new ValuesPage(app);
        SettingsPage = new SettingsPage(app);
        _pages = new()
        {
            [NavTrade] = TradePage,
            [NavValues] = ValuesPage,
            [NavSettings] = SettingsPage,
        };
        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
            PageHost.Children.Add(page);
        }
        ShowPage(TradePage, animate: false);

        _app.Toast += ShowToast;
        _toastTimer.Tick += (_, _) => HideToast();
    }

    public void ShowTradePage() => NavTrade.IsChecked = true;

    internal void SelectPage(string name) =>
        (name switch { "values" => NavValues, "settings" => NavSettings, _ => NavTrade }).IsChecked = true;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_pages == null || sender is not RadioButton rb || !_pages.TryGetValue(rb, out var page)) return;
        Sounds.Play(Sfx.Tap);
        ShowPage(page, animate: true);
    }

    private void ShowPage(FrameworkElement page, bool animate)
    {
        if (_current == page) return;
        if (_current != null) _current.Visibility = Visibility.Collapsed;
        _current = page;
        page.Visibility = Visibility.Visible;
        if (animate) Anim.FadeUp(page, dy: 8, ms: 200);
    }

    private void ShowToast(string message, ToastKind kind)
    {
        var (glyph, fg, bg) = kind switch
        {
            ToastKind.Success => ("", "WinBrush", "WinSoftBrush"),
            ToastKind.Error => ("", "LossBrush", "LossSoftBrush"),
            _ => ("", "AccentHotBrush", "AccentSoftBrush"),
        };
        ToastIcon.Text = glyph;
        ToastIcon.Foreground = (Brush)FindResource(fg);
        ToastIconHost.Background = (Brush)FindResource(bg);
        ToastText.Text = message;

        if (kind == ToastKind.Error) Sounds.Play(Sfx.Error);

        ToastCard.Visibility = Visibility.Visible;
        ToastCard.BeginAnimation(OpacityProperty, null);
        Anim.FadeUp(ToastCard, dy: 14, ms: 220);

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 6 : 3.5);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => ToastCard.Visibility = Visibility.Collapsed;
        ToastCard.BeginAnimation(OpacityProperty, fade);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
