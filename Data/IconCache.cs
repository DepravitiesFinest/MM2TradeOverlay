// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TradeValueOverlay;

public static class IconCache
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MM2TradeOverlay/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/png,image/*;q=0.8,*/*;q=0.5");
        return client;
    }
    private static readonly Dictionary<string, Task<ImageSource?>> Memory = new();

    public static Task<ImageSource?> GetAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult<ImageSource?>(null);
        lock (Memory)
        {
            if (!Memory.TryGetValue(url, out var task))
            {
                task = LoadAsync(url);
                Memory[url] = task;
            }
            return task;
        }
    }

    public static async Task<Frame?> GetFrameAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var bytes = await GetBytesAsync(url);
            return await Task.Run(() =>
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.DecodePixelWidth = 128;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                return Frame.FromBitmapSource(img);
            });
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> GetBytesAsync(string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var file = Path.Combine(AppPaths.IconDir, hash + Path.GetExtension(new Uri(url).AbsolutePath));
        if (File.Exists(file)) return await File.ReadAllBytesAsync(file);

        var bytes = await Http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(file, bytes);
        return bytes;
    }

    private static async Task<ImageSource?> LoadAsync(string url)
    {
        try
        {
            var bytes = await GetBytesAsync(url);

            return await Task.Run(() =>
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.DecodePixelWidth = 72;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                return (ImageSource?)img;
            });
        }
        catch
        {
            return null;
        }
    }
}
