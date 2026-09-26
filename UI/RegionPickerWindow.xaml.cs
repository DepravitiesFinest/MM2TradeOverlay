// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TradeValueOverlay;

public partial class RegionPickerWindow : Window
{
    private readonly PixelRect _monitor;
    private readonly List<Rect> _boxes = new();
    private Point? _dragStart;
    private Rect? _current;

    public PixelRect? Yours { get; private set; }
    public PixelRect? Theirs { get; private set; }

    public RegionPickerWindow(Frame screenshot, PixelRect monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        Shot.Source = screenshot.ToBitmapSource();

        SourceInitialized += (_, _) => PlaceOnMonitor();
        Loaded += (_, _) =>
        {
            PlaceOnMonitor();
            Activate();
            Focus();
            Redraw();
        };

        Layer.MouseLeftButtonDown += OnDown;
        Layer.MouseMove += OnMove;
        Layer.MouseLeftButtonUp += OnUp;
        MouseRightButtonUp += (_, _) => Undo();
        KeyDown += OnKey;
        SizeChanged += (_, _) => Redraw();
    }

    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, Native.SWP_SHOWWINDOW);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_boxes.Count >= 2) return;
        _dragStart = e.GetPosition(Layer);
        _current = null;
        Layer.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        _current = new Rect(start, e.GetPosition(Layer));
        Redraw();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart == null) return;
        Layer.ReleaseMouseCapture();
        _dragStart = null;

        if (_current is { } r && r.Width >= 12 && r.Height >= 12) _boxes.Add(r);
        _current = null;
        Redraw();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                DialogResult = false;
                break;
            case Key.Back:
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                Undo();
                break;
            case Key.Enter when _boxes.Count == 2:
                Save();
                break;
        }
    }

    private void Undo()
    {
        if (_boxes.Count > 0) _boxes.RemoveAt(_boxes.Count - 1);
        Redraw();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        _boxes.Clear();
        Redraw();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        Yours = ToPhysical(_boxes[0]);
        Theirs = ToPhysical(_boxes[1]);
        DialogResult = true;
    }

    private PixelRect ToPhysical(Rect r)
    {
        double sx = _monitor.Width / Math.Max(1, Layer.ActualWidth);
        double sy = _monitor.Height / Math.Max(1, Layer.ActualHeight);
        int x = _monitor.X + (int)Math.Round(r.X * sx);
        int y = _monitor.Y + (int)Math.Round(r.Y * sy);
        return new PixelRect(x, y, (int)Math.Round(r.Width * sx), (int)Math.Round(r.Height * sy));
    }

    private void Redraw()
    {
        if (Layer.ActualWidth <= 0) return;

        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, Layer.ActualWidth, Layer.ActualHeight)));
        foreach (var b in _boxes) geometry.Children.Add(new RectangleGeometry(b, 4, 4));
        if (_current is { } c) geometry.Children.Add(new RectangleGeometry(c, 4, 4));
        Dim.Data = geometry;

        for (int i = Layer.Children.Count - 1; i >= 0; i--)
            if (Layer.Children[i] != Dim) Layer.Children.RemoveAt(i);

        for (int i = 0; i < _boxes.Count; i++) DrawBox(_boxes[i], i, final: true);
        if (_current is { } cur) DrawBox(cur, _boxes.Count, final: false);

        UpdateInstructions();
    }

    private void DrawBox(Rect r, int index, bool final)
    {
        var brush = (Brush)FindResource(index == 0 ? "GiveBrush" : "GetBrush");

        var outline = new Border
        {
            Width = r.Width,
            Height = r.Height,
            BorderBrush = brush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(outline, r.X);
        Canvas.SetTop(outline, r.Y);
        Layer.Children.Add(outline);

        var px = ToPhysical(r);
        var label = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{(index == 0 ? "YOUR OFFER" : "THEIR OFFER")}   {px.Width} × {px.Height}",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12)),
            },
        };
        Canvas.SetLeft(label, r.X);
        Canvas.SetTop(label, r.Y >= 26 ? r.Y - 26 : r.Bottom + 6);
        label.Opacity = final ? 1 : 0.85;
        Layer.Children.Add(label);
    }

    private void UpdateInstructions()
    {
        int step = _boxes.Count;
        var give = (Brush)FindResource("GiveBrush");
        var get = (Brush)FindResource("GetBrush");
        var off = (Brush)FindResource("Surface3Brush");

        StepDot1.Background = step >= 0 ? give : off;
        StepDot2.Background = step >= 1 ? get : off;

        switch (step)
        {
            case 0:
                StepText.Text = "STEP 1 OF 2";
                TitleText.Text = "Drag a box around YOUR offer";
                HintText.Text = "Cover every item slot on your side, including the item names. A little extra space is fine.";
                break;
            case 1:
                StepText.Text = "STEP 2 OF 2";
                TitleText.Text = "Now drag a box around THEIR offer";
                HintText.Text = "Same again for the other player's side of the trade.";
                break;
            default:
                StepText.Text = "DONE";
                TitleText.Text = "Both regions set";
                HintText.Text = "Press Enter or click Save. You only need to redo this if the trade window moves or changes size.";
                break;
        }

        Instructions.Visibility = _dragStart != null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmBar.Visibility = step >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Cursor = step >= 2 ? Cursors.Arrow : Cursors.Cross;
    }
}
