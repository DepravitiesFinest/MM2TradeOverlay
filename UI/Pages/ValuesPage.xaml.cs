// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TradeValueOverlay;

public partial class ValuesPage : UserControl
{
    private readonly AppController _app;
    private readonly ValuesVM _vm = new();
    private ValueSnapshot? _loaded;
    private bool _spinning;

    public ValuesPage(AppController app)
    {
        _app = app;
        InitializeComponent();
        List.ItemsSource = _vm.View;

        _app.StateChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var snap = _app.Values.Snapshot;
        if (!ReferenceEquals(snap, _loaded))
        {
            _loaded = snap;
            _vm.Load(_app.Values);
            UpdateCounts();
        }

        SourceText.Text = snap == null
            ? "No value list loaded yet."
            : $"{_app.Values.ItemCount:N0} items from {snap.Source}  ·  updated {Fmt.Ago(snap.FetchedAt)}";

        UpdateButton.IsEnabled = !_app.IsUpdatingValues;
        UpdateText.Text = _app.IsUpdatingValues ? "Updating…" : "Update values";
        Progress.Visibility = _app.IsUpdatingValues ? Visibility.Visible : Visibility.Hidden;
        Progress.Value = _app.ValuesProgress;
        Spin(_app.IsUpdatingValues);
    }

    private void Spin(bool on)
    {
        if (on == _spinning) return;
        _spinning = on;
        UpdateSpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private void UpdateCounts() =>
        NoResults.Visibility = _vm.VisibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _vm.Search = SearchBox.Text;
        UpdateCounts();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !IsInitialized) return;
        var tag = (string)rb.Tag;
        _vm.UnitFilter = tag.Length == 0 ? null : Enum.Parse<ValueUnit>(tag);
        Sounds.Play(Sfx.Tap);
        UpdateCounts();
    }

    private void Sort_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !IsInitialized) return;
        _vm.SortBy = (string)rb.Tag;
        Sounds.Play(Sfx.Tap);
    }

    private void Update_Click(object sender, RoutedEventArgs e) => _ = _app.UpdateValuesAsync();
}
