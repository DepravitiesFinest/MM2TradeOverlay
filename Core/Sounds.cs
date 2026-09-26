// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Media;

namespace TradeValueOverlay;

public enum Sfx { Tap, Scan, Win, Fair, Loss, Error, Saved }

public static class Sounds
{
    private const int Rate = 44100;
    private static readonly Dictionary<Sfx, SoundPlayer> Players = new();

    public static bool Enabled { get; set; } = true;

    private static double _volume = 1;

    public static double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(value - _volume) < 0.001) return;
            _volume = value;
            foreach (var p in Players.Values) p.Dispose();
            Players.Clear();
        }
    }

    public static double VolumeFor(int level) => level switch { 0 => 0.45, 2 => 1.7, _ => 1.0 };

    public static void Play(Sfx sfx)
    {
        if (!Enabled) return;
        try
        {
            if (!Players.TryGetValue(sfx, out var player))
            {
                player = new SoundPlayer(new MemoryStream(Build(sfx)));
                player.Load();
                Players[sfx] = player;
            }
            player.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Sound failed", ex);
        }
    }

    private static byte[] Build(Sfx sfx) => sfx switch
    {
        Sfx.Tap => Render((1650, 0, 35, 0.10)),
        Sfx.Scan => Render((988, 0, 70, 0.16), (1319, 55, 90, 0.14)),
        Sfx.Win => Render((784, 0, 110, 0.17), (988, 70, 110, 0.17), (1319, 140, 260, 0.18)),
        Sfx.Fair => Render((880, 0, 120, 0.16), (880, 110, 200, 0.12)),
        Sfx.Loss => Render((659, 0, 130, 0.17), (494, 110, 280, 0.17)),
        Sfx.Error => Render((196, 0, 150, 0.20), (185, 120, 170, 0.18)),
        Sfx.Saved => Render((1175, 0, 90, 0.14), (1568, 60, 180, 0.14)),
        _ => Render((1000, 0, 40, 0.1)),
    };

    private static byte[] Render(params (double Freq, int StartMs, int LengthMs, double Gain)[] notes)
    {
        int total = notes.Max(n => n.StartMs + n.LengthMs) * Rate / 1000 + Rate / 50;
        var mix = new double[total];

        foreach (var (freq, startMs, lengthMs, gain) in notes)
        {
            int start = startMs * Rate / 1000, length = lengthMs * Rate / 1000;
            for (int i = 0; i < length && start + i < total; i++)
            {
                double t = i / (double)Rate;
                double attack = Math.Min(1, i / (Rate * 0.004));
                double decay = Math.Exp(-t * 7000.0 / lengthMs);
                double tone = Math.Sin(2 * Math.PI * freq * t) + 0.18 * Math.Sin(4 * Math.PI * freq * t);
                mix[start + i] += tone * attack * decay * gain * _volume;
            }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = total * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in mix) w.Write((short)(Math.Clamp(s, -1, 1) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
