# Phase 09 — P0 UI Takeover

- 状态：complete
- Phase：09 — P0 UI Takeover
- 执行环境：WSL Codex CLI；Windows 11 x64 构建、测试与 UI 验证
- 创建日期：2026-08-28
- 最后更新：2026-09-03
- 范围：Windows Avalonia 收藏与 Reader 交互、Wiki 资料库、数据管理、设置、导入导出、维护与错误流程、本机 AI 配置、Chat 交互视觉与 Token 花费展示、空状态与页头操作样式
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/application-use-cases/spec.md`、`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户在 Phase 08 实机界面上确认：`GridSplitter` 拖到两端时会破坏左右方框布局，当前分隔条视觉过重；左侧星标页面应改为统一收藏页；Chat 右上角需要收藏当前单轮；顶部右侧需要 Wiki 按钮恢复或打开共享 Reader。

事实：源码基线为内层 `6844809`、外层 `1450324`。`WikiLibraryUseCases` 已提供 Wiki 收藏查询、添加、移除与幂等语义，Presentation 无需访问 SQLite。当前 Chat 明确是独立单轮且不持久化；Windows assembly 还没有 Chat 收藏存储。当前共享 Reader 只有一个 `NativeWebView`/`ViewerWebViewSession`。

决定：本切片启动 Phase 09，但不宣称整个 P0 UI Takeover 完成。Chat 收藏使用 Windows 宿主级、版本化原子 JSON，只保存用户显式收藏的当前已完成单轮，不保存普通问答历史，不修改 SQLite schema、Application 公共 API、AI 协议或 Agent。

2026-09-02 继续 Phase 09 时确认：收藏与 Reader 切片已由独立源码/治理提交保存；新 Avalonia Shell 尚未显示旧 UI 的“全部页面”和“历史记录”，但 `WikiLibraryUseCases.GetPagesAsync`、`GetHistoryAsync` 与 `ClearHistoryAsync` 已提供排序、分页、标题过滤和清空语义。下一最小切片接管这两项只读/低风险资料库入口，Presentation 不直接访问 SQLite，也不进入数据管理、设置或 MAUI 退休。

2026-09-02 用户通过实机截图确认：Chat 空状态的三个示例问题和收藏/资料库页头“刷新”按钮继承 Fluent 默认灰色背景，与当前 Terraria 语义配色不协调。用户明确要求删除三个示例问题，并局部重做两个刷新操作的样式与配色；不改变刷新命令、资料库数据或其他页面行为。

2026-09-02 确认新产品不应继续寄存在上游参考仓库的本地分支中。Phase 09 功能开发暂停，先执行非 Phase 变更 `separate-product-from-reference`：以产品提交 `d67d904` 建立外层根级 `src/`、`tests/`、`agent/`、`assets/` 与 solution 基线，并把 `terraria-wiki-offline/` 固定为只读上游 Submodule。此次前置修正不改变本 Phase 行为范围、完成状态或历史提交路径。

2026-09-02 用户明确要求完成 Phase 09，因而授权补齐此前留待后续切片的数据管理、设置、导入导出、维护与对应错误流程；授权不扩展到 Phase 10 的参考 MAUI 退休或 Phase 11 的安装、更新渠道和日志产品化。外层 tracked 工作树在实施前干净，恢复点为 `ca1c031`；现有未跟踪治理归档、`.codex/`、`experiments/` 与历史架构文件保持原样，不纳入本 Phase。

## 目标与完成定义

