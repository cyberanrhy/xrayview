using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace XrayDesktop;

public partial class App : System.Windows.Application
{
    private const string MutexName = "XrayDesktop-{8A2B3C4D-5E6F-7890-ABCD-EF1234567890}";
    private static readonly Mutex _mutex = new(true, MutexName);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_mutex.WaitOne(TimeSpan.Zero, true))
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            var currentId = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("XrayDesktop"))
            {
                if (p.Id == currentId) continue;
                p.WaitForInputIdle(2000);
                var hWnd = p.MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, 9);
                    SetForegroundWindow(hWnd);
                }
                break;
            }
        }
        catch { }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
