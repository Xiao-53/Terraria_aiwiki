# Phase 09 — P0 UI Takeover（收藏与 Reader 交互切片）

- 状态：in-progress
- Phase：09 — P0 UI Takeover
- 执行环境：WSL Codex CLI；Windows 11 x64 构建、测试与 UI 验证
- 创建日期：2026-08-28
- 最后更新：2026-08-28
- 范围：Windows Avalonia 收藏导航、Chat/Wiki 收藏、Reader 入口与可调整分栏
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/application-use-cases/spec.md`、`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户在 Phase 08 实机界面上确认：`GridSplitter` 拖到两端时会破坏左右方框布局，当前分隔条视觉过重；左侧星标页面应改为统一收藏页；Chat 右上角需要收藏当前单轮；顶部右侧需要 Wiki 按钮恢复或打开共享 Reader。

事实：源码基线为内层 `6844809`、外层 `1450324`。`WikiLibraryUseCases` 已提供 Wiki 收藏查询、添加、移除与幂等语义，Presentation 无需访问 SQLite。当前 Chat 明确是独立单轮且不持久化；Windows assembly 还没有 Chat 收藏存储。当前共享 Reader 只有一个 `NativeWebView`/`ViewerWebViewSession`。

决定：本切片启动 Phase 09，但不宣称整个 P0 UI Takeover 完成。Chat 收藏使用 Windows 宿主级、版本化原子 JSON，只保存用户显式收藏的当前已完成单轮，不保存普通问答历史，不修改 SQLite schema、Application 公共 API、AI 协议或 Agent。

## 目标与完成定义

- 左侧导航固定为“AI 问答”和“收藏”；收藏页同时列出对话收藏与活动 Wiki 的 Wiki 收藏。
- Chat 右上角星标只在已有最终回答时可用；添加/移除显式收藏并提供状态反馈。
- Reader 标题栏可收藏当前 Wiki 页面；收藏页 Wiki 项可在唯一共享 Reader 中打开并可移除。
- 顶部最右侧 Wiki 按钮打开/恢复共享 Reader；不创建第二个 Viewer。
- 宽布局分隔条具有清晰而克制的拖柄；左右硬性最小宽度。向右拖到 Reader 关闭阈值后关闭 Reader，主内容安全扩展；向左不能越过主内容下限。
- Windows Release build 0 warning/0 error；既有和新增测试通过；高 DPI 下实际拖动两端、收藏页、Chat/Reader 星标与窄窗口无方框错位。

## 前置条件

- Phase 00–08 已完成并归档。
- Windows 11 x64、.NET 10 与既有本地数据根可用。
- 用户已明确授权本次收藏导航与分栏行为。

## 范围与非目标

范围内：Windows Presentation 内部状态/view models/XAML；Windows Chat 收藏 JSON；复用 Application Wiki Library；相关测试、持续 Presentation spec 和当前状态文档。

非目标：多轮会话、自动保存历史、同步 Chat 收藏、云账户、第二个 Viewer、SQLite/schema 变化、AI/NDJSON 变化、全部页面、历史、数据管理、设置、导入导出、维护、旧 MAUI 退休、Android。

## P0 / 兼容性影响

- Wiki 收藏只经 `WikiLibraryUseCases` 读写现有 `WikiFavorite`，不直接访问 Core/SQLite。
- Chat 收藏写独立宿主 JSON，失败只影响收藏 UI，不阻塞 AI、Search 或 Viewer。
- 唯一 Reader、Viewer 内容协议、访问记录、返回、主题和资源链路保持不变。
- 分栏只改变 Presentation 布局；窄布局继续使用覆盖层。

## 方案与待决问题

- `FavoritesViewModel` 组合 Chat 收藏与 Wiki 收藏，不建立第二个 library API。
- `ChatFavoriteStore` 采用版本化 DTO、32 KiB answer 上限、原子 replace 与损坏文件安全降级；默认路径位于 `%LocalAppData%\Terraria AI Wiki\chat-favorites.json`。
- Chat 收藏快照包含 ID、问题、最终 Markdown、可信来源和收藏时间；列表点击恢复为只读单轮结果，不向 Agent 发送历史。
- Reader 收藏使用当前 `ReaderPageRequest.Title`（去除 anchor），状态随打开页面刷新。
- 分栏用 Grid 列最小宽度约束与拖动完成事件判定关闭阈值；关闭动作委托 `ShellViewModel.CloseReader()`。
- 待决问题：无；收藏跨设备同步和完整 Phase 09 其余页面留给后续明确任务。

## 文件变更