- 左侧导航显示已实现的“AI 问答”“收藏”“Wiki 资料库”“数据管理”和“设置”；收藏页同时列出对话收藏与活动 Wiki 的 Wiki 收藏。
- Chat 右上角星标只在已有最终回答时可用；添加/移除显式收藏并提供状态反馈。
- Reader 标题栏可收藏当前 Wiki 页面；收藏页 Wiki 项可在唯一共享 Reader 中打开并可移除。
- 顶部最右侧 Wiki 按钮打开/恢复共享 Reader；不创建第二个 Viewer。
- 宽布局分隔条具有清晰而克制的拖柄与左右硬性最小宽度；拖到两端不会破坏布局，也不会自动关闭 Reader。宽布局关闭只由 Reader 叉号执行。
- 顶部 Wiki 只展开或恢复 Reader，不把左侧主内容替换为原生 Wiki 搜索页；启动失败时可重新选择数据文件夹。
- 默认 Reader 首页具有真实页面收藏状态，点击星标可立即收藏或取消，不再因缺少当前标题而静默无响应。
- 按用户明确要求，Windows 本机 `ai-runtime.json` 使用新版配置保存 API key；Host 只把该值放入所属 Sidecar 的进程环境，不进入命令参数、NDJSON、错误或 Git。
- Chat 思考中使用连续内容流、明确的运行状态和紧凑工具轨迹，不再以孤立居中的问题卡、厚重工具卡和重复输入内容形成大面积空白。
- 输入框以预览路由处理 Enter，Shift+Enter 保留换行；中文输入法存在未提交的 preedit 文本时 Enter 只确认候选，组词结束后 Enter 才发送。已提交问题保留在底部只读输入栏，不再另建右上角问题卡。
- 取消 Agent 的美元费用安全预算，不再因 PydanticAI 可识别的 provider 定价达到 `$0.10` 而中止回答；请求/工具循环、输出/context 和超时技术限位继续生效。
- Chat 原 Usage 区域改为“Token 花费”，只显示输入、输出与合计 token，不显示模型请求数、工具调用数或美元估算。
- Chat 思考期间的停止按钮图标必须固定居中，不因无边框 Button 模板的默认 content alignment 偏移；回答内容与右上角收藏星标使用独立布局列，窄分栏下也不得重叠。
- Chat 回答标题使用独立的小型“AI 结论”标签，不与 Markdown 正文的标题或粗体混淆；可信来源使用等宽整行卡片，Token 花费与完成状态使用轻量信息条。
- Windows Release build 0 warning/0 error；既有和新增测试通过；高 DPI 下实际拖动两端、收藏页、Chat/Reader 星标与窄窗口无方框错位。
- 左侧新增可用的“Wiki 资料库”入口；资料库内可在“全部页面”和“历史记录”间切换，按标题过滤并按 Application 分页继续加载。
- 全部页面显示标题与更新时间；历史记录保持最新优先和日期分组，清空历史必须经过明确的二次确认。
- 点击任一页面或历史项只在唯一共享 Reader 中打开，不创建第二个 Viewer，不替换或复制 Wiki 数据。
- Chat 空状态移除三个示例问题按钮，仅保留品牌图形、标题与说明；两个页头刷新入口使用统一的 Terraria 次级操作样式、刷新图标及完整交互状态。
- 左侧新增可用的“数据管理”和“设置”入口；数据管理只经现有 Application 用例列出/切换 Wiki、显示统计并执行下载、更新、资源、失败项和数据库维护操作。
- 长操作显示安全进度、当前动作与取消入口；切换 Wiki、删除资源、删除整库、清理失败项和导入覆盖均使用页内确认，失败不泄漏本机路径且不阻塞离线可用页面。
- `.pkg` 导入遵守 Inspect → inspection token → 显式确认 → Import；导出由 Windows 保存文件选择器决定目标并在同目录临时输出成功后替换已确认的旧文件，不改变包格式。
- 设置页加载并保存 Application 全部强类型设置；应用主题、内容主题和侧键返回在当前 Runtime 生效，下载/Search 参数供后续用例立即读取。
- 活动 Wiki 切换、活动库导入或整库删除后，同一个 Viewer 重新绑定到恢复后的动态 loopback 地址；收藏、资料库与 Reader 首页刷新，不创建第二个 Viewer。
- Windows Release build 0 warning/0 error、全量测试通过；Light/Dark、1280×820、900×600、宽/窄 Reader 和关键确认/错误状态完成目标 Windows 验收后，Phase 09 才标记完成并归档。

## 前置条件

- Phase 00–08 已完成并归档。
- Windows 11 x64、.NET 10 与既有本地数据根可用。
- 用户已明确授权本次收藏导航与分栏行为。

## 范围与非目标

范围内：Windows Presentation 内部状态/view models/XAML；Windows Chat 收藏 JSON；复用 Application Wiki Library、ActiveWiki、Data 与 Settings；资料库的全部页面、历史记录、标题过滤、增量分页和历史清空确认；Wiki 切换、数据下载/更新/失败处理/维护、`.pkg` 导入导出、强类型设置、宿主文件选择与 Runtime 内主题/侧键应用；相关测试、持续 specs 和当前状态文档。

