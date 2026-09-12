# 修复 Wiki 收藏、Viewer 页面身份与单顶栏

- 状态：ready-for-user-acceptance；实现、自动化验证与本机 Windows smoke/窗口检查已完成，保留用户最终实机验收
- Phase：无；承接 `post-phase-11-roadmap` 第一项
- 执行环境：WSL2 Codex CLI 负责规划、源码编辑与静态检查；Windows 11 x64 负责 .NET/Avalonia 构建、测试、NativeWebView 与真实窗口验收
- 创建日期：2026-09-12
- 最后更新：2026-09-12
- 范围：Viewer → Reader 当前页同步、所有本地 Wiki 收藏聚合与指定 Wiki 操作、独立 Wiki 单顶栏
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`openspec/specs/application-use-cases/spec.md`、ADR 0002、0003、0007
- 来源：用户提供的 `C:\Users\Xiao\Desktop\preplan-wiki-favorites-viewer-header-fix-20260912.md`；历史边界见 `openspec/changes/archive/add-local-wiki-search-page/plan.md`
- 当前授权：实施并验证本计划，可在独立功能分支选择性提交当前范围；不合并、不 push、不发布或重建安装器。

## 背景与当前证据

用户要求解决收藏错页、收藏仅显示活动 Wiki 和三层原生抬头的问题。预计划基于源码 ZIP；本计划已核对当前产品仓库，不能把 ZIP 中的建议直接视为实现事实。

### 已核验事实

规划时位于 `master`，HEAD 为 `b9ca593`。工作树已有 README、后续路线图、RAR Skill 退休与 ZIP Skill 新增等未提交改动；这些改动不属于本任务，不纳入本计划的保存点提交。

| 证据位置 | 当前实现与影响 |
|---|---|
| `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderPaneViewModel.cs` | `OpenAsync` 在导航任务返回后把请求标题写入 `_currentPageTitle`；`BackAsync` 清空标题与星标；`SetHomePageAsync` 直接设置首页身份。均无法代表 WebView 实际提交页。 |
| `assets/windows/Viewer/Web/_common/wiki-app-common.js` | `redirect` 等待 `PageRedirectAsync` 后检查 `navigationVersion`，再写 `window.pageTitle` 与 DOM。C# Read 成功并不等于页面提交；旧结果可能直接被丢弃。 |
| 同一 JS 的 `gotoPage` / `backToPage` | 内链和宿主 `GotoPage` 共用 `gotoPage`；Back 再经过 `redirect` / `PageRedirectAsync`。历史取导航前的 `window.pageTitle`，正常来源是上一次响应的规范标题，而非本次请求别名；历史在图片等待后保存。 |
| `ViewerBridge.cs` / `ViewerWebViewSession.cs` | Bridge 已取得 `read.Title`；Session 目前只有 Ready/Status/ProtocolError 等事件，没有页面提交事件。`ViewerReady` 只通知首屏；空 Back 只报告错误并正常返回 Task。Bridge 返回栈未按 Wiki 身份分区。 |
| `WikiLibraryUseCases.cs` / `WikiDtos.cs` | 收藏读写绑定 `_state.ContentDatabase`；`WikiFavoriteItem` 只有标题和时间。旧 API 还有现有测试调用，适合保留并新增带 Wiki 身份的边界。 |
| `FavoritesViewModel.cs` | 收藏打开和删除只传标题；刷新清空集合后只取 `PageRequest(0, 200)`；`IsBusy` 时直接返回，会丢失重叠刷新请求。 |
| `WikiDataPaths.cs` / `DatabaseModels.cs` | 当前内容库路径为 `<root>/<DataFolder>/data.db`，不是预计划写的 `Content.db`。`WikiFavorite.WikiTitle` 是每库主键，日期为 `DateTime`。 |
| `DatabaseService.cs` | 构造会创建目录，连接含 `ReadWrite | Create`；`Init` 设置 WAL、建所有表、FTS 与首页；查询也调用 Init，Close 可切 journal mode。不能用于无副作用的收藏扫描。 |
| `WikiManagementViewModel.cs` / `MainWindow.cs` | 管理页调用 `ActiveWiki.SwitchAsync`，再触发 `DataChanged`；`MainWindow.OnWikiDataChanged` 是 `async void`，先重置 Shell 再等待 `RebindViewerAsync`。当前没有供收藏直接等待的完整切换入口。 |
| `ActiveWikiUseCases.cs` / `WindowsClientRuntime.cs` | Application 切换复用 CoordinationGate、consumer pause、OperationGate 与回滚；Runtime 组合 Viewer 与 WikiTool consumer。不能从收藏复制一套切换或自行重启 AI。 |
| `ShellView.axaml` / `ReaderView.axaml` | 现有 64 DIP Shell 顶栏、工作区独立 `WikiToolbar` 和固定 48 DIP Reader header 确实叠加；隐藏 Border 不会自动去掉固定行高。 |
| `ShellViewModel.cs` | Home/Search/Reading 三态已有；仅搜索结果打开时显式进入 Reading，首页内链没有对应状态通知；数据刷新和部分列表刷新为 fire-and-forget。 |
| `PresentationFlowTests.cs` | `ReaderFavoriteUsesCurrentResolvedPageAndClearsOnBack` 当前断言 Back 后不可收藏，需要改为新的正确行为回归，不能沿用旧断言。 |

