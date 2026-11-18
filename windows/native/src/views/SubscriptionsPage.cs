using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WeiboReader.Views {
// 持续盯防管理页：搜索 + 类型筛选 + 条目卡片（内嵌设置面板、暂停/恢复、两步删除）。
// 列表数据来自 Main.State.Follows（壳在 followState 事件后自动更新）。
public class SubscriptionsPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    readonly StackPanel listHost = new StackPanel();
    readonly TextBlock emptyText = new TextBlock {
        Text = "从盯单人或盯版聊页开启持续盯防。每日预算按持续盯防对象分别设置。",
        Foreground = Theme.Muted, FontSize = 13, Margin = new Thickness(4, 18, 4, 10), TextWrapping = TextWrapping.Wrap,
    };
    readonly TextBlock engineText = new TextBlock {
        Text = "运行引擎未连接，无法读取持续盯防状态；请重新打开应用。",
        Foreground = Theme.Warn, FontSize = 12, Margin = new Thickness(4, 0, 4, 10), TextWrapping = TextWrapping.Wrap,
    };
    readonly TextBox searchBox = new TextBox {
        MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center,
        Background = Theme.Bg, Foreground = Theme.Text, BorderBrush = Theme.PanelBorderBrush,
        CaretBrush = Theme.Text, Padding = new Thickness(10, 5, 10, 5), FontSize = 13,
    };
    readonly TextBlock searchWatermark = new TextBlock {
        Text = "搜索昵称、UID、微博 ID", Foreground = Theme.Muted, FontSize = 12,
        Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
    };
    readonly List<Button> chips = new List<Button>();
    // followState 原始条目（kind:id → 原始字典），保存设置时保留 mode/frequency 等不可编辑字段。
    readonly Dictionary<string, Dictionary<string, object>> rawFollows = new Dictionary<string, Dictionary<string, object>>();
    string filter = "all";
    string expandedKey = "";
    string pendingRemoveKey = "";
    DateTime lastListRequest = DateTime.MinValue;

    static readonly Dictionary<string, string> StatusNames = new Dictionary<string, string> {
        { "waiting", "订阅中" }, { "running", "检查中" }, { "paused", "已暂停" }, { "queued", "排队中" },
        { "budget-paused", "预算暂停" }, { "error-paused", "异常暂停" }, { "retry-wait", "等待重试" },
        { "attention-required", "等待重试" }, { "billing-paused", "费用待核对" },
    };

    public SubscriptionsPage(MainWindow main) {
        Main = main;
        Build();
    }

    // ------------------------------------------------------------ build
    void Build() {
        var root = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 2, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "持续盯防", Foreground = Theme.Blue, FontSize = 11 });
        titles.Children.Add(new TextBlock { Text = "管理持续盯防", Foreground = Theme.Text, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) });
        titles.Children.Add(new TextBlock { Text = "关闭窗口后持续盯防继续运行；退出应用后停止。", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(titles, 0);
        header.Children.Add(titles);
        var refresh = SecondaryButton("刷新状态");
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Click += (s, e) => RequestList(true);
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var toolbar = Theme.Card();
        var toolbarBody = new StackPanel();
        var searchHost = new Grid();
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(searchWatermark);
        toolbarBody.Children.Add(searchHost);
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        AddChip(chipRow, "全部", "all");
        AddChip(chipRow, "盯单人", "user");
        AddChip(chipRow, "盯版聊", "post");
        UpdateChips();
        toolbarBody.Children.Add(chipRow);
        toolbar.Child = toolbarBody;
        root.Children.Add(toolbar);

        root.Children.Add(engineText);
        root.Children.Add(listHost);
        root.Children.Add(emptyText);

        var scroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };
        Content = scroll;
        searchBox.TextChanged += (s, e) => {
            searchWatermark.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderList();
        };
        searchWatermark.Visibility = Visibility.Visible;
        emptyText.Visibility = Visibility.Collapsed;
        UpdateEngineHint();
    }

    void AddChip(StackPanel host, string label, string value) {
        var chip = StyledButton(label, Theme.Muted, Brushes.Transparent, Theme.PanelBorderBrush, Theme.Hover, new Thickness(14, 5, 14, 5), HorizontalAlignment.Center);
        chip.Margin = new Thickness(0, 0, 8, 0);
        chip.Tag = value;
        chip.Click += (s, e) => { filter = value; UpdateChips(); RenderList(); };
        chips.Add(chip);
        host.Children.Add(chip);
    }

    void UpdateChips() {
        foreach (var chip in chips) {
            var active = (string)chip.Tag == filter;
            chip.Foreground = active ? Theme.Accent : Theme.Muted;
            chip.BorderBrush = active ? Theme.Accent : Theme.PanelBorderBrush;
            chip.Background = active ? Theme.Hover : Brushes.Transparent;
            chip.FontWeight = active ? FontWeights.SemiBold : FontWeights.Regular;
        }
    }

    // ------------------------------------------------------------ rendering
    void RenderList() {
        if (Main == null) return;
        listHost.Children.Clear();
        var term = searchBox.Text.Trim().ToLowerInvariant();
        var rows = new List<FollowRow>();
        foreach (var row in Main.State.Follows) {
            if (filter == "user" && row.TargetKind != "user") continue;
            if (filter == "post" && row.TargetKind != "post") continue;
            if (term.Length > 0) {
                var hay = (row.Uid + " " + row.PostId + " " + row.Nickname + " " + TitleFor(row)).ToLowerInvariant();
                if (hay.IndexOf(term, StringComparison.Ordinal) < 0) continue;
            }
            rows.Add(row);
        }
        foreach (var row in rows) listHost.Children.Add(BuildCard(row));
        emptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    Border BuildCard(FollowRow row) {
        var key = FollowKey(row);
        var card = new Border {
            Background = Theme.Panel, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10),
        };
        var body = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel();
        identity.Children.Add(new TextBlock { Text = row.TargetKind == "post" ? "盯版聊评论" : "盯单人互动", Foreground = Theme.Blue, FontSize = 11 });
        identity.Children.Add(new TextBlock { Text = TitleFor(row), Foreground = Theme.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        identity.Children.Add(new TextBlock { Text = (row.TargetKind == "post" ? "微博 ID " : "UID ") + (row.TargetKind == "post" ? row.PostId : row.Uid), Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(identity, 0);
        head.Children.Add(identity);
        var badge = new Border {
            Background = Brushes.Transparent, BorderBrush = row.Enabled ? Theme.Ok : Theme.Warn,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = row.Enabled ? "持续盯防中" : "已暂停", Foreground = row.Enabled ? Theme.Ok : Theme.Warn, FontSize = 11 },
        };
        Grid.SetColumn(badge, 1);
        head.Children.Add(badge);
        body.Children.Add(head);

        var facts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        AddFact(facts, "下次检查", NextCheckText(row.NextCheckAt));
        AddFact(facts, "模式", ModeText(row.Mode));
        AddFact(facts, "本期额度", row.DailyBudget.ToString("N0") + " credits");
        AddFact(facts, "未读", row.Unread.ToString("N0") + (row.TargetKind == "post" ? " 条未读评论" : " 条未读互动"));
        if (row.Status.Length > 0) AddFact(facts, "状态", StatusText(row.Status));
        body.Children.Add(facts);
        if (row.Reason.Length > 0) {
            body.Children.Add(new TextBlock { Text = "说明：" + row.Reason, Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var settingsButton = TextButton("设置", Theme.Blue);
        settingsButton.Click += (s, e) => { expandedKey = expandedKey == key ? "" : key; pendingRemoveKey = ""; RenderList(); };
        actions.Children.Add(settingsButton);
        var toggleButton = TextButton(row.Enabled ? "暂停" : "恢复", row.Enabled ? Theme.Warn : Theme.Ok);
        toggleButton.Click += (s, e) => SendToggle(row);
        actions.Children.Add(toggleButton);
        Button removeButton = null;
        removeButton = TextButton(pendingRemoveKey == key ? "确认删除？" : "删除订阅", Theme.Danger);
        removeButton.Click += (s, e) => {
            if (pendingRemoveKey == key) {
                pendingRemoveKey = "";
                removeButton.Content = "删除订阅";
                SendRemove(row);
            } else {
                pendingRemoveKey = key;
                removeButton.Content = "确认删除？";
            }
        };
        actions.Children.Add(removeButton);
        body.Children.Add(actions);

        if (expandedKey == key) body.Children.Add(BuildSettingsPanel(row));
        card.Child = body;
        return card;
    }

    Border BuildSettingsPanel(FollowRow row) {
        var panel = new Border {
            Background = Theme.Bg, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0),
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "持续盯防设置", Foreground = Theme.Text, FontSize = 13, FontWeight = FontWeights.SemiBold });

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var intervalBox = NumberBox(row.Interval > 0 ? row.Interval.ToString(CultureInfo.InvariantCulture) : "15");
        var roundBox = NumberBox(row.RoundLimit > 0 ? row.RoundLimit.ToString(CultureInfo.InvariantCulture) : "100");
        var budgetBox = NumberBox(row.DailyBudget > 0 ? row.DailyBudget.ToString(CultureInfo.InvariantCulture) : "1000");
        grid.Children.Add(LabeledInput("间隔分钟（1–1440）", intervalBox, 0));
        grid.Children.Add(LabeledInput("每轮条数上限", roundBox, 1));
        grid.Children.Add(LabeledInput("每日额度（credits）", budgetBox, 2));
        body.Children.Add(grid);

        var enabledCheck = new CheckBox {
            Content = "启用持续盯防", Foreground = Theme.Text, FontSize = 12,
            IsChecked = row.Enabled, Margin = new Thickness(0, 12, 0, 0), Cursor = Cursors.Hand,
        };
        body.Children.Add(enabledCheck);
        body.Children.Add(new TextBlock { Text = "保存后从下一轮检查生效；每日额度按持续盯防对象分别设置。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var saveButton = PrimaryButton("保存设置");
        saveButton.Click += (s, e) => SendSave(row, intervalBox.Text, roundBox.Text, budgetBox.Text, enabledCheck.IsChecked == true);
        actions.Children.Add(saveButton);
        var cancelButton = TextButton("取消", Theme.Muted);
        cancelButton.Click += (s, e) => { expandedKey = ""; RenderList(); };
        actions.Children.Add(cancelButton);
        body.Children.Add(actions);

        panel.Child = body;
        return panel;
    }

    StackPanel LabeledInput(string label, TextBox box, int column) {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        Grid.SetColumn(panel, column);
        return panel;
    }

    TextBox NumberBox(string text) {
        return new TextBox {
            Text = text, Background = Theme.Bg, Foreground = Theme.Text, BorderBrush = Theme.PanelBorderBrush,
            CaretBrush = Theme.Text, Padding = new Thickness(8, 5, 8, 5), FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
    }

    // ------------------------------------------------------------ actions
    void RequestList(bool force) {
        if (Main == null) return;
        var now = DateTime.UtcNow;
        if (!force && (now - lastListRequest).TotalSeconds < 3) return;
        lastListRequest = now;
        Main.Send("followList", null);
    }

    void SendToggle(FollowRow row) {
        if (Main == null) return;
        var payload = Json.Obj();
        if (row.TargetKind == "post") Json.Put(payload, "postId", row.PostId);
        else Json.Put(payload, "uid", row.Uid);
        Json.Put(payload, "targetKind", row.TargetKind);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send(row.Enabled ? "followPause" : "followResume", payload);
        Main.Toast(row.Enabled ? "已请求暂停持续盯防。" : "已请求恢复持续盯防。", false);
        pendingRemoveKey = "";
    }

    void SendRemove(FollowRow row) {
        if (Main == null) return;
        var payload = Json.Obj();
        if (row.TargetKind == "post") Json.Put(payload, "postId", row.PostId);
        else Json.Put(payload, "uid", row.Uid);
        Json.Put(payload, "targetKind", row.TargetKind);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("followRemove", payload);
        Main.Toast("已请求删除持续盯防；数据和费用记录保留。", false);
    }

    void SendSave(FollowRow row, string intervalText, string roundText, string budgetText, bool enabled) {
        if (Main == null) return;
        long interval;
        if (!long.TryParse(intervalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) || interval < 1 || interval > 1440) {
            Main.Toast("检查间隔请填写 1–1440 分钟", true);
            return;
        }
        long roundLimit;
        if (!long.TryParse(roundText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out roundLimit) || roundLimit < 1) {
            Main.Toast("单轮取数上限必须为正整数", true);
            return;
        }
        double budget;
        if (!double.TryParse(budgetText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out budget) || budget <= 0) {
            Main.Toast("每日 credit 预算必须为有效正数", true);
            return;
        }
        var isPost = row.TargetKind == "post";
        var mode = ModeOf(row);
        if (mode == "both" && roundLimit < 2) {
            Main.Toast("单轮取数上限必须为正整数，双向至少为 2", true);
            return;
        }
        // settings 复制 followState 原始条目并覆盖可编辑字段，保留 mode/frequency 等。
        var settings = RawClone(row);
        settings["targetKind"] = row.TargetKind;
        settings["uid"] = isPost ? row.PostId : row.Uid;
        if (isPost) settings["postId"] = row.PostId;
        if (string.IsNullOrEmpty(mode)) settings["mode"] = isPost ? "post-comments" : "both";
        settings["interval"] = interval;
        settings["roundLimit"] = roundLimit;
        settings["dailyBudget"] = budget;
        settings["enabled"] = enabled;
        var payload = Json.Obj();
        Json.Put(payload, isPost ? "postId" : "uid", isPost ? row.PostId : row.Uid);
        Json.Put(payload, "targetKind", row.TargetKind);
        Json.Put(payload, "scope", isPost ? "post" : "user");
        Json.Put(payload, "settings", settings);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("followSave", payload);
        expandedKey = "";
        pendingRemoveKey = "";
        Main.Toast("已保存持续盯防设置。", false);
        RenderList();
    }

    Dictionary<string, object> RawClone(FollowRow row) {
        Dictionary<string, object> raw;
        if (rawFollows.TryGetValue(FollowKey(row), out raw) && raw != null) return new Dictionary<string, object>(raw);
        return Json.Obj();
    }

    string ModeOf(FollowRow row) {
        Dictionary<string, object> raw;
        if (rawFollows.TryGetValue(FollowKey(row), out raw) && raw != null) {
            var mode = Json.S(raw, "mode");
            if (mode.Length > 0) return mode;
        }
        if (row.Mode.Length > 0) return row.Mode;
        return "";
    }

    // ------------------------------------------------------------ events
    public void OnEngineEvent(Dictionary<string, object> e) {
        if (Main == null || e == null) return;
        var type = Json.S(e, "type");
        if (type == "followState") {
            CaptureRawEntries(e);
            RenderList();
        } else if (type == "followRemoved") {
            Main.Toast(Json.B(e, "pending") ? "当前请求保存后会移除持续盯防，数据和费用记录保留。" : "持续盯防已删除，数据和费用记录保留。", false);
        } else if (type == "followError") {
            var message = Json.S(e, "message");
            if (message.Length > 0) Main.Toast(message, true);
        }
        // 其余未知事件忽略。
    }

    void CaptureRawEntries(Dictionary<string, object> e) {
        foreach (var item in Json.L(e, "entries")) {
            var entry = Json.M(item);
            if (entry == null) continue;
            var kind = Json.S(entry, "targetKind");
            if (string.IsNullOrEmpty(kind)) kind = "user";
            var id = kind == "post" ? Json.S(entry, "postId") : Json.S(entry, "uid");
            rawFollows[kind + ":" + id] = entry;
        }
    }

    public void OnShown() {
        if (Main == null) return;
        UpdateEngineHint();
        RenderList();
        RequestList(false);
    }

    void UpdateEngineHint() {
        var alive = Main != null && Main.Engine != null && Main.Engine.Alive;
        engineText.Visibility = alive ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------ helpers
    string FollowKey(FollowRow row) {
        return (row.TargetKind == "post" ? "post:" + row.PostId : "user:" + row.Uid);
    }

    string TitleFor(FollowRow row) {
        if (row.TargetKind == "post") {
            if (Main != null) {
                var post = Main.State.FindPost(row.PostId);
                if (post != null && post.Title.Length > 0) return post.Title;
            }
            if (row.Nickname.Length > 0) return row.Nickname;
            return "微博 " + row.PostId;
        }
        if (row.Nickname.Length > 0) return row.Nickname;
        return Main != null ? Main.State.DisplayName(row.Uid, "") : "用户 " + row.Uid;
    }

    string NextCheckText(string raw) {
        if (string.IsNullOrEmpty(raw)) return "未安排";
        DateTime at;
        if (DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out at)) return at.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return raw;
    }

    string ModeText(string mode) {
        if (mode == "both") return "双向评论";
        if (mode == "sent") return "发出的评论";
        if (mode == "received") return "收到的评论";
        if (mode == "post-comments") return "博文评论";
        if (mode.Length == 0) return "默认";
        return mode;
    }

    string StatusText(string status) {
        string name;
        if (StatusNames.TryGetValue(status, out name)) return name;
        return status;
    }

    void AddFact(StackPanel host, string label, string value) {
        var box = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
        box.Children.Add(new TextBlock { Text = label + "  ", Foreground = Theme.Muted, FontSize = 12 });
        box.Children.Add(new TextBlock { Text = value, Foreground = Theme.Text, FontSize = 12, FontWeight = FontWeights.Medium });
        host.Children.Add(box);
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

    Button StyledButton(string text, Brush foreground, Brush background, Brush borderBrush, Brush hover, Thickness padding, HorizontalAlignment contentAlignment) {
        var button = new Button {
            Content = text,
            Foreground = foreground,
            Background = background,
            BorderBrush = borderBrush,
            Padding = padding,
            HorizontalContentAlignment = contentAlignment,
            Style = DarkButtonStyle(hover),
        };
        return button;
    }

    Button PrimaryButton(string text) {
        return StyledButton(text, Theme.Bg, Theme.Accent, Theme.Accent, Theme.AccentDimBrush, new Thickness(16, 7, 16, 7), HorizontalAlignment.Left);
    }

    Button SecondaryButton(string text) {
        return StyledButton(text, Theme.Text, Theme.Panel, Theme.PanelBorderBrush, Theme.Hover, new Thickness(14, 6, 14, 6), HorizontalAlignment.Left);
    }

    Button TextButton(string text, Brush color) {
        return StyledButton(text, color, Brushes.Transparent, Brushes.Transparent, Theme.Hover, new Thickness(10, 4, 10, 4), HorizontalAlignment.Left);
    }
}
}
