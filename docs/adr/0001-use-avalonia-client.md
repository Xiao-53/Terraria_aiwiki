# ADR 0001：新客户端首选 Avalonia，Windows 退出 MAUI

- 状态：accepted；Phase 00 宿主与 Phase 08 Presentation 验证通过
- 日期：2026-08-20；Phase 00 验证更新于 2026-08-23；Phase 08 更新于 2026-08-28
- 决策范围：Windows 客户端宿主、Presentation 技术与后续 Android 路线；不包含其他产品平台
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/changes/archive/phase-00-migration-baseline/plan.md`

## 背景

现有 Terraria Wiki Offline 是一个 .NET 10 MAUI Blazor Hybrid 单项目。它已经包含成熟的 C# Wiki 数据核心、SQLite/FTS5、抓取更新、Redirect、资源、数据包、旧数据迁移和 Local Wiki Viewer。

用户已经确定：Windows 版本不继续使用 MAUI。原因是当前 Windows 开发不应再要求安装十几 GB 的 MAUI/Android SDK/JDK/Emulator/workload。产品当前目标仅为 Windows，未来只考虑 Android；iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均不是本决策的产品目标。

源码证据表明问题主要位于宿主边缘，而非 Wiki 算法本身：

- 数据模型和 `DatabaseService` 基本是普通 C# + sqlite-net；
- `DataService` 保留核心业务，但同时通过静态 `App.*` 访问状态/数据库，并混入 MAUI 路径、设置、文件选择、弹窗和平台导出；
- Local Viewer 的 HTML/CSS/JS 和数据库资源路由可保留，包内资源读取与生命周期需适配；
- Razor Presentation 直接访问 `App.ContentDb`/`App.DataManager`，不具备稳定 Application 边界；
- 窗口、键盘、安全区和 `Platforms/*` 属于旧 MAUI 宿主实现。

## 决策驱动因素

按优先级排序：

1. 当前 Windows 开发环境轻量，不安装 MAUI 或 Android 工具链；
2. 最大限度保留 C# Wiki Core、SQLite/FTS5 和数据兼容性；
3. 新客户端不是旧 Razor UI 加 Chat，Presentation 允许重建；
4. Android 后续能复用 Core、Application 和尽可能多的 UI/状态代码；
5. Local Viewer 能继续承载现有 HTML/CSS/JS、图片和音频；
6. 方案可由 CLI 构建、测试和发布，不依赖大型 IDE 才能工作；
7. 平台与第三方风险可以在小型 PoC 中被证伪。

## 候选比较

评分为 1（差）到 5（好），用于表达相对取舍，不替代 Phase 00 实测。

| 候选 | Windows 工具链 | Core 复用 | 旧 Razor 复用 | Android 复用 | Viewer | 主要问题 |
|---|---:|---:|---:|---:|---:|---|
| Avalonia + NativeWebView | 5 | 5 | 1 | 5 | 4 | 新 Presentation 要改为 Avalonia；WebView/发布需 PoC |
| WPF + Blazor Hybrid | 5 | 5 | 5 | 2 | 5 | WPF 无 Android；未来形成第二套宿主或重新引入 MAUI |
| WinUI 3 + WebView2 | 3 | 5 | 1 | 1 | 5 | Windows 专用，Windows App SDK/打包更重 |
| Uno Platform | 3 | 5 | 1 | 5 | 4 | 仍重写 Razor，引入额外 Uno/WinUI 层与工具链 |
| Electron/Tauri + C# sidecar | 3 | 3 | 2 | 1 | 5 | Android 难以复用进程内 C# Core，RPC/sidecar 胶水增多 |
| Flutter/React Native | 2 | 1 | 1 | 5 | 3 | 会重写或远程化已有 C# 后端 |

## 决策

项目级接受以下首选方向，但生产迁移必须先通过 Phase 00：

```text
Portable .NET 10 Wiki Core
→ thin Application use cases
→ Avalonia Presentation
→ NativeWebView Local Viewer
→ Windows Adapter now / Android Adapter later
```

具体含义：

- 新 Windows 客户端不引用 MAUI 包和 workload。
- Core/Application 不引用 Avalonia；Avalonia 只存在于 Presentation/平台宿主。
- 新 Presentation 使用 Avalonia 视图与状态模型；旧 Razor 页面是行为参考和过渡基线，不要求零修改复用。
- Local Viewer 优先保留现有 HTML/CSS/JS、Wiki 页面 HTML、WikiAsset 和 JS bridge。当前 loopback server 先保留为候选，不在 ADR 中提前决定替换。
- Android 进入对应 Phase 前不安装 Android workload、SDK、JDK 或 Emulator；Android Phase 使用 .NET for Android/Avalonia 目标，并可优先物理设备验证。
- WPF + Blazor Hybrid 是明确回退方案，不是并行建设的第二客户端。

## Phase 00 接受门槛

只有以下项目全部通过，才可在本 ADR 中把技术验证记为通过并进入生产迁移：

1. 在无 MAUI/Android/JDK/Emulator 的 Windows 环境用 .NET CLI build/run/publish。
2. 打开代表性旧数据库并实际通过 FTS5 trigram 建表/查询。
3. NativeWebView 加载现有 Viewer 页面、数据库图片/资源、内部链接和 JS↔C# 消息。
4. 现有数据包只读导入和导出兼容检查通过，PoC 不修改用户生产数据。
5. Windows 文件选择、目录选择、剪贴板、外链、主题、窗口和取消/错误路径有可实现的 Adapter 方案。
6. 发布产物在无开发工具的干净 Windows 环境启动，WebView2 Runtime 策略明确。

若 FTS5、Viewer 或发布门槛无法在限定 PoC 内满足，则停止采用 Avalonia，执行 WPF + Blazor Hybrid 回退评估，并用新 ADR supersede 本决策；不得通过复制 Wiki Core 或重写 Viewer 规避失败。

## Phase 00 实测结论（2026-08-23）

本次只新增可删除的 `experiments/phase-00-host-spike/**`，没有修改 `terraria-wiki-offline/**`、生产数据库或 `.pkg` 字节格式。Windows .NET SDK 10.0.400、无 MAUI/Android/JDK/Emulator/workload 的环境中，以下证据已经取得：

- `dotnet restore -r win-x64`、Release solution build、framework-dependent 和 self-contained `win-x64` publish 成功；依赖锁定为 Avalonia 12.1.1、`Avalonia.Controls.WebView` 12.0.1、`sqlite-net-pcl` 1.11.285。
- 27 个 SQLite/WAL/FTS5、数据包安全和 Viewer transport tests 全部通过。Windows provider 实测 SQLite 3.53.3、`ENABLE_FTS5` 和 trigram；真实 Manager/Content 的 `.db/-wal/-shm` 先形成稳定字节快照，SQLite 仅打开临时快照，CRUD/backup 仅作用于可写副本，源文件集合前后不变。
- 独立 oracle 流式验证用户 golden `Terraria_Wiki.pkg`（3,584,607,183 bytes，SHA-256 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`）；独立 DTO/序列化的临时 writer 输出也通过 oracle。路径、长度、数量、hash、截断、尾随和输出覆盖输入的安全边界均有回归证据。
- NativeWebView 在开发机两种发布和无开发工具的 Windows Sandbox 中通过代表性 Viewer 垂直切片：现有静态页面、数据库图片/音频请求、Redirect/anchor 数据、内部导航/返回响应、主题、history、C#↔JS request/response、消息来源拒绝及 loopback 生命周期。
- 人工平台验收通过文件/目录选择、剪贴板写后读回、系统浏览器外链、主题和窗口关闭；取消与错误均有独立可观察 outcome/error 分支。
- framework-dependent 与 self-contained `win-x64` 均可发布。无 `dotnet` 命令的 Sandbox 中，WebView2 缺失时 Host 可启动并给出明确 not-found；只读提供 WebView2 151.0.4129.93 后完整 Viewer smoke 通过。LuYao.TlsClient 1.2.0.0 的实际 HTTPS 请求返回 HTTP 200。

因此本 ADR 的 Phase 00 接受门槛全部通过，Avalonia + NativeWebView 继续作为正式迁移首选。这里的“通过”只批准进入后续生产迁移计划，不表示 PoC 已是产品客户端，也不提前实现 Phase 01 之后的完整 Viewer Adapter、安装器或 Android 支持。

Windows WebView2 策略收敛为：产品默认依赖系统 Evergreen Runtime，宿主在启动时检测并对缺失给出可操作错误；self-contained .NET 不等于捆绑 WebView2。固定 runtime 目录仅用于干净环境验证，不是当前产品分发决定。Evergreen 安装/bootstrap、签名和发布渠道由 Windows Productization Phase 决定。

没有历史 `.pak` 样本，因此不对旧 README 中该扩展名作额外格式承诺；当前源码实际 `.pkg` 契约和 golden 样本已通过。Windows 10、ARM64 与 Android 不在本次已授权验证矩阵。

## Phase 08 Presentation 实施结论（2026-08-28）

正式 Windows WinExe 已从代码构造临时壳重建为 XAML AI-first Presentation，并保持 ADR 原有分层：状态模型、commands、Markdig AST renderer 与布局全部位于 Windows assembly 内部，Application/Core、Viewer 内容协议、Python Agent 和 NDJSON v1 未新增 UI 依赖。

- 默认宽屏使用固定窄导航栏和 AI/共享 Reader 双栏；窄于 1100 DIP 时默认只显示 AI，可信来源或 Wiki 阅读按需以覆盖层打开同一个 Reader。
- Chat 保持独立单轮、流式工具/正文/来源/usage、停止和安全故障隔离；Markdown 只由 Markdig 1.3.2 产生 AST，再用原生 Avalonia 控件显式允许列表渲染，不生成 HTML、不加载图片、不启用模型链接。
- 整个 Presentation 只有一个 `NativeWebView` 与一个 `ViewerWebViewSession`；Chat 来源按 resolved title/anchor 打开，Wiki 搜索复用同一 Reader，Viewer 内部历史、主题和资源链路保持不变。
- Windows Release build 为 0 warning/0 error，完整测试 237/237；无外网 Python Sidecar、两种 `win-x64` 发布 smoke、正式 WinExe 和 1280×820/窄窗、浅/深主题窗口检查均通过。当前验收显示器为 225% 缩放，因此不把精确 100%/150% 人工检查记为已执行。

Phase 08 证明 Avalonia 能在不复制 Core/Viewer、也不建立第二客户端的前提下承载正式 AI/Wiki 核心 Presentation。Phase 09 仍负责收藏、历史、全部页面、数据管理、设置和 Wiki 切换等 P0 UI 接管；本结论不批准提前实现这些入口。

## 正面后果

- 当前 Windows 开发只承担 Windows/.NET 所需工具链。
- Windows 与 Android 共享同一客户端框架方向，降低后续 UI 与状态层迁移成本。
- Existing Wiki Core 继续是唯一数据事实源。
- Viewer 继续使用平台原生 WebView，不打包额外 Chromium。
- Presentation 重建与 Core 解耦可以独立推进和验证。

## 负面后果与风险

- 旧 Razor 页面不能作为新 Avalonia UI 原样复用，必须重建产品 Presentation。
- Windows Avalonia NativeWebView 与 WebView2 有/无路径已在 Phase 00 验证；Android WebView 行为仍须在 Android Phase 实测。
- Android 最终仍必须安装 Android workload、SDK 和 JDK；本决策只能延后并隔离该成本，不能消除它。
- sqlite-net/FTS5 与 `LuYao.TlsClient` 已在当前 Windows 版本实测；依赖升级、发布裁剪/AOT 和其他目标环境仍不能由本次结果外推。
- 现有源码没有自动化测试，提取前需要 characterization tests。

## 被否决方案何时可重新考虑

- 若项目放弃 Android，WPF + Blazor Hybrid 可重新成为最小迁移方案。
- 若 Phase 00 证明 NativeWebView 或 Avalonia 发布不可用，优先回退 WPF，不立即转向重写 C# 后端的技术栈。
- 若未来产品变为远程服务且不再要求设备内离线数据库，纯 Web/Tauri/Flutter 等路线才可重新评估。

## 官方依据

- [Avalonia supported platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia NativeWebView and platform prerequisites](https://docs.avaloniaui.net/docs/app-development/embedding-web-content)
- [ASP.NET Core Blazor Hybrid with WPF/Windows Forms](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0)
- [.NET for Android installation requirements](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/net-android)