非目标：多轮会话、自动保存 Chat 历史、同步 Chat 收藏、云账户、第二个 Viewer、SQLite/schema 或包格式变化、AI/NDJSON 变化、应用更新检查/发布渠道、日志系统重建、旧 MAUI 退休、Windows 产品化、Android。

## P0 / 兼容性影响

- Wiki 收藏只经 `WikiLibraryUseCases` 读写现有 `WikiFavorite`，不直接访问 Core/SQLite。
- 全部页面与历史只经 `WikiLibraryUseCases` 分页读取；清空历史调用既有 `ClearHistoryAsync`，并在 Presentation 显式二次确认，不改变历史写入或排序语义。
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
- Agent 的 `UsageLimits` 不再设置 `cost_limit`；typed usage 与 NDJSON 继续兼容 provider 返回的可选 `cost_usd`，避免改变协议。其余限位属于稳定性与协议护栏，不作为费用预算展示。
- 资料库使用一个 Windows 内部 view model 和两个内页状态，调用 Application 的 `PageRequest`（每批不超过 200）；过滤或切页时取消过期请求，避免大 Wiki 一次性载入内存。
- 历史清空采用页内二次确认，不新增平台对话框 port；操作失败只显示安全状态并保留当前列表。
- 数据管理以一个 Windows 内部 view model 编排现有 Application 用例；不增加公共 port、Core 接口或第二把数据门闩。Windows 文件选择和安全覆盖留在宿主边缘，Presentation 只接收选择结果。
- 设置使用显式保存，Application 先整体验证再持久化；宿主随后更新当前主题、Viewer 内容主题与侧键返回。内容主题变化通过既有 Viewer 重启/重绑路径生效。
- 待决问题：无；收藏跨设备同步、应用更新、日志产品化、MAUI 退休和发布打包留给后续明确 Phase。

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
- MODIFY Python Agent settings/tests：移除美元费用上限配置和执行限制，保留非费用运行限位。
- MODIFY Chat XAML/view model 与 Windows tests：把 Usage 展示替换为 Token 花费，并更新运行限位安全文案。
- MODIFY Chat XAML/App styles 与 Presentation boundary tests：修正停止图标居中，并为收藏星标建立独立布局栏。
- MODIFY Chat XAML/App styles 与 Presentation boundary tests：重排回答标签、来源列表、Token 与完成状态的视觉层级。
- MODIFY Chat input behavior/view model/XAML 与 Presentation flow/boundary tests：识别 IME preedit、保留 composer 问题文字并移除重复问题卡。
- ADD `Presentation/Library/*`：全部页面/历史的分页状态、过滤、日期分组、清空确认与 Avalonia 视图。
- MODIFY `Presentation/Shell/ShellViewModel.cs` / `ShellView.axaml`：增加资料库导航，组合 Application Library 并继续复用唯一 Reader。
- MODIFY `MainWindow.cs`：组合 Chat 收藏存储与 Application Wiki 收藏用例。
- MODIFY Windows tests：持久化、收藏流程、资料库分页/过滤/清空、唯一 Reader、导航、分隔条静态和目标行为。
- MODIFY Chat XAML/view model、收藏/资料库 XAML、共享控件样式与 Presentation boundary tests：移除示例问题入口，统一两个页头刷新按钮的 Terraria 次级操作视觉和交互状态。
- ADD `Presentation/Management/*`：Wiki 列表/切换、统计、下载更新、失败项、维护、导入导出、取消与页内确认。
- ADD `Presentation/Settings/*`：Application 强类型设置表单、显式保存与安全状态。
- MODIFY `Presentation/Shell/*`：增加数据管理/设置导航，组合新页面并在活动 Wiki 改变后刷新依赖状态。
- MODIFY `MainWindow.cs` / `WindowsClientRuntime.cs` / `ViewerBridge.cs`：Windows `.pkg` 打开/保存边缘、安全导出替换、设置实时应用与动态 Viewer 重绑。
- MODIFY Windows tests：数据管理状态机、确认/取消、两阶段导入、设置保存、导航/唯一 Viewer/Presentation 边界与宿主文件策略。
- MODIFY `openspec/specs/windows-presentation/spec.md`、README/项目基线/架构当前状态：记录完整 P0 UI 接管能力与 Phase 09 完成证据。
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
- [x] 移除 Agent 美元费用安全预算，并把 Chat Usage 区域改为 Token 花费。
- [x] 更新 AI Backend/Windows Presentation 持续契约与 ADR 0005。
- [x] 执行 Python Agent、Windows build/tests、diff/secret/双 Git 状态检查并分别提交源码与治理变更。
- [x] 修正思考中停止按钮图标偏移和回答内容与收藏星标重叠。
- [x] 执行最小 Windows Presentation build/tests、diff/双 Git 状态检查并分别提交源码与治理变更；人工 UI 验收由用户执行。
- [x] 优化回答标题、可信来源、Token 花费与完成状态的视觉层级。
- [x] 执行最小 Windows Presentation 定向验证并分别提交源码与治理变更；人工 UI 验收由用户执行。
- [x] 修正中文输入法 Enter 抢发、候选条锚点错位和发送后输入栏空白。
- [x] 执行最小 Windows Presentation 定向验证并分别提交源码与治理变更；人工 IME/UI 验收由用户执行。
- [x] 实现 Wiki 资料库 view model/XAML，接通全部页面、历史、过滤、增量分页和清空确认。
- [x] 将资料库加入 Shell，接通 Application Library 与唯一共享 Reader；补齐 Presentation flow/boundary 回归。
- [x] 执行 Windows Release build、相关/全量 tests 与静态边界检查；记录人工 UI 未验证项。
- [x] 更新持续 Presentation spec、README/项目状态和本计划；检查双 Git 状态并分别提交源码与治理变更。
- [x] 删除 Chat 空状态三个示例问题及其内部命令，统一收藏/资料库刷新按钮样式。
- [x] 执行 Windows Presentation build/tests、静态样式检查与差异检查；更新契约和本计划并分别保存源码/治理提交。
- [x] 完成并验证 `separate-product-from-reference` 外层源码基线；后续从根 solution 继续 Phase 09 功能开发。
- [x] 更新 Phase 09 完整接管范围并记录外层恢复点；复读 Application/Data/Settings、Windows Runtime、旧 UI 行为和 UI Skill。
- [x] 实现数据管理 view model/XAML 与 Shell 导航，复用 ActiveWiki/Data 用例并覆盖进度、取消、确认和安全错误。
- [x] 接通 Windows `.pkg` 打开/保存、两阶段导入与成功后安全覆盖；活动数据变化后重绑唯一 Viewer 并刷新依赖页面。
- [x] 实现设置 view model/XAML 与 Shell 导航，保存全部 Application 设置并在当前 Runtime 应用主题、内容主题和侧键返回。
- [x] 补齐 Presentation/宿主行为与边界测试，执行 Windows Release build、全量 tests、静态边界和 diff/status 检查。
- [x] 在目标 Windows 检查 Light/Dark、默认/最小窗口、宽/窄 Reader、管理操作确认与设置状态；记录证据。
- [x] 更新持续 specs、README、项目基线和架构当前状态；完成并归档 Phase 09，仅提交当前 tracked 范围。

