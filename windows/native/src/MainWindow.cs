using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;

namespace WeiboReader {
// Main window: dark shell with a left sidebar, a header showing credits and a
// content frame. Owns the engine lifecycle and routes events to the active
// page (IEnginePage) and into AppState. Pages are code-behind UserControls.
public interface IEnginePage {
    void OnEngineEvent(Dictionary<string, object> e);
    void OnShown();
}

public partial class MainWindow : Window {
    public static MainWindow Instance;
    public readonly AppState State = new AppState();
    public EngineClient Engine;
    readonly Dictionary<string, IEnginePage> pages = new Dictionary<string, IEnginePage>();
    readonly Dictionary<string, DateTime> lastShownAt = new Dictionary<string, DateTime>();
    string currentPageKey = "";
    TextBlock creditsText;
    TextBlock toastText;
    Border toastHost;
    Grid contentHost;
    System.Windows.Threading.DispatcherTimer toastTimer;

    public MainWindow() {
        Instance = this;
        Title = "Weibo Reader";
        Width = 1240; Height = 850;
        MinWidth = 980; MinHeight = 700;
        Background = Theme.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildLayout();
        SourceInitialized += (s, e) => StartEngine();
        Closed += (s, e) => { if (Engine != null) Engine.Shutdown(); };
    }
    void BuildLayout() {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border { Background = Theme.Panel, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(0, 0, 1, 0) };
        var side = new Grid { Margin = new Thickness(0, 18, 0, 12) };
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var brand = new StackPanel { Margin = new Thickness(14, 0, 12, 18) };
        brand.Children.Add(new TextBlock { Text = "Weibo Reader", Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.Bold });
        brand.Children.Add(new TextBlock { Text = "原生 Windows 端", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        side.Children.Add(brand);
        var nav = new StackPanel();
        nav.Children.Add(NavButton("盯单人", "users"));
        nav.Children.Add(NavButton("盯版聊", "posts"));
        nav.Children.Add(NavButton("持续盯防", "subscriptions"));
        nav.Children.Add(NavButton("账户与设置", "settings"));
        Grid.SetRow(nav, 1);
        side.Children.Add(nav);
        var foot = new TextBlock { Text = "保存在本机", Foreground = Theme.Ok, FontSize = 11, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(foot, 2);
        side.Children.Add(foot);
        sidebar.Child = side;
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var main = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var headerTitle = new TextBlock { Text = "Weibo Reader", Foreground = Theme.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        creditsText = new TextBlock { Foreground = Theme.Accent, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Text = "credits —" };
        header.Children.Add(headerTitle);
        header.Children.Add(creditsText);
        Grid.SetRow(header, 0);
        main.Children.Add(header);
        contentHost = new Grid();
        Grid.SetRow(contentHost, 1);
        main.Children.Add(contentHost);

        toastHost = new Border {
            Background = Theme.Panel, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed, MaxWidth = 560,
        };
        toastText = new TextBlock { Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
        toastHost.Child = toastText;
        Grid.SetRow(toastHost, 1);
        toastHost.SetValue(Grid.ZIndexProperty, 50);
        main.Children.Add(toastHost);

        var host = new Border { Child = main };
        Grid.SetColumn(host, 1);
        root.Children.Add(host);
        Content = root;
        toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        toastTimer.Tick += (s, e) => { toastTimer.Stop(); toastHost.Visibility = Visibility.Collapsed; };
    }
    Button NavButton(string label, string key) {
        var button = new Button { Content = label, Style = Theme.SidebarButtonStyle(), Tag = key };
        button.Click += (s, e) => Navigate(key);
        return button;
    }

    // ------------------------------------------------------------ lifecycle
    void StartEngine() {
        if (string.IsNullOrEmpty(Program.Workspace)) Program.Workspace = DefaultWorkspace();
        Engine = new EngineClient();
        Engine.Event += OnEngineEventRaw;
        Engine.EngineLog += message => Program.Log("bridge: " + message);
        Engine.EngineExited += () => Dispatcher.BeginInvoke((Action)(() => {
            if (State.EngineStoppedShown) return;
            State.EngineStoppedShown = true;
            Toast("运行引擎已结束，请重新打开应用。", true);
        }));
        Engine.Start(Program.Workspace, true);
        Engine.Send("bootstrap", Json.Obj());
    }
    static string DefaultWorkspace() {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = System.IO.Path.Combine(appData, "selnokk.weibo.reader");
        try { System.IO.Directory.CreateDirectory(dir); } catch {}
        return dir;
    }

    // ------------------------------------------------------------ events
    void OnEngineEventRaw(Dictionary<string, object> e) {
        // Engine thread → UI thread.
        Dispatcher.BeginInvoke((Action)(() => OnEngineEvent(e)));
    }
    void OnEngineEvent(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        if (Program.LogEvents) Program.Log("event " + type);
        switch (type) {
            case "library": {
                State.Apply(e);
                creditsText.Text = State.Credits > 0 ? State.Credits.ToString("N0") + " credits" : "credits —";
                if (!State.BootstrapSent) {
                    State.BootstrapSent = true;
                    Engine.Send("library", null);
                }
                break;
            }
            case "open": {
                // Same interception as the macOS shell: the shell owns opening.
                var path = Json.S(e, "path");
                var kind = Json.S(e, "kind");
                var success = false;
                try {
                    if (kind == "folder") { Dialogs.OpenFolder(path); success = true; }
                    else if (System.IO.File.Exists(path)) { Dialogs.OpenReport(path); success = true; }
                } catch {}
                var reply = Json.Obj();
                Json.Put(reply, "id", Json.S(e, "id"));
                Json.Put(reply, "workspace", Json.S(e, "workspace"));
                if (e.ContainsKey("requestId")) Json.Put(reply, "requestId", Json.S(e, "requestId"));
                Json.Put(reply, "type", success ? "browserOpened" : "browserOpenFailed");
                if (!success) Json.Put(reply, "message", "无法打开报告");
                Deliver(reply);
                if (e.ContainsKey("handoffTicket") && success) {
                    var handoff = Json.Obj();
                    Json.Put(handoff, "token", Json.S(e, "handoffTicket"));
                    Json.Put(handoff, "success", true);
                    Json.Put(handoff, "requestId", Json.S(e, "handoffTicket"));
                    Engine.Send("reportHandoff", handoff);
                } else if (e.ContainsKey("unreadTicket") && success) {
                    var opened = Json.Obj();
                    Json.Put(opened, "token", Json.S(e, "unreadTicket"));
                    Engine.Send("reportOpened", opened);
                }
                return;
            }
            case "engineStopped":
            case "engineTransportError": {
                if (!State.EngineStoppedShown) {
                    State.EngineStoppedShown = true;
                    Toast(Json.S(e, "message") + "（运行引擎已结束，请重新打开应用。）", true);
                }
                return;
            }
        }
        if (type == "browserOpened" && State.PendingPairRequest.Length > 0 && Json.S(e, "requestId") == State.PendingPairRequest) {
            State.PendingPairRequest = "";
            Toast("互动档案已生成并在默认浏览器打开。", false);
            return;
        }
        if (type == "browserOpenFailed" && State.PendingPairRequest.Length > 0 && Json.S(e, "requestId") == State.PendingPairRequest) {
            State.PendingPairRequest = "";
            Toast(Json.S(e, "message") + "；档案已保存在 output\\pair\\。", true);
            return;
        }
        var changed = State.Apply(e);
        IEnginePage page;
        if (pages.TryGetValue(currentPageKey, out page)) page.OnEngineEvent(e);
        // Refreshed home data re-renders the active page. OnShown is throttled
        // per page: events the page itself triggered would otherwise loop
        // (library → OnShown → library) and spam the engine.
        if (changed && (currentPageKey == "users" || currentPageKey == "posts" || currentPageKey == "subscriptions" || currentPageKey == "settings")) {
            DateTime previous;
            var at = lastShownAt.TryGetValue(currentPageKey, out previous) ? previous : DateTime.MinValue;
            if ((DateTime.UtcNow - at).TotalMilliseconds >= 2000) {
                lastShownAt[currentPageKey] = DateTime.UtcNow;
                if (pages.TryGetValue(currentPageKey, out page)) page.OnShown();
            }
        }
    }
    void Deliver(Dictionary<string, object> e) {
        // Simulate an engine-originated event for page handling (post-open acks).
        OnEngineEvent(e);
    }

    // ------------------------------------------------------------ navigation
    public void Navigate(string key) {
        currentPageKey = key;
        contentHost.Children.Clear();
        IEnginePage page;
        if (!pages.TryGetValue(key, out page)) {
            var control = PageFactory.Build(key, this);
            pages[key] = (IEnginePage)control;
            contentHost.Children.Add(control);
        } else {
            contentHost.Children.Add((UIElement)page);
        }
        if (pages.TryGetValue(key, out page)) page.OnShown();
    }
    public void NavigateToUser(string uid) { Navigate("users"); var page = pages.ContainsKey("users") ? pages["users"] as Views.UserListPage : null; if (page != null) page.OpenDetail(uid); }
    public void NavigateToPost(string postId) { Navigate("posts"); var page = pages.ContainsKey("posts") ? pages["posts"] as Views.PostListPage : null; if (page != null) page.OpenDetail(postId); }

    // ------------------------------------------------------------ helpers for views
    public void Toast(string message, bool error) {
        toastText.Text = message;
        toastText.Foreground = error ? Theme.Warn : Theme.Text;
        toastHost.Visibility = Visibility.Visible;
        toastTimer.Stop(); toastTimer.Start();
        Program.Log("toast: " + message);
    }
    public void UpdateCredits() { creditsText.Text = State.Credits > 0 ? State.Credits.ToString("N0") + " credits" : "credits —"; }
    public void Send(string action, Dictionary<string, object> payload) { Engine.Send(action, payload); }
    public void PickWorkspace() {
        var chosen = Dialogs.ChooseFolder(this);
        if (string.IsNullOrEmpty(chosen)) return;
        var payload = Json.Obj();
        Json.Put(payload, "originWorkspace", State.Workspace);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Json.Put(payload, "path", chosen);
        Engine.Send("workspace", payload);
        Toast("已请求切换资料目录：" + chosen, false);
    }
}

public static class PageFactory {
    public static UIElement Build(string key, MainWindow main) {
        switch (key) {
            case "users": return new Views.UserListPage(main);
            case "posts": return new Views.PostListPage(main);
            case "subscriptions": return new Views.SubscriptionsPage(main);
            case "settings": return new Views.SettingsPage(main);
        }
        var empty = new TextBlock { Text = "未知页面：" + key, Foreground = Theme.Muted };
        return empty;
    }
}
}
