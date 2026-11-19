using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;

namespace WeiboReader {
// Entry point and application-wide wiring. The UI is a native WPF front over the
// same Node bridge (stdio JSON lines) used by the macOS/WebKit and web shells.
public class App : Application {}
public static class Program {
    public static string RepoRoot;
    public static string Workspace;
    public static bool LogEvents;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    public static int Main(string[] args) {
        RepoRoot = AppDomain.CurrentDomain.BaseDirectory;
        // exe lives in windows\native\out; repo root is three levels up.
        RepoRoot = Path.GetFullPath(Path.Combine(RepoRoot, "..", "..", ".."));
        Workspace = null;
        bool selftest = false;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--workspace" && i + 1 < args.Length) Workspace = args[i + 1];
            if (args[i] == "--selftest") selftest = true;
            if (args[i] == "--log-events") LogEvents = true;
        }
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        // Single instance: a second engine on the same workspace would fight
        // over the data locks. Signal the existing window instead of starting.
        bool owned;
        var mutex = new System.Threading.Mutex(false, "Local\\WeiboReader.Native", out owned);
        if (!owned) {
            var existing = Process.GetProcessesByName("WeiboReader");
            if (existing.Length > 0) {
                try {
                    foreach (var p in existing) {
                        if (p.MainWindowHandle != IntPtr.Zero) {
                            ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                        }
                    }
                } catch {}
            }
            MessageBox.Show("Weibo Reader 已在运行，已将现有窗口置前。", "Weibo Reader");
            return 0;
        }
        var app = new App();
        try {
            if (selftest) return SelfTest.RunAll();
            var win = new MainWindow();
            win.Show();
            app.Run();
            return 0;
        } catch (Exception error) {
            Log("FATAL: " + error);
            MessageBox.Show("Weibo Reader 崩溃：" + error.Message + "\n\n日志见 windows\\native\\ui.log",
                "Weibo Reader", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    static void OnUnhandled(object sender, UnhandledExceptionEventArgs e) {
        Log("UNHANDLED: " + e.ExceptionObject);
    }
    public static void Log(string message) {
        try {
            File.AppendAllText(Path.Combine(RepoRoot, "windows", "native", "ui.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        } catch {}
    }
}
}
