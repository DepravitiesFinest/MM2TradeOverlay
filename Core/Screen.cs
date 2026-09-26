// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TradeValueOverlay;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;
    [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IntersectsWith(PixelRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public override string ToString() => $"{Width}×{Height} at ({X}, {Y})";
}

public sealed class Frame(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width * 4);
        bmp.Freeze();
        return bmp;
    }

    public void SavePng(string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource()));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    public static Frame FromBitmapSource(BitmapSource src)
    {
        if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32)
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[src.PixelWidth * src.PixelHeight * 4];
        src.CopyPixels(px, src.PixelWidth * 4, 0);
        return new Frame(src.PixelWidth, src.PixelHeight, px);
    }
}

public static class ScreenGrabber
{
    public static Frame Capture(PixelRect r)
    {
        if (r.IsEmpty) throw new ArgumentException("The capture region is empty. Please recalibrate.");

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memDc = Native.CreateCompatibleDC(screenDc);
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = r.Width,
                biHeight = -r.Height,
                biPlanes = 1,
                biBitCount = 32,
            };
            bitmap = Native.CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("Could not allocate a capture buffer.");

            previous = Native.SelectObject(memDc, bitmap);
            if (!Native.BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, Native.SRCCOPY))
                throw new InvalidOperationException("Screen capture failed.");

            var pixels = new byte[r.Width * r.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new Frame(r.Width, r.Height, pixels);
        }
        finally
        {
            if (previous != IntPtr.Zero) Native.SelectObject(memDc, previous);
            if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
            if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

public static class Monitors
{
    public static PixelRect FromCursor(bool workArea = false)
    {
        Native.GetCursorPos(out var p);
        return Info(Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST), workArea);
    }

    public static PixelRect FromRect(PixelRect r, bool workArea = false)
    {
        var rc = new Native.RECT { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
        return Info(Native.MonitorFromRect(ref rc, Native.MONITOR_DEFAULTTONEAREST), workArea);
    }

    public static bool IsVisible(PixelRect r)
    {
        var rc = new Native.RECT { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
        return Native.MonitorFromRect(ref rc, Native.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }

    private static PixelRect Info(IntPtr monitor, bool workArea)
    {
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref mi);
        return (workArea ? mi.rcWork : mi.rcMonitor).ToPixelRect();
    }
}

public static class RobloxWindow
{
    public static IntPtr Find()
    {
        foreach (var name in new[] { "RobloxPlayerBeta", "Windows10Universal" })
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                    }
                    catch {  }
                }
            }
        }
        return Native.FindWindow("WINDOWSCLIENT", "Roblox");
    }

    public static PixelRect? ClientArea()
    {
        var hWnd = Find();
        if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd) || Native.IsIconic(hWnd) || !Native.IsWindowVisible(hWnd)) return null;
        if (!Native.GetClientRect(hWnd, out var rc)) return null;
        var origin = new Native.POINT();
        Native.ClientToScreen(hWnd, ref origin);
        var r = new PixelRect(origin.X, origin.Y, rc.Right - rc.Left, rc.Bottom - rc.Top);
        return r.IsEmpty ? null : r;
    }
}
