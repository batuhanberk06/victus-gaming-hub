// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;

namespace VictusHub.App;
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private static string ExitEventName => @"Local\VictusHub.Exit." + WindowsIdentity.GetCurrent().User!.Value;
    public static string[] Arguments { get; private set; } = [];
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Arguments = e.Args;
        if (e.Args is ["--startup", var startup])
        {
            if (startup is not ("enable" or "disable")) { Shutdown(2); return; }
            UserStartup.SetEnabled(startup == "enable");
            Shutdown(); return;
        }
        if (e.Args is ["--quit"])
        {
            try { using var signal = EventWaitHandle.OpenExisting(ExitEventName); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        _singleInstance = new Mutex(true, Array.IndexOf(e.Args, "--design-preview") >= 0 ? @"Local\VictusHub.Preview" : @"Local\VictusHub.App", out bool first);
        if (!first) { if (Array.IndexOf(e.Args, "--tray") < 0) System.Windows.MessageBox.Show("Victus Gaming Hub is already running. Open it from the system tray.", "Victus Gaming Hub"); Shutdown(); return; }
        DispatcherUnhandledException += (_, error) =>
        {
            System.Windows.MessageBox.Show(error.Exception.Message, "Victus Gaming Hub — error");
            error.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        if (Array.IndexOf(e.Args, "--design-preview") < 0)
        {
            var security = new EventWaitHandleSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User!, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            _exitEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ExitEventName, out _, security);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(new Action(window.RequestExit)), null, Timeout.Infinite, false);
        }
        if (Array.IndexOf(e.Args, "--tray") < 0) window.Show();
        else
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Minimized;
            window.Show(); window.Hide();
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = true;
        }
    }
    protected override void OnExit(ExitEventArgs e) { _exitWait?.Unregister(null); _exitEvent?.Dispose(); _singleInstance?.Dispose(); base.OnExit(e); }
}


