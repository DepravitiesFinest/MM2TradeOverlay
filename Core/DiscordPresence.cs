// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using DiscordRPC;

namespace TradeValueOverlay;

public sealed class DiscordPresence : IDisposable
{
    private const string ApplicationId = "1554158992211583006";
    private const string LogoUrl = "https://raw.githubusercontent.com/DepravitiesFinest/MM2TradeOverlay/main/Assets/app.png";
    private const string GitHubIconUrl = "https://github.githubassets.com/favicons/favicon-dark.png";

    private readonly DateTime _openedAt = DateTime.UtcNow;
    private DiscordRpcClient? _client;
    private int _tradesChecked;

    public void SetEnabled(bool enabled)
    {
        if (enabled) Connect();
        else Disconnect();
    }

    public void TradeChecked()
    {
        _tradesChecked++;
        Publish();
    }

    private void Connect()
    {
        if (_client != null) return;
        try
        {
            _client = new DiscordRpcClient(ApplicationId) { SkipIdenticalPresence = true };
            _client.Initialize();
            Publish();
        }
        catch (Exception ex)
        {
            Log.Error("Discord status could not start", ex);
            Disconnect();
        }
    }

    private void Publish()
    {
        if (_client == null) return;
        try
        {
            _client.SetPresence(new RichPresence
            {
                Details = "Checking MM2 trades",
                State = _tradesChecked switch
                {
                    0 => "Waiting for a trade",
                    1 => "1 trade checked",
                    var n => $"{n} trades checked",
                },
                Timestamps = new Timestamps(_openedAt),
                Assets = new Assets
                {
                    LargeImageKey = LogoUrl,
                    LargeImageText = $"MM2 Trade Overlay {AppInfo.Version}",
                    SmallImageKey = GitHubIconUrl,
                    SmallImageText = "Free and open source on GitHub",
                },
                Buttons = new[]
                {
                    new Button { Label = "Get the overlay", Url = AppInfo.ReleasesUrl },
                    new Button { Label = "Join the Discord", Url = AppInfo.DiscordUrl },
                },
            });
        }
        catch (Exception ex)
        {
            Log.Error("Discord status update failed", ex);
        }
    }

    private void Disconnect()
    {
        if (_client == null) return;
        try
        {
            _client.ClearPresence();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Discord status did not shut down cleanly", ex);
        }
        _client = null;
    }

    public void Dispose() => Disconnect();
}
