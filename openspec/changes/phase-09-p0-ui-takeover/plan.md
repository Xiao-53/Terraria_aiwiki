# Phase 09 — P0 UI Takeover（收藏与 Reader 交互切片）

- 状态：in-progress
- Phase：09 — P0 UI Takeover
- 执行环境：WSL Codex CLI；Windows 11 x64 构建、测试与 UI 验证
- 创建日期：2026-08-28
- 最后更新：2026-08-28
- 范围：Windows Avalonia 收藏导航、Chat/Wiki 收藏、Reader 入口、可调整分栏、本机 AI 配置与 Chat 交互视觉
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
- 宽布局分隔条具有清晰而克制的拖柄与左右硬性最小宽度；拖到两端不会破坏布局，也不会自动关闭 Reader。宽布局关闭只由 Reader 叉号执行。
- 顶部 Wiki 只展开或恢复 Reader，不把左侧主内容替换为原生 Wiki 搜索页；启动失败时可重新选择数据文件夹。
- 默认 Reader 首页具有真实页面收藏状态，点击星标可立即收藏或取消，不再因缺少当前标题而静默无响应。
- 按用户明确要求，Windows 本机 `ai-runtime.json` 使用新版配置保存 API key；Host 只把该值放入所属 Sidecar 的进程环境，不进入命令参数、NDJSON、错误或 Git。
- Chat 思考中使用连续内容流、明确的运行状态和紧凑工具轨迹，不再以孤立居中的问题卡、厚重工具卡和重复输入内容形成大面积空白。
- 输入框以预览路由拦截 Enter 并发送，Shift+Enter 保留换行；发送后清空编辑框，底部显示键盘提示。
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
- 分栏只使用 Grid 列最小宽度约束，不注册拖动完成关闭事件；Reader 关闭动作只由既有显式关闭入口委托 `ShellViewModel.CloseReader()`。
- 已保存数据根失效或内容不兼容时，错误区提供重新选择入口；旧数据遗留 WAL/SHM 先正常 checkpoint 并切回 DELETE journal，不直接删除 sidecar。
- `ai-runtime.json` 升级为严格 v2，新增顶层 `apiKey`；值只在内存中传给 `ProcessStartInfo.Environment["TERRARIA_AI_API_KEY"]`。配置与用户级 Python 环境位于 `%LOCALAPPDATA%`，不纳入仓库。
- 待决问题：无；收藏跨设备同步和完整 Phase 09 其余页面留给后续明确任务。

## 文件变更

- ADD `Presentation/Favorites/*`：收藏页、view model 与显示项。
- ADD `Hosting/ChatFavoriteStore.cs`：内部 JSON 持久化。
- MODIFY `ChatViewModel` / `ChatView.axaml`：收藏状态、快照恢复与右上角星标。
- MODIFY `ReaderPaneViewModel` / `ReaderView.axaml`：Wiki 收藏状态与按钮。
- MODIFY `ShellViewModel` / `ShellView.axaml(.cs)`：收藏导航、只展开 Reader 的顶部 Wiki 按钮和纯限位分隔约束。
- MODIFY `MainWindow` / data-root 启动链路：错误后重新选择数据根并重新组合 Runtime。
- MODIFY Core 兼容实现：在既有 anchor 迁移前安全 checkpoint 旧 WAL；不改变数据 schema 或公共用例 API。
- MODIFY Windows AI configuration/process：读取 v2 本机密钥并仅通过子进程环境交付；不改变 NDJSON 或 Python Agent 公共设置。
- MODIFY Reader view model / MainWindow：为默认首页建立当前页面收藏上下文。
- MODIFY Chat XAML/view model/input behavior/styles：重排思考态、清空已提交输入并可靠处理 Enter/Shift+Enter。
- MODIFY `MainWindow.cs`：组合 Chat 收藏存储与 Application Wiki 收藏用例。
- MODIFY Windows tests：持久化、收藏流程、唯一 Reader、导航、分隔条静态和目标行为。
- MODIFY `openspec/specs/windows-presentation/spec.md`、README/架构当前状态：只记录完成能力与 Phase 09 仍在进行。
- KEEP Application/Core、SQLite、Python、协议、Viewer 内容和旧 MAUI。

