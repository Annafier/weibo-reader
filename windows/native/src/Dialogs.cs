using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;

namespace WeiboReader {
// Native dialogs and shell interop. Mirrors the macOS shell's native actions:
// folder/file pickers, clipboard, external links (same allow-list as
// desktop/ExternalLinks.swift) and report/folder opening.
public static class Dialogs {
    public static string ChooseFolder(Window owner) {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "选择资料目录";
        dialog.ShowNewFolderButton = true;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) return dialog.SelectedPath;
        return "";
    }
    public static string ChooseFile(Window owner, bool save, string defaultName) {
        if (save) {
            var dialog = new SaveFileDialog { Filter = "WeiboReader 数据包(*.wrdpack)|*.wrdpack", AddExtension = true, FileName = defaultName };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : "";
        }
        var open = new OpenFileDialog { Filter = "WeiboReader 数据包(*.wrdpack)|*.wrdpack", CheckFileExists = true };
        return open.ShowDialog(owner) == true ? open.FileName : "";
    }
    public static void CopyClipboard(string text) {
        try { Clipboard.SetText(text ?? ""); } catch {}
    }
    public static void OpenReport(string path) {
        OpenWithShell(path, "无法打开报告");
    }
    public static void OpenFolder(string path) {
        if (!Directory.Exists(path)) { MessageBox.Show("目录不存在：" + path, "Weibo Reader"); return; }
        try { Process.Start("explorer.exe", "\"" + path + "\""); } catch (Exception error) { MessageBox.Show("无法打开文件夹：" + error.Message, "Weibo Reader"); }
    }
    static void OpenWithShell(string target, string failure) {
        try {
            var start = new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/d /s /c start \"\" \"" + target + "\"", UseShellExecute = false, CreateNoWindow = true };
            Process.Start(start);
        } catch (Exception error) { MessageBox.Show(failure + "：" + error.Message, "Weibo Reader"); }
    }
    // Same allow-list as desktop/ExternalLinks.swift: only canonical profile,
    // post-comment and service URLs; no redirects, files or custom schemes.
    public static string ExternalUrl(string raw) {
        if (string.IsNullOrEmpty(raw) || raw.Length >= 200) return null;
        if (raw.IndexOf('%') >= 0 || raw.IndexOf('\\') >= 0) return null;
        foreach (var ch in raw) if (char.IsWhiteSpace(ch)) return null;
        Uri url;
        try { url = new Uri(raw); } catch { return null; }
        if (url.Scheme != "https" || url.UserInfo.Length > 0 || url.IsDefaultPort == false && url.Port != 443) return null;
        var host = url.Host.ToLowerInvariant();
        var path = url.AbsolutePath;
        var simple = string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
        var user = simple && host == "weibo.cn" && System.Text.RegularExpressions.Regex.IsMatch(path, "^/u/[1-9][0-9]{4,19}$");
        var legacyPost = simple && host == "www.weibo.com" && System.Text.RegularExpressions.Regex.IsMatch(path, "^/detail/[1-9][0-9]{4,19}$");
        var service = simple && host == "open.weibo.com" && path == "/cli/plan";
        var post = host == "weibo.cn" && System.Text.RegularExpressions.Regex.IsMatch(path, "^/comment/[0-9A-Za-z]{1,12}$")
            && url.Fragment == "#cmtfrm" && ValidCommentQuery(url);
        var webPost = host == "weibo.com" && System.Text.RegularExpressions.Regex.IsMatch(path, "^/(?:[1-9][0-9]{4,19}/[0-9A-Za-z]{1,12}|detail/[1-9][0-9]{4,19})$")
            && url.Query == "?from=feed" && url.Fragment == "#comment";
        return user || legacyPost || service || post || webPost ? url.ToString() : null;
    }
    static bool ValidCommentQuery(Uri url) {
        var parsed = System.Web.HttpUtility.ParseQueryString(url.Query.Length > 0 ? url.Query.Substring(1) : "");
        var names = parsed.AllKeys;
        if (names == null || !System.Linq.Enumerable.Contains(names, "rl")) return false;
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var name in names) {
            if (!seen.Add(name)) return false;
            var value = parsed[name] ?? "";
            if (name == "rl") { if (value != "0" && value != "1") return false; }
            else if (name == "uid") { if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[1-9][0-9]{4,19}$")) return false; }
            else return false;
        }
        return true;
    }
    public static void OpenExternal(Window owner, string raw) {
        var url = ExternalUrl(raw);
        if (url == null) { MessageBox.Show("链接未通过安全校验，未打开", "Weibo Reader"); return; }
        try {
            var start = new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/d /s /c start \"\" \"" + url + "\"", UseShellExecute = false, CreateNoWindow = true };
            Process.Start(start);
        } catch (Exception error) { MessageBox.Show("无法打开微博链接：" + error.Message, "Weibo Reader"); }
    }
}
}
