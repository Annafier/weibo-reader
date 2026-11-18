# Weibo Reader Windows 端

复用项目的全部核心实现（`lib/` 数据与采集、`desktop/bridge.js` 引擎桥），Windows 端提供**两套前端**：

1. **原生 WPF 壳（推荐）**：`windows/native/` —— C# 5 + WPF，用 Windows 自带的 in-box `csc.exe` 编译，**无需安装任何 SDK**。引擎仍是 `desktop/bridge.js`（stdio JSON 行协议），UI 为原生控件（深色主题，对标 macOS 版功能子集）。
2. **网页壳**：`windows/win-reader.js` —— 纯 Node.js 壳 + 共享 HTML 界面（`desktop/ui/`），作为原生壳的回退。

## 运行要求

- Node.js 22.5 或更新版本（使用内置 `node:sqlite`）。
- 原生壳：Windows 10/11 自带的 .NET Framework 4.x + csc.exe 即可编译；首次运行自动编译（约 10 秒）。
- 网页壳回退时按优先级选择：本机的 Edge / Chrome / Brave（应用模式无边框窗口）→ Firefox（独立资料）→ 系统默认浏览器。

## 启动

双击仓库根目录的 [`一键启动.cmd`](../一键启动.cmd)：自动检查 Node.js → 安装依赖 → 编译原生 UI（首次）→ 启动 `WeiboReader.exe`。

命令行方式：

```powershell
npm install
windows\native\build.cmd     # 编译原生 UI（可选）
windows\native\out\WeiboReader.exe --workspace D:\某个目录
npm run win                  # 回退：网页壳
```

- 默认资料目录为 `%APPDATA%\selnokk.weibo.reader`。
- 原生壳支持 `--selftest`（11 项自检）、`--log-events`（事件日志）。
- 关闭窗口即优雅退出：壳向引擎发送 `shutdown`，5 秒超时强杀。

## 原生结构（windows/native/）

- `build.cmd` —— 定位 in-box csc 与 GAC 程序集，编译 `src/*.cs` → `out/WeiboReader.exe`
- `src/Program.cs` —— 入口/日志/崩溃兜底；`src/Json.cs` —— JavaScriptSerializer 访问器（嵌套数组是 ArrayList）
- `src/EngineClient.cs` —— bridge 子进程托管与事件泵；`src/AppState.cs` —— 状态归并
- `src/MainWindow.cs` —— 侧边栏导航、credits 头、open 事件拦截、reportHandoff 回执、OnShown 节流
- `src/Dialogs.cs` —— 目录/文件对话框、剪贴板、与 ExternalLinks.swift 同规则外链白名单
- `src/views/` —— 盯单人（列表+详情）、盯版聊（列表+详情）、持续盯防、账户与设置
- `CONTRACT.md` —— 页面与引擎协议契约（并行实现的依据）

## 网页壳结构

- `windows/win-reader.js` —— 壳本体：托管引擎、本地回环 HTTP/SSE 服务、原生动作路由、会话单例、窗口生命周期（跟踪 SSE 连接）。
- `windows/windows-shim.js` —— 页面侧桥（对应 macOS 壳的注入脚本）。
- `windows/smoke.cjs` —— 无头冒烟测试（`npm run win:smoke`）。

界面文件与 macOS 版完全同源：网页壳在服务 `index.html` 时动态注入 shim 引用并把 CSP 的 `connect-src 'none'` 放宽为 `'self'`，磁盘上的共享文件不做修改。

## 与 macOS 版的行为差异

- 打开报告/文件夹使用系统默认浏览器与资源管理器；报告未读回执仍由引擎的交接凭据保证（`reportHandoff`/`reportOpened`）。
- 外部微博链接沿用 `ExternalLinks.swift` 的同一套白名单校验。
- 反馈诊断基于壳内环形日志（引擎 stderr 为主）；导出为本地 txt，由你自行发送。
- 测试套件中依赖 POSIX FIFO / 无特权符号链接的个别用例在 Windows 上自动跳过（`npm test` 当前 131 通过 / 1 跳过）。
- 双端同步支持新的「定向互动档案」（见根 README）；报告输出到 `output/pair/`，由壳经 `open` 事件在默认浏览器打开。