## 执行步骤

- [x] 建立 Chat 收藏存储与失败隔离测试。
- [x] 建立 Favorites view model/page，复用 Wiki Library 并接通打开/移除。
- [x] 接通 Chat 与 Reader 收藏按钮、顶部 Wiki 入口和导航语义。
- [x] 修正 GridSplitter 最小宽度、视觉与窄布局；按用户修正移除拖动自动关闭。
- [x] 执行 Windows build/tests 和目标 Windows 宽布局 UI 自动化验收。
- [x] 更新持续 spec、项目状态、计划验证记录；分别提交源码与治理变更。
- [x] 按用户指定的本机配置边界接通 AI key、准备兼容 Python 环境并修复 Reader 首页收藏。
- [x] 执行 Windows build/自动化测试和静态脱敏检查；人工 UI 验收由用户执行。
- [x] 优化 AI 思考态和工具轨迹视觉，接通预览 Enter 发送并补齐回归测试。
- [x] 执行 Windows build/自动化测试与静态检查；本次人工 UI 验收继续由用户执行。

## 验证与回归计划

- WSL：`rg` 静态边界、`git diff --check`、secret/生成物/双 Git 状态。
- Windows：Release restore/build/test；目标为 0 warning/0 error、全部测试通过。
- Windows UI：本切片自动化覆盖宽屏限位、顶部 Wiki 重开与收藏页布局；分隔拖动不得自动关闭 Reader。Chat/Wiki 收藏状态、收藏页打开和窄布局由 Presentation 回归测试覆盖。显式切换到 225% 缩放的人工检查留作后续目标平台验收，不在本切片结果中宣称通过。
- 回归：只存在一个 `NativeWebView`/`ViewerWebViewSession`；AI 单轮发送、Wiki Search、来源精确页面/anchor、Reader 返回与 AI 故障隔离不变。

## 风险与回退

- Chat JSON 损坏：返回空收藏并显示安全状态；保留 `.backup`，不影响主 Runtime。
- 极端分栏导致测量循环：用 Grid `MinWidth` 和一次性布局状态限制；不以自动关闭掩盖测量错误，可回退到上一提交 `6844809`。
- Wiki 收藏数据操作占用：捕获 Application 稳定错误并留在收藏页，不改变收藏数据库语义。
- 新导航与 Phase 08 spec 冲突：实现完成后更新持续 spec；本计划保持 Phase 09 in-progress，不提前宣称其余 P0 接管完成。

## 进度记录

- 2026-08-28：读取 Phase 09 路线、Presentation/Application specs、Shell/Chat/Reader、Runtime、Wiki Library 和测试；确认复用现有 Wiki 收藏并为 Chat 收藏增加独立宿主 JSON。
- 2026-08-28：完成收藏页、Chat/Wiki 星标、顶部 Wiki 入口和分栏约束；早期 UI Automation 曾验证自动关闭，随后按用户修正删除该行为，只保留硬性限位与显式关闭。
- 2026-08-28：Windows Release build 0 warning/0 error；全量 .NET 测试 Core 89、Application 54、Windows 73、WikiTools 28，共 244/244 通过。
- 2026-08-28：当前切片源码以 `5ee1600` 提交；持续 spec、README、项目基线、架构状态和本计划由独立外层治理提交保存。
- 2026-08-28：用户修正顶部 Wiki 语义为“只展开右侧 Reader”，并要求删除分隔条向右自动关闭、只保留限位；实现和回归已相应更新。
- 2026-08-28：检查已保存数据根，确认目录和 3.4 GB 中文 Wiki 数据正确；启动失败根因为遗留 16 MB WAL/SHM 被安全迁移护栏拒绝。新增正常 checkpoint 与数据根重选入口后，真实数据启动并加载 Reader 成功，关闭后 WAL/SHM 已正常合并且无 anchor 临时文件残留。
- 2026-08-28：修正后 Windows Release build 0 warning/0 error；全量 .NET 测试 Core 90、Application 54、Windows 74、WikiTools 28，共 246/246 通过。
- 2026-08-28：真实窗口执行“关闭 Reader → 点击顶部 Wiki”，截图确认 AI 主页面保持可见且 Reader 重新展开；原生 Wiki 搜索页未替换主内容。
- 2026-08-28：后续修正以源码提交 `df9c723` 保存；用户接管此后的人工 UI 测试，Agent 后续仅执行编译、自动化测试与静态检查，除非用户另行要求。
- 2026-08-28：用户明确要求 API key 直接保存到本机配置，并报告星标点击无反应；确认默认 Reader 首页未设置 `_currentPageTitle`，导致收藏命令不可执行。开始严格 v2 本机配置与首页收藏修正，不发起付费 Provider 请求。
- 2026-08-28：已在 `%LOCALAPPDATA%\Terraria AI Wiki\python` 建立 Python 3.12.3 / PydanticAI 2.31.0 环境并写入用户级 v2 配置；真实 Sidecar ready/shutdown 握手通过，未发送 answer 或 Provider 请求。源码提交为 `ec124ff`。
- 2026-08-28：用户提供实机截图，要求优化 AI 思考态并增加 Enter 发送。确认现有输入仅在 TextBox 冒泡 `KeyDown` 上处理，而 `AcceptsReturn` 可能先消费 Enter；改为预览路由处理，并同步修正重复问题与厚重工具卡布局。
- 2026-08-28：Chat 思考态、回答区、完成后工具摘要与输入区已重排；预览路由 Enter/Shift+Enter、发送后清空编辑框及状态转换回归通过。源码提交为 `c23c4b9`，人工 UI 验收按用户要求留给用户。