已读取根 solution、相关 Core/Application/Windows 与 Windows tests 项目配置、上述实现和直接调用方、Application 收藏测试、Viewer bridge 测试、Presentation 回归位置、主题 token 与按钮/header 样式。不新增 project 或依赖。

### 事实边界与文档冲突

- 预计划引用 `Snipaste_2026-09-12_17-31-17.png`；本轮在桌面未找到该文件，未实际查看原图。三层结构有 XAML 和归档反馈佐证，不能把它写成此次实机验证通过。
- `local-wiki-viewer/spec.md` 与 ADR 0003 仍有 `RecordVisitAsync` 描述；当前 Application spec、项目基线、源码与测试明确停止持久 WikiHistory 读写。按能力职责采用当前 Application 契约，本 change 不恢复历史；实施时在涉及的 Viewer spec 中修正失效条目，ADR 的旧描述仅加历史澄清，不重做 transport 决策。
- UI Skill 仍引用旧 `terraria-wiki-offline/src/` 和两个 Git 状态；按当前根治理规则使用根级 `src/` 与唯一产品 Git。只把此偏差记录在计划，不修改 Skill。
- 归档旧计划的文件 KEEP 清单只约束旧 change；本 change 明确包括 Reader、Application/Core 收藏边界和最小 Viewer JS 协议修复。

## 目标与完成定义

1. Viewer 实际提交的 `(WikiId, CanonicalTitle)` 是 Reader 标题、位置与星标的身份来源；搜索、资料库、收藏、Chat 来源、首页、内链、Redirect 和 Back 全部一致。
2. 导航失败、空 Back、过期结果和取消不错误覆盖仍显示的页面；切换 Wiki/数据根或重绑会话后不接受旧页面事件，不将旧标题写入新 Wiki。
3. 收藏页显示当前数据根中全部已注册且已有本地内容库的收藏，每条显示页面、所属 Wiki、时间；不同 Wiki 同名页面独立，不静默只显示 200 条。
4. 查询与取消非活动 Wiki 收藏不切 ActiveWiki、不暂停 Viewer、不更改设置；打开非活动 Wiki 收藏复用完整切换流程，等待 Viewer 可交互后再打开目标。
5. 收藏仍存于各内容库 `WikiFavorite`，不搬迁至 Manager，不改变 schema、时间编码、主键或包格式，已有收藏不要求重新添加，回退后仍可见。
6. 独立 Wiki 只保留 Shell 单顶栏；Reader header 真正折叠为零。搜索、主页、返回搜索、页面返回、收藏和底部状态反馈均可用。
7. 普通 Reader 的 header、关闭、1100 DIP 分栏/覆盖和唯一 Viewer 保持；Light/Dark、1280×820、900×600 与窗口缩放验收完成。
8. 定向与全量 Windows .NET 回归、Release XAML 构建、实际窗口和数据兼容检查通过后才标记 completed。用户实机验收通过后才合并。

## 前置条件

- Phase 00–10 已完成，Phase 11 本地基线可复用；本任务不依赖公开发布输入，不进入 Phase 12/13。
- 已从 `master@b9ca593` 建立 `fix/wiki-favorites-viewer-header`。开始时已有 README、后续路线图和 Skill 退休/新增等未提交改动，不能安全建立包含整个工作树的 checkpoint；以 `b9ca593` 为恢复基线，后续只选择性提交本 change 文件，不自动 cherry-pick 旧分叉。
- 使用临时合成数据根准备至少两个 Wiki、同名页面、Redirect/anchor、首页、现有收藏、缺失/损坏库与 WAL fixture；不对用户正式数据运行写入测试。
- 需 Windows .NET 10、现有依赖、WebView2 和可交互窗口。没有目标环境时明确记录缺失验收，不能用 WSL 或静态测试代替。
- 不需要付费 AI、联网 Wiki、下载新数据、重打安装器或修改现有安装版本。

## 范围与非目标

允许修改：当前页提交与会话失效接线、收藏专用 Core 存取和 Application 聚合/显式身份接口、宿主可等待切换协调、Favorites 行元信息与刷新一致性、Wiki 顶栏/Reader header 布局、相关测试及持续契约。

非目标：搜索排序/FTS5/防抖算法、抓取更新、导入导出算法、全局 Quick Access、托盘、AI/MCP/Sidecar/会话存储、安装发布、跨设备同步、历史恢复、Manager 中心化迁移、多平台泛化、namespace 重命名、收藏整页视觉重做、第二 Viewer，以及网页 Logo、横幅、正文 HTML/CSS 样式变更。

## P0 / 兼容性影响

- 所有数据库访问留在 Core；Application 只编排并返回 DTO，Presentation 不接触 SQLite 或持久化模型。
- 聚合范围是当前配置数据根的 Manager 注册 Wiki，不扫描磁盘其他数据根，不把 UI 可见收藏当成新的数据副本。
- 扫描不得执行 Init、建库、建表、FTS 初始化、首页 seed、迁移、checkpoint 或 journal-mode 切换。读取 WAL 中已经提交的收藏，不能使用忽略 WAL 的方式得到假空结果。
- 旧库没有收藏表时可返回空并附兼容状态；损坏、权限、路径歧义和忙碌必须可区分。已有收藏旧标题原样保留；新导航收藏使用规范标题，不在扫描中批量重写旧别名行。
- 明确 Wiki 整库删除或包替换继续沿用既有收藏数据影响语义；列表相应刷新，不承诺从已删除数据恢复收藏。
- 写入保持幂等，保留首次收藏时间；取消收藏只删除指定 Wiki 的指定主键，不触及页面、Redirect、资源、FTS 或旧 WikiHistory。

