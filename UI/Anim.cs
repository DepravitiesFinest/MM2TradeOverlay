// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TradeValueOverlay;

public static class Anim
{
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

    public static void FadeUp(UIElement e, double dy = 10, int ms = 220, int delayMs = 0)
    {
        var shift = new TranslateTransform(0, dy);
        e.RenderTransform = shift;
        e.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = EaseOut });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = begin, EasingFunction = EaseOut });
    }

    public static void Pop(FrameworkElement e, int delayMs = 0)
    {
        var scale = new ScaleTransform(0.96, 0.96);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = scale;
        e.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var d = TimeSpan.FromMilliseconds(260);
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { BeginTime = begin });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, d) { BeginTime = begin, EasingFunction = Spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, d) { BeginTime = begin, EasingFunction = Spring });
    }

    public static void StaggerRows(DependencyObject root, int startMs = 90, int stepMs = 30)
    {
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            int i = 0;
            foreach (var list in Descendants<ItemsControl>(root))
            {
                for (int k = 0; k < list.Items.Count; k++)
                {
                    if (list.ItemContainerGenerator.ContainerFromIndex(k) is UIElement row)
                        FadeUp(row, 6, 200, startMs + Math.Min(i++, 14) * stepMs);
                }
            }
        });
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
