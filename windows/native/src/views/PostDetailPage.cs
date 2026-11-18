using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WeiboReader.Views {
// SKELETON STUB — agent-owned file.
public class PostDetailPage : UserControl, IEnginePage {
    protected readonly MainWindow Main;
    public PostDetailPage(MainWindow main) { Main = main; }
    public virtual void OnEngineEvent(Dictionary<string, object> e) {}
    public virtual void OnShown() {}
}
}