## 验证与回归计划

- WSL：`rg` 静态边界、`git diff --check`、secret/生成物/双 Git 状态。
- Windows：Release restore/build/test；目标为 0 warning/0 error、全部测试通过。
- Windows UI：真实窗口以隔离宿主设置覆盖 Light/Dark、1280×820、900×600、宽/窄布局、数据源列表、页内切换确认、设置滚动与状态；只打开确认而不执行数据变更，不发起 AI provider 请求。
- 回归：只存在一个 `NativeWebView`/`ViewerWebViewSession`；AI 单轮发送、Wiki Search、来源精确页面/anchor、Reader 返回与 AI 故障隔离不变。
- 资料库：用确定性 fake Application delegates 覆盖首批/继续加载、过滤取消、全部页面与历史打开、日期分组、清空确认/失败；静态检查 Presentation 不出现 Core/SQLite 访问。
- 空状态/页头操作：静态回归确认 Chat 不再包含示例问题或废弃命令，两个刷新入口复用同一语义样式，且该样式包含 pointer-over、pressed、focus-visible 与 disabled 状态；Windows XAML 编译确认图标和模板有效。

## 风险与回退

- Chat JSON 损坏：返回空收藏并显示安全状态；保留 `.backup`，不影响主 Runtime。
- 极端分栏导致测量循环：用 Grid `MinWidth` 和一次性布局状态限制；不以自动关闭掩盖测量错误，可回退到上一提交 `6844809`。
- Wiki 收藏数据操作占用：捕获 Application 稳定错误并留在收藏页，不改变收藏数据库语义。
- 新导航与 Phase 08 spec 冲突：已在完成时更新持续 Presentation spec，五个真实入口成为当前契约。
- 大 Wiki 列表一次载入导致卡顿：严格使用 Application 分页和增量加载；过滤重置 offset 并取消过期请求，可整体回退到资料库切片前源码提交 `37fbccf`。
- 清空历史误操作：只有页内二次确认后调用既有用例；失败保留可恢复状态，不触碰页面、收藏或 Wiki 内容。
- 数据删除或 import 覆盖误操作：必须显示目标 Wiki 与影响范围并二次确认；Core 既有 pause/staging/backup/rollback 继续负责数据安全，UI 不复制交换算法。
- 动态 Viewer 在活动数据切换后持有旧端口：宿主只复用现有 `ViewerWebViewSession`，等待消费者恢复 Ready 后导航到新 BaseUri；失败留在页内/顶栏并允许重试。
- 导出覆盖损坏既有包：Windows 保存选择后先在目标同目录写唯一临时包，成功后再替换目标；失败清理临时文件并保留原文件。
- 长操作关闭窗口或用户取消：所有命令使用可取消 token，窗口生命周期继续取消 Runtime；取消不显示为数据失败。

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
- 2026-08-28：用户要求关闭安全预算并把原显示位置改为 Token 花费。代码确认唯一费用预算为 `WikiAgentSettings.cost_limit_usd` / PydanticAI `UsageLimits.cost_limit`；决定只移除美元费用中止，保留防循环、协议长度、context 与超时技术护栏，同时维持 typed usage/NDJSON 的可选费用字段兼容。
- 2026-08-28：移除公开 settings 的费用预算覆盖与 PydanticAI `cost_limit`，Chat 改为只显示输入/输出/合计 Token，`budget_exceeded` 兼容错误改用“运行限制”安全文案。源码提交为 `28f2d82`。
- 2026-08-28：用户实机截图确认思考中停止方块偏离圆形按钮，回答内容会进入悬浮收藏星标区域。代码确认停止按钮模板未设置 content alignment，星标与 ScrollViewer 位于同一覆盖 Grid；决定分别强制居中并改为独立星标布局列。
- 2026-08-28：完成 12×12 停止方块双向居中与 54 DIP 独立收藏栏，ScrollViewer 固定留在内容列；按用户要求只执行直接相关的 Presentation 定向验证。源码提交为 `27e5dde`。
- 2026-08-28：用户实机截图要求优化可信来源/Token/状态区域，并区分回答标题与 Markdown 正文。决定保持现有数据与命令不变，只用 XAML/styles 建立“AI 结论”标签、等宽来源行和轻量元信息条。
- 2026-08-28：完成“AI 结论”标签、绿色来源容器、等宽无边框来源行、圆形箭头，以及 Token/状态信息条。标准 Release 输出被用户正在运行的进程锁定后，未关闭该进程，改用 Windows 临时独立输出完成最小验证。源码提交为 `3f5d8f5`。
- 2026-08-28：用户实机截图确认中文输入法候选条锚到右侧 Reader，底部输入栏在回答期间为空，问题文字另成右上角小卡。代码确认 Tunnel Enter 未判断 `TextPresenter.PreeditText` 且发送时主动清空 `Question`；决定让 preedit Enter 只提交候选，并将问题保留在底部 composer。
- 2026-08-28：完成 IME preedit 检测、composer 问题保留与右上角问题卡移除；使用独立 Windows Temp 输出避免运行中应用锁定，最小相关验证通过。源码提交为 `37fbccf`。
- 2026-09-02：用户要求继续工作。读取计划规范、Application/Presentation/data compatibility specs、Phase 08 记录、旧全部页面/历史实现、当前 Shell/Favorites/Wiki、Application Library contracts/use cases 与 Windows tests；确定下一个最小切片为 Wiki 资料库，恢复点为内层 `37fbccf`、外层 `5c48f9e`。
- 2026-09-02：新增 Windows 内部 Wiki 资料库 view model/XAML 和 Shell 导航；每批 100 项，支持标题过滤、过期请求取消、继续加载、历史日期分组与页内清空确认。全部页面和历史项继续在唯一 Reader 中打开；Windows Presentation 定向编译与 tests 23/23 通过。
- 2026-09-02：Windows Release build 0 warning/0 error；全量 .NET tests 253/253 通过（Core 90、Application 54、Windows 81、WikiTools 28）。静态边界确认资料库 Presentation 不访问 Core/SQLite，且仍只有一个 `NativeWebView`/`ViewerWebViewSession`；本次未启动真实 UI，人工视觉验收待用户执行。
- 2026-09-02：Wiki 资料库源码以 `379ec17`（`feat(windows): add wiki library navigation`）保存；持续 Presentation spec、README、项目基线、架构状态和本计划由独立外层治理提交保存，不 push。
- 2026-09-02：用户以实机截图要求删除 Chat 三个示例问题，并修正收藏/资料库页头刷新按钮的默认灰色外观；确认本切片只涉及 Windows Presentation 内部 XAML、未再使用的内部命令、共享样式与静态回归，恢复点为内层 `e553e10`。
- 2026-09-02：删除三个示例按钮及未再使用的 `UseExampleCommand`；为收藏/资料库页头增加共享 `toolbar-action`，使用品牌浅色底、绿色描边与刷新图标，并定义 hover/pressed/focus-visible/disabled 状态。Windows 实际窗口截图确认 Chat 空状态和两个页头按钮在当前缩放下布局正常；测试产生的截图/脚本已清理，测试误写的数据根配置已恢复为真实目录。源码提交为 `d67d904`。
- 2026-09-02：暂停 Phase 09 功能开发，启动 `separate-product-from-reference` 前置修正。迁移内容基线为内层 `d67d904`，外层恢复点为 `d73436b`，上游参考点为 `a1dd899`；迁移后产品路径改为外层根级目录，Phase 09 仍保持 `in-progress`。
- 2026-09-02：外层源码基线完成 Windows Release 0-warning build、260/260 .NET tests、framework-dependent/self-contained publish smoke 与冻结 Python 回归；参考 Submodule 固定 `a1dd899`，Phase 09 仍为 `in-progress` 并可从根 solution 继续。
- 2026-09-02：用户明确要求完成 Phase 09。以外层 `ca1c031` 为恢复点，复读 Application Data/Settings、Windows Runtime、旧 MAUI 行为与 Terraria UI Skill；未跟踪治理归档、`.codex/`、`experiments/` 和历史架构文件保持用户原样。
- 2026-09-02：新增数据管理与设置页面，接通 Wiki 列表/切换、统计、下载更新、失败处理、维护、删除、`.pkg` 两阶段导入/安全导出和全部强类型设置；活动数据变化刷新依赖页面并只重绑既有 Viewer 会话。
- 2026-09-03：修正完整数据状态下更新入口被隐藏和高缩放开关状态裁切；Windows Release build 0 warning/0 error，全量 .NET tests 267/267 通过（Core 91、Application 54、Windows 94、WikiTools 28）。
- 2026-09-03：使用隔离 host settings 在目标 Windows 实际窗口检查 Light/Dark、1280×820、900×600、宽/窄布局、数据源列表、切换确认和设置状态；未确认任何数据操作、未保存设置、未请求 AI provider。完成持续文档同步并归档 Phase 09。