## 方案与待决问题

### A. DOM 提交驱动页面身份

采用最小内部 `ViewerPageCommitted` 通知；已查实 `PageRedirectAsync` 不是提交信号，因此本计划明确需要修改公共站点 JS 的协议接线。

```text
PageRedirectAsync：读规范页面并携带本次会话/读取标识
  → JS 检查 navigationVersion
  → 写入规范标题与正文 DOM
  → ViewerPageCommitted（导航序号 + 本次读取标识）
  → Bridge / Session 校验当前会话，发布 ViewerPageIdentity
  → MainWindow 转到 UI 线程 → Reader 更新身份与收藏状态
```

- 新内部 `ViewerPageIdentity` 至少含 WikiId、CanonicalTitle；可选 Anchor 仅表达定位，不进入收藏主键。WikiId 来自宿主绑定会话的活动 Wiki 快照，不能相信 JS 任意传入，也不能在异步完成时重新读取已变化的 ActiveWiki 来补身份。
- 每次 Navigate/强制重载/换 Wiki 建立新的 session generation，清理旧身份、待提交读取及旧 Wiki history；同一 generation 下以 JS navigationVersion 拒绝倒序通知。同 origin 强制重载也必须失效，不能只靠端口变化。
- PageRedirect 响应附带与本次 canonical Read 关联的标识，commit 回传后宿主据此取可信身份。临时关联有界并在提交、导航失效或 session 重置时释放；不保存完整 HTML 副本。
- 通知点位于 `redirect` 最新导航检查和同步 DOM 写入之后、站点 `config.refresh()` 之前，与首屏 Ready 的现有边界相邻。不等图片加载完成才更新收藏；站点增强脚本失败不把已显示正文身份退回旧页。
- Ready 继续只表示会话可交互；启动首页的 commit 若早于 Reader Attach，应由 Session 保留最新身份，Attach 时应用一次。移除 `SetHomePageAsync` 猜身份的用途，首页目标配置与实际显示身份分开。
- `OpenAsync` 保留命令、loading、取消与错误处理；不再用请求标题设置最终身份或凭命令返回宣布另一页已打开。Back 的空栈/错误需显式传递，不能紧接着显示“已返回上一页”。
- Reader 查询/写入收藏时捕获完整身份与递增版本；旧异步状态不得覆盖新页。收藏状态加载期间禁用 toggle，避免按旧星标反向写入；写入成功与状态未知/失败明确区分。
- 导航取消若发生在 DOM 提交前，保留旧页；若提交已发生，显示页身份继续生效，不能因 C# await 被取消而退回旧身份。无效会话的迟到通知一律忽略。
- 首页内链 commit 到非首页时，Shell 进入 Reading 并提供正文操作。返回首页 commit 恢复 Home；用户已主动进入 Search 时，隐藏 Reader 的迟到 commit 不抢回正文。需要保留 Shell 意图版本与现有三态，而不是新增搜索状态机。
- 普通内存返回继续保存 canonical title 与滚动位置。数据源重绑时清空旧栈；空栈不清身份。失败 Back 是否恢复弹出的历史项以定向测试确定最小补丁，不扩展为新历史系统。

### B. 跨库收藏，零迁移

保留现有 `WikiFavoriteItem` 与当前活动 Wiki API。新增 `GlobalWikiFavoriteItem(WikiId, WikiDisplayTitle, PageTitle, FavoritedAt)`，以及可携带每 Wiki 安全读取问题的 `GlobalWikiFavoritesResult`。新增 `GetAllFavoritesAsync`、带 wikiId 的 Is/Set 收藏用例；方法命名在实施时可微调，身份和行为契约固定。

