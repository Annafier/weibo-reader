using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace WeiboReader {
// Owns the engine process (node desktop/bridge.js) over stdio JSON lines — the
// same wire protocol the macOS WebKit shell and the Windows web shell speak.
// Events are raised on a worker thread; UI handlers must marshal themselves.
public class EngineClient : IDisposable {
    Process process;
    StreamWriter stdin;
    readonly object writeLock = new object();
    volatile bool terminating;
    StringBuilder stdoutBuffer = new StringBuilder();
    StringBuilder stderrBuffer = new StringBuilder();

    public event Action<Dictionary<string, object>> Event;
    public event Action<string> EngineLog;
    public event Action EngineExited;
    public bool Alive { get { return process != null && !process.HasExited; } }

    public void Start(string workspace, bool waitForReady) {
        var root = Program.RepoRoot;
        var info = new ProcessStartInfo {
            FileName = "node",
            Arguments = "desktop\\bridge.js --workspace \"" + workspace + "\"" + (waitForReady ? " --wait-for-ready" : ""),
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.EnvironmentVariables["NODE_ENV"] = "production";
        info.EnvironmentVariables["NODE_TLS_REJECT_UNAUTHORIZED"] = "1";
        // weibo-cli 的 Windows openBrowser（cmd start）会把授权 URL 在 & 处拆断，
        // 通过 NODE_OPTIONS --require 注入 quoting 修复（见 windows\cli-open-fix.cjs）。
        var openFix = Path.Combine(root, "windows", "cli-open-fix.cjs");
        if (File.Exists(openFix)) {
            // NODE_OPTIONS mangles backslashes/quotes on Windows: use forward slashes unquoted.
            var forward = openFix.Replace('\\', '/');
            var nodeOptions = info.EnvironmentVariables["NODE_OPTIONS"];
            info.EnvironmentVariables["NODE_OPTIONS"] = string.IsNullOrEmpty(nodeOptions)
                ? "--require " + forward
                : nodeOptions + " --require " + forward;
        }
        process = Process.Start(info);
        stdin = process.StandardInput;
        stdin.AutoFlush = true;
        new Thread(ReadStdout) { IsBackground = true }.Start();
        new Thread(ReadStderr) { IsBackground = true }.Start();
        new Thread(() => {
            process.WaitForExit();
            if (!terminating) Raise(EngineExited);
        }) { IsBackground = true }.Start();
    }
    public void Send(string action, Dictionary<string, object> payload) {
        if (payload == null) payload = new Dictionary<string, object>();
        payload["action"] = action;
        if (!payload.ContainsKey("workspace")) payload["workspace"] = Program.Workspace;
        var line = Json.Serialize(payload);
        try { lock (writeLock) { if (stdin != null) stdin.WriteLine(line); } }
        catch (Exception error) { Program.Log("engine stdin write failed: " + error.Message); }
    }
    void ReadStdout() {
        try {
            var stdout = process.StandardOutput;
            var buffer = new char[8192];
            while (!stdout.EndOfStream) {
                int read = stdout.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                stdoutBuffer.Append(buffer, 0, read);
                DrainLines();
            }
        } catch (Exception error) { Program.Log("stdout reader: " + error.Message); }
    }
    void DrainLines() {
        string all = stdoutBuffer.ToString();
        while (true) {
            int at = all.IndexOf('\n');
            if (at < 0) break;
            var line = all.Substring(0, at).TrimEnd('\r');
            all = all.Substring(at + 1);
            if (line.Length > 4 * 1024 * 1024) { Program.Log("engine line over 4MB dropped"); continue; }
            if (line.Length == 0) continue;
            var data = Json.Parse(line);
            if (data != null && Event != null) Event(data);
        }
        stdoutBuffer.Clear().Append(all);
    }
    void ReadStderr() {
        try {
            var stderr = process.StandardError;
            var buffer = new char[4096];
            while (!stderr.EndOfStream) {
                int read = stderr.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                stderrBuffer.Append(buffer, 0, read);
                string all = stderrBuffer.ToString();
                int at = all.LastIndexOf('\n');
                if (at < 0) continue;
                string done = all.Substring(0, at);
                stderrBuffer.Clear().Append(all.Substring(at + 1));
                foreach (var line in done.Split('\n')) {
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.Length == 0) continue;
                    Program.Log("bridge: " + trimmed);
                    if (EngineLog != null) EngineLog(trimmed);
                }
            }
        } catch (Exception error) { Program.Log("stderr reader: " + error.Message); }
    }
    public void Shutdown() {
        terminating = true;
        try { Send("shutdown", null); } catch {}
        var worker = new Thread(() => {
            bool exited = false;
            try { exited = process != null && process.WaitForExit(5000); } catch {}
            if (!exited) {
                try {
                    var kill = Process.Start(new ProcessStartInfo {
                        FileName = "taskkill", Arguments = "/pid " + process.Id + " /T /F",
                        CreateNoWindow = true, UseShellExecute = false,
                    });
                    kill.WaitForExit(8000);
                } catch {}
            }
            if (EngineExited != null) EngineExited();
        }) { IsBackground = true };
        worker.Start();
    }
    public void Dispose() { Shutdown(); }
    static void Raise(Action handler) { if (handler != null) handler(); }
}
}
