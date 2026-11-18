using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WeiboReader.Views {
// 账户与设置页：资料目录、WeiboCLI 服务账户（credits/授权登录/查询余额）、
// 数据导出与导入、价格参考、维护清理与引擎日志。账户与 credits 事件由壳先行写入 State。
public class SettingsPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    TextBlock workspaceText;
    TextBlock creditsText;
    TextBlock loginStatusText;
    TextBox accountBox;
    TextBlock exchangeStatusText;
    TextBox priceBox;
    TextBox logBox;
    readonly List<string> logLines = new List<string>();
    DateTime lastLibraryRequest = DateTime.MinValue;

    public SettingsPage(MainWindow main) {
        Main = main;
        Build();
    }

    // ------------------------------------------------------------ build
    void Build() {
        var root = new StackPanel();

        var header = new StackPanel { Margin = new Thickness(0, 2, 0, 14) };
        header.Children.Add(new TextBlock { Text = "账户与设置", Foreground = Theme.Blue, FontSize = 11 });
        header.Children.Add(new TextBlock { Text = "账户与数据", Foreground = Theme.Text, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) });
        root.Children.Add(header);

        var engineText = new TextBlock {
            Text = "运行引擎未连接；账户、数据交换等操作需要引擎，请重新打开应用。",
            Foreground = Theme.Warn, FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(engineText);

        // ---- 资料目录
        var workspaceCard = Theme.Card();
        var workspaceBody = new StackPanel();
        workspaceBody.Children.Add(SectionTitle("资料目录"));
        workspaceBody.Children.Add(new TextBlock { Text = "切换目录不会移动或删除旧数据。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        workspaceText = new TextBlock { Text = "—", Foreground = Theme.Blue, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        workspaceBody.Children.Add(workspaceText);
        var workspaceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var pickButton = SecondaryButton("切换资料目录");
        pickButton.Click += (s, e) => { if (Main != null) Main.PickWorkspace(); };
        workspaceRow.Children.Add(pickButton);
        workspaceBody.Children.Add(workspaceRow);
        workspaceCard.Child = workspaceBody;
        root.Children.Add(workspaceCard);

        // ---- 服务账户
        var accountCard = Theme.Card();
        var accountBody = new StackPanel();
        accountBody.Children.Add(SectionTitle("WeiboCLI 服务账户"));
        accountBody.Children.Add(new TextBlock { Text = "用于通过 WeiboCLI 获取数据。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        creditsText = new TextBlock { Text = "credits —", Foreground = Theme.Accent, FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) };
        accountBody.Children.Add(creditsText);
        loginStatusText = new TextBlock { Text = "尚未登录或未查询。", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        accountBody.Children.Add(loginStatusText);
        accountBox = ReadOnlyBox(110);
        accountBox.Margin = new Thickness(0, 10, 0, 0);
        accountBox.Text = "尚未查询账户信息。查询余额不会读取微博内容。";
        accountBody.Children.Add(accountBox);
        var accountRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var loginButton = PrimaryButton("授权登录");
        loginButton.Click += (s, e) => SendLogin();
        accountRow.Children.Add(loginButton);
        var checkButton = SecondaryButton("查询余额");
        checkButton.Margin = new Thickness(8, 0, 0, 0);
        checkButton.Click += (s, e) => SendAccount();
        accountRow.Children.Add(checkButton);
        var cancelButton = TextButton("授权取消", Theme.Muted);
        cancelButton.VerticalAlignment = VerticalAlignment.Center;
        cancelButton.Click += (s, e) => SendLoginCancel();
        accountRow.Children.Add(cancelButton);
        accountBody.Children.Add(accountRow);
        accountBody.Children.Add(new TextBlock { Text = "在浏览器完成授权，应用不保存密码。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        accountCard.Child = accountBody;
        root.Children.Add(accountCard);

        // ---- 数据导出/导入
        var exchangeCard = Theme.Card();
        var exchangeBody = new StackPanel();
        exchangeBody.Children.Add(SectionTitle("本地数据与报告"));
        exchangeBody.Children.Add(new TextBlock { Text = "导出可合并的数据包，0 credits。不含登录信息、订阅、私人备注或已读状态。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        var exchangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var exportButton = SecondaryButton("导出数据包…");
        exportButton.Click += (s, e) => ExportData();
        exchangeRow.Children.Add(exportButton);
        var importButton = SecondaryButton("导入数据包…");
        importButton.Margin = new Thickness(8, 0, 0, 0);
        importButton.Click += (s, e) => ImportData();
        exchangeRow.Children.Add(importButton);
        exchangeBody.Children.Add(exchangeRow);
        exchangeStatusText = new TextBlock { Text = "", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        exchangeBody.Children.Add(exchangeStatusText);
        exchangeCard.Child = exchangeBody;
        root.Children.Add(exchangeCard);

        // ---- 价格参考
        var priceCard = Theme.Card();
        var priceBody = new StackPanel();
        priceBody.Children.Add(SectionTitle("费用参考 · credits 价目表"));
        priceBody.Children.Add(new TextBlock { Text = "本地查看与导出：0 credits。联网取数按接口计费，发出和收到分别计算。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        var priceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var priceButton = SecondaryButton("查看价格");
        priceButton.Click += (s, e) => SendPricing();
        priceRow.Children.Add(priceButton);
        priceBody.Children.Add(priceRow);
        priceBox = ReadOnlyBox(150);
        priceBox.Margin = new Thickness(0, 10, 0, 0);
        priceBox.FontFamily = new FontFamily("Consolas");
        priceBox.Text = "优先显示本地已确认价格；点击「查看价格」读取。";
        priceBody.Children.Add(priceBox);
        priceCard.Child = priceBody;
        root.Children.Add(priceCard);

        // ---- 维护
        var cleanupCard = Theme.Card();
        var cleanupBody = new StackPanel();
        cleanupBody.Children.Add(SectionTitle("维护"));
        cleanupBody.Children.Add(new TextBlock { Text = "所有数据保存在本机；报告位于 output/ 目录。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        cleanupBody.Children.Add(new TextBlock { Text = "每位用户或博文保留最新 1 份报告。只清理可再生成的旧报告，原始数据、历史与续读进度保留。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var cleanupRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var cleanupButton = SecondaryButton("清理报告");
        cleanupButton.Click += (s, e) => SendCleanup();
        cleanupRow.Children.Add(cleanupButton);
        cleanupBody.Children.Add(cleanupRow);
        cleanupCard.Child = cleanupBody;
        root.Children.Add(cleanupCard);

        // ---- 引擎日志
        var logCard = Theme.Card();
        var logBody = new StackPanel();
        logBody.Children.Add(SectionTitle("引擎日志"));
        logBody.Children.Add(new TextBlock { Text = "引擎运行日志；最多保留 200 行，超出丢弃最旧。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        logBox = ReadOnlyBox(190);
        logBox.Margin = new Thickness(0, 8, 0, 0);
        logBox.FontFamily = new FontFamily("Consolas");
        logBox.Text = "暂无引擎日志。";
        logBody.Children.Add(logBox);
        logCard.Child = logBody;
        root.Children.Add(logCard);

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };
        Content = scroll;
        UpdateEngineHint(engineText);
        RefreshFromState();
    }

    TextBlock SectionTitle(string text) {
        return new TextBlock { Text = text, Foreground = Theme.Text, FontSize = 14, FontWeight = FontWeights.SemiBold };
    }

    TextBox ReadOnlyBox(double height) {
        return new TextBox {
            IsReadOnly = true, Height = height,
            Background = Theme.Bg, Foreground = Theme.Text, BorderBrush = Theme.PanelBorderBrush,
            CaretBrush = Theme.Text, Padding = new Thickness(8), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    // ------------------------------------------------------------ actions
    void SendAccount() {
        if (Main == null) return;
        var payload = Json.Obj();
        Json.Put(payload, "loginRequestId", Guid.NewGuid().ToString());
        Main.Send("account", payload);
        SetLoginStatus("正在查询账户与余额…");
    }

    void SendLogin() {
        if (Main == null) return;
        Main.Send("login", Json.Obj());
        SetLoginStatus("已发起授权登录，请在浏览器完成；完成后可查询余额确认。");
    }

    void SendLoginCancel() {
        if (Main == null) return;
        Main.Send("loginCancel", Json.Obj());
        SetLoginStatus("已请求取消授权登录。");
    }

    void SendPricing() {
        if (Main == null) return;
        Main.Send("pricingReference", Json.Obj());
    }

    void SendCleanup() {
        if (Main == null) return;
        Main.Send("cleanup", Json.Obj());
        Main.Toast("正在清理旧报告…", false);
    }

    void ExportData() {
        if (Main == null) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Dialogs.ChooseFile(Main, true, "WeiboReader-数据-" + stamp + ".wrdpack");
        if (string.IsNullOrEmpty(path)) return;
        var payload = Json.Obj();
        Json.Put(payload, "workspace", Main.State.Workspace);
        Json.Put(payload, "path", path);
        Json.Put(payload, "selection", "all");
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("exchangeExport", payload);
        SetExchangeStatus("正在整理并保存数据包…");
    }

    void ImportData() {
        if (Main == null) return;
        var path = Dialogs.ChooseFile(Main, false, null);
        if (string.IsNullOrEmpty(path)) return;
        var payload = Json.Obj();
        Json.Put(payload, "workspace", Main.State.Workspace);
        Json.Put(payload, "path", path);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("exchangeInspect", payload);
        SetExchangeStatus("正在检查数据包及文件校验值…");
    }

    // ------------------------------------------------------------ events
    public void OnEngineEvent(Dictionary<string, object> e) {
        if (Main == null || e == null) return;
        var type = Json.S(e, "type");
        if (type == "library") {
            RefreshFromState();
        } else if (type == "credits") {
            RefreshCredits();
            SetLoginStatus("余额已更新：" + Main.State.Credits.ToString("N0") + " credits。");
        } else if (type == "account") {
            ShowAccount(e);
            SetLoginStatus("服务账户已核实。");
        } else if (type == "accountError") {
            var message = Json.S(e, "message");
            var text = "查询失败：" + (message.Length > 0 ? message : "未知原因");
            if (Json.B(e, "authenticationNeeded")) text += "\n需要登录授权：请点击「授权登录」后重试。";
            accountBox.Text = text;
            SetLoginStatus("账户查询失败。");
        } else if (type == "accountLoading") {
            if (Json.B(e, "loading")) SetLoginStatus("正在查询账户与余额…");
        } else if (type == "finished" && Json.S(e, "kind") == "login") {
            SetLoginStatus(Json.N(e, "code") == 0 ? "授权登录完成。" : "授权登录未完成；可重新发起或查询余额确认。");
        } else if (type == "log") {
            AppendLog(Json.S(e, "message"));
        } else if (type == "pricingReference") {
            priceBox.Text = Json.Serialize(e);
        } else if (type == "pricingError") {
            var message = Json.S(e, "message");
            Main.Toast(message.Length > 0 ? message : "价格查询未完成；已保存价格保留。", true);
        } else if (type == "exchangeExported") {
            var file = Json.S(e, "file");
            var message = "数据包已保存：" + (file.Length > 0 ? file : "已完成") + " · 新增 0 credits";
            Main.Toast(message, false);
            SetExchangeStatus(message);
        } else if (type == "exchangeImported") {
            string message;
            if (Json.B(e, "noChanges")) message = "数据已存在，无需重复导入。新增 0 credits。";
            else message = "合并完成：新增 " + Json.N(e, "added") + " 条，重复 " + Json.N(e, "duplicate") + " 条，保留 " + Json.N(e, "conflicts") + " 项版本差异。新增 0 credits。" + (Json.B(e, "partial") ? " 部分进度未复用，正文已保留。" : "");
            Main.Toast(message, false);
            SetExchangeStatus(message);
        } else if (type == "exchangeInspected") {
            var summary = Json.M(e.ContainsKey("summary") ? e["summary"] : null);
            long count = 0;
            if (summary != null && summary.ContainsKey("objectCount")) count = Json.N(summary, "objectCount");
            else count = Json.L(e, "objects").Count;
            Main.Toast("数据包校验完成：涉及 " + count + " 个对象。", false);
            SetExchangeStatus("数据包校验完成：涉及 " + count + " 个对象。确认合并请使用网页端；原生端本次未修改数据。");
        } else if (type == "exchangeResult") {
            var message = Json.S(e, "message");
            if (message.Length == 0) message = "数据交换完成。";
            Main.Toast(message, false);
            SetExchangeStatus(message);
        } else if (type == "exchangeError") {
            var message = Json.S(e, "message");
            if (message.Length == 0) message = "数据包处理未完成。";
            Main.Toast(message, true);
            SetExchangeStatus(message);
        } else if (type == "exchangeCancelled") {
            Main.Toast("已取消，没有修改本地数据。", false);
            SetExchangeStatus("已取消，没有修改本地数据。");
        } else if (type == "cleanup") {
            var removed = Json.N(e, "removed");
            var skipped = Json.N(e, "skipped");
            var message = "清理完成：移除 " + removed.ToString("N0") + " 份旧报告，保留 " + skipped.ToString("N0") + " 份。";
            Main.Toast(message, false);
        } else if (type == "error") {
            var message = Json.S(e, "message");
            if (message.Length > 0) Main.Toast(message, true);
        }
        // 其余未知事件忽略。
    }

    public void OnShown() {
        if (Main == null) return;
        RefreshFromState();
        var now = DateTime.UtcNow;
        if ((now - lastLibraryRequest).TotalSeconds >= 3) {
            lastLibraryRequest = now;
            Main.Send("library", null);
        }
    }

    // ------------------------------------------------------------ state refresh
    void RefreshFromState() {
        if (Main == null) return;
        workspaceText.Text = Main.State.Workspace.Length > 0 ? Main.State.Workspace : "—";
        RefreshCredits();
        if (Main.State.AccountText.Length > 0) accountBox.Text = Main.State.AccountText;
    }

    void RefreshCredits() {
        creditsText.Text = Main.State.Credits > 0 ? Main.State.Credits.ToString("N0") + " credits" : "credits —";
    }

    void ShowAccount(Dictionary<string, object> e) {
        var text = Main.State.AccountText;
        if (string.IsNullOrEmpty(text)) {
            var name = Json.S(e, "username");
            string balance = "暂不可用";
            if (e.ContainsKey("balance") && e["balance"] != null) balance = Json.N(e, "balance").ToString("N0") + " credits";
            var service = Json.S(e, "service");
            text = "账户：" + (name.Length > 0 ? name : "未提供")
                 + "\n余额：" + balance
                 + "\n服务状态：" + (service.Length > 0 ? ServiceText(service) : "未提供");
        }
        accountBox.Text = text;
    }

    string ServiceText(string service) {
        if (service == "formal_active") return "正式服务";
        if (service == "trial_active") return "体验服务";
        return service;
    }

    void SetLoginStatus(string text) {
        loginStatusText.Text = text;
    }

    void SetExchangeStatus(string text) {
        exchangeStatusText.Text = text;
    }

    void AppendLog(string line) {
        if (string.IsNullOrEmpty(line)) return;
        logLines.Add(DateTime.Now.ToString("HH:mm:ss ") + line);
        if (logLines.Count > 200) logLines.RemoveRange(0, logLines.Count - 200);
        logBox.Text = string.Join("\n", logLines.ToArray());
        logBox.ScrollToEnd();
    }

    void UpdateEngineHint(TextBlock hint) {
        var alive = Main != null && Main.Engine != null && Main.Engine.Alive;
        hint.Visibility = alive ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------ shared controls
    static Style DarkButtonStyle(Brush hover) {
        var style = new Style(typeof(Button));
        var template = new ControlTemplate(typeof(Button));
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.Name = "Chrome";
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        chrome.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        presenter.SetBinding(FrameworkElement.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.AppendChild(presenter);
        template.VisualTree = chrome;
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Chrome"));
        template.Triggers.Add(over);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Chrome"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Border.OpacityProperty, 0.45, "Chrome"));
        template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(12, 6, 12, 6)));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Button.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    Button StyledButton(string text, Brush foreground, Brush background, Brush borderBrush, Brush hover, Thickness padding) {
        var button = new Button {
            Content = text,
            Foreground = foreground,
            Background = background,
            BorderBrush = borderBrush,
            Padding = padding,
            Style = DarkButtonStyle(hover),
        };
        return button;
    }

    Button PrimaryButton(string text) {
        return StyledButton(text, Theme.Bg, Theme.Accent, Theme.Accent, Theme.AccentDimBrush, new Thickness(16, 7, 16, 7));
    }

    Button SecondaryButton(string text) {
        return StyledButton(text, Theme.Text, Theme.Panel, Theme.PanelBorderBrush, Theme.Hover, new Thickness(14, 6, 14, 6));
    }

    Button TextButton(string text, Brush color) {
        return StyledButton(text, color, Brushes.Transparent, Brushes.Transparent, Theme.Hover, new Thickness(10, 4, 10, 4));
    }
}
}
