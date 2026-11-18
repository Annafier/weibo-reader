using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WeiboReader.Views {
// 盯单人详情：头部与操作行、持续盯防、获取数据表单与任务进度、导出数据、
// 最近报告、定向互动档案。由 UserListPage 页内切换进入（SetUser 设定对象）。
public class UserDetailPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    readonly StackPanel progressPanel = new StackPanel { Visibility = Visibility.Collapsed };
    readonly TextBlock progressText = new TextBlock { Foreground = Theme.Text, FontSize = 12 };
    readonly ProgressBar progressBar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel reportList = new StackPanel();
    readonly StackPanel pairPanel = new StackPanel { Visibility = Visibility.Collapsed };
    string uid = "";
    bool running;
    string runningId = "";
    bool deleteArmed;
    bool subPanelOpen;
    string pairRequestId = "";

    public event Action BackRequested;
    public string CurrentUid { get { return uid; } }
    public UserDetailPage(MainWindow main) {
        Main = main;
        Content = new TextBlock { Text = "选择一位盯单人查看详情。", Foreground = Theme.Muted, Margin = new Thickness(12) };
    }
    public void SetUser(string value) {
        uid = value;
        deleteArmed = false;
        running = false;
        runningId = "";
        Content = Build();
    }
    UserRow User() { return Main.State.FindUser(uid); }
    string Nickname() {
        var user = User();
        return Main.State.DisplayName(uid, user != null ? user.Nickname : "");
    }

    UIElement Build() {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel();

        var back = new Button { Content = "‹ 返回盯单人列表", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 10) };
        back.Click += (s, e) => { if (BackRequested != null) BackRequested(); };
        root.Children.Add(back);

        var head = Theme.Card();
        var headStack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = Nickname(), Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.Bold });
        titleRow.Children.Add(new TextBlock { Text = "  UID " + uid, Foreground = Theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        headStack.Children.Add(titleRow);
        headStack.Children.Add(new TextBlock { Text = "按互动发生时间阅读本地数据；获取与导出由你主动操作。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        head.Child = headStack;
        root.Children.Add(head);

        var actions = Theme.Card();
        var actionRow = new WrapPanel();
        actionRow.Children.Add(ActionButton("打开微博主页 ↗", () => Dialogs.OpenExternal(MainWindow.Instance, "https://weibo.cn/u/" + uid)));
        var noteBox = new TextBox { Width = 200 };
        actionRow.Children.Add(WrapField("备注", noteBox));
        actionRow.Children.Add(ActionButton("保存备注", () => {
            var payload = Json.Obj();
            Json.Put(payload, "uid", uid);
            Json.Put(payload, "text", noteBox.Text);
            Main.Send("userNoteSave", payload);
            Main.Toast("备注已保存。", false);
        }));
        actionRow.Children.Add(DeleteButton());
        actions.Child = actionRow;
        root.Children.Add(actions);

        root.Children.Add(SubscriptionCard());
        root.Children.Add(FetchCard());
        root.Children.Add(ExportCard());
        root.Children.Add(PairCard());

        var reportCard = Theme.Card();
        reportCard.Child = new StackPanel();
        var reportStack = (StackPanel)reportCard.Child;
        reportStack.Children.Add(Theme.Title("最近报告"));
        reportStack.Children.Add(new TextBlock { Text = "每位盯单人保留 1 份报告。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 6) });
        reportStack.Children.Add(reportList);
        root.Children.Add(reportCard);
        RenderReports();

        scroll.Content = root;
        return scroll;
    }
    Button ActionButton(string label, Action click) {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (s, e) => click();
        return button;
    }
    UIElement WrapField(string label, Control box) {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Theme.Label(label));
        stack.Children.Add(box);
        return stack;
    }
    Button DeleteButton() {
        var button = new Button { Content = "删除盯单人", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (s, e) => {
            if (!deleteArmed) { deleteArmed = true; button.Content = "确认删除？"; button.Background = Theme.Danger; Main.Toast("再次点击确认删除盯单人及全部本地数据。", true); return; }
            var plan = Json.Obj();
            Json.Put(plan, "uid", uid);
            Main.Send("userDeletePlan", plan);
            var confirm = Json.Obj();
            Json.Put(confirm, "uid", uid);
            Json.Put(confirm, "confirm", true);
            Main.Send("userDeleteConfirm", confirm);
            Main.Toast("已请求删除盯单人。", false);
        };
        return button;
    }
    UIElement SubscriptionCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var follow = Main.State.FindFollow(uid, "", "user");
        var check = new CheckBox { Content = "持续盯防", Foreground = Theme.Text, IsChecked = follow != null && follow.Enabled, VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = follow != null ? (follow.Enabled ? "盯防中" : "已暂停") : "未开启", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var toggle = ActionButton(follow != null ? "盯防设置" : "开启盯防", () => {
            subPanelOpen = !subPanelOpen;
            panel.Visibility = subPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        });
        row.Children.Add(check);
        row.Children.Add(hint);
        row.Children.Add(toggle);
        stack.Children.Add(row);

        var fields = new Grid();
        for (int i = 0; i < 3; i++) fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var intervalBox = new TextBox { Text = "5" };
        var roundBox = new TextBox { Text = "10" };
        var budgetBox = new TextBox { Text = "300" };
        AddField(fields, 0, "检查间隔（分钟）", intervalBox);
        AddField(fields, 1, "每轮条数", roundBox);
        AddField(fields, 2, "每日额度（credits）", budgetBox);
        panel.Children.Add(fields);
        var save = new Button { Content = "保存盯防设置", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 10, 0, 0) };
        save.Click += (s, e) => {
            long interval, round, budget;
            if (!long.TryParse(intervalBox.Text.Trim(), out interval) || interval < 1) { Main.Toast("检查间隔必须是正整数分钟。", true); return; }
            if (!long.TryParse(roundBox.Text.Trim(), out round) || round < 1) { Main.Toast("每轮条数必须是正整数。", true); return; }
            if (!long.TryParse(budgetBox.Text.Trim(), out budget) || budget < 0) { Main.Toast("每日额度必须是非负整数。", true); return; }
            var settings = Json.Obj();
            Json.Put(settings, "uid", uid);
            Json.Put(settings, "mode", "comments-both");
            Json.Put(settings, "frequency", "fixed");
            Json.Put(settings, "interval", interval);
            Json.Put(settings, "roundLimit", round);
            Json.Put(settings, "dailyBudget", budget);
            Json.Put(settings, "enabled", true);
            Json.Put(settings, "timeZone", "UTC");
            var payload = Json.Obj();
            Json.Put(payload, "uid", uid);
            Json.Put(payload, "targetKind", "user");
            Json.Put(payload, "scope", "user");
            Json.Put(payload, "settings", settings);
            Main.Send("followSave", payload);
            Main.Toast("盯防设置已保存。", false);
            panel.Visibility = Visibility.Collapsed;
        };
        panel.Children.Add(save);
        stack.Children.Add(panel);
        check.Click += (s, e) => {
            var payload = Json.Obj();
            Json.Put(payload, "uid", uid);
            Json.Put(payload, "targetKind", "user");
            if (check.IsChecked == true) {
                Json.Put(payload, "scope", "user");
                var settings = Json.Obj();
                Json.Put(settings, "uid", uid);
                Json.Put(settings, "mode", "comments-both");
                Json.Put(settings, "frequency", "fixed");
                Json.Put(settings, "interval", 5);
                Json.Put(settings, "roundLimit", 10);
                Json.Put(settings, "dailyBudget", 300);
                Json.Put(settings, "enabled", true);
                Json.Put(settings, "timeZone", "UTC");
                Json.Put(payload, "settings", settings);
                Main.Send("followSave", payload);
                Main.Toast("已开启持续盯防（默认 5 分钟，可在设置中调整）。", false);
            } else {
                Main.Send("followPause", payload);
                Main.Toast("已暂停持续盯防。", false);
            }
        };
        card.Child = stack;
        return card;
    }
    static void AddField(Grid grid, int column, string label, TextBox box) {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(Theme.Label(label));
        box.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(box);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }
    static ComboBox ModeCombo() {
        var combo = new ComboBox { Width = 220, SelectedIndex = 0 };
        combo.Items.Add("发出和收到的评论");
        combo.Items.Add("发出的评论");
        combo.Items.Add("收到的评论");
        combo.Items.Add("被 @ 的评论");
        combo.Items.Add("被 @ 的微博");
        combo.Items.Add("评论与 @");
        return combo;
    }
    static string ModeValue(ComboBox combo) {
        switch (combo.SelectedIndex) {
            case 1: return "comments-sent";
            case 2: return "comments-received";
            case 3: return "mentions-comments";
            case 4: return "mentions-posts";
            case 5: return "comments-and-mentions";
        }
        return "comments-both";
    }
    UIElement FetchCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("获取数据"));
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        modeRow.Children.Add(Theme.Label("互动内容"));
        var combo = ModeCombo();
        modeRow.Children.Add(combo);
        stack.Children.Add(modeRow);

        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var limitBox = new TextBox { Text = "50", Width = 160 };
        var unlimited = new CheckBox { Content = "全部（仅限所选范围）", Foreground = Theme.Text, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        limitRow.Children.Add(Theme.Label("本次覆盖条数（双向各半）"));
        limitRow.Children.Add(limitBox);
        limitRow.Children.Add(unlimited);
        stack.Children.Add(limitRow);

        var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var rangeCheck = new CheckBox { Content = "指定日期范围", Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center };
        var fromBox = new TextBox { Text = "2026-11-01", Width = 110, IsEnabled = false };
        var toBox = new TextBox { Text = "", Width = 110, IsEnabled = false };
        rangeCheck.Click += (s, e) => { var on = rangeCheck.IsChecked == true; fromBox.IsEnabled = on; toBox.IsEnabled = on; };
        rangeRow.Children.Add(rangeCheck);
        rangeRow.Children.Add(WrapField("起始日期", fromBox));
        rangeRow.Children.Add(WrapField("截止日期", toBox));
        stack.Children.Add(rangeRow);

        var fetch = new Button { Content = "获取数据", Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };
        fetch.Click += (s, e) => {
            if (running) { Main.Send("stop", null); Main.Toast("已请求停止当前任务。", false); return; }
            long limit;
            if (!long.TryParse(limitBox.Text.Trim(), out limit) || limit < 1) { Main.Toast("覆盖条数必须是正整数。", true); return; }
            var rangeOn = rangeCheck.IsChecked == true;
            var from = rangeOn ? fromBox.Text.Trim() : "";
            var to = rangeOn ? toBox.Text.Trim() : "";
            if (rangeOn && (from.Length == 0 || to.Length == 0 || string.Compare(from, to, StringComparison.Ordinal) > 0)) { Main.Toast("日期范围无效：需同时填写起止日期且起始不晚于截止。", true); return; }
            var payload = Json.Obj();
            Json.Put(payload, "target", uid);
            Json.Put(payload, "subject", "user");
            Json.Put(payload, "mode", ModeValue(combo));
            Json.Put(payload, "policy", rangeOn ? "update" : "cache");
            Json.Put(payload, "syncOnly", true);
            Json.Put(payload, "task", "sync");
            Json.Put(payload, "syncOperation", "regular");
            Json.Put(payload, "dateRange", rangeOn);
            Json.Put(payload, "markUnread", false);
            Json.Put(payload, "peerUid", "");
            Json.Put(payload, "limit", unlimited.IsChecked == true ? "all" : limitBox.Text.Trim());
            Json.Put(payload, "unlimited", unlimited.IsChecked == true);
            Json.Put(payload, "returnBudget", "");
            Json.Put(payload, "pages", "");
            Json.Put(payload, "pageSize", "");
            Json.Put(payload, "from", from);
            Json.Put(payload, "to", to);
            Json.Put(payload, "before", "");
            Json.Put(payload, "seekLimit", "");
            Json.Put(payload, "emptyWindowLimit", "");
            Json.Put(payload, "historyRecovery", true);
            Json.Put(payload, "downloadImages", false);
            Json.Put(payload, "fullText", false);
            Json.Put(payload, "verbose", false);
            Json.Put(payload, "preview", false);
            var run = Json.Obj();
            Json.Put(run, "requestId", Guid.NewGuid().ToString());
            Json.Put(run, "query", payload);
            runningId = Json.S(run, "requestId");
            running = true;
            fetch.Content = "停止";
            Main.Send("run", run);
        };
        stack.Children.Add(fetch);
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressBar);
        stack.Children.Add(progressPanel);
        card.Child = stack;
        return card;
    }
    UIElement ExportCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("导出数据"));
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var limitBox = new TextBox { Text = "200", Width = 160 };
        var markUnread = new CheckBox { Content = "仅未读互动", Foreground = Theme.Text, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        limitRow.Children.Add(Theme.Label("报告条数"));
        limitRow.Children.Add(limitBox);
        limitRow.Children.Add(markUnread);
        stack.Children.Add(limitRow);
        var button = new Button { Content = "生成并打开报告", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 8, 0, 0) };
        button.Click += (s, e) => {
            var payload = Json.Obj();
            Json.Put(payload, "target", uid);
            Json.Put(payload, "subject", "user");
            Json.Put(payload, "mode", "comments-both");
            Json.Put(payload, "policy", "database");
            Json.Put(payload, "syncOnly", false);
            Json.Put(payload, "task", "report");
            Json.Put(payload, "dateRange", false);
            Json.Put(payload, "markUnread", markUnread.IsChecked == true);
            Json.Put(payload, "peerUid", "");
            Json.Put(payload, "limit", limitBox.Text.Trim());
            Json.Put(payload, "unlimited", false);
            Json.Put(payload, "from", "");
            Json.Put(payload, "to", "");
            Json.Put(payload, "before", "");
            Json.Put(payload, "historyRecovery", false);
            Json.Put(payload, "downloadImages", false);
            Json.Put(payload, "fullText", false);
            Json.Put(payload, "verbose", false);
            Json.Put(payload, "preview", false);
            Main.Send("output", payload);
        };
        stack.Children.Add(button);
        card.Child = stack;
        return card;
    }
    UIElement PairCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("定向互动档案"));
        stack.Children.Add(new TextBlock { Text = "从双方本地资料提取全部互动，生成带数据看板的离线报告（0 credits）。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 6) });
        var pairButton = ActionButton("生成互动档案", () => {
            pairPanel.Visibility = pairPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (pairPanel.Visibility == Visibility.Visible) RenderPairPanel(pairPanel);
        });
        stack.Children.Add(pairButton);
        stack.Children.Add(pairPanel);
        card.Child = stack;
        return card;
    }
    void RenderPairPanel(StackPanel panel) {
        panel.Children.Clear();
        var combo = new ComboBox { Width = 260 };
        var others = Main.State.Users;
        if (others.Count <= 1) {
            panel.Children.Add(new TextBlock { Text = "先在其他用户页获取互动数据，再回到这里生成互动档案。", Foreground = Theme.Muted, FontSize = 11 });
            return;
        }
        for (int i = 0; i < others.Count; i++) {
            if (others[i].Uid == uid) continue;
            var option = new ComboBoxItem { Content = (others[i].Nickname.Length > 0 ? others[i].Nickname : others[i].Uid) + " · " + others[i].Uid, Tag = others[i].Uid };
            combo.Items.Add(option);
            if (combo.Items.Count == 1) combo.SelectedIndex = 0;
        }
        var go = new Button { Content = "生成互动档案", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(10, 0, 0, 0) };
        go.Click += (s, e) => {
            var item = combo.SelectedItem as ComboBoxItem;
            if (item == null) { Main.Toast("请选择互动对方。", true); return; }
            pairRequestId = Guid.NewGuid().ToString();
            Main.State.PendingPairRequest = pairRequestId;
            var payload = Json.Obj();
            Json.Put(payload, "uidA", uid);
            Json.Put(payload, "uidB", Convert.ToString(item.Tag));
            Json.Put(payload, "requestId", pairRequestId);
            Main.Send("pairReport", payload);
            panel.Visibility = Visibility.Collapsed;
            Main.Toast("正在生成互动档案…", false);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(combo);
        row.Children.Add(go);
        panel.Children.Add(row);
    }
    void RenderReports() {
        reportList.Children.Clear();
        var found = false;
        foreach (var report in Main.State.Reports) {
            if (report.Category != "user-comments" || report.Uid != uid) continue;
            found = true;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = report.Name + " · " + report.Count + " 条", Foreground = Theme.Blue, FontSize = 12 });
            text.Children.Add(new TextBlock { Text = report.ModifiedText, Foreground = Theme.Muted, FontSize = 11 });
            Grid.SetColumn(text, 0);
            row.Children.Add(text);
            var open = new Button { Content = "打开报告", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0) };
            var openPayload = Json.Obj();
            Json.Put(openPayload, "id", report.Id);
            Json.Put(openPayload, "kind", "report");
            open.Click += (s, e) => Main.Send("open", openPayload);
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            var folder = new Button { Content = "打开文件夹", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0) };
            var folderPayload = Json.Obj();
            Json.Put(folderPayload, "id", report.Id);
            Json.Put(folderPayload, "kind", "folder");
            folder.Click += (s, e) => Main.Send("open", folderPayload);
            Grid.SetColumn(folder, 2);
            row.Children.Add(folder);
            reportList.Children.Add(row);
        }
        if (!found) reportList.Children.Add(new TextBlock { Text = "尚无本地报告；获取数据后即可导出。", Foreground = Theme.Muted, FontSize = 11 });
    }

    public void OnEngineEvent(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        if (type == "started") {
            var query = Json.M(e.ContainsKey("query") ? e["query"] : null);
            if (query != null && Json.S(query, "target") == uid) {
                running = true;
                runningId = Json.S(e, "id");
                progressText.Text = "正在准备…";
                progressBar.Value = 0;
                progressPanel.Visibility = Visibility.Visible;
            }
        } else if (type == "progress") {
            if (running) {
                progressText.Text = Json.S(e, "message");
                var percent = Json.N(e, "percent");
                if (percent > 0) progressBar.Value = percent;
            }
        } else if (type == "collection") {
            if (running) progressText.Text = "已读取 " + Json.N(e, "fetched") + " 条";
        } else if (type == "finished") {
            if (Json.S(e, "id") == runningId) {
                running = false;
                progressPanel.Visibility = Visibility.Collapsed;
                var code = Json.N(e, "code");
                Main.Toast(code == 0 ? "数据已保存。" : "任务结束，代码 " + code + "；详见报告或日志。", code != 0);
                Main.Send("library", null);
            }
        } else if (type == "pairReportResult") {
            if (Json.S(e, "requestId") == pairRequestId) {
                Main.Toast("互动档案已生成：" + Json.S(e, "file"), false);
            }
        } else if (type == "userDeleted") {
            if (Json.S(e, "uid") == uid && BackRequested != null) BackRequested();
        } else if (type == "libraryData") {
            RenderReports();
        }
    }
    public void OnShown() {
        Main.Send("library", null);
        RenderReports();
    }
}
}