1. Application 在已有协调边界内枚举 Manager WikiBook，使用 `WikiDataPaths.Create` 解析路径。扫描以全部实际收藏为结果，按 `FavoritedAt DESC`，同时间用 WikiId 和 ordinal title 稳定排序。当前数据规模采用一次聚合，不传 `PageRequest(0, 200)`；增加超过 200 条测试，不预建索引或缓存框架。
2. 新增 Core 内部窄 `WikiFavoriteStore`，复用现有 sqlite-net 依赖、`WikiFavorite` 映射和路径契约；只负责已存在数据库的收藏读写，不复制 DatabaseService 初始化或其他业务。Core 已允许 Application 访问 internal，无需新 project/public port。
3. 读连接使用只读打开且不含 Create，写连接使用 ReadWrite 且不含 Create；不调用完整 ContentDbService。连接每次短生命周期，取消后确保底层操作结束再关闭连接/释放门闩，避免只取消 await 后仍在后台写入。
4. `WikiDataPaths` 的词法校验不足以独自保证现存目录安全；复用兼容层的 reparse-point 规则检查目标目录/数据库，不跟随逃逸路径。检查存在和真正 open 之间消失时返回缺失，不创建替代库。
5. 当前标准名为 `data.db`，旧名称为 `Terraria_Wiki.db`。对未激活过的已有旧库，使用兼容层规则做只读、无歧义解析；只存在可确定归属的旧库时读取现有表。两套库/目录或孤立 sidecar 无法确定权威来源时返回问题，不自动合并或迁移，也不把同一 legacy 库归给多个 Wiki。需要的窄解析方法放在 Core，不能在 Application 复制迁移逻辑。
6. 扫描和写入按既有顺序复用 CoordinationGate 与 OperationGate，避免与切换/导入/删除并发；不取得 consumer pause lease。占用时保留旧列表并显示可重试状态，不无限排队。活动库写入优先复用现有已初始化 ContentDatabase 的收藏方法；非活动库通过窄 store。不能嵌套获取同一个门闩。
7. 库不存在视为无本地数据；有库但损坏/不可读返回该 Wiki 的安全问题并保留其他成功结果。此前成功但本次失败的 Wiki 项可保留为标记不可用的旧项，不显示为确认已删除。取消传播，不转为空列表；顶层失败不提前清空现有集合。
8. Set 操作以事务或已有互斥范围保证幂等，返回成功后的真实状态；失败传递安全错误，不用 `!favorite` 冒充真实值。缺表/缺库写入失败，不建新 schema。
9. 刷新采用单次在途 + pending 合并请求，并捕获 attach/runtime generation；结束前保证有一次覆盖最新变更的刷新。Detach 或重组后的旧结果不回填；通过可控异步测试先复现 Busy 丢刷新，再实施最小修正。Chat 收藏读取失败与 Wiki 读取失败分开反馈，保留另一类可用内容。
10. 触发点：收藏增删、进入收藏、手动刷新、Wiki 切换、数据根重组、导入/删除/替换完成；复用现有通知，不新增全局事件总线。取消当前显示页收藏后同时刷新 Reader 星标。

替代方案：短生命周期 ContentDbService 仍有建表/seed/journal 副作用，不采用；Manager 中心化需要迁移和包/回退新语义，本 change 不采用；轮流切 ActiveWiki 扫描或取消收藏明确禁止。

### C. 可等待的 Windows 切换与打开

当前唯一完整链路是管理页 → Application Switch → DataChanged → MainWindow Rebind，不能把其中的事件触发当成完成。

- 在现有 MainWindow 宿主接线中抽出可等待的切换/刷新方法，通过现有内部 action/delegate 接给 Shell/Favorites 与管理页；不创建公共 Application UI port 或另一 Runtime。
- 两入口共用：防重入与失效旧导航 → `ActiveWiki.SwitchAsync`（已有 pause/resume/回滚）→ Shell 搜索/Home/列表失效与管理状态刷新 → `RebindViewerAsync` → 等待 Ready/对应 generation → 可选打开收藏目标。
- 管理页继续保留当前切换确认；收藏行明确显示所属 Wiki，点击打开即按本需求执行必要切换，不新增重复确认弹层。该收藏打开语义在 Presentation spec 中与管理页手动切换确认分别表述。
- 将 `OnWikiDataChanged` 内可复用工作收敛为 Task；事件包装只服务已有导入/删除通知。避免同一次切换既由协调方法重绑、又因事件第二次重绑。数据根重组/关闭取消在途工作。
- 同 Wiki 收藏不重新切换或回首页；非活动 Wiki 必须完成重绑后再发 `GotoPage`。打开后主页面仍是收藏，采用普通 Reader header/宽窄布局。
- 切换失败由 Application 恢复旧库；重绑失败可能已经切库成功，需如实保留新活动状态、禁用目标打开并提供恢复，不声称回到旧 Wiki。目标缺失也不自动删除收藏或偷偷切回旧 Wiki。
- WikiTool/AI consumer 的暂停恢复由当前 Composite coordinator 完成；使用离线 fake 验证，不修改 Agent/MCP/Sidecar。

### D. 单顶栏布局

保持 Shell 的 72 DIP rail 与 64 DIP 顶栏；移除 Workspace 的独立 WikiToolbar 行，将四类内容及唯一 ReaderHost 的行索引一起调整，仍在同一父级。

| Wiki 状态 | 顶栏上下文操作 | 内容 |
|---|---|---|
| Home | 搜索输入、搜索；如需保留首页收藏/已有页面返回，使用同栏 Reader 图标且按能力启用 | 唯一 Viewer 的真实首页 |
| Search | 主页、搜索输入、搜索 | 原全宽搜索结果 |
| Reading | 主页、返回搜索（有可返回的查询/结果时）、页面返回、收藏、搜索输入、搜索 | 唯一 Viewer 正文与底部状态栏 |

