// MM2 Trade Overlay
// Copyright (c) 2026 Hyper (https://github.com/DepravitiesFinest)
// Licensed under the MIT License. See LICENSE in the project root.

using System.Windows;

namespace TradeValueOverlay;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private AppController? _controller;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}\n\nDetails were written to {AppPaths.LogFile}",
                "MM2 Trade Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            Shutdown(await SelfTest.RunAsync(e.Args.Skip(1).ToArray()));
            return;
        }

        _singleInstance = new Mutex(true, @"Local\MM2TradeOverlay.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("MM2 Trade Overlay is already running. Check your taskbar.", "MM2 Trade Overlay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Log.Info($"Starting v{AppInfo.Version}");
        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
