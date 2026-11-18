using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WeiboReader.Views {
// 盯版聊详情：头部信息与操作行、持续盯防开关、获取数据表单与任务进度、
// 导出数据与最近报告。由 PostListPage 页内切换进入。
public class PostDetailPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    readonly string postId;
    PostRow post;
    readonly StackPanel progressPanel = new StackPanel { Visibility = Visibility.Collapsed };
    readonly TextBlock progressText = new TextBlock { Foreground = Theme.Text, FontSize = 12 };
    readonly ProgressBar progressBar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel reportList = new StackPanel();
    bool running;
    string runningId = "";
    bool deleteArmed;
    bool subPanelOpen;

    public PostDetailPage(MainWindow main, string id) {
        Main = main;
        postId = id;
        RefreshPost();
        Content = Build();
    }
    void RefreshPost() { post = Main.State.FindPost(postId); }

    UIElement Build() {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel();

        var back = new Button { Content = "‹ 返回盯版聊列表", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 10) };
        back.Click += (s, e) => BackToList();
        root.Children.Add(back);

        var head = Theme.Card();
        var headStack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = post != null && post.Title.Length > 0 ? post.Title : "未命名博文", Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.Bold });
        titleRow.Children.Add(new TextBlock { Text = "  微博 ID " + postId, Foreground = Theme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        headStack.Children.Add(titleRow);
        headStack.Children.Add(new TextBlock { Text = post != null ? "发布者 " + Main.State.DisplayName(post.AuthorUid, post.Author) : "本地资料尚未收录该微博", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
        head.Child = headStack;
        root.Children.Add(head);

        var actions = Theme.Card();
        var actionRow = new WrapPanel();
        actionRow.Children.Add(ActionButton("weibo.com ↗", () => {
            var authorUid = post != null ? post.AuthorUid : "";
            if (authorUid.Length > 0) Dialogs.OpenExternal(MainWindow.Instance, "https://weibo.com/" + authorUid + "/" + postId);
            else Main.Toast("缺少作者 UID，无法生成桌面端链接。", true);
        }));
        actionRow.Children.Add(ActionButton("weibo.cn ↗", () => Dialogs.OpenExternal(MainWindow.Instance, "https://weibo.cn/detail/" + postId)));
        actionRow.Children.Add(ActionButton("复制微博 ID", () => { Dialogs.CopyClipboard(postId); Main.Toast("已复制微博 ID。", false); }));
        actionRow.Children.Add(ActionButton("补齐作者头像", () => {
            var payload = Json.Obj();
            Json.Put(payload, "postId", postId);
            Json.Put(payload, "authorUid", post != null ? post.AuthorUid : "");
            Json.Put(payload, "requestId", Guid.NewGuid().ToString());
            Main.Send("postAuthorAvatar", payload);
            Main.Toast("已请求补齐作者头像。", false);
        }));
        var deleteButton = ActionButton("删除盯版聊", () => {
            if (!deleteArmed) { deleteArmed = true; Main.Toast("再次点击「删除盯版聊」确认删除。", true); return; }
            var targets = new List<object>();
            var target = Json.Obj();
            Json.Put(target, "targetKind", "post");
            Json.Put(target, "id", postId);
            targets.Add(target);
            var plan = Json.Obj();
            Json.Put(plan, "targets", targets);
            Main.Send("manageDeletePlan", plan);
            Main.Send("manageDeleteConfirm", plan);
            Main.Toast("已请求删除盯版聊。", false);
        });
        actionRow.Children.Add(deleteButton);
        actions.Child = actionRow;
        root.Children.Add(actions);

        root.Children.Add(SubscriptionCard());
        root.Children.Add(FetchCard());
        root.Children.Add(ExportCard());

        var reportCard = Theme.Card();
        reportCard.Child = new StackPanel();
        var reportStack = (StackPanel)reportCard.Child;
        reportStack.Children.Add(Theme.Title("最近报告"));
        reportStack.Children.Add(new TextBlock { Text = "每位盯版聊保留 1 份报告。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 6) });
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
    UIElement SubscriptionCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var follow = Main.State.FindFollow(null, postId, "post");
        var check = new CheckBox { Content = "持续盯防", Foreground = Theme.Text, IsChecked = follow != null && follow.Enabled, VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = follow != null ? (follow.Enabled ? "盯防中" : "已暂停") : "未开启", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var toggleButton = ActionButton(follow != null ? "盯防设置" : "开启盯防", () => {
            subPanelOpen = !subPanelOpen;
            panel.Visibility = subPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        });
        row.Children.Add(check);
        row.Children.Add(hint);
        row.Children.Add(toggleButton);
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
            Json.Put(settings, "postId", postId);
            Json.Put(settings, "uid", post != null ? post.AuthorUid : "");
            Json.Put(settings, "mode", "post-comments");
            Json.Put(settings, "frequency", "fixed");
            Json.Put(settings, "interval", interval);
            Json.Put(settings, "roundLimit", round);
            Json.Put(settings, "dailyBudget", budget);
            Json.Put(settings, "enabled", true);
            var payload = Json.Obj();
            Json.Put(payload, "postId", postId);
            Json.Put(payload, "targetKind", "post");
            Json.Put(payload, "scope", "post");
            Json.Put(payload, "settings", settings);
            Main.Send("followSave", payload);
            Main.Toast("盯防设置已保存。", false);
            panel.Visibility = Visibility.Collapsed;
        };
        panel.Children.Add(save);
        stack.Children.Add(panel);
        check.Click += (s, e) => {
            var payload = Json.Obj();
            Json.Put(payload, "postId", postId);
            Json.Put(payload, "targetKind", "post");
            if (check.IsChecked == true) {
                Json.Put(payload, "scope", "post");
                Json.Put(payload, "settings", Json.Put(Json.Put(Json.Put(Json.Put(Json.Put(Json.Obj(), "postId", postId), "mode", "post-comments"), "interval", 5), "roundLimit", 10), "enabled", true));
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
    UIElement FetchCard() {
        var card = Theme.Card();
        var stack = new StackPanel();
        stack.Children.Add(Theme.Title("获取数据 · 盯版聊下的评论"));
        stack.Children.Add(new TextBlock { Text = "读取这条微博下的可见评论；引用上下文不计入正式条数。", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 8) });

        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var limitBox = new TextBox { Text = "50", Width = 160 };
        var unlimited = new CheckBox { Content = "全部（仅限所选范围）", Foreground = Theme.Text, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        limitRow.Children.Add(Theme.Label("本次覆盖条数"));
        limitRow.Children.Add(limitBox);
        limitRow.Children.Add(unlimited);
        stack.Children.Add(limitRow);

        var fetch = new Button { Content = "获取数据", Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };
        fetch.Click += (s, e) => {
            if (running) { Main.Send("stop", null); Main.Toast("已请求停止当前任务。", false); return; }
            long limit;
            if (!long.TryParse(limitBox.Text.Trim(), out limit) || limit < 1) { Main.Toast("覆盖条数必须是正整数。", true); return; }
            var payload = Json.Obj();
            Json.Put(payload, "target", postId);
            Json.Put(payload, "subject", "post");
            Json.Put(payload, "mode", "post-comments");
            Json.Put(payload, "policy", "cache");
            Json.Put(payload, "syncOnly", true);
            Json.Put(payload, "task", "sync");
            Json.Put(payload, "syncOperation", "regular");
            Json.Put(payload, "dateRange", false);
            Json.Put(payload, "markUnread", false);
            Json.Put(payload, "limit", unlimited.IsChecked == true ? "all" : limitBox.Text.Trim());
            Json.Put(payload, "unlimited", unlimited.IsChecked == true);
            Json.Put(payload, "from", "");
            Json.Put(payload, "to", "");
            Json.Put(payload, "returnBudget", "");
            Json.Put(payload, "pages", "");
            Json.Put(payload, "pageSize", "");
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
        limitRow.Children.Add(Theme.Label("报告条数"));
        limitRow.Children.Add(limitBox);
        stack.Children.Add(limitRow);
        var button = new Button { Content = "生成并打开报告", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 8, 0, 0) };
        button.Click += (s, e) => {
            var payload = Json.Obj();
            Json.Put(payload, "target", postId);
            Json.Put(payload, "subject", "post");
            Json.Put(payload, "mode", "post-comments");
            Json.Put(payload, "policy", "database");
            Json.Put(payload, "syncOnly", false);
            Json.Put(payload, "task", "report");
            Json.Put(payload, "dateRange", false);
            Json.Put(payload, "markUnread", false);
            Json.Put(payload, "limit", limitBox.Text.Trim());
            Json.Put(payload, "unlimited", false);
            Json.Put(payload, "from", "");
            Json.Put(payload, "to", "");
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
    void RenderReports() {
        reportList.Children.Clear();
        var found = false;
        foreach (var report in Main.State.Reports) {
            if (report.Category != "post-comments" || report.PostId != postId) continue;
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
    void BackToList() {
        if (Main.State.Posts.Count >= 0) {
            var list = new PostListPage(Main);
            Content = list;
            list.OnShown();
        }
    }

    public void OnEngineEvent(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        if (type == "started") {
            var query = Json.M(e.ContainsKey("query") ? e["query"] : null);
            if (query != null && Json.S(query, "target") == postId) {
                running = true;
                runningId = Json.S(e, "id");
                progressText.Text = "正在获取评论…";
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
        } else if (type == "postAuthorAvatarResult") {
            var updated = Json.B(e, "updated");
            Main.Toast(updated ? "作者头像已更新。" : Json.S(e, "message"), !updated);
        } else if (type == "userDeleted" || type == "postDeleted") {
            if (Json.S(e, "postId") == postId) BackToList();
        } else if (type == "libraryData") {
            RenderReports();
        }
    }
    public void OnShown() {
        RefreshPost();
        Main.Send("library", null);
        RenderReports();
    }
}
}
