using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WeiboReader.Views {
// SKELETON STUB — agent-owned file. Implement the full 盯单人 list page here.
public class UserListPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public UserListPage(MainWindow main) {
        Main = main;
        Content = new TextBlock { Text = "盯单人（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) };
    }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
    public virtual void OpenDetail(string uid) {}
}
}
