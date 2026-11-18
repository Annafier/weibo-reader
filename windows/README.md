# Weibo Reader Windows 端

复用项目的全部核心实现（`lib/` 数据与采集、`desktop/bridge.js` 引擎桥、`desktop/ui/` 界面），仅将 macOS 的 AppKit/WebKit 原生壳替换为一个纯 Node.js 的 Windows 壳，不引入任何新的 npm 依赖。

## 运行要求

- Node.js 22.5 或更新版本（使用内置 `node:sqlite`）。
- 界面窗口按优先级自动选择：本机的 Edge / Chrome / Brave（Chromium 内核，应用模式无边框窗口）→ Firefox（独立资料的普通窗口）→ 系统默认浏览器。
- 原生对话框（选择资料目录、导出/导入数据包）与剪贴板通过系统 PowerShell 实现，无需额外安装。

## 启动

```powershell
npm install
npm run win            # 等价于 node windows/win-reader.js
```

- 默认资料目录为 `%APPDATA%\selnokk.weibo.reader`；首次启动可用 `npm run win -- --workspace D:\某个目录` 指定并持久化。
- 重复启动不会开第二个引擎：新进程只会在同一会话上再开一个应用窗口。
- 关闭窗口即优雅退出：壳向引擎发送 `shutdown`，等待数据落盘后结束（5 秒超时强杀）。

## 结构

- `windows/win-reader.js` —— 壳本体：托管引擎子进程（stdio JSON 行协议）、本地回环 HTTP 服务（静态页面 + SSE 事件流 + `/bridge` 请求上行）、原生动作路由、会话单例、窗口生命周期（跟踪 SSE 连接，全部窗口关闭后优雅退出）。
- `windows/windows-shim.js` —— 页面侧桥：`window.weiboBrowserHarness` 上行请求，SSE 下行事件分发到 `window.weiboReceive`（对应 macOS 壳的 `window.webkit.messageHandlers` 注入脚本）。
- `windows/smoke.cjs` —— 无头冒烟测试（`npm run win:smoke`）：验证页面服务、CSP 改写、引擎握手与事件流。

界面文件与 macOS 版完全同源：壳在服务 `index.html` 时动态注入 shim 引用并把 CSP 的 `connect-src 'none'` 放宽为 `'self'`，磁盘上的共享文件不做修改。

## 与 macOS 版的行为差异

- 打开报告/文件夹使用系统默认浏览器与资源管理器；报告未读回执仍由引擎的交接凭据保证（`reportHandoff`/`reportOpened`）。
- 外部微博链接沿用 `ExternalLinks.swift` 的同一套白名单校验。
- 反馈诊断基于壳内环形日志（引擎 stderr 为主）；导出为本地 txt，由你自行发送。
- 测试套件中依赖 POSIX FIFO / 无特权符号链接的个别用例在 Windows 上自动跳过（`npm test` 当前 131 通过 / 1 跳过）。
- 双端同步支持新的「定向互动档案」（见根 README）；报告输出到 `output/pair/`，由壳经 `open` 事件在默认浏览器打开。