- ADD `Presentation/Favorites/*`：收藏页、view model 与显示项。
- ADD `Hosting/ChatFavoriteStore.cs`：内部 JSON 持久化。
- MODIFY `ChatViewModel` / `ChatView.axaml`：收藏状态、快照恢复与右上角星标。
- MODIFY `ReaderPaneViewModel` / `ReaderView.axaml`：Wiki 收藏状态与按钮。
- MODIFY `ShellViewModel` / `ShellView.axaml(.cs)`：收藏导航、顶部 Wiki 按钮、分隔约束和关闭阈值。
- MODIFY `MainWindow.cs`：组合 Chat 收藏存储与 Application Wiki 收藏用例。
- MODIFY Windows tests：持久化、收藏流程、唯一 Reader、导航、分隔条静态和目标行为。
- MODIFY `openspec/specs/windows-presentation/spec.md`、README/架构当前状态：只记录完成能力与 Phase 09 仍在进行。
- KEEP Application/Core、SQLite、Python、协议、Viewer 内容和旧 MAUI。

## 执行步骤

- [x] 建立 Chat 收藏存储与失败隔离测试。
- [x] 建立 Favorites view model/page，复用 Wiki Library 并接通打开/移除。
- [x] 接通 Chat 与 Reader 收藏按钮、顶部 Wiki 入口和导航语义。
- [x] 修正 GridSplitter 最小宽度、关闭阈值、视觉与窄布局。
- [x] 执行 Windows build/tests 和目标 Windows 宽布局 UI 自动化验收。
- [x] 更新持续 spec、项目状态、计划验证记录；分别提交源码与治理变更。

## 验证与回归计划

- WSL：`rg` 静态边界、`git diff --check`、secret/生成物/双 Git 状态。
- Windows：Release restore/build/test；目标为 0 warning/0 error、全部测试通过。
- Windows UI：本切片自动化覆盖宽屏左限位、向右关闭、顶部 Wiki 重开与收藏页布局；Chat/Wiki 收藏状态、收藏页打开和窄布局由 Presentation 回归测试覆盖。显式切换到 225% 缩放的人工检查留作后续目标平台验收，不在本切片结果中宣称通过。
- 回归：只存在一个 `NativeWebView`/`ViewerWebViewSession`；AI 单轮发送、Wiki Search、来源精确页面/anchor、Reader 返回与 AI 故障隔离不变。

## 风险与回退

- Chat JSON 损坏：返回空收藏并显示安全状态；保留 `.backup`，不影响主 Runtime。
- 极端分栏导致测量循环：用 Grid `MinWidth`、关闭阈值和一次性布局状态限制；可回退到上一提交 `6844809`。
- Wiki 收藏数据操作占用：捕获 Application 稳定错误并留在收藏页，不改变收藏数据库语义。
- 新导航与 Phase 08 spec 冲突：实现完成后更新持续 spec；本计划保持 Phase 09 in-progress，不提前宣称其余 P0 接管完成。

## 进度记录

- 2026-08-28：读取 Phase 09 路线、Presentation/Application specs、Shell/Chat/Reader、Runtime、Wiki Library 和测试；确认复用现有 Wiki 收藏并为 Chat 收藏增加独立宿主 JSON。
- 2026-08-28：完成收藏页、Chat/Wiki 星标、顶部 Wiki 入口和分栏约束；Windows UI Automation 实测分隔条初始物理坐标 1997，向左拖动停在 1260，向右拖动关闭 Reader，顶部 Wiki 按钮可重开同一个 Reader。
- 2026-08-28：Windows Release build 0 warning/0 error；全量 .NET 测试 Core 89、Application 54、Windows 73、WikiTools 28，共 244/244 通过。
- 2026-08-28：当前切片源码以 `5ee1600` 提交；持续 spec、README、项目基线、架构状态和本计划由独立外层治理提交保存。

## 发现、偏差与决策

- 发现：现有 Application 已完整覆盖 Wiki 收藏，无需新增公共 API。
- 决定：Chat 收藏是用户显式操作的快照，不改变“每次提问独立单轮、普通问答不持久化”的 AI 契约。
- 决定：本次是 Phase 09 的首个切片；其余 P0 页面不在当前授权中，完成本切片后计划仍保留进行中状态。

## 验证结果

- Windows Release build：通过，0 warning / 0 error。
- Windows 全量 .NET tests：244/244 通过（Core 89、Application 54、Windows 73、WikiTools 28）。
- 目标 Windows UI Automation：左侧安全限位、向右阈值关闭 Reader、顶部 Wiki 重开 Reader 均通过；检查截图中主框与 Reader 框未在极端拖动后错位。
- UI 验证使用当前本机启动条件，Reader 内容因本地 Runtime/数据不可用显示安全降级；本切片未重复验证真实 Wiki 内容链路，沿用 Phase 08 的 Viewer 回归基线。

## 完成摘要

当前授权的收藏与 Reader 交互切片已实现并验证，源码提交为 `5ee1600`；Phase 09 整体仍为 `in-progress`，其余 P0 页面不在本次完成范围内。
