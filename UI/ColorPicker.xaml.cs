// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TradeValueOverlay;

public partial class ColorPicker : UserControl
{
    private double _hue, _saturation, _value;
    private bool _writingText;

    public Color Color { get; private set; }

    public event Action<Color>? ColorChanged;

    public ColorPicker()
    {
        InitializeComponent();
        Field.SizeChanged += (_, _) => PlaceThumbs();
        HueBar.SizeChanged += (_, _) => PlaceThumbs();
    }

    public void SetColor(Color color)
    {
        Color = color;
        (_hue, _saturation, _value) = ToHsv(color, _hue);
        Render(writeText: true);
    }

    private void Field_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Field.CaptureMouse();
        PickFromField(e.GetPosition(Field));
    }

    private void Field_MouseMove(object sender, MouseEventArgs e)
    {
        if (Field.IsMouseCaptured) PickFromField(e.GetPosition(Field));
    }

    private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        PickFromHue(e.GetPosition(HueBar));
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured) PickFromHue(e.GetPosition(HueBar));
    }

    private void Drag_MouseUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void PickFromField(Point p)
    {
        _saturation = Math.Clamp(p.X / Field.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(p.Y / Field.ActualHeight, 0, 1);
        Commit();
    }

    private void PickFromHue(Point p)
    {
        _hue = Math.Clamp(p.X / HueBar.ActualWidth, 0, 1) * 359.9;
        Commit();
    }

    private void Commit()
    {
        Color = FromHsv(_hue, _saturation, _value);
        Render(writeText: true);
        ColorChanged?.Invoke(Color);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_writingText || !ThemeManager.TryParseHex(HexBox.Text, out var typed) || typed == Color) return;
        Color = typed;
        (_hue, _saturation, _value) = ToHsv(typed, _hue);
        Render(writeText: false);
        ColorChanged?.Invoke(Color);
    }

    private void HexBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Render(writeText: true);

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Render(writeText: true);
        HexBox.SelectAll();
    }

    private void Render(bool writeText)
    {
        var hue = new SolidColorBrush(FromHsv(_hue, 1, 1));
        var current = new SolidColorBrush(Color);
        HueFill.Background = hue;
        HueThumb.Fill = hue;
        FieldThumb.Fill = current;
        Preview.Background = current;

        if (writeText)
        {
            _writingText = true;
            HexBox.Text = ThemeManager.ToHex(Color);
            _writingText = false;
        }
        PlaceThumbs();
    }

    private void PlaceThumbs()
    {
        Canvas.SetLeft(FieldThumb, _saturation * Field.ActualWidth - FieldThumb.Width / 2);
        Canvas.SetTop(FieldThumb, (1 - _value) * Field.ActualHeight - FieldThumb.Height / 2);
        Canvas.SetLeft(HueThumb, _hue / 360 * HueBar.ActualWidth - HueThumb.Width / 2);
    }

    internal static (double H, double S, double V) ToHsv(Color c, double fallbackHue)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        if (delta == 0) return (fallbackHue, 0, max);

        double h = max == r ? (g - b) / delta % 6
                 : max == g ? (b - r) / delta + 2
                 : (r - g) / delta + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, delta / max, max);
    }

    internal static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
