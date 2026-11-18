using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WeiboReader.Views {
// SKELETON STUBS — each page is rebuilt in full by its own module file
// (UserListPage.cs / UserDetailPage.cs / PostListPage.cs / PostDetailPage.cs /
// SubscriptionsPage.cs / SettingsPage.cs keep one page per class, one class per
// view feature). This file only guarantees the app compiles before they land.
public class UserListPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public UserListPage(MainWindow main) { Main = main; Content = new TextBlock { Text = "盯单人（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) }; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
    public virtual void OpenDetail(string uid) {}
}
public class UserDetailPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public UserDetailPage(MainWindow main) { Main = main; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
}
public class PostListPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public PostListPage(MainWindow main) { Main = main; Content = new TextBlock { Text = "盯版聊（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) }; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
    public virtual void OpenDetail(string postId) {}
}
public class PostDetailPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public PostDetailPage(MainWindow main) { Main = main; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
}
public class SubscriptionsPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public SubscriptionsPage(MainWindow main) { Main = main; Content = new TextBlock { Text = "持续盯防（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) }; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
}
public class SettingsPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public SettingsPage(MainWindow main) { Main = main; Content = new TextBlock { Text = "账户与设置（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) }; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
}
}
