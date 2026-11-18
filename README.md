# Weibo Reader

Weibo Reader 是一款本地阅读工具。可以读取并保存某位微博用户发出和收到的评论、提及该用户的内容、该用户的微博，或专门保存一条微博下面的评论。保存后，可以离线搜索、生成 HTML 报告，也可以让应用按计划检查新评论。

**定向互动档案**：从两位已归档账号的本地资料中提取双方全部互动（评论对方的原微博、回复/引用对方的评论），生成带数据看板的离线报告——命中统计、活跃天数、往返次数与中位响应间隔、峰值日/时段、每天互动量与时段分布图。纯本地计算，0 credits；在用户详情页点「互动档案」选择对方即可，或用 CLI：`node weibo_cli.js pair <UID-A> <UID-B>`。

本工具数据获取完全依赖于微博开放平台CLI服务。如果遇到问题，欢迎反馈。

[下载安装包（macOS）](https://github.com/selnokk/weibo-reader/releases/download/weibo-reader/Weibo-Reader.dmg) · [使用手册](weibo_reader_user_guide.md) · [Windows 端说明](windows/README.md)

## Windows

Windows 端无需 DMG 安装包，直接从源码运行（需 Node.js 22.5+；界面窗口自动选用本机的 Edge/Chrome/Firefox 或默认浏览器）：

```powershell
npm install
npm run win
```

或者直接双击仓库根目录的 [`一键启动.cmd`](一键启动.cmd)（自动检查 Node.js、安装依赖并启动）。

详细说明见 [windows/README.md](windows/README.md)。macOS 桌面版构建流程保持不变。