## 发现、偏差与决策

- 发现：现有 Application 已完整覆盖 Wiki 收藏，无需新增公共 API。
- 决定：Chat 收藏是用户显式操作的快照，不改变“每次提问独立单轮、普通问答不持久化”的 AI 契约。
- 决定：本次是 Phase 09 的首个切片；其余 P0 页面不在当前授权中，完成本切片后计划仍保留进行中状态。
- 决定：用户明确覆盖 ADR 0006 原有“密钥不落配置文件”选择；仅 Windows 本机配置改变，Python Agent 仍只从子进程环境读取，NDJSON 和命令参数继续不携带密钥。

## 验证结果

- Windows Release build：通过，0 warning / 0 error。
- Windows 全量 .NET tests：246/246 通过（Core 90、Application 54、Windows 74、WikiTools 28）。
- 目标 Windows：真实旧数据根完成 WAL checkpoint、Application 初始化和 Reader 首页加载；退出后只保留主 `data.db`，无 WAL/SHM 或 anchor 临时文件。
- Presentation 回归确认顶部 Wiki 保留当前 Chat/收藏主页面，分隔条无自动关闭事件，左右最小宽度限位继续生效。
- 目标 Windows 窗口验证：关闭 Reader 后点击顶部 Wiki，Chat 仍为主页面且同一个 Reader 重新可见。
- Reader 已用当前本机真实数据完成首页内容加载；AI 未发起付费 Provider 请求，AI 配置不影响本次离线启动与 Reader 验证。
- Windows Release build：通过，0 warning / 0 error；全量 .NET tests：247/247 通过（Core 90、Application 54、Windows 75、WikiTools 28）。
- 本机 AI 配置结构验证：v2、Python 路径、DeepSeek provider/model/API 和非空 key 均有效；真实 Sidecar 返回 Runtime 1、Python 3.12.3、PydanticAI 2.31.0 ready 后正常退出。未启动 UI，未请求付费 Provider。
- 仓库 secret scan：通过；真实 key 只存在用户级 `ai-runtime.json`，未进入源码、测试、文档、Git 或临时文件。
- Chat 修正后 Windows Release build：通过，0 warning / 0 error；全量 .NET tests：248/248 通过（Core 90、Application 54、Windows 76、WikiTools 28）。XAML 编译、预览 Enter 绑定、Shift+Enter 判定、思考态到完成态转换和编辑框清空均有自动化回归；未启动 UI、未请求 Provider。

## 完成摘要

当前授权的收藏、Reader 交互、本机 AI 配置与 Chat 视觉切片已由源码提交 `5ee1600`、`df9c723`、`ec124ff` 和 `c23c4b9` 保存；Phase 09 整体仍为 `in-progress`，人工 UI 验收由用户执行，其余 P0 页面不在本次完成范围内。
