using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WeiboReader.Views {
// SKELETON STUB — agent-owned file.
public class SubscriptionsPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public SubscriptionsPage(MainWindow main) { Main = main; Content = new TextBlock { Text = "SubscriptionsPage（建设中）", Foreground = Theme.Muted, Margin = new Thickness(20) }; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
    public virtual void OpenDetail(string id) {}}
}
