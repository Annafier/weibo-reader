using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WeiboReader.Views {
// 盯版聊首页：搜索、添加、筛选与博文卡片列表；点击卡片进入详情（页内视图切换）。
public class PostListPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    readonly StackPanel cardList = new StackPanel();
    readonly TextBox searchBox = new TextBox();
    readonly TextBox addBox = new TextBox();
    readonly TextBlock emptyHint = new TextBlock();
    string filter = "all";
    bool detailMode;

    public PostListPage(MainWindow main) {
        Main = main;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tools = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchBox.MinWidth = 320;
        searchBox.TextChanged += (s, e) => Render();
        Grid.SetColumn(searchBox, 0);
        tools.Children.Add(searchBox);
        var addPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        addBox.Width = 320;
        addBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) AddPost(); };
        addPanel.Children.Add(addBox);
        var addButton = new Button { Content = "添加盯版聊", Padding = new Thickness(14, 6, 14, 6) };
        addButton.Click += (s, e) => AddPost();
        addPanel.Children.Add(addButton);
        Grid.SetColumn(addPanel, 1);
        tools.Children.Add(addPanel);
        top.Children.Add(tools);

        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        chips.Children.Add(Chip("全部盯版聊", "all"));
        chips.Children.Add(Chip("重点紧盯", "fav"));
        top.Children.Add(chips);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        cardList.Margin = new Thickness(0);
        scroll.Content = cardList;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }
    Button Chip(string label, string key) {
        var button = new Button { Content = label, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 8), Tag = key };
        button.Click += (s, e) => { filter = key; Render(); };
        return button;
    }
    void AddPost() {
        var value = addBox.Text.Trim();
        if (value.Length == 0) { Main.Toast("请输入微博 ID 或微博链接。", true); return; }
        Main.Send("postAdd", Json.Put(Json.Obj(), "value", value));
        addBox.Text = "";
        Main.Toast("已请求添加盯版聊，添加完成后列表刷新。", false);
    }

    public void OnEngineEvent(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        if (type == "postDeleted") {
            var postId = Json.S(e, "postId");
            Main.State.Posts.RemoveAll(p => p.PostId == postId);
            Render();
        } else if (type == "library" || type == "postsHome") {
            Render();
        }
    }
    public void OnShown() {
        detailMode = false;
        Main.Send("library", null);
        Render();
    }
    public void OpenDetail(string postId) {
        var detail = new PostDetailPage(Main, postId);
        Content = detail;
        detailMode = true;
        detail.OnShown();
    }
    void BackToList() {
        detailMode = false;
        OnShown();
    }

    List<PostRow> VisiblePosts() {
        var term = searchBox.Text.Trim().ToLowerInvariant();
        var list = new List<PostRow>();
        foreach (var post in Main.State.Posts) {
            if (filter == "fav" && !post.Favorite) continue;
            if (term.Length > 0) {
                var haystack = (post.PostId + " " + post.Author + " " + post.Title).ToLowerInvariant();
                if (!haystack.Contains(term)) continue;
            }
            list.Add(post);
        }
        list.Sort((a, b) => string.CompareOrdinal(b.UpdatedAt, a.UpdatedAt));
        return list;
    }
    void Render() {
        if (detailMode) return;
        cardList.Children.Clear();
        var posts = VisiblePosts();
        if (Main.State.Posts.Count == 0 || posts.Count == 0) {
            emptyHint.Text = Main.State.Posts.Count == 0
                ? "还没有盯版聊：粘贴微博链接或 ID 添加；添加不会自动获取评论。"
                : "没有匹配的盯版聊，调整搜索或筛选。";
            emptyHint.Foreground = Theme.Muted;
            emptyHint.Margin = new Thickness(8, 10, 0, 0);
            cardList.Children.Add(emptyHint);
            return;
        }
        foreach (var post in posts) cardList.Children.Add(Card(post));
    }
    Border Card(PostRow post) {
        var card = Theme.Card();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = new Border {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = Theme.AccentDimBrush, Child = new TextBlock {
                Text = post.Title.Length > 0 ? post.Title.Substring(0, 1) : "博",
                Foreground = Theme.Text, FontSize = 17, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        };
        grid.Children.Add(avatar);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = post.Title.Length > 0 ? post.Title : "未命名博文", Foreground = Theme.Text, FontSize = 14, FontWeight = FontWeights.SemiBold, MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis });
        titleRow.Children.Add(new TextBlock { Text = "  微博 ID " + post.PostId, Foreground = Theme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        info.Children.Add(titleRow);
        var facts = string.Format("发布者 {0} · 数据更新 {1}", Main.State.DisplayName(post.AuthorUid, post.Author), post.UpdatedAt.Length > 0 ? post.UpdatedAt : "尚未获取");
        info.Children.Add(new TextBlock { Text = facts, Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        if (post.UnreadUnavailable) info.Children.Add(new TextBlock { Text = "未读状态暂不可用", Foreground = Theme.Muted, FontSize = 11 });
        else if (post.Unread > 0) {
            var badge = new Border {
                BorderBrush = Theme.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = post.Unread + " 条未读", Foreground = Theme.Accent, FontSize = 11 },
            };
            info.Children.Add(badge);
        }
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var star = new Button { Content = post.Favorite ? "★" : "☆", FontSize = 16, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 8, 0) };
        star.Click += (s, e) => {
            var payload = Json.Obj();
            Json.Put(payload, "postId", post.PostId);
            Json.Put(payload, "on", !post.Favorite);
            Main.Send("postFavorite", payload);
            post.Favorite = !post.Favorite;
            Render();
        };
        right.Children.Add(star);
        var open = new Button { Content = "查看评论 ›", Padding = new Thickness(12, 5, 12, 5) };
        open.Click += (s, e) => OpenDetail(post.PostId);
        right.Children.Add(open);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        card.Child = grid;
        card.Cursor = Cursors.Hand;
        card.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) OpenDetail(post.PostId); };
        return card;
    }
}
}
