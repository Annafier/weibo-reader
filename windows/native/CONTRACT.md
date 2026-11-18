# WeiboReader.exe 原生 UI · 子代理实现契约

目标：把 `windows/native/src/views/` 下的占位页面重写为完整的原生 WPF 页面，
替代原 HTML/JS 前端。引擎是 `desktop/bridge.js`（stdio JSON 行协议），壳已接好。

## 铁律

1. **C# 5 语法**（in-box csc.exe 编译）：禁止 `$"插值"`、`?.`、`nameof`、表达式体成员、
   自动属性初始化器、`using static`、`[k]=v` 字典初始化器。用 `string.Format`/`+` 拼接。
2. **只能修改自己名下的文件**（见任务卡），绝不改 src 根目录的骨架文件
   （Program/Json/EngineClient/AppState/Theme/MainWindow/Dialogs/SelfTest）。
   若发现骨架缺陷，在最终报告里写明，不要自行改。
3. 每个页面类一个文件；删除 `views/Pages.cs` 中对应占位类（保留其他占位！）。
   若 Pages.cs 里只剩你的类也要删，其他占位类保持原样。
4. **验证**：改完运行 `powershell -NoProfile -Command "cmd /c 'windows\native\build.cmd' 2>&1"`，
   必须编译通过（其他 agent 的文件可能正被改动——只修属于你文件的错误；
   若错误明确来自他人文件且阻塞你的验证，等 30 秒重试一次，仍失败则跳过并在报告注明）。
5. 视觉遵循深色主题：背景 #0d0d10、面板 #14131 7、边框 #2a2523、主色 #ff6a3d、
   辅蓝 #6aa6ff、弱字 #9a8b85。全部用 Theme.* 画刷，不要自造颜色。
6. 中文文案与原 HTML 端一致（参考 `desktop/ui/*.js` 同名功能的文案）。

## 骨架 API（已就绪，直接用）

```csharp
Main            // MainWindow（构造函数传入）
Main.State      // AppState：Users/Posts/Follows/Reports/Credits/BootstrapId…
Main.Engine     // EngineClient
Main.Send(action, payloadDict)             // 上行请求（自动补 workspace）
Main.Toast(message, isError)               // 底部气泡提示
Main.Navigate("users"|"posts"|"subscriptions"|"settings")
Main.NavigateToUser(uid) / NavigateToPost(postId)
Main.PickWorkspace()                       // 原生目录选择并发起 workspace 切换
Dialogs.ChooseFolder(this) / ChooseFile(this, save, defaultName)
Dialogs.CopyClipboard(text) / OpenReport(path) / OpenFolder(path)
Dialogs.OpenExternal(this, url)            // 带白名单校验
Theme.Card() / Theme.Label(text) / Theme.Title(text) / Theme.* 画刷
Json.S(map,key) / Json.N / Json.B / Json.L / Json.M / Json.Parse / Json.Serialize / Json.Obj / Json.Put
```

页面基类模式（实现 IEnginePage）：

```csharp
public class XxxPage : UserControl, IEnginePage {
    readonly MainWindow Main;
    public XxxPage(MainWindow main) { Main = main; Build(); }
    void Build() { /* 构建控件树 */ }
    public void OnEngineEvent(Dictionary<string, object> e) { /* 引擎事件（UI线程） */ }
    public void OnShown() { /* 每次导航到本页；按需刷新渲染或发请求 */ }
}
```

事件都是 UI 线程回调；`Main.State` 已在 MainWindow.OnEngineEvent 中先行更新
（homeUsers/postsHome/followState/libraryData/library/credits 都会自动进 State）。

## 上行动作（Main.Send）与触发点

