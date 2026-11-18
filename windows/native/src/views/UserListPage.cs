using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WeiboReader.Views {
// 盯单人首页：查找、筛选 chips、添加盯单人、用户卡片列表。
// 详情视图（UserDetailPage）作为子控件挂在本控件内，由 OpenDetail/ShowList 切换。
// 数据来自 Main.State（MainWindow 已把 homeUsers/libraryData/followState 等写入 State）。
public class UserListPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;

    readonly Grid root = new Grid();
    Grid listRoot;
    StackPanel listContent;
    TextBox searchBox;
    TextBox addBox;
    Button chipAll;
    Button chipFavorite;
    Button chipUnread;
    string filter = "all";
    // 重点紧盯来自 library 事件的 favorites 数组（AppState 不缓存），页面自行留存。
    readonly HashSet<string> favorites = new HashSet<string>();
    // homeUsers 原始事件里的备注与最近检查时间（AppState 不缓存），页面自行留存。
    readonly Dictionary<string, string> noteByUid = new Dictionary<string, string>();
    readonly Dictionary<string, string> lastCheckByUid = new Dictionary<string, string>();
    // library 事件会触发 MainWindow 对本页再次调用 OnShown；若在 OnShown 里无条件
    // 发 library 会形成 library → homeUsers → OnShown → library 的刷新回环。
    bool skipNextLibraryRequest;
    bool showingDetail;
    UserDetailPage detail;

    public UserListPage(MainWindow main) {
        Main = main;
        Build();
    }

    // ------------------------------------------------------------ 布局
    void Build() {
        listRoot = new Grid();
        listRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(Theme.Title("盯单人"));
        header.Children.Add(new TextBlock {
            Text = "按互动发生时间阅读本地数据；获取与导出由你主动操作。",
            Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 4, 0, 0),
        });

        var searchLabel = Theme.Label("查找盯单人（昵称或 UID）");
        searchLabel.Margin = new Thickness(0, 12, 0, 4);
        header.Children.Add(searchLabel);
        searchBox = new TextBox { Style = InputStyle() };
        searchBox.TextChanged += delegate { RenderList(); };
        header.Children.Add(searchBox);

        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        chipAll = MakeChip("全部盯单人", "all");
        chipFavorite = MakeChip("重点紧盯", "favorite");
        chipUnread = MakeChip("未读", "unread");
        chips.Children.Add(chipAll);
        chips.Children.Add(chipFavorite);
        chips.Children.Add(chipUnread);
        header.Children.Add(chips);

        var addLabel = Theme.Label("添加盯单人（UID 或用户主页链接）");
        addLabel.Margin = new Thickness(0, 12, 0, 4);
        header.Children.Add(addLabel);
        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addBox = new TextBox { Style = InputStyle() };
        addBox.KeyDown += AddOnKeyDown;
        Grid.SetColumn(addBox, 0);
        addRow.Children.Add(addBox);
        var addButton = new Button { Content = "添加盯单人", Style = ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 12) };
        addButton.Margin = new Thickness(8, 0, 0, 0);
        addButton.Click += delegate { AddUser(); };
        Grid.SetColumn(addButton, 1);
        addRow.Children.Add(addButton);
        header.Children.Add(addRow);

        Grid.SetRow(header, 0);
        listRoot.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        listContent = new StackPanel();
        scroll.Content = listContent;
        Grid.SetRow(scroll, 1);
        listRoot.Children.Add(scroll);

        root.Children.Add(listRoot);
        Content = root;
        ApplyChipStyles();
        RenderList();
    }

    // ------------------------------------------------------------ 列表渲染
    void RenderList() {
        if (listContent == null) return;
        listContent.Children.Clear();
        if (Main == null) return; // SelfTest 以 null 构造本页
        var users = Main.State.Users;
        if (users.Count == 0) {
            listContent.Children.Add(EmptyCard("还没有盯单人。", "在上方输入 UID 或用户主页链接，添加第一个盯单人。"));
            return;
        }
        var term = (searchBox.Text ?? "").Trim().ToLowerInvariant();
        var rows = new List<UserRow>();
        foreach (var user in users) {
            if (user == null || string.IsNullOrEmpty(user.Uid)) continue;
            if (filter == "favorite" && !IsFavorite(user.Uid)) continue;
            if (filter == "unread" && user.Unread <= 0) continue;
            if (term.Length > 0) {
                var haystack = ((user.Nickname ?? "") + " " + user.Uid + " " + NoteOf(user.Uid)).ToLowerInvariant();
                if (!haystack.Contains(term)) continue;
            }
            rows.Add(user);
        }
        if (rows.Count == 0) {
            if (filter == "unread") listContent.Children.Add(EmptyCard("暂无符合条件的未读用户。", ""));
            else if (filter == "favorite") listContent.Children.Add(EmptyCard("暂无重点紧盯用户。", "点击卡片右侧的星标，把用户加入重点紧盯。"));
            else listContent.Children.Add(EmptyCard("没有匹配的用户。", "换个关键词试试。"));
            return;
        }
        rows.Sort(CompareRows);
        foreach (var user in rows) listContent.Children.Add(UserCard(user));
    }

    int CompareRows(UserRow a, UserRow b) {
        var fa = IsFavorite(a.Uid) ? 1 : 0;
        var fb = IsFavorite(b.Uid) ? 1 : 0;
        if (fa != fb) return fb - fa; // 重点紧盯排前
        return string.CompareOrdinal(a.Nickname ?? "", b.Nickname ?? "");
    }

    bool IsFavorite(string uid) {
        if (favorites.Contains(uid)) return true;
        var row = Main != null ? Main.State.FindUser(uid) : null;
        return row != null && row.Favorite;
    }

    string NoteOf(string uid) {
        string note;
        return noteByUid.TryGetValue(uid, out note) ? (note ?? "") : "";
    }

    UIElement EmptyCard(string title, string hint) {
        var card = Theme.Card();
        var stack = new StackPanel { Margin = new Thickness(0, 28, 0, 28) };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Theme.Muted, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
        if (!string.IsNullOrEmpty(hint)) {
            stack.Children.Add(new TextBlock {
                Text = hint, Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            });
        }
        card.Child = stack;
        return card;
    }

    UIElement UserCard(UserRow user) {
        var card = Theme.Card();
        card.Margin = new Thickness(0, 0, 0, 10);
        card.Cursor = Cursors.Hand;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = Avatar(user.Nickname, 44);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(avatar, 0);
        grid.Children.Add(avatar);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        info.Children.Add(new TextBlock {
            Text = user.Nickname.Length > 0 ? user.Nickname : "用户 " + user.Uid,
            Foreground = Theme.Text, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        info.Children.Add(new TextBlock { Text = "UID " + user.Uid, Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });

        var metaRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (user.Unread > 0) {
            var badge = new Border {
                BorderBrush = Theme.Danger, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 8, 2), VerticalAlignment = VerticalAlignment.Center,
            };
            badge.Child = new TextBlock { Text = user.Unread + " 条未读", Foreground = Theme.Danger, FontSize = 11 };
            metaRow.Children.Add(badge);
        } else if (user.UnreadUnavailable) {
            metaRow.Children.Add(new TextBlock { Text = "未读统计暂不可用", Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        metaRow.Children.Add(new TextBlock { Text = "数据更新时间：" + LastCheckText(user), Foreground = Theme.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        info.Children.Add(metaRow);

        if (user.ProfileStatus == "error" && user.ProfileMessage.Length > 0) {
            info.Children.Add(new TextBlock { Text = user.ProfileMessage, Foreground = Theme.Warn, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var favorite = IsFavorite(user.Uid);
        var star = new Button {
            Content = favorite ? "★" : "☆",
            Foreground = favorite ? Theme.Warn : Theme.Muted,
        };
        star.Style = ButtonStyle(Brushes.Transparent, Theme.Hover, Brushes.Transparent, favorite ? Theme.Warn : Theme.Muted, 16);
        star.Background = Brushes.Transparent;
        star.BorderThickness = new Thickness(0);
        star.VerticalAlignment = VerticalAlignment.Center;
        star.ToolTip = favorite ? "取消重点紧盯" : "设为重点紧盯";
        star.Click += delegate {
            var uidNow = user.Uid;
            var on = !IsFavorite(uidNow);
            var target = Main.State.FindUser(uidNow);
            if (target != null) target.Favorite = on;
            if (on) favorites.Add(uidNow); else favorites.Remove(uidNow);
            var payload = Json.Obj();
            Json.Put(payload, "uid", uidNow);
            Json.Put(payload, "on", on);
            Main.Send("favorite", payload);
            RenderList();
        };
        Grid.SetColumn(star, 2);
        grid.Children.Add(star);

        card.Child = grid;
        card.MouseLeftButtonUp += delegate { OpenDetail(user.Uid); };
        return card;
    }

    Border Avatar(string nickname, double size) {
        var first = "微";
        if (!string.IsNullOrEmpty(nickname)) {
            var trimmed = nickname.Trim();
            if (trimmed.Length > 0) first = trimmed.Substring(0, 1);
        }
        var box = new Border {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = Theme.AccentDimBrush, BorderBrush = Theme.PanelBorderBrush, BorderThickness = new Thickness(1),
        };
        box.Child = new TextBlock {
            Text = first, Foreground = Theme.Text, FontSize = size * 0.42, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        return box;
    }

    string LastCheckText(UserRow user) {
        string raw;
        if (lastCheckByUid.TryGetValue(user.Uid, out raw) && !string.IsNullOrEmpty(raw)) return FormatTime(raw);
        if (!string.IsNullOrEmpty(user.UpdatedAt)) return FormatTime(user.UpdatedAt);
        return "暂无记录";
    }

    // ------------------------------------------------------------ 交互
    Button MakeChip(string text, string key) {
        var button = new Button { Content = text };
        button.Click += delegate {
            filter = key;
            ApplyChipStyles();
            RenderList();
        };
        return button;
    }

    void ApplyChipStyles() {
        if (chipAll == null) return;
        chipAll.Style = ChipStyle(filter == "all");
        chipFavorite.Style = ChipStyle(filter == "favorite");
        chipUnread.Style = ChipStyle(filter == "unread");
    }

    void AddOnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter || e.Key == Key.Return) AddUser();
    }

    void AddUser() {
        if (Main == null) return;
        var value = (addBox.Text ?? "").Trim();
        if (value.Length == 0) {
            Main.Toast("请输入有效的 UID 或用户主页链接", true);
            return;
        }
        var payload = Json.Obj();
        Json.Put(payload, "uid", value);
        Json.Put(payload, "value", value);
        Json.Put(payload, "requestId", Guid.NewGuid().ToString());
        Main.Send("userAdd", payload);
        addBox.Text = "";
        Main.Toast("已请求添加盯单人：" + value, false);
    }

    // ------------------------------------------------------------ 视图切换
    public void OpenDetail(string uid) {
        if (string.IsNullOrEmpty(uid)) return;
        if (detail == null) {
            detail = new UserDetailPage(Main);
            detail.BackRequested += ShowListFromBack;
        }
        detail.SetUser(uid);
        if (!root.Children.Contains(detail)) root.Children.Add(detail);
        detail.Visibility = Visibility.Visible;
        listRoot.Visibility = Visibility.Collapsed;
        showingDetail = true;
        detail.OnShown();
    }

    void ShowListFromBack() { ShowList(true); }

    void ShowList(bool refreshData) {
        showingDetail = false;
        if (detail != null) detail.Visibility = Visibility.Collapsed;
        listRoot.Visibility = Visibility.Visible;
        if (refreshData && Main != null) Main.Send("library", null);
        ApplyChipStyles();
        RenderList();
    }

    // ------------------------------------------------------------ 生命周期
    public void OnShown() {
        if (showingDetail && detail != null) {
            skipNextLibraryRequest = false;
            detail.OnShown();
            return;
        }
        if (Main != null && !skipNextLibraryRequest) Main.Send("library", null);
        skipNextLibraryRequest = false;
        ApplyChipStyles();
        RenderList();
    }

    public void OnEngineEvent(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        if (type == "library") {
            skipNextLibraryRequest = true;
            StoreFavorites(e);
        } else if (type == "homeUsers") {
            skipNextLibraryRequest = true;
            StoreUserExtras(e);
            RenderList();
        } else if (type == "followState" || type == "libraryData") {
            skipNextLibraryRequest = true;
        } else if (type == "userProfile") {
            ApplyUserProfile(e);
        } else if (type == "userAdded") {
            var added = Json.S(e, "uid");
            if (added.Length > 0 && !showingDetail) {
                OpenDetail(added);
                return;
            }
        } else if (type == "userDeleted") {
            HandleUserDeleted(Json.S(e, "uid"));
        } else if (type == "error") {
            var message = Json.S(e, "message");
            if (message.Length > 0 && Main != null) Main.Toast(message, true);
        }
        if (showingDetail && detail != null) detail.OnEngineEvent(e);
    }

    void StoreFavorites(Dictionary<string, object> e) {
        favorites.Clear();
        foreach (var item in Json.L(e, "favorites")) {
            var uid = Convert.ToString(item);
            if (!string.IsNullOrEmpty(uid)) favorites.Add(uid);
        }
    }

    void StoreUserExtras(Dictionary<string, object> e) {
        foreach (var item in Json.L(e, "users")) {
            var map = Json.M(item);
            if (map == null) continue;
            var uid = Json.S(map, "uid");
            if (uid.Length == 0) continue;
            noteByUid[uid] = Json.S(map, "note");
            lastCheckByUid[uid] = Json.S(map, "lastCheckAt");
        }
    }

    void ApplyUserProfile(Dictionary<string, object> e) {
        var uid = Json.S(e, "uid");
        if (uid.Length == 0 || Main == null) return;
        var row = Main.State.FindUser(uid);
        if (row != null) {
            var nickname = Json.S(e, "nickname");
            if (nickname.Length > 0) row.Nickname = nickname;
            row.ProfileStatus = Json.S(e, "profileStatus");
            row.ProfileMessage = Json.S(e, "profileMessage");
        }
        Main.State.Nicknames[uid] = Json.S(e, "nickname");
        RenderList();
    }

    void HandleUserDeleted(string uid) {
        if (uid.Length == 0 || Main == null) return;
        var row = Main.State.FindUser(uid);
        if (row != null) Main.State.Users.Remove(row);
        noteByUid.Remove(uid);
        lastCheckByUid.Remove(uid);
        favorites.Remove(uid);
        Main.Toast("已彻底删除该用户的相关数据", false);
        if (showingDetail && detail != null && detail.CurrentUid == uid) ShowList(false);
        else RenderList();
        Main.Send("library", null); // 同步刷新报告与首页统计
    }

    // ------------------------------------------------------------ 工具
    static string FormatTime(string iso) {
        if (string.IsNullOrEmpty(iso)) return "暂无记录";
        DateTime value;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)) {
            var local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        return iso;
    }

    // ------------------------------------------------------------ 控件样式（页面私有，C# 5）
    static Style ChipStyle(bool active) {
        if (active) {
            return ButtonStyle(Theme.Accent, Theme.AccentDimBrush, Theme.Accent, Theme.Bg, 12, new CornerRadius(14), new Thickness(12, 4, 12, 4));
        }
        return ButtonStyle(Brushes.Transparent, Theme.Hover, Theme.PanelBorderBrush, Theme.Muted, 12, new CornerRadius(14), new Thickness(12, 4, 12, 4));
    }

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

    // 统一按钮模板：深色底 + 悬停变色 + 圆角，颜色全部来自 Theme.* 画刷。
    static Style ButtonStyle(Brush background, Brush hoverBackground, Brush borderBrush, Brush foreground, double fontSize) {
        return ButtonStyle(background, hoverBackground, borderBrush, foreground, fontSize, new CornerRadius(6), new Thickness(10, 5, 10, 5));
    }

    static Style ButtonStyle(Brush background, Brush hoverBackground, Brush borderBrush, Brush foreground, double fontSize, CornerRadius corner, Thickness padding) {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, background));
        style.Setters.Add(new Setter(Button.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, borderBrush));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.PaddingProperty, padding));
        style.Setters.Add(new Setter(Button.FontSizeProperty, fontSize));
        style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Button.HorizontalAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(Button.VerticalAlignmentProperty, VerticalAlignment.Center));

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "bd";
        border.SetValue(Border.CornerRadiusProperty, corner);
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
        disabled.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        template.Triggers.Add(disabled);

        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }
}
}