- 所有状态左侧仅显示 `Terraria AI Wiki`。去掉原生 `Terraria Wiki` / `本地搜索` 重复文本，网页 Logo 不动。
- Home 的默认视觉以标题和搜索为主，但不能使原有首页收藏能力完全消失；优先同栏保留紧凑收藏图标。页面返回与返回搜索使用不同 Tooltip/Automation Name；无历史时页面返回禁用。
- 使用 Auto 内容栏 + 剩余宽度搜索 Grid，不用横向 StackPanel 固定搜索 300。计划搜索宽度为 180–300 DIP，剩余空间可伸缩；上下文与搜索图标均为 44×44，组内间距 4–8 DIP。
- 900 DIP 窗口扣除 rail 72 与现有左右 margin 44 后约 784 DIP；产品标题预留约 200，最多五个 44 图标约 220，搜索至少 180，间距控制在余量内。此为布局预算，必须通过实际字体与缩放测量，不能作为已通过证据。普通状态保持单行；启动错误优先保留恢复按钮与截断状态，不让不可用 Wiki 操作挤占恢复入口。
- 若实测剩余空间不足，先移除按钮文字、压缩组间留白，保留图标与搜索下限；不减小 44 DIP 命中区，不恢复第二工具栏，不产生水平滚动。测试 100%/150% 缩放与键盘焦点。
- Reader 改为 `RowDefinitions="Auto,*,Auto"`，header 容器在非 Wiki 可见时保持 48 DIP，在独立 Wiki 隐藏时其行实际高度为零；底部状态条始终保留。普通 header 的 Back/title/location/favorite/close 与 Esc/遮罩不变。
- 复用 `icon-button`、现有 Path 和语义 token；移位后保留 `WikiSearchInput` 名称、Enter/中文输入法接线与 Automation IDs；不增加图标包、主题或页面颜色。

### 待实施验证项

三个主要技术不确定项已由源码收敛：真实提交在 JS DOM 写入后；跨库需窄 store；完整切换需把宿主事件链改为可等待方法。以下不是假定已通过的事实：

- WAL 只读连接在实际 Windows 文件权限/锁场景下的行为、旧库歧义解析和取消期间的连接释放需要 fixture 验证；失败必须可观察，不能降级为创建或迁移。
- Busy 丢刷新具有源码依据，仍需确定性测试复现后决定 pending 的最小接线。
- 900×600 尺寸预算、首页同栏收藏和各图标的真实视觉需要 Windows 验收；原截图本轮不可用，实施验收前可补看原图，但不阻塞计划创建。
- 不新增长期架构决策。若实现发现必须中心化迁移、扩展依赖或新建公共框架，应停止该扩展并修订计划，不能以本 draft 自动授权。

## 文件变更

以下为后续实施清单；本次仅 ADD 本 `plan.md`。

| 操作 | 文件 | 用途 |
|---|---|---|
| ADD | `src/Terraria_Wiki.Windows/Viewer/ViewerPageIdentity.cs` | 小型内部身份/提交契约；不含数据库模型 |
| MODIFY | `src/Terraria_Wiki.Windows/Viewer/ViewerBridge.cs` | Read 关联、commit 校验与身份通知、会话切换历史失效 |
| MODIFY | `src/Terraria_Wiki.Windows/Viewer/ViewerWebViewSession.cs` | generation、最新已提交身份、重绑失效、Back 错误语义 |
| MODIFY | `assets/windows/Viewer/Web/_common/wiki-app-common.js` | 在既有 DOM commit 处发送最小通知并回传关联信息 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderPaneViewModel.cs` | 消费身份、显式 Wiki 收藏、异步结果防过期 |
| MODIFY | `src/Terraria_Wiki.Windows/MainWindow.cs` | 页面事件接线、可等待切换/重绑共享入口、解绑与取消 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Management/WikiManagementViewModel.cs` | 复用可等待宿主切换 action，避免重复 DataChanged 重绑 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs` | 全身份打开收藏、Home 内链状态、header/顶栏可见性 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml` | 搜索/上下文移入单顶栏，删除工作区 toolbar 行并调整行索引 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderView.axaml` | header 真正折叠，普通模式保持高度及行为 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Favorites/FavoritesViewModel.cs` | 全局结果、显式身份操作、pending/generation、部分失败 |
| MODIFY | `src/Terraria_Wiki.Windows/Presentation/Favorites/FavoritesView.axaml` | 所属 Wiki 元信息、不可用/部分失败必要反馈 |
| MODIFY | `src/Terraria_Wiki.Application/Contracts/WikiDtos.cs` | 全局收藏与每 Wiki 安全问题 DTO |
| MODIFY | `src/Terraria_Wiki.Application/UseCases/WikiLibraryUseCases.cs` | 聚合及指定 Wiki 读写，复用现有门闩 |
| ADD | `src/Terraria_Wiki.Core/Data/WikiFavoriteStore.cs` | 已存在库的窄收藏存取，复用既有 SQLite/model |
| MODIFY IF REQUIRED | `src/Terraria_Wiki.Core/Compatibility/LegacyWikiCompatibility.cs` | 暴露最小只读现存库路径解析/校验，原迁移流程保持 |
| MODIFY IF REQUIRED | `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml.cs` | 顶栏宽度适配与原搜索 Enter 接线验证，不重建 Reader |
| MODIFY | `tests/Terraria_Wiki.Windows.Tests/ViewerBridgeTests.cs` | commit、安全来源、canonical、generation 和 history 回归 |
| ADD | `tests/Terraria_Wiki.Windows.Tests/ViewerPageIdentityTests.cs` | Session/Reader 身份竞态与提交顺序的定向测试 |
| MODIFY | `tests/Terraria_Wiki.Windows.Tests/PresentationFlowTests.cs` | 替换 Back 清空错误断言，补齐全局收藏/三态/切换流程 |
| MODIFY | `tests/Terraria_Wiki.Windows.Tests/PresentationBoundaryTests.cs` | 单顶栏、零高度、唯一 Viewer 与薄 Presentation 边界 |
| MODIFY IF REQUIRED | `tests/Terraria_Wiki.Windows.Tests/Phase09TakeoverTests.cs` | 管理页共享切换 action 和确认/数据变化回归 |
| MODIFY | `tests/Terraria_Wiki.Application.Tests/WikiLibraryUseCaseTests.cs` | 全局排序、>200、幂等、门闩、取消与部分失败 |
| MODIFY IF REQUIRED | `tests/Terraria_Wiki.Application.Tests/ActiveWikiTests.cs` | 复用原切换、回滚、无隐式切换断言 |
| ADD | `tests/Terraria_Wiki.Core.Tests/WikiFavoriteStoreTests.cs` | 只读零建库、WAL、路径、旧表兼容与定向写入 |
| MODIFY | `openspec/specs/windows-presentation/spec.md` | 全局收藏、实际身份、单顶栏与收藏点击切换语义 |
| MODIFY | `openspec/specs/local-wiki-viewer/spec.md` | 内部 commit 身份契约，清除旧持久历史调用描述 |
| MODIFY | `openspec/specs/application-use-cases/spec.md` | 全局收藏边界、显式 Wiki 身份、无副作用扫描 |
| MODIFY | `assets/windows/README.md` | 记录 JS 产品覆盖、来源及更新后的资产清单 hash |
| MODIFY IF REQUIRED | `docs/adr/0003-use-loopback-local-viewer-transport.md` | 仅澄清旧历史写入描述已被当前契约替代 |
| MODIFY AT COMPLETION | `README.md`、`openspec/project.md`、`openspec/changes/post-phase-11-roadmap/plan.md` | 更新本 change 的实际入口/状态；保护已有编辑 |

