// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinLanguage = Windows.Globalization.Language;

namespace TradeValueOverlay;

public sealed record WordBox(string Text, Rect Bounds);

public sealed record OcrTextLine(IReadOnlyList<WordBox> Words);

public sealed class OcrUnavailableException(string message) : Exception(message);

public sealed class OcrService
{
    private readonly OcrEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string LanguageName { get; }

    public static int MaxDimension => (int)OcrEngine.MaxImageDimension;

    private OcrService(OcrEngine engine)
    {
        _engine = engine;
        LanguageName = engine.RecognizerLanguage.DisplayName;
    }

    public static OcrService Create()
    {
        foreach (var tag in new[] { "en-US", "en-GB", "en" })
        {
            var lang = new WinLanguage(tag);
            if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } engine)
                return new OcrService(engine);
        }

        var english = OcrEngine.AvailableRecognizerLanguages
            .FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (english != null && OcrEngine.TryCreateFromLanguage(english) is { } en)
            return new OcrService(en);

        if (OcrEngine.TryCreateFromUserProfileLanguages() is { } fallback)
            return new OcrService(fallback);

        throw new OcrUnavailableException(
            "Windows text recognition isn't installed. Open Settings → Time & language → Language & region, " +
            "add \"English (United States)\", then restart this app.");
    }

    public async Task<List<OcrTextLine>> RecognizeAsync(Frame frame)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            frame.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Premultiplied);

        await _gate.WaitAsync();
        try
        {
            var result = await _engine.RecognizeAsync(bitmap);
            return result.Lines
                .Select(line => new OcrTextLine(line.Words
                    .Select(w => new WordBox(w.Text, new Rect(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)))
                    .ToList()))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }
}
