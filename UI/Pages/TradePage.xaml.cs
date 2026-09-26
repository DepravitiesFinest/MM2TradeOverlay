// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TradeValueOverlay;

public partial class TradePage : UserControl
{
    private readonly AppController _app;
    private TradeScan? _shown;

    public TradePage(AppController app)
    {
        _app = app;
        InitializeComponent();

        SetupSteps.ItemsSource = new[]
        {
            new { Number = "01", Title = "Open a trade in Murder Mystery 2", Detail = "Any trade will do, as long as both offer boxes are on screen." },
            new { Number = "02", Title = "Drag a box around your offer", Detail = "Include every item slot and the item names." },
            new { Number = "03", Title = "Drag a box around their offer", Detail = "That's it. Regions follow the Roblox window if you move it." },
        };

        _app.StateChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var s = _app.Settings;
        ScanKeys.Content = HotkeyBox.BuildKeycaps(s.ScanHotkey);

        bool ocrOk = _app.Ocr != null;
        OcrBanner.Visibility = ocrOk ? Visibility.Collapsed : Visibility.Visible;
        OcrText.Text = _app.OcrError ?? "";

        ScanButton.IsEnabled = ocrOk && s.IsCalibrated && _app.Values.ItemCount > 0 && !_app.IsScanning && !_app.IsCalibrating;
        ScanButtonText.Text = _app.IsScanning ? "Scanning…" : "Scan now";
        RecalibrateButton.Visibility = s.IsCalibrated ? Visibility.Visible : Visibility.Collapsed;
        RecalibrateButton.IsEnabled = ocrOk && !_app.IsCalibrating;

        SetupCard.Visibility = ocrOk && !s.IsCalibrated ? Visibility.Visible : Visibility.Collapsed;
        EmptyCard.Visibility = ocrOk && s.IsCalibrated && _app.LastScan == null ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = _app.LastScan != null ? Visibility.Visible : Visibility.Collapsed;

        if (_app.LastScan != null && !ReferenceEquals(_app.LastScan, _shown))
        {
            _shown = _app.LastScan;
            ResultPanel.DataContext = new TradeVM(_shown, _app);
            ResultTime.Text = $"Scanned at {_shown.Time:HH:mm}";
            YourPreview.Child = BuildPreview(_shown.Yours);
            TheirPreview.Child = BuildPreview(_shown.Theirs);
            if (IsVisible)
            {
                Anim.Pop(Hero);
                Anim.StaggerRows(ResultPanel);
            }
        }
    }

    private UIElement BuildPreview(OfferScan offer)
    {
        var cap = offer.Capture;
        var surface = new Grid { Width = cap.Width, Height = cap.Height };
        surface.Children.Add(new Image { Source = cap.ToBitmapSource(), Stretch = Stretch.Fill });

        var canvas = new Canvas();
        double stroke = Math.Max(1.5, cap.Width / 160.0);
        void Box(Rect r, string brushKey, string tip)
        {
            var shape = new System.Windows.Shapes.Rectangle
            {
                Width = r.Width + 4,
                Height = r.Height + 4,
                Stroke = (Brush)FindResource(brushKey),
                StrokeThickness = stroke,
                RadiusX = 3,
                RadiusY = 3,
                ToolTip = tip,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(shape, r.X - 2);
            Canvas.SetTop(shape, r.Y - 2);
            canvas.Children.Add(shape);
        }
        foreach (var item in offer.Items) Box(item.Bounds, "WinBrush", $"{item.Group.Name} (read as \"{item.OcrText}\")");
        foreach (var u in offer.Unknown) Box(u.Bounds, "WarnBrush", $"Not recognised: \"{u.Text}\"");
        surface.Children.Add(canvas);

        return new Viewbox { Child = surface, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => _ = _app.ScanAsync();

    private void Calibrate_Click(object sender, RoutedEventArgs e) => _app.Calibrate();

    private void FixOcr_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
}