KEEP：`WindowsClientRuntime.cs` 的组合与 AI 生命周期、`ActiveWikiUseCases.cs` 的切换/回滚、`ApplicationState.cs` 的现有门闩、`DatabaseService.cs` 的通用初始化、全部搜索实现与 WikiViewModel、共享主题、持久化模型、项目依赖、ViewerServer/outer host/iframe bridge、四站点 HTML/CSS、Agent 和打包。若定向证据要求微调这些文件，先在此记录原因和最小差异，不能自动扩大范围。

## 执行步骤

- [x] 读取预计划、治理、相关契约与当前代码，确认 DOM commit、数据库副作用和切换链；建立独立 draft。
- [x] 实施授权与保存点：范围已获批；从 `master@b9ca593` 创建 `fix/wiki-favorites-viewer-header`，记录并排除既有工作树改动；回退以该基线及后续选择性提交为准，不覆盖用户工作。
- [x] 行为回归测试：覆盖 Reader A→内链 B→Back A、alias→canonical、空 Back/失败/过期 commit、启动首页、跨 Wiki 同名、Busy 合并刷新。偏差：实施期间先完成最小协议接线再补测试，未保留旧实现逐项 red 日志；首轮编译失败仅用于收敛新 API，不能冒充行为 red 证据。
- [x] Viewer 身份：实现 generation + read token + DOM commit 通知、Reader 消费、Ready/历史重绑失效及 Back 修正；bridge/Reader 回归和真实 NativeWebView smoke 通过。
- [x] Core/Application 收藏：实现无 Create/Init 的窄 store、全局 DTO、全量聚合与显式 Wiki 操作；覆盖零建库、缺表、WAL、歧义库、>200、损坏降级和不隐式切换。
- [x] Favorites/宿主接线：实现来源元信息、单在途 + pending 刷新、Reader 星标更新及可等待切换打开；管理页与收藏共用一次宿主重绑。
- [x] 单顶栏：迁移现有控件、折叠 Wiki Reader header、补首页内链状态；XAML 边界测试和本机 1280×820/900×600 浅深色窗口检查通过。
- [x] 文档与资产证据：更新三个持续 spec、澄清 ADR 0003 旧历史描述，记录 JS 产品差异与最终资产 hash。
- [x] Windows 自动验证与本机检查：Release build、全量 tests、NativeWebView Redirect/Back/主题/资源 smoke，以及当前 125% DPI 下浅深色 1280×820/900×600 截图已完成；日志和截图位于忽略的 `artifacts/fix-wiki-*`。100%/150% DPI、IME/hover/focus 与主观视觉仍归入下一项用户实机验收，不据此宣称通过。
- [ ] 用户实机验收：确认收藏正确性、所有 Wiki 可见、单顶栏与普通 Reader；通过后按授权合并，不自行 push、发布或重建安装器。
- [x] 实施收尾：检查 diff/status/忽略的合成数据与截图，选择性提交当前 change；保留用户既有 README、路线图和 Skill 改动。因用户实机验收尚未确认，不标记 completed、不归档、不合并。

## 验证与回归计划

### 确定性行为矩阵