| action | payload 要点 | 触发 |
|---|---|---|
| `library` | 无 | 进入任一页刷新首页数据 |
| `run` | `{query:{uid,mode,limit/update…}}` 结构复杂 → v1 用 `postAdd/userAdd` 之外的获取走 query 详细结构，见下「获取数据」 | 获取数据 |
| `stop` | 无 | 停止当前任务 |
| `open` | `{id,kind:"report"|"folder"}` | 打开报告（壳会拦截 open 事件打开并回执） |
| `output` | `{limit?,from?,to?}` | 本地导出（导出数据 tab） |
| `favorite` | `{uid?|postId?,on}` | 星标 |
| `userAdd` | `{value}`（UID 或主页链接） | 添加盯单人 |
| `postAdd` | `{value}` | 添加盯版聊 |
| `userDeletePlan` → `userDeleteConfirm` | `{uid}` / `{uid,confirm:true}` | 删除用户（两步） |
| `manageDeletePlan` → `manageDeleteConfirm` | `{targets:[{targetKind,id}]}` | 批量删除 |
| `postFavorite` | `{postId,on}` | 博文星标 |
| `userViewed` | `{uid,metadata:{nickname}}` | 打开用户详情时上报 |
| `userProfileRetry` / `userProfileDismiss` | `{uid}` | 资料重试/关闭 |
| `userNoteSave` / `postNoteSave` | `{uid|postId,text}` | 备注 |
| `postAuthorAvatar` | `{postId,authorUid,requestId}` | 补齐作者头像 |
| `followList` | 无 | 订阅列表（followState 事件返回） |
| `followSave` | `{uid|postId,targetKind,settings:{uid,mode,frequency,interval,roundLimit,dailyBudget,enabled,timeZone?},scope:"user"|"post"}` | 保存订阅 |
| `followConfigure`/`followPause`/`followResume`/`followRemove`/`followWake` | `{uid,postId?,targetKind,…}` | 订阅操作 |
| `account` | `{loginRequestId}` | 查询余额 |
| `login` / `loginCancel` | `{}` / `{}` | 授权（登录流程桥自动处理） |
| `pricingReference` / `pricingRefresh` | 无 | 价格参考 |
| `pairReport` | `{uidA,uidB,requestId}` | 定向互动档案（browserOpened/browserOpenFailed 回执） |
| `updateHistory` | `{mode,target}` | 更新历史 |
| `cleanup` / `retention` | `{}` | 清理 |
| `manageBatch` | `{operation:"subscribe",settings,targets:[…]}` | 批量订阅 |
| `exchangeExport/Inspect/Commit/Discard` | 壳的对话框先行：选文件后 `{workspace,path,requestId,…}` | 数据导出/导入 |
| `stop` | 无 | 停止 |
| `shutdown` | 由壳退出时自动发，页面不要发 | — |

## 下行事件（OnEngineEvent 收到，type 字段）

- `library`/`homeUsers`/`postsHome`/`followState`/`libraryData`/`credits`/`account`/`userProfile`：已进 State；`libraryState{section,status,message}` 是分段加载状态。
- `started{id,query,automatic,pid}` / `finished{id,code,summary…}` / `preparing` / `progress{message,percent…}` / `collection{kind,phase,fetched,…}` / `source{kind,reason,…}`：任务进度流（详情页任务卡）。
- `localExported{files,summary,reused,requestId}` / `localData{…}` / `localError{message,requestId}`：导出数据流；files[0] 后调 `Dialogs.OpenReport` 由桥的 open 事件打开。
- `userDeleted{uid}` / `postDeleted{postId}`：删除完成，刷新。
- `pairReportResult{requestId,file,total,nicknames}`：互动档案产物（打开由壳 open 事件完成）。
- `log{message}`：引擎 stderr。
- 其他未知事件：忽略即可（Return 而非抛错）。

## 任务卡（详情页右侧）渲染建议

State.Tasks 不自动维护；页面自行监听 `started/preparing/finished/progress`（按 query.uid/postId
匹配当前对象）维护局部任务状态；`finished.code==0` 后刷新报告（发 library）。

## 验证清单（agent 自查）

1. `build.cmd` 编译 0 error。
2. 每个页面：空数据不出错、OnShown 幂等（重复导航不叠加控件）、事件未知类型忽略。
3. 所有用户可见文案用中文。
4. 最终报告列出：改动文件、编译输出尾部 3 行、遗留问题。
