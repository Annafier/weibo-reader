using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WeiboReader.Views {
// 盯单人详情页：头部（返回/头像/昵称/未读/主页/备注/删除）、持续盯防设置、
// 获取数据表单与任务进度卡、导出报告、最近报告列表、互动档案入口。
// 由 UserListPage 承载并在引擎事件到来时转发；只调用 Main.Send/State/Dialogs 骨架 API。
public class UserDetailPage : UserControl, IEnginePage {
    readonly MainWindow Main;

    string uid = "";
    bool viewedSent;
    string currentNote = "";
    // 备注在 State 里不缓存，从 homeUsers 原始事件按 uid 留存。
    readonly Dictionary<string, string> noteByUid = new Dictionary<string, string>();

    // 头部
    TextBlock nameText;
    TextBlock uidText;
    TextBlock profileText;
    TextBlock avatarChar;
    Button unreadButton;
    Button openProfileButton;
    Button deleteButton;
    TextBox noteBox;

    // 持续盯防
    CheckBox followCheck;
    StackPanel followPanel;
    Dropdown followMode;
    TextBox intervalBox;
    TextBox roundBox;
    TextBox budgetBox;
    TextBlock followStatusText;
    Button followPauseButton;

    // 获取数据
    Dropdown fetchMode;
    TextBox fetchLimitBox;
    CheckBox unlimitedCheck;
    CheckBox rangeCheck;
    TextBox fromBox;
    TextBox toBox;
    Button fetchButton;

    // 任务进度卡
    Border taskCard;
    TextBlock taskMessageText;
    TextBlock taskCreditsText;
    ProgressBar taskBar;
    Button stopButton;
    bool taskRunning;
    string taskId = "";
    string taskMessage = "";
    long taskPercent;
    string taskCredits = "";

    // 导出报告
    TextBox exportLimitBox;
    CheckBox exportRangeCheck;
    TextBox exportFromBox;
    TextBox exportToBox;
    Button exportButton;
    bool exporting;
    string exportRequestId = "";

    // 最近报告
    StackPanel reportsHost;
    TextBlock reportsEmpty;

    // 互动档案
    Button pairButton;
    Border pairPanel;
    Dropdown pairSelect;
    Button pairGoButton;
    TextBlock pairHint;

    // 两步删除状态：第一次点击统计删除范围（拿到 token），再次点击确认。
    bool deleteArmed;
    string deleteToken = "";

    public event Action BackRequested;

    public UserDetailPage(MainWindow main) {
        Main = main;
        Build();
    }

    public string CurrentUid { get { return uid; } }

    // ------------------------------------------------------------ 布局
    void Build() {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(0, 0, 4, 8) };

        var back = new Button { Content = "‹ 返回盯单人列表", Style = ButtonStyle(Brushes.Transparent, Theme.Hover, Theme.PanelBorderBrush, Theme.Muted, 12) };
        back.Margin = new Thickness(0, 0, 0, 10);
        back.Click += delegate {
            if (BackRequested != null) BackRequested();
        };
        body.Children.Add(back);

        body.Children.Add(HeaderCard());
        body.Children.Add(FollowCard());
        body.Children.Add(FetchCard());
        body.Children.Add(TaskCard());
        body.Children.Add(ExportCard());
        body.Children.Add(ReportsCard());
        body.Children.Add(PairCard());

        scroll.Content = body;
        Content = scroll;
    }

    UIElement HeaderCard() {
        var card = Theme.Card();
        var stack = new StackPanel();

        var identityRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var avatar = new Border {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
            Background = Theme.AccentDimBrush, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        avatarChar = new TextBlock {
            Text = "微", Foreground = Theme.Text, FontSize = 20, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        avatar.Child = avatarChar;
        Grid.SetColumn(avatar, 0);
        identityRow.Children.Add(avatar);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        nameText = new TextBlock { Text = "", Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        uidText = new TextBlock { Text = "", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        profileText = new TextBlock { Text = "", Foreground = Theme.Warn, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        info.Children.Add(nameText);
        info.Children.Add(uidText);
        info.Children.Add(profileText);
        Grid.SetColumn(info, 1);
        identityRow.Children.Add(info);
        stack.Children.Add(identityRow);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        unreadButton = new Button { Content = "查看未读", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 12) };
        unreadButton.IsEnabled = false;
        unreadButton.Visibility = Visibility.Collapsed;
        unreadButton.ToolTip = "请先导出未读报告";
        unreadButton.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(unreadButton);

        openProfileButton = new Button { Content = "打开微博主页", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Blue, 12) };
        openProfileButton.Margin = new Thickness(0, 0, 8, 4);
        openProfileButton.Click += delegate {
            if (uid.Length > 0 && Main != null) Dialogs.OpenExternal(Main, "https://weibo.cn/u/" + uid);
        };
        actions.Children.Add(openProfileButton);

        deleteButton = new Button { Content = "删除盯单人", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Danger, 12) };
        deleteButton.Margin = new Thickness(0, 0, 8, 4);
        deleteButton.Click += delegate { OnDeleteClick(); };
        actions.Children.Add(deleteButton);
        stack.Children.Add(actions);

        var noteLabel = Theme.Label("备注（仅保存在本机）");
        noteLabel.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(noteLabel);
        var noteRow = new Grid();
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        noteBox = new TextBox { Style = InputStyle() };
        Grid.SetColumn(noteBox, 0);
        noteRow.Children.Add(noteBox);
        var noteSave = new Button { Content = "保存备注", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 12) };
        noteSave.Margin = new Thickness(8, 0, 0, 0);
        noteSave.Click += delegate { SaveNote(); };
        Grid.SetColumn(noteSave, 1);
        noteRow.Children.Add(noteSave);
        stack.Children.Add(noteRow);

        card.Child = stack;
        return card;
    }

    UIElement FollowCard() {
        var card = Theme.Card();
        var stack = new StackPanel();

        stack.Children.Add(Theme.Title("持续盯防"));
        followStatusText = new TextBlock { Text = "", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(followStatusText);

        followCheck = new CheckBox { Content = "持续盯防（按固定间隔自动检查新互动）", Style = CheckBoxStyle(), Margin = new Thickness(0, 0, 0, 8) };
        followCheck.Checked += delegate { followPanel.Visibility = Visibility.Visible; };
        followCheck.Unchecked += delegate { followPanel.Visibility = Visibility.Collapsed; };
        stack.Children.Add(followCheck);

        followPanel = new StackPanel { Visibility = Visibility.Collapsed };

        followPanel.Children.Add(FormRow("互动方向", BuildFollowMode()));
        followPanel.Children.Add(FormRow("检查间隔（分钟）", BuildSmallInput(out intervalBox, "15")));
        followPanel.Children.Add(FormRow("单轮条数", BuildSmallInput(out roundBox, "10")));
        followPanel.Children.Add(FormRow("每日额度", BuildSmallInput(out budgetBox, "30")));

        var followButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var followSave = new Button { Content = "保存订阅设置", Style = ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 12) };
        followSave.Click += delegate { SaveFollow(); };
        followButtons.Children.Add(followSave);
        followPauseButton = new Button { Content = "暂停盯防", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 12) };
        followPauseButton.Margin = new Thickness(8, 0, 0, 0);
        followPauseButton.Visibility = Visibility.Collapsed;
        followPauseButton.Click += delegate { ToggleFollowPause(); };
        followButtons.Children.Add(followPauseButton);
        followPanel.Children.Add(followButtons);

        stack.Children.Add(followPanel);
        card.Child = stack;
        return card;
    }

    UIElement FetchCard() {
        var card = Theme.Card();
        var stack = new StackPanel();

        stack.Children.Add(Theme.Title("获取数据"));
        stack.Children.Add(new TextBlock {
            Text = "只读取新增与指定范围的数据，已保存的正文不会重复计费。",
            Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(FormRow("互动内容", BuildFetchMode()));

        var limitRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var limitLabel = Theme.Label("本次条数");
        limitLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(limitLabel, 0);
        limitRow.Children.Add(limitLabel);
        var limitCell = new StackPanel { Orientation = Orientation.Horizontal };
        fetchLimitBox = new TextBox { Style = InputStyle(), Width = 90, Text = "50" };
        limitCell.Children.Add(fetchLimitBox);
        unlimitedCheck = new CheckBox { Content = "全部（不限制条数）", Style = CheckBoxStyle(), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        unlimitedCheck.Checked += delegate { fetchLimitBox.IsEnabled = false; };
        unlimitedCheck.Unchecked += delegate { fetchLimitBox.IsEnabled = true; };
        limitCell.Children.Add(unlimitedCheck);
        Grid.SetColumn(limitCell, 1);
        limitRow.Children.Add(limitCell);
        stack.Children.Add(limitRow);

        var rangeRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        rangeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        rangeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rangeCell = new StackPanel { Orientation = Orientation.Horizontal };
        rangeCheck = new CheckBox { Content = "指定日期范围", Style = CheckBoxStyle(), VerticalAlignment = VerticalAlignment.Center };
        rangeCell.Children.Add(rangeCheck);
        fromBox = new TextBox { Style = InputStyle(), Width = 110, Margin = new Thickness(10, 0, 0, 0), Text = "" };
        toBox = new TextBox { Style = InputStyle(), Width = 110, Margin = new Thickness(8, 0, 0, 0), Text = "" };
        rangeCell.Children.Add(fromBox);
        rangeCell.Children.Add(toBox);
        rangeCell.Children.Add(new TextBlock { Text = "YYYY-MM-DD", Foreground = Theme.Muted, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(rangeCell, 1);
        rangeRow.Children.Add(rangeCell);
        stack.Children.Add(rangeRow);
        rangeCheck.Checked += delegate { SetRangeBoxesEnabled(true); };
        rangeCheck.Unchecked += delegate { SetRangeBoxesEnabled(false); };
        SetRangeBoxesEnabled(false);

        fetchButton = new Button {
            Content = "获取数据", Style = ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0),
        };
        fetchButton.Click += delegate { StartFetch(); };
        stack.Children.Add(fetchButton);

        card.Child = stack;
        return card;
    }

    UIElement TaskCard() {
        taskCard = new Border {
            Background = Theme.Panel, BorderBrush = Theme.Warn, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "获取任务进行中", Foreground = Theme.Warn, FontSize = 13, FontWeight = FontWeights.SemiBold });
        taskMessageText = new TextBlock { Text = "", Foreground = Theme.Text, FontSize = 12, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(taskMessageText);
        taskBar = new ProgressBar { Style = ProgressBarStyle(), Height = 6, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        stack.Children.Add(taskBar);
        taskCreditsText = new TextBlock { Text = "", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        stack.Children.Add(taskCreditsText);
        stopButton = new Button { Content = "停止", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.Danger, Theme.Danger, 12), Margin = new Thickness(0, 10, 0, 0) };
        stopButton.Click += delegate {
            if (Main != null) Main.Send("stop", Json.Obj());
        };
        stack.Children.Add(stopButton);
        taskCard.Child = stack;
        return taskCard;
    }

    UIElement ExportCard() {
        var card = Theme.Card();
        var stack = new StackPanel();

        stack.Children.Add(Theme.Title("导出数据"));
        stack.Children.Add(new TextBlock {
            Text = "从本地已保存的数据生成 HTML 报告并自动打开，不消耗 credits。",
            Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap,
        });

        var limitRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        limitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var limitLabel = Theme.Label("导出条数");
        limitLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(limitLabel, 0);
        limitRow.Children.Add(limitLabel);
        exportLimitBox = new TextBox { Style = InputStyle(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Text = "200" };
        Grid.SetColumn(exportLimitBox, 1);
        limitRow.Children.Add(exportLimitBox);
        stack.Children.Add(limitRow);

        var rangeRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        rangeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        rangeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rangeCell = new StackPanel { Orientation = Orientation.Horizontal };
        exportRangeCheck = new CheckBox { Content = "指定日期范围", Style = CheckBoxStyle(), VerticalAlignment = VerticalAlignment.Center };
        rangeCell.Children.Add(exportRangeCheck);
        exportFromBox = new TextBox { Style = InputStyle(), Width = 110, Margin = new Thickness(10, 0, 0, 0) };
        exportToBox = new TextBox { Style = InputStyle(), Width = 110, Margin = new Thickness(8, 0, 0, 0) };
        rangeCell.Children.Add(exportFromBox);
        rangeCell.Children.Add(exportToBox);
        rangeCell.Children.Add(new TextBlock { Text = "YYYY-MM-DD", Foreground = Theme.Muted, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(rangeCell, 1);
        rangeRow.Children.Add(rangeCell);
        stack.Children.Add(rangeRow);
        exportRangeCheck.Checked += delegate { SetExportRangeBoxesEnabled(true); };
        exportRangeCheck.Unchecked += delegate { SetExportRangeBoxesEnabled(false); };
        SetExportRangeBoxesEnabled(false);

        exportButton = new Button {
            Content = "生成并打开报告", Style = ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        exportButton.Click += delegate { StartExport(); };
        stack.Children.Add(exportButton);

        card.Child = stack;
        return card;
    }

    UIElement ReportsCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("最近报告"));
        reportsEmpty = new TextBlock {
            Text = "暂无本地报告。先获取数据，再在这里生成并打开报告。",
            Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
        };
        reportsHost = new StackPanel();
        stack.Children.Add(reportsEmpty);
        stack.Children.Add(reportsHost);
        card.Child = stack;
        return card;
    }

    UIElement PairCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("互动档案"));
        stack.Children.Add(new TextBlock {
            Text = "生成两位盯单人之间的定向互动报告（0 credits）。",
            Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap,
        });
        pairButton = new Button { Content = "生成互动档案", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Blue, 12) };
        pairButton.Click += delegate { TogglePairPanel(); };
        stack.Children.Add(pairButton);

        pairPanel = new Border {
            Background = Theme.Hover, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var pairStack = new StackPanel();
        pairHint = new TextBlock { Text = "", Foreground = Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        pairStack.Children.Add(pairHint);
        pairSelect = new Dropdown();
        pairStack.Children.Add(pairSelect);
        pairGoButton = new Button { Content = "生成", Style = ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 12), Margin = new Thickness(0, 8, 0, 0) };
        pairGoButton.Click += delegate { GeneratePairReport(); };
        pairStack.Children.Add(pairGoButton);
        pairPanel.Child = pairStack;
        stack.Children.Add(pairPanel);

        card.Child = stack;
        return card;
    }

    // ------------------------------------------------------------ 下拉项
    FrameworkElement BuildFollowMode() {
        followMode = new Dropdown();
        var items = new List<KeyValuePair<string, string>>();
        items.Add(new KeyValuePair<string, string>("both", "发出和收到的评论"));
        items.Add(new KeyValuePair<string, string>("sent", "发出的评论"));
        items.Add(new KeyValuePair<string, string>("received", "收到的评论"));
        followMode.SetItems(items, "both");
        return followMode;
    }

    FrameworkElement BuildFetchMode() {
        fetchMode = new Dropdown();
        var items = new List<KeyValuePair<string, string>>();
        items.Add(new KeyValuePair<string, string>("comments-both", "发出和收到的评论"));
        items.Add(new KeyValuePair<string, string>("comments-sent", "发出的评论"));
        items.Add(new KeyValuePair<string, string>("comments-received", "收到的评论"));
        items.Add(new KeyValuePair<string, string>("mentions-in-comments", "被 @ 的评论"));
        items.Add(new KeyValuePair<string, string>("mentions-in-posts", "被 @ 的微博"));
        fetchMode.SetItems(items, "comments-both");
        return fetchMode;
    }

    FrameworkElement BuildSmallInput(out TextBox box, string defaultValue) {
        box = new TextBox { Style = InputStyle(), Width = 110, HorizontalAlignment = HorizontalAlignment.Left, Text = defaultValue };
        return box;
    }

    UIElement FormRow(string labelText, FrameworkElement input) {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = Theme.Label(labelText);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return grid;
    }

    // ------------------------------------------------------------ 用户切换与渲染
    public void SetUser(string value) {
        uid = value ?? "";
        viewedSent = false;
        deleteArmed = false;
        deleteToken = "";
        taskRunning = false;
        taskId = "";
        taskMessage = "";
        taskPercent = 0;
        taskCredits = "";
        exporting = false;
        exportRequestId = "";
        string storedNote;
        currentNote = noteByUid.TryGetValue(uid, out storedNote) ? storedNote : "";
        noteBox.Text = currentNote;
        PrefillFollowSettings();
        Render();
    }

    public void OnShown() {
        if (uid.Length == 0 || Main == null) return;
        if (!viewedSent) {
            viewedSent = true;
            var metadata = Json.Obj();
            var row = Main.State.FindUser(uid);
            Json.Put(metadata, "nickname", row != null ? row.Nickname : "");
            var payload = Json.Obj();
            Json.Put(payload, "uid", uid);
            Json.Put(payload, "metadata", metadata);
            Main.Send("userViewed", payload);
        }
        Render();
    }

    void Render() {
        RenderHeader();
        RenderFollow();
        RenderTaskCard();
        RenderExportButton();
        RenderReports();
    }

    void RenderHeader() {
        if (nameText == null) return;
        var row = Main != null && uid.Length > 0 ? Main.State.FindUser(uid) : null;
        var nickname = row != null && row.Nickname.Length > 0 ? row.Nickname : (uid.Length > 0 ? "用户 " + uid : "");
        nameText.Text = nickname;
        uidText.Text = "UID " + uid;
        var trimmed = nickname.Trim();
        avatarChar.Text = trimmed.Length > 0 ? trimmed.Substring(0, 1) : "微";
        if (row != null && row.ProfileStatus == "error" && row.ProfileMessage.Length > 0) {
            profileText.Text = row.ProfileMessage;
            profileText.Visibility = Visibility.Visible;
        } else if (row != null && row.ProfileStatus == "loading") {
            profileText.Text = row.ProfileMessage.Length > 0 ? row.ProfileMessage : "正在识别昵称与头像…";
            profileText.Visibility = Visibility.Visible;
        } else {
            profileText.Visibility = Visibility.Collapsed;
        }
        if (row != null && row.Unread > 0) {
            unreadButton.Visibility = Visibility.Visible;
            unreadButton.Content = "查看未读 · " + row.Unread;
            unreadButton.IsEnabled = false;
            unreadButton.ToolTip = "请先导出未读报告";
        } else {
            unreadButton.Visibility = Visibility.Collapsed;
        }
    }

    void RenderFollow() {
        if (followStatusText == null) return;
        var row = Main != null && uid.Length > 0 ? Main.State.FindFollow(uid, null, "user") : null;
        if (row != null) {
            followCheck.IsChecked = row.Enabled;
            followStatusText.Text = FollowStatusLine(row);
            followPauseButton.Visibility = Visibility.Visible;
            followPauseButton.Content = row.Enabled ? "暂停盯防" : "恢复盯防";
        } else {
            followStatusText.Text = "尚未设置持续盯防。勾选后按固定间隔自动检查新互动，读取消耗 credits。";
            followPauseButton.Visibility = Visibility.Collapsed;
        }
    }

    string FollowStatusLine(FollowRow row) {
        var text = row.Enabled ? "订阅已启用 · " : "订阅已暂停 · ";
        text += ModeDisplayName(row.Mode) + " · 间隔 " + row.Interval + " 分钟 · 单轮 " + row.RoundLimit + " 条 · 每日额度 " + row.DailyBudget;
        if (!string.IsNullOrEmpty(row.NextCheckAt)) text += " · 下次检查 " + FormatTime(row.NextCheckAt);
        if (!string.IsNullOrEmpty(row.Reason)) text += " · " + row.Reason;
        return text;
    }

    void RenderTaskCard() {
        if (taskCard == null) return;
        taskCard.Visibility = taskRunning ? Visibility.Visible : Visibility.Collapsed;
        fetchButton.IsEnabled = !taskRunning;
        fetchButton.Content = taskRunning ? "获取中…" : "获取数据";
        stopButton.Visibility = taskRunning ? Visibility.Visible : Visibility.Collapsed;
        if (!taskRunning) return;
        taskMessageText.Text = taskMessage.Length > 0 ? taskMessage : "正在读取数据…";
        taskBar.Visibility = taskPercent > 0 ? Visibility.Visible : Visibility.Collapsed;
        taskBar.Value = taskPercent > 100 ? 100 : taskPercent;
        if (taskCredits.Length > 0) {
            taskCreditsText.Text = taskCredits;
            taskCreditsText.Visibility = Visibility.Visible;
        } else {
            taskCreditsText.Visibility = Visibility.Collapsed;
        }
    }

    void RenderExportButton() {
        if (exportButton == null) return;
        exportButton.Content = exporting ? "正在生成报告…" : "生成并打开报告";
        exportButton.IsEnabled = !exporting;
    }

    void RenderReports() {
        if (reportsHost == null || Main == null) return;
        reportsHost.Children.Clear();
        var rows = new List<ReportRow>();
        foreach (var report in Main.State.Reports) {
            if (report != null && report.Uid == uid && report.Category != "post-comments") rows.Add(report);
        }
        rows.Sort(delegate(ReportRow a, ReportRow b) { return b.Modified.CompareTo(a.Modified); });
        reportsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var report in rows) reportsHost.Children.Add(ReportRowView(report));
    }

    UIElement ReportRowView(ReportRow report) {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = report.Name.Length > 0 ? report.Name : report.Id, Foreground = Theme.Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = "";
        if (report.Count > 0) meta += report.Count + " 条";
        if (report.ModifiedText.Length > 0) meta += (meta.Length > 0 ? " · " : "") + report.ModifiedText;
        else if (report.Modified > 0) meta += (meta.Length > 0 ? " · " : "") + "更新于 " + FormatTimeFromMs(report.Modified);
        info.Children.Add(new TextBlock { Text = meta.Length > 0 ? meta : "本地报告", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var open = new Button { Content = "打开报告", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 11) };
        open.Click += delegate { OpenReportItem(report.Id, "report"); };
        buttons.Children.Add(open);
        var folder = new Button { Content = "打开文件夹", Style = ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 11), Margin = new Thickness(6, 0, 0, 0) };
        folder.Click += delegate { OpenReportItem(report.Id, "folder"); };
        buttons.Children.Add(folder);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    void OpenReportItem(string id, string kind) {
        if (Main == null || string.IsNullOrEmpty(id)) return;
        var payload = Json.Obj();
        Json.Put(payload, "id", id);
        Json.Put(payload, "kind", kind);
        Main.Send("open", payload);
    }

    // ------------------------------------------------------------ 动作
    void SaveNote() {
        if (Main == null || uid.Length == 0) return;
        var text = noteBox.Text ?? "";
        if (text.Length > 2000) {
            Main.Toast("备注最多 2000 字", true);
            return;
        }
        var payload = Json.Obj();
        Json.Put(payload, "uid", uid);
        Json.Put(payload, "note", text);
        Json.Put(payload, "text", text);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("userNoteSave", payload);
    }

    void PrefillFollowSettings() {
        var row = Main != null && uid.Length > 0 ? Main.State.FindFollow(uid, null, "user") : null;
        if (row != null) {
            followMode.Select(row.Mode.Length > 0 ? row.Mode : "both");
            intervalBox.Text = row.Interval > 0 ? row.Interval.ToString(CultureInfo.InvariantCulture) : "15";
            roundBox.Text = row.RoundLimit > 0 ? row.RoundLimit.ToString(CultureInfo.InvariantCulture) : "10";
            budgetBox.Text = row.DailyBudget > 0 ? row.DailyBudget.ToString(CultureInfo.InvariantCulture) : "30";
        } else {
            followMode.Select("both");
            intervalBox.Text = "15";
            roundBox.Text = "10";
            budgetBox.Text = "30";
        }
        followCheck.IsChecked = row != null && row.Enabled;
    }

    void SaveFollow() {
        if (Main == null || uid.Length == 0) return;
        var mode = followMode.Value;
        if (mode.Length == 0) {
            Main.Toast("请选择互动方向", true);
            return;
        }
        int interval;
        if (!int.TryParse((intervalBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) || interval < 1 || interval > 1440) {
            Main.Toast("检查间隔请填写 1–1440 分钟", true);
            return;
        }
        int round;
        if (!int.TryParse((roundBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out round) || round < 1) {
            Main.Toast("单轮取数上限必须为正整数，双向至少为 2", true);
            return;
        }
        if (mode == "both" && round < 2) {
            Main.Toast("单轮取数上限必须为正整数，双向至少为 2", true);
            return;
        }
        double budget;
        if (!double.TryParse((budgetBox.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out budget) || budget <= 0) {
            Main.Toast("每日 credit 预算必须为有效正数", true);
            return;
        }
        var settings = Json.Obj();
        Json.Put(settings, "uid", uid);
        Json.Put(settings, "mode", mode);
        Json.Put(settings, "frequency", "fixed");
        Json.Put(settings, "interval", interval);
        Json.Put(settings, "roundLimit", round);
        Json.Put(settings, "dailyBudget", budget);
        Json.Put(settings, "enabled", followCheck.IsChecked == true);
        var payload = Json.Obj();
        Json.Put(payload, "uid", uid);
        Json.Put(payload, "targetKind", "user");
        Json.Put(payload, "scope", "user");
        Json.Put(payload, "settings", settings);
        Main.Send("followSave", payload);
        Main.Toast("已保存盯防设置", false);
    }

    void ToggleFollowPause() {
        if (Main == null || uid.Length == 0) return;
        var row = Main.State.FindFollow(uid, null, "user");
        if (row == null) {
            Main.Toast("请先保存盯防设置", true);
            return;
        }
        var payload = Json.Obj();
        Json.Put(payload, "uid", uid);
        Json.Put(payload, "targetKind", "user");
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send(row.Enabled ? "followPause" : "followResume", payload);
        Main.Toast(row.Enabled ? "已请求暂停盯防" : "已请求恢复盯防", false);
    }

    void SetRangeBoxesEnabled(bool enabled) {
        fromBox.IsEnabled = enabled;
        toBox.IsEnabled = enabled;
    }

    void SetExportRangeBoxesEnabled(bool enabled) {
        exportFromBox.IsEnabled = enabled;
        exportToBox.IsEnabled = enabled;
    }

    // 获取数据：run 动作，query 字段结构与 desktop/ui/data-query.js build() 一致。
    // 日期范围启用时 policy 仍为 "cache"（桥端校验：syncOperation=regular 与 policy=update 组合会报错）。
    void StartFetch() {
        if (Main == null || uid.Length == 0) return;
        if (taskRunning) {
            Main.Toast("已有任务在运行，请先停止", true);
            return;
        }
        var mode = fetchMode.Value;
        if (mode.Length == 0) {
            Main.Toast("请选择互动内容", true);
            return;
        }
        var unlimited = unlimitedCheck.IsChecked == true;
        long limit = 50;
        if (!unlimited) {
            if (!long.TryParse((fetchLimitBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit <= 0) {
                Main.Toast("本次条数请填写正整数", true);
                return;
            }
        }
        var useRange = rangeCheck.IsChecked == true;
        string from = "";
        string to = "";
        if (useRange) {
            from = (fromBox.Text ?? "").Trim();
            to = (toBox.Text ?? "").Trim();
            DateTime dayFrom;
            DateTime dayTo;
            if (!TryParseDate(from, out dayFrom) || !TryParseDate(to, out dayTo)) {
                Main.Toast("日期格式应为 YYYY-MM-DD", true);
                return;
            }
            if (dayFrom > dayTo) {
                Main.Toast("开始日期不能晚于结束日期", true);
                return;
            }
        }

        var query = Json.Obj();
        Json.Put(query, "target", uid);
        var subject = Json.Obj();
        Json.Put(subject, "kind", "user");
        Json.Put(subject, "uid", uid);
        Json.Put(query, "subject", subject);
        Json.Put(query, "mode", mode);
        Json.Put(query, "policy", "cache");
        Json.Put(query, "syncOnly", true);
        Json.Put(query, "task", "sync");
        Json.Put(query, "syncOperation", "regular");
        Json.Put(query, "dateRange", useRange);
        Json.Put(query, "limit", limit.ToString(CultureInfo.InvariantCulture));
        Json.Put(query, "unlimited", unlimited);
        Json.Put(query, "from", useRange ? from : "");
        Json.Put(query, "to", useRange ? to : "");
        Json.Put(query, "returnBudget", "");
        Json.Put(query, "pages", "");
        Json.Put(query, "pageSize", "");
        Json.Put(query, "seekLimit", "");
        Json.Put(query, "emptyWindowLimit", "");
        Json.Put(query, "historyRecovery", true);
        Json.Put(query, "downloadImages", false);
        Json.Put(query, "fullText", false);
        Json.Put(query, "verbose", false);
        Json.Put(query, "preview", false);

        var payload = Json.Obj();
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Json.Put(payload, "query", query);
        taskRunning = true;
        taskId = "";
        taskMessage = "";
        taskPercent = 0;
        taskCredits = "";
        Main.Send("run", payload);
        RenderTaskCard();
    }

    // 导出报告：桥端实际处理报告生成的是 localExport（output 动作只负责打开 output 文件夹）。
    // query 字段与任务卡指定的导出结构一致；openWhenReady 让桥在生成后自动发出 open 事件。
    void StartExport() {
        if (Main == null || uid.Length == 0) return;
        if (exporting) {
            Main.Toast("报告正在生成，请稍候", true);
            return;
        }
        long limit = 200;
        if (!long.TryParse((exportLimitBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) || limit <= 0) {
            Main.Toast("导出条数请填写正整数", true);
            return;
        }
        var useRange = exportRangeCheck.IsChecked == true;
        string from = "";
        string to = "";
        if (useRange) {
            from = (exportFromBox.Text ?? "").Trim();
            to = (exportToBox.Text ?? "").Trim();
            DateTime dayFrom;
            DateTime dayTo;
            if (!TryParseDate(from, out dayFrom) || !TryParseDate(to, out dayTo)) {
                Main.Toast("日期格式应为 YYYY-MM-DD", true);
                return;
            }
            if (dayFrom > dayTo) {
                Main.Toast("开始日期不能晚于结束日期", true);
                return;
            }
        }
        var mode = fetchMode.Value.Length > 0 ? fetchMode.Value : "comments-both";

        var query = Json.Obj();
        Json.Put(query, "target", uid);
        var subject = Json.Obj();
        Json.Put(subject, "kind", "user");
        Json.Put(subject, "uid", uid);
        Json.Put(query, "subject", subject);
        Json.Put(query, "mode", mode);
        Json.Put(query, "policy", "database");
        Json.Put(query, "syncOnly", false);
        Json.Put(query, "task", "report");
        Json.Put(query, "dateRange", useRange);
        Json.Put(query, "limit", limit.ToString(CultureInfo.InvariantCulture));
        Json.Put(query, "unlimited", false);
        Json.Put(query, "from", useRange ? from : "");
        Json.Put(query, "to", useRange ? to : "");
        Json.Put(query, "markUnread", false);
        Json.Put(query, "peerUid", "");
        Json.Put(query, "historyRecovery", false);
        Json.Put(query, "downloadImages", false);
        Json.Put(query, "fullText", false);
        Json.Put(query, "verbose", false);
        Json.Put(query, "preview", false);

        exportRequestId = Guid.NewGuid().ToString();
        var payload = Json.Obj();
        Json.Put(payload, "requestId", exportRequestId);
        Json.Put(payload, "query", query);
        Json.Put(payload, "openWhenReady", true);
        exporting = true;
        Main.Send("localExport", payload);
        RenderExportButton();
    }

    void TogglePairPanel() {
        if (pairPanel == null) return;
        if (pairPanel.Visibility == Visibility.Visible) {
            pairPanel.Visibility = Visibility.Collapsed;
            return;
        }
        BuildPairItems();
        pairPanel.Visibility = Visibility.Visible;
    }

    void BuildPairItems() {
        var others = new List<KeyValuePair<string, string>>();
        if (Main != null) {
            foreach (var user in Main.State.Users) {
                if (user == null || string.IsNullOrEmpty(user.Uid) || user.Uid == uid) continue;
                var name = user.Nickname.Length > 0 ? user.Nickname : "用户 " + user.Uid;
                others.Add(new KeyValuePair<string, string>(user.Uid, name + " · " + user.Uid));
            }
        }
        if (others.Count == 0) {
            pairSelect.Visibility = Visibility.Collapsed;
            pairGoButton.Visibility = Visibility.Collapsed;
            pairHint.Text = "先在其他用户页获取互动数据，再回到这里生成互动档案。";
            pairHint.Visibility = Visibility.Visible;
            return;
        }
        pairHint.Visibility = Visibility.Collapsed;
        pairSelect.Visibility = Visibility.Visible;
        pairGoButton.Visibility = Visibility.Visible;
        pairSelect.SetItems(others, "");
    }

    void GeneratePairReport() {
        if (Main == null || uid.Length == 0) return;
        var other = pairSelect.Value;
        if (string.IsNullOrEmpty(other)) {
            Main.Toast("请选择互动对方", true);
            return;
        }
        var requestId = Guid.NewGuid().ToString();
        Main.State.PendingPairRequest = requestId;
        var payload = Json.Obj();
        Json.Put(payload, "uidA", uid);
        Json.Put(payload, "uidB", other);
        Json.Put(payload, "requestId", requestId);
        Main.Send("pairReport", payload);
        pairPanel.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ 两步删除
    void OnDeleteClick() {
        if (Main == null || uid.Length == 0) return;
        if (!deleteArmed) {
            deleteArmed = true;
            deleteToken = "";
            RenderDeleteButton();
            var payload = Json.Obj();
            Json.Put(payload, "uid", uid);
            Json.Put(payload, "requestId", Guid.NewGuid().ToString());
            Main.Send("userDeletePlan", payload);
            return;
        }
        if (deleteToken.Length == 0) {
            Main.Toast("正在统计本地数据，请稍候再点击确认", true);
            return;
        }
        var confirm = Json.Obj();
        Json.Put(confirm, "uid", uid);
        Json.Put(confirm, "token", deleteToken);
        Json.Put(confirm, "confirm", true);
        Json.Put(confirm, "requestId", Guid.NewGuid().ToString());
        Main.Send("userDeleteConfirm", confirm);
        deleteArmed = false;
        deleteToken = "";
        RenderDeleteButton();
    }

    void HandleDeletePlan(Dictionary<string, object> e) {
        if (Json.S(e, "uid") != uid) return;
        deleteToken = Json.S(e, "token");
        if (deleteToken.Length > 0) Main.Toast("删除范围已统计，再次点击「确认彻底删除」完成删除", false);
        RenderDeleteButton();
    }

    void RenderDeleteButton() {
        if (deleteButton == null) return;
        if (deleteArmed) {
            deleteButton.Content = deleteToken.Length > 0 ? "确认彻底删除" : "正在统计删除范围…";
            deleteButton.Foreground = Theme.Danger;
            deleteButton.BorderBrush = Theme.Danger;
        } else {
            deleteButton.Content = "删除盯单人";
            deleteButton.Foreground = Theme.Danger;
            deleteButton.BorderBrush = Theme.PanelBorderBrush;
        }
    }

    // ------------------------------------------------------------ 引擎事件
    public void OnEngineEvent(Dictionary<string, object> e) {
        if (Main == null) return;
        var type = Json.S(e, "type");
        if (type == "homeUsers") {
            StoreNoteFromHome(e);
            Render();
        } else if (type == "library" || type == "libraryData" || type == "followState") {
            Render();
        } else if (type == "userProfile") {
            HandleUserProfile(e);
        } else if (type == "userNoteSaved") {
            HandleNoteSaved(e);
        } else if (type == "userNoteError") {
            Main.Toast(ErrorMessage(e, "备注保存失败"), true);
        } else if (type == "started") {
            HandleStarted(e);
        } else if (type == "progress") {
            HandleProgress(e);
        } else if (type == "collection") {
            HandleCollection(e);
        } else if (type == "credits") {
            HandleCredits(e);
        } else if (type == "finished") {
            HandleFinished(e);
        } else if (type == "localExported") {
            HandleLocalExported(e);
        } else if (type == "localError") {
            HandleLocalError(e);
        } else if (type == "userDeletePlan") {
            HandleDeletePlan(e);
        } else if (type == "userDeleted") {
            deleteArmed = false;
            deleteToken = "";
            RenderDeleteButton();
        } else if (type == "userDeleteError") {
            deleteArmed = false;
            deleteToken = "";
            RenderDeleteButton();
            Main.Toast(ErrorMessage(e, "删除未完成"), true);
        } else if (type == "followError") {
            Main.Toast(ErrorMessage(e, "订阅操作未完成"), true);
        } else if (type == "error") {
            if (taskRunning && taskId.Length == 0) {
                taskRunning = false;
                RenderTaskCard();
            }
            var message = Json.S(e, "message");
            if (message.Length > 0) Main.Toast(message, true);
        }
    }

    void StoreNoteFromHome(Dictionary<string, object> e) {
        foreach (var item in Json.L(e, "users")) {
            var map = Json.M(item);
            if (map == null) continue;
            var mapUid = Json.S(map, "uid");
            if (mapUid.Length == 0) continue;
            noteByUid[mapUid] = Json.S(map, "note");
            if (mapUid == uid) currentNote = noteByUid[mapUid];
        }
    }

    void HandleUserProfile(Dictionary<string, object> e) {
        if (Json.S(e, "uid") != uid) return;
        var row = Main.State.FindUser(uid);
        if (row != null) {
            var nickname = Json.S(e, "nickname");
            if (nickname.Length > 0) row.Nickname = nickname;
            row.ProfileStatus = Json.S(e, "profileStatus");
            row.ProfileMessage = Json.S(e, "profileMessage");
        }
        RenderHeader();
    }

    void HandleNoteSaved(Dictionary<string, object> e) {
        if (Json.S(e, "uid") != uid) return;
        currentNote = Json.S(e, "note");
        noteByUid[uid] = currentNote;
        noteBox.Text = currentNote;
        Main.Toast("备注已保存", false);
    }

    void HandleStarted(Dictionary<string, object> e) {
        if (Json.B(e, "automatic")) return;
        var query = Json.M(e.ContainsKey("query") ? e["query"] : null);
        if (!QueryMatchesUid(query)) return;
        taskRunning = true;
        taskId = Json.S(e, "id");
        if (taskId.Length == 0) taskId = Json.S(e, "taskId");
        taskMessage = "";
        taskPercent = 0;
        taskCredits = "";
        RenderTaskCard();
    }

    bool QueryMatchesUid(Dictionary<string, object> query) {
        if (query == null || uid.Length == 0) return false;
        var subject = Json.M(query.ContainsKey("subject") ? query["subject"] : null);
        if (subject != null && Json.S(subject, "kind") == "post") return false;
        var target = Json.S(query, "target");
        var queryUid = Json.S(query, "uid");
        return target == uid || queryUid == uid;
    }

    bool OwnsTaskEvent(Dictionary<string, object> e) {
        var id = Json.S(e, "taskId");
        if (id.Length == 0) id = Json.S(e, "id");
        if (Json.B(e, "automatic") && (taskId.Length == 0 || id != taskId)) return false;
        if (taskId.Length == 0) return true;
        return id.Length == 0 || id == taskId;
    }

    void HandleProgress(Dictionary<string, object> e) {
        if (!taskRunning || !OwnsTaskEvent(e)) return;
        var message = Json.S(e, "message");
        if (message.Length > 0) taskMessage = message;
        var percent = Json.N(e, "percent");
        if (percent > 0) taskPercent = percent;
        RenderTaskCard();
    }

    void HandleCollection(Dictionary<string, object> e) {
        if (!taskRunning || !OwnsTaskEvent(e)) return;
        var fetched = Json.N(e, "fetched");
        if (fetched > 0) {
            taskMessage = "正在获取" + CollectionKindName(Json.S(e, "kind")) + "，已保存 " + fetched + " 条";
            RenderTaskCard();
        }
    }

    void HandleCredits(Dictionary<string, object> e) {
        if (!taskRunning || !OwnsTaskEvent(e)) return;
        var estimated = Json.N(e, "estimatedCredits");
        if (estimated > 0) {
            taskCredits = "本次估算费用：" + estimated.ToString("N0", CultureInfo.InvariantCulture) + " credits";
            RenderTaskCard();
        }
    }

    void HandleFinished(Dictionary<string, object> e) {
        if (!taskRunning) return;
        var id = Json.S(e, "id");
        if (id.Length == 0) id = Json.S(e, "taskId");
        if (taskId.Length > 0) {
            if (id.Length > 0 && id != taskId) return;
        } else {
            if (Json.B(e, "automatic")) return; // 未绑定任务号时忽略自动任务的收尾
            if (id.Length == 0) return;
            taskId = id;
        }
        var cancelled = Json.B(e, "cancelled");
        var code = Json.N(e, "code");
        var summary = SummaryText(e);
        taskRunning = false;
        taskId = "";
        if (cancelled) {
            Main.Toast("获取已停止", false);
        } else if (code == 0) {
            Main.Toast("获取完成" + (summary.Length > 0 ? "：" + summary : ""), false);
            Main.Send("library", null);
        } else {
            Main.Toast("获取未完成" + (summary.Length > 0 ? "：" + summary : ""), true);
            Main.Send("library", null);
        }
        RenderTaskCard();
    }

    void HandleLocalExported(Dictionary<string, object> e) {
        if (exportRequestId.Length > 0 && Json.S(e, "requestId") != exportRequestId) return;
        exportRequestId = "";
        exporting = false;
        var names = new List<string>();
        foreach (var file in Json.L(e, "files")) names.Add(Convert.ToString(file));
        var reused = Json.B(e, "reused");
        Main.Toast((reused ? "复用已有报告" : "报告已生成") + (names.Count > 0 ? "：" + string.Join("，", names.ToArray()) : ""), false);
        RenderExportButton();
        Main.Send("library", null); // 刷新最近报告列表
    }

    void HandleLocalError(Dictionary<string, object> e) {
        var requestId = Json.S(e, "requestId");
        if (exportRequestId.Length > 0 && requestId.Length > 0 && requestId != exportRequestId) return;
        exportRequestId = "";
        exporting = false;
        Main.Toast(ErrorMessage(e, "报告未生成"), true);
        RenderExportButton();
    }

    // ------------------------------------------------------------ 工具
    static bool TryParseDate(string text, out DateTime value) {
        return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    static string ErrorMessage(Dictionary<string, object> e, string fallback) {
        var message = Json.S(e, "message");
        return message.Length > 0 ? message : fallback;
    }

    static string SummaryText(Dictionary<string, object> e) {
        object raw;
        if (e.TryGetValue("summary", out raw) && raw is string) return ((string)raw).Trim();
        return "";
    }

    static string CollectionKindName(string kind) {
        if (kind == "sent") return "发出的评论";
        if (kind == "received") return "收到的评论";
        if (kind == "mentions-comments") return "被 @ 的评论";
        if (kind == "mentions-posts") return "被 @ 的微博";
        if (kind == "user-posts") return "微博";
        return kind.Length > 0 ? kind : "数据";
    }

    static string ModeDisplayName(string mode) {
        if (mode == "sent") return "发出的评论";
        if (mode == "received") return "收到的评论";
        if (mode == "both") return "发出和收到的评论";
        return mode.Length > 0 ? mode : "双向评论";
    }

    static string FormatTime(string iso) {
        if (string.IsNullOrEmpty(iso)) return "";
        DateTime value;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)) {
            var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        return iso;
    }

    static string FormatTimeFromMs(long milliseconds) {
        if (milliseconds <= 0) return "";
        var time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds).ToLocalTime();
        return time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------ 控件样式（页面私有，C# 5）
    static Style InputStyle() {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.BackgroundProperty, Theme.Hover));
        style.Setters.Add(new Setter(TextBox.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(TextBox.BorderBrushProperty, Theme.PanelBorderBrush));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, Theme.Text));
        style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(TextBox.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Theme.AccentDimBrush));
        return style;
    }

    static Style CheckBoxStyle() {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(CheckBox.ForegroundProperty, Theme.Text));
        style.Setters.Add(new Setter(CheckBox.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(CheckBox.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center));

        var template = new ControlTemplate(typeof(CheckBox));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var box = new FrameworkElementFactory(typeof(Border));
        box.Name = "box";
        box.SetValue(Border.WidthProperty, 14.0);
        box.SetValue(Border.HeightProperty, 14.0);
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        box.SetValue(Border.BorderBrushProperty, Theme.PanelBorderBrush);
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.BackgroundProperty, Theme.Hover);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));

        var check = new FrameworkElementFactory(typeof(Path));
        check.Name = "check";
        check.SetValue(Path.DataProperty, Geometry.Parse("M 3,7 L 6,10 L 11,4"));
        check.SetValue(Shape.StrokeProperty, Theme.Accent);
        check.SetValue(Shape.StrokeThicknessProperty, 2.0);
        check.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        check.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        check.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(check);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.ContentSourceProperty, "Content");

        panel.AppendChild(box);
        panel.AppendChild(content);
        template.VisualTree = panel;

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        var checkSetter = new Setter(Shape.VisibilityProperty, Visibility.Visible);
        checkSetter.TargetName = "check";
        checkedTrigger.Setters.Add(checkSetter);
        template.Triggers.Add(checkedTrigger);

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        var boxHoverSetter = new Setter(Border.BackgroundProperty, Theme.Panel);
        boxHoverSetter.TargetName = "box";
        hoverTrigger.Setters.Add(boxHoverSetter);
        template.Triggers.Add(hoverTrigger);

        style.Setters.Add(new Setter(CheckBox.TemplateProperty, template));
        return style;
    }

    static Style ProgressBarStyle() {
        var style = new Style(typeof(ProgressBar));
        style.Setters.Add(new Setter(ProgressBar.ForegroundProperty, Theme.Accent));
        style.Setters.Add(new Setter(ProgressBar.BackgroundProperty, Theme.Hover));
        style.Setters.Add(new Setter(ProgressBar.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ProgressBar.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));

        var template = new ControlTemplate(typeof(ProgressBar));
        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "PART_Track";
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        track.SetValue(Border.BackgroundProperty, Theme.Hover);
        var indicator = new FrameworkElementFactory(typeof(Border));
        indicator.Name = "PART_Indicator";
        indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        indicator.SetValue(Border.BackgroundProperty, Theme.Accent);
        indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        track.AppendChild(indicator);
        template.VisualTree = track;
        style.Setters.Add(new Setter(ProgressBar.TemplateProperty, template));
        return style;
    }

    static Style ButtonStyle(Brush background, Brush hoverBackground, Brush borderBrush, Brush foreground, double fontSize) {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, background));
        style.Setters.Add(new Setter(Button.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, borderBrush));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 5, 10, 5)));
        style.Setters.Add(new Setter(Button.FontSizeProperty, fontSize));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Button.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(Button.VerticalAlignmentProperty, VerticalAlignment.Center));

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        content.SetBinding(FrameworkElement.VerticalAlignmentProperty, new Binding("VerticalContentAlignment") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.AppendChild(content);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        var hoverSetter = new Setter(Border.BackgroundProperty, hoverBackground);
        hoverSetter.TargetName = "bd";
        hover.Setters.Add(hoverSetter);
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        var pressedSetter = new Setter(Border.BackgroundProperty, hoverBackground);
        pressedSetter.TargetName = "bd";
        pressed.Setters.Add(pressedSetter);
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Button.ForegroundProperty, Theme.Muted));
        template.Triggers.Add(disabled);

        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }

    // 轻量下拉选择：按钮 + Popup 列表，避免 WPF 默认 ComboBox 的浅色外观。
    class Dropdown : UserControl {
        readonly Button head;
        readonly Popup popup;
        readonly StackPanel itemsHost;
        readonly List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
        string selectedValue = "";

        public event Action SelectionChanged;

        public Dropdown() {
            head = new Button { Content = "请选择" };
            head.Style = UserDetailPage.ButtonStyle(Theme.Hover, Theme.Panel, Theme.PanelBorderBrush, Theme.Text, 12);
            head.HorizontalAlignment = HorizontalAlignment.Stretch;
            head.HorizontalContentAlignment = HorizontalAlignment.Left;
            head.Click += delegate {
                if (items.Count > 0) popup.IsOpen = true;
            };
            popup = new Popup {
                Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            popup.PlacementTarget = head;
            var border = new Border {
                Background = Theme.Panel, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), MaxHeight = 280,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            itemsHost = new StackPanel();
            scroll.Content = itemsHost;
            border.Child = scroll;
            popup.Child = border;
            Content = head;
        }

        public string Value { get { return selectedValue; } }

        public void SetItems(List<KeyValuePair<string, string>> list, string defaultValue) {
            items.Clear();
            items.AddRange(list);
            itemsHost.Children.Clear();
            selectedValue = "";
            head.Content = "请选择";
            foreach (var item in items) {
                var itemValue = item.Key;
                var itemText = item.Value;
                var button = new Button { Content = itemText };
                button.Style = UserDetailPage.ButtonStyle(Brushes.Transparent, Theme.Hover, Brushes.Transparent, Theme.Text, 12);
                button.BorderThickness = new Thickness(0);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(10, 7, 10, 7);
                button.Click += delegate {
                    selectedValue = itemValue;
                    head.Content = itemText;
                    popup.IsOpen = false;
                    if (SelectionChanged != null) SelectionChanged();
                };
                itemsHost.Children.Add(button);
            }
            Select(defaultValue);
        }

        public void Select(string value) {
            foreach (var item in items) {
                if (item.Key == value) {
                    selectedValue = value;
                    head.Content = item.Value;
                    return;
                }
            }
            selectedValue = "";
            head.Content = "请选择";
        }
    }
}
}