| 场景 | 必须断言 |
|---|---|
| 搜索 A，内链 B，收藏，页面返回 A | 收藏只写 B；回 A 后星标来自 A 的实际状态 |
| alias A 与另一 alias C 均指 B | 新增收藏主键均为 B，同 Wiki 不重复；anchor 不成为主键 |
| PageRedirect 已返回但 JS 判为过期 | 不发送 commit，不改变 Reader 身份 |
| A 状态查询慢于 B commit；收藏写入后切 Wiki | A 的结果不能覆盖 B；写入身份是点击时捕获的 Wiki/标题 |
| 空 Back、页面不存在、取消、config.refresh 异常 | 显示页未变时保留旧身份；DOM 已提交时采用新身份；错误不被成功状态覆盖 |
| 同 origin 重载、换 Wiki、换数据根、关闭 | 旧 generation/Read 标识被拒绝，旧栈不导航进新 Wiki |
| 首页启动、首页内链、返回首页、搜索中迟到提交 | 初始身份可重放；内链后正文按钮出现；Search 不被旧页面抢占 |
| Wiki A 收藏 X/Y，Wiki B 收藏 X | 全局三项，来源清楚；同时间顺序稳定，超过 200 项不截断 |
| Active=A，取消 B/X | 只改 B/X，活动 ID/设置/Viewer URI/当前正文不变，consumer pause 次数不增加 |
| Active=A，打开 B/X | 复用正式切换/consumer 链，只重绑一次，Ready 后才打开 X；收藏主页面保留 |
| 缺失、缺表、损坏、只读、路径逃逸、旧文件名、WAL | 无创建/迁移/误归属，成功 Wiki 不被清空，错误安全，WAL 已提交收藏可见 |
| 导入/删除/切换占用，刷新期间增删，Detach 后旧请求完成 | 门闩正确，最终刷新不丢，过期数据不回填，不把取消吞为空 |
| UI 导航、Chat/收藏/资料库/数据管理/设置 | 原宽窄规则、header/关闭、活动回答和会话收藏不回归 |

### WSL 静态检查

- `git diff --check`，核对实际文件清单、生成物、未提交改动和 plan 复选框。
- `rg -n '<NativeWebView|new ViewerWebViewSession\(' src/Terraria_Wiki.Windows`：保持各一个实例位置。
- 检查 Presentation 未新增 SQLite/Core 数据访问、依赖或 raw hex；所有新移位图标命中区、Tooltip、Automation Name 和交互状态符合 UI Skill。
- 检查 JS diff 仅涉及导航提交消息与必要 identity 传递，保留来源注释、版权和站点内容；对照 `assets/windows/README.md` 的原清单计算变更后 hash。
- WSL 不承担 Windows 平台通过结论；规划阶段不为此运行构建或修改测试。

### Windows 11 x64

在 Windows PowerShell、仓库根执行现有 .NET SDK：

```powershell
dotnet build Terraria_Wiki.Core.slnx -c Release
dotnet test tests/Terraria_Wiki.Core.Tests/Terraria_Wiki.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~WikiFavoriteStoreTests
dotnet test tests/Terraria_Wiki.Application.Tests/Terraria_Wiki.Application.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~WikiLibraryUseCaseTests|FullyQualifiedName~ActiveWikiTests'
dotnet test tests/Terraria_Wiki.Windows.Tests/Terraria_Wiki.Windows.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ViewerBridgeTests|FullyQualifiedName~ViewerPageIdentityTests|FullyQualifiedName~PresentationFlowTests|FullyQualifiedName~PresentationBoundaryTests|FullyQualifiedName~Phase09TakeoverTests'
dotnet test Terraria_Wiki.Core.slnx -c Release --no-build
```

要求 Release 为 0 warning / 0 error；记录实际通过数，不沿用旧计划的 349/349。测试类名称以实施新增后的实际名称复核，不用空筛选“通过”当证据。

- 使用既有 Windows smoke/合成 fixture 扩充真实 JS commit 验证，覆盖内链、Back、Redirect、快速导航；Bridge 单元测试本身不能证明 DOM 消息已发送。复用四站点的公共脚本，至少验证四个入口均能提交首页身份。
- 1280×820 与 900×600，各 Light/Dark，100% 与 150% 缩放：Home/Search/Reading 顶栏一层、无重叠/水平滚动、header 实测零高度、状态条可见、网页 Logo 保留。
- 检查首页收藏、主页、返回搜索与页面返回的不同语义；Tab/Enter/中文输入法、hover/pressed/focus/disabled 与星标反馈。
- 普通 Reader 在 1100 DIP 两侧、拖柄、关闭/遮罩/Esc、进入退出 Wiki/设置时保持预期。缩放与布局过程中 NativeWebView/session 不重建。
- 使用临时双 Wiki 数据根实测聚合/跨 Wiki 打开/非活动移除；在应用正常关闭后核对数据库与 WAL 一致性。只读扫描需核对 schema、收藏数据和未创建目录/库；不把 SQLite 必要锁管理误称为业务数据迁移。
- 全量 P0 既有测试覆盖 Search/Redirect/asset、同步、package/rollback、数据布局兼容、Viewer listener 生命周期；AI 仅运行既有离线 fake，不发真实 provider 请求。

Android、其他平台、publish/安装器、公开发布本次均不适用。

## 风险与回退

- 页面事件晚于 UI 操作、同 origin reload 或旧库 Read 跨切换：以 generation、read 关联与导航序号共同约束，不能只新增一个标题事件。
- 扫描绕过 Core 初始化后，缺表、旧日期编码或 WAL 读取不一致：复用现有映射，先用兼容 fixture 验证；不为绕过错误而开启 Create/迁移。
- 新 store 与导入/删除竞争或取消后仍占用连接：沿用锁顺序、限定连接生命周期并验证失败清理，不使用不可控后台任务。
- 切换只完成数据库但 Viewer 重绑失败：如实区分完成阶段，禁用无有效身份的收藏并可恢复；不默默跳回旧 Wiki。
- 顶栏隐藏 header 会丢失首页内链操作、留下空行或挤压搜索：明确 Home/Reading 接线、实测行高与最小窗口预算。
- 现有错误断言可能让错误行为被“保护”：仅替换本次需求改变的断言，保留所有无关 P0 回归。
- 回退按本 change 独立提交逆序 revert，在工作树有用户修改时先隔离处理。JS 与 C# commit 协议作为同一兼容单元回退。无 schema/data migration，既有及新增收藏仍在原 `WikiFavorite` 中，旧版本可读取；不得用源码回退覆盖用户后来数据。

