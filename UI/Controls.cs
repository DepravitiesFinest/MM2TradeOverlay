// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TradeValueOverlay;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BalanceMeter : FrameworkElement
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(BalanceMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(double), typeof(BalanceMeter), new PropertyMetadata(0.0, (d, e) => ((BalanceMeter)d).Glide()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(BalanceMeter),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(BalanceMeter),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickProperty = DependencyProperty.Register(
        nameof(Tick), typeof(Brush), typeof(BalanceMeter),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CutoutProperty = DependencyProperty.Register(
        nameof(Cutout), typeof(Brush), typeof(BalanceMeter),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public double Target { get => (double)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush Tick { get => (Brush)GetValue(TickProperty); set => SetValue(TickProperty, value); }

    public Brush Cutout { get => (Brush)GetValue(CutoutProperty); set => SetValue(CutoutProperty, value); }

    public BalanceMeter()
    {
        Height = 18;
        Loaded += (_, _) => Glide(from: 0);
    }

    private void Glide(double? from = null)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(Math.Clamp(Target, -1, 1), TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.25 },
        };
        if (from is { } f) anim.From = f;
        BeginAnimation(PositionProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, mid = h / 2;
        if (w <= 0) return;

        const double trackHeight = 4;
        double centre = w / 2, needle = centre + Math.Clamp(Position, -1, 1) * (w / 2 - 3);

        dc.DrawRoundedRectangle(Track, null, new Rect(0, mid - trackHeight / 2, w, trackHeight), 2, 2);

        var span = new Rect(Math.Min(centre, needle), mid - trackHeight / 2, Math.Abs(needle - centre), trackHeight);
        if (span.Width > 0.5) dc.DrawRectangle(Fill, null, span);

        dc.DrawRectangle(Tick, null, new Rect(centre - 0.75, mid - 6, 1.5, 12));

        dc.DrawRoundedRectangle(Cutout, null, new Rect(needle - 4, 0, 8, h), 4, 4);
        dc.DrawRoundedRectangle(Fill, null, new Rect(needle - 2, 1.5, 4, h - 3), 2, 2);
    }
}

public sealed class HotkeyBox : Button
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox), new PropertyMetadata("", (d, _) => ((HotkeyBox)d).Render()));

    public static readonly DependencyProperty IsCapturingProperty = DependencyProperty.Register(
        nameof(IsCapturing), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public bool IsCapturing
    {
        get => (bool)GetValue(IsCapturingProperty);
        private set => SetValue(IsCapturingProperty, value);
    }

    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureEnded;
    public event EventHandler<string>? HotkeyChosen;

    public HotkeyBox()
    {
        Loaded += (_, _) => Render();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (IsCapturing) return;
        IsCapturing = true;
        Keyboard.Focus(this);
        CaptureStarted?.Invoke(this, EventArgs.Empty);
        Render();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Render(Keyboard.Modifiers);
            return;
        }
        if (key == Key.Escape)
        {
            End(null);
            return;
        }

        var mods = Keyboard.Modifiers;
        bool isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (mods == ModifierKeys.None && !isFunctionKey)
        {
            Content = Caption("Add Ctrl, Alt or Shift");
            return;
        }

        End(new Hotkey(mods, key).ToString());
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        End(null);
    }

    private void End(string? chosen)
    {
        if (!IsCapturing) return;
        IsCapturing = false;
        CaptureEnded?.Invoke(this, EventArgs.Empty);
        if (chosen != null && chosen != Hotkey)
        {
            Hotkey = chosen;
            HotkeyChosen?.Invoke(this, chosen);
        }
        Render();
    }

    private void Render(ModifierKeys? partial = null)
    {
        if (IsCapturing)
        {
            if (partial is { } mods && mods != ModifierKeys.None)
                Content = Keycaps(new Hotkey(mods, Key.None).ToString().Replace("+None", "+…"));
            else
                Content = Caption("Press a shortcut…");
            return;
        }
        Content = string.IsNullOrEmpty(Hotkey) ? Caption("Not set") : Keycaps(Hotkey);
    }

    public static StackPanel BuildKeycaps(string combo, double fontSize = 12) => Keycaps(combo, fontSize);

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)Application.Current.FindResource("TextMutedBrush"),
        Margin = new Thickness(6, 0, 6, 0),
    };

    private static StackPanel Keycaps(string combo, double fontSize = 12)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in combo.Split('+'))
        {
            panel.Children.Add(new Border
            {
                Style = (Style)Application.Current.FindResource("Keycap"),
                Child = new TextBlock
                {
                    Text = part,
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                },
            });
        }
        return panel;
    }
}