## 发现、偏差与决策

- 发现：现有 Application 已完整覆盖 Wiki 收藏，无需新增公共 API。
- 决定：Chat 收藏是用户显式操作的快照，不改变“每次提问独立单轮、普通问答不持久化”的 AI 契约。
- 决定：收藏/Reader 与 Wiki 资料库是 Phase 09 的前两个切片；用户于 2026-09-02 明确授权完成本 Phase，后续切片补齐数据管理、设置、导入导出、维护和错误流程，但不进入 Phase 10/11。
- 决定：用户明确覆盖 ADR 0006 原有“密钥不落配置文件”选择；仅 Windows 本机配置改变，Python Agent 仍只从子进程环境读取，NDJSON 和命令参数继续不携带密钥。
- 决定：空状态不再提供预设问题；用户仍从底部 composer 直接输入。页头刷新使用品牌浅色次级操作，不使用 Fluent 默认中性灰背景，也不改变命令语义。

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
- 费用预算/Token 展示修正后：WSL Python 3.12.3 Ruff lint、format 和 strict mypy 通过；离线 Agent tests 77 passed / 4 skipped（跨语言与付费 Provider 未启用）。Windows Release build 0 warning / 0 error；全量 .NET tests 249/249 通过（Core 90、Application 54、Windows 77、WikiTools 28）。未启动 UI、未请求 Provider。
- 停止按钮/收藏栏修正后：Windows Release 编译由定向 `Terraria_Wiki.Windows.Tests` 测试命令完成；Presentation boundary/flow 19/19 通过。未运行完整测试集、未启动 UI，人工视觉验收由用户执行。
- 回答结果视觉修正后：首次定向命令因运行中的 `Terraria_Wiki.Windows` 锁定既有 Release DLL 而未完成；使用独立 Windows Temp `OutDir` 后 XAML/Release 编译成功，Presentation boundary 4/4 通过。未运行完整测试集、未关闭或启动 UI。
- IME/composer 修正后：独立 Windows Temp `OutDir` XAML/Release 编译成功，IME key behavior、composer state 与 Presentation boundary 定向测试 6/6 通过。未运行完整测试集、未关闭或启动 UI；真实中文输入法视觉验收由用户执行。
- Wiki 资料库切片：Windows Release build 通过，0 warning / 0 error；Presentation flow/boundary 定向 tests 23/23、全量 .NET tests 253/253 通过（Core 90、Application 54、Windows 81、WikiTools 28）。覆盖首批/继续加载、标题过滤与过期请求取消、日期分组、清空确认/失败和唯一 Reader 打开；未启动真实 UI，人工视觉验收尚未执行。
- 空状态/刷新操作视觉切片：Windows `Terraria_Wiki.Core.slnx` Release build 通过，0 warning / 0 error；Presentation boundary/flow 定向 tests 24/24、Windows tests 85/85 通过。真实 Windows 客户端使用现有数据根启动，自动切换并截取 Chat、收藏和资料库三个页面；确认示例问题已移除，两个刷新按钮均显示 Terraria 浅绿色语义样式和刷新图标。应用正常关闭，无残留 Windows 客户端或 Python 进程；临时验证文件已清理。
- Phase 09 完整接管：Windows `Terraria_Wiki.Core.slnx` Release build 通过，0 warning / 0 error；全量 .NET tests 267/267 通过（Core 91、Application 54、Windows 94、WikiTools 28），其中 Phase 09/Presentation/Viewer 定向 17/17。静态检查确认 Presentation 无 SQLite/`DatabaseService` 访问、页面 XAML 无散落 raw hex、全应用仍只有一个 `NativeWebView`/`ViewerWebViewSession`。
- Phase 09 目标窗口：在 216 DPI（225%）下按 per-monitor DPI API 精确设置 1280×820 与 900×600 DIP；Light/Dark、宽/窄 Reader、数据管理列表、设置完整滚动和页内切换确认均可读，开关状态未被覆盖式滚动条裁切。验证使用临时 host settings 与缺失 AI 配置，只读取现有数据统计；确认动作未执行，真实活动 Wiki、设置和内容未改变。

## 完成摘要

Phase 09 已完成。Windows Avalonia Presentation 现以五个真实导航入口接管 AI 问答、收藏、Wiki 资料库、数据管理和设置；P0 Wiki 生命周期、导入导出、维护与错误流程继续复用现有 Application/Core 和唯一共享 Reader。最终 Windows Release build、267/267 .NET tests、静态边界和目标窗口验收均通过；公共 API、SQLite schema、`.pkg` 格式、AI/NDJSON 与 Viewer 内容协议未改变。Phase 10 的参考 MAUI 退休和 Phase 11 的 Windows 产品化保持独立范围。