## 进度记录

- 2026-09-12：读取用户桌面预计划并核验真实仓库；确认三大修复范围与非 Phase 定位。
- 2026-09-12：完成关键调用链调查；确定最小 DOM commit 通知、Core 窄收藏 store、宿主可等待切换方向，并补齐首页内链、>200 收藏与刷新竞态。
- 2026-09-12：创建本 draft。仅规划，未创建分支/checkpoint、未提交、未修改产品或运行产品测试。
- 2026-09-12：用户明确要求完成 plan；状态转为 in-progress，从 `master@b9ca593` 创建 `fix/wiki-favorites-viewer-header`。既有未提交改动保留在工作树且明确排除，未建立会夹带它们的整体 checkpoint。
- 2026-09-12：完成 DOM commit 身份链、跨 Wiki 收藏聚合/显式操作、宿主协调切换与 Wiki 单顶栏；同步持续 spec、ADR 和资产 hash。
- 2026-09-12：Windows Release build 0 warning/0 error，全量 .NET 362/362；最终 NativeWebView smoke `success=true`、12/12 Web 消息有效，最后完成方法为 `ViewerPageCommitted`。
- 2026-09-12：在隔离合成数据根完成浅/深色 1280×820 与 900×600 窗口截图检查；Reading 最小窗口的六个顶栏控件均在同一行，Reader header 自动化元素不存在。当前系统为 125% DPI，100%/150% 和交互主观项留给用户验收。

## 发现、偏差与决策

- 事实修正：预计划 `Content.db` 为概念性称呼，正式路径使用 `WikiDataPaths.ContentDatabasePath`，当前为 `data.db`，旧文件名由兼容层识别。
- 方案选择：不能在 `PageRedirectAsync` 返回时发布实际页身份；新增最小 JS commit 消息有充分源码依据。
- 方案选择：不复用会初始化全库的 ContentDbService 做扫描；新增窄存取是为避免写副作用，不能扩张成第二数据库框架。
- 方案选择：保留 per-Wiki 表与旧 API，新增显式 Wiki 身份能力；无需 Manager 迁移、新依赖或新 ADR。
- 方案选择：首页标题栏隐藏后仍保留首页收藏入口，并由实际内链提交推动正文状态，防止视觉改动丢行为。
- 偏差：计划要求分阶段独立提交，但实现与测试在共享文件中交叉收敛；为避免人为拆分出不可构建提交，最终采用一个当前 change 的原子选择性提交。
- 决策：不对用户正式数据库执行写测试；全部数据兼容/WAL/跨库写入验证使用临时 fixture，真实应用 smoke 使用仓库忽略的合成数据根。
- 审查修正：收尾发现旧 Ready 与失败 Back 恢复存在跨 session 的窄竞态，已把 RequestCompleted generation 与历史恢复 token 纳入校验，并增加回归。

## 验证结果

- Windows `dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：9 项目，0 warning，0 error。
- Windows `dotnet test Terraria_Wiki.Core.slnx -c Release --no-build --nologo`：Core 100、Application 59、WikiTools 45、Windows 158，共 362/362 通过。
- 最终真实 Avalonia + NativeWebView smoke：`success=true`，Search=1，script/style/image/audio 均命中，12/12 Web 消息有效，`viewer_last_completed_method=ViewerPageCommitted`，退出清理完成。WebView2 关闭时保留既有 Chromium class unregister 1412 诊断，进程与 smoke 结果均成功。
- 窗口证据：`artifacts/fix-wiki-header-{light,dark}-{1280x820,900x600}.png`。实际检查确认单顶栏、网页 Logo、底部状态、零 Reader header；900×600 Reading 的主页/返回搜索/页面返回/收藏/搜索控件无水平溢出。
- 静态检查：唯一 `NativeWebView` XAML 与唯一 Session 构造位置保持；Presentation 未新增 SQLite 访问、project/依赖或新 raw hex；JS 仅修改公共导航身份/历史接线。最终 JS SHA-256 为 `c5cd8fac92a395df8685f1392d9ec3864be6b4178d46782bf2f35c20e0bb02e8`，94/96 文件聚合 hash 分别为 `ca4f78dbc48ffa2d2b61c7324ca5b5534bc777598c9782b3d583ed7e78c2f27c`、`abecefdca1496042d2c88d6d078dfe7fd1cf2761ffea73c1b3e8146fece28c98`。
- 未执行：用户正式数据写入、付费 AI、联网 Wiki、publish/安装器、100%/150% DPI 与用户最终主观交互验收。

## 完成摘要

本 change 的产品实现、持续契约、自动化回归、真实 NativeWebView smoke 与当前 Windows 窗口检查均已完成。分支保持未合并、未 push、未发布；下一步只有用户在正式使用环境确认收藏、跨 Wiki 打开、100%/150% DPI 与交互观感。确认后才把状态改为 completed、更新路线图并归档/合并。
