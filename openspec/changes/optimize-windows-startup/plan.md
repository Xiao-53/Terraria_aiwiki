# Windows 本地 Wiki 启动可靠性与首屏性能优化

- 状态：draft；已合并 2026-09-10 Release 启动故障诊断，本轮只修订计划，未授权实施源码
- Phase：无；独立启动可靠性与性能变更，不推进 Phase 11 发布或其他 Phase
- 执行环境：WSL2 Codex CLI 负责阅读、编辑与静态检查；Windows 11 x64 负责 .NET 构建、测试和首屏计时
- 创建日期：2026-09-09
- 最后更新：2026-09-10
- 范围：数据库启动准备、旧/新数据目录冲突、稳定错误与恢复提示、迁移触发条件、统计刷新时机、Windows 启动分段计时
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/wiki-data-compatibility/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/windows-productization/spec.md`、`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`docs/adr/0007-separate-product-source-from-reference.md`、`docs/adr/0009-use-managed-default-windows-data-root.md`、`openspec/changes/archive/optimize-anchor-migration-startup/plan.md`

## 背景与当前证据

用户报告每次启动本地 Viewer 都要等待几秒，提出“数据库兼容检查下载下来检查一次就行了，不用每次启动都检查”，要求创建启动优化计划。目标是取消或延后重复工作，保持现有数据能力和界面功能，不以删除安全保护换取表面速度。

2026-09-10 用户进一步报告 Release 中本地 Viewer 无法启动。只读诊断证明这是同一启动链上的数据兼容故障，不是 Release 漏包或 WebView2 缺失，因此合并到本 change 一同处理，不另建重复计划：

- 当前 `Release/net10.0-windows10.0.17763.0/win-x64` 输出含完整 93 个 `ViewerAssets` 文件；使用隔离合成数据运行同一 Release 可执行文件，Viewer server、DOM Ready、CSS、JS、图片、音频和退出清理均通过。测得 Runtime Ready 1923 ms、Viewer server Ready 1936 ms、DOM Ready 2136 ms，WebView2 152.0.4191.66 可用。
- 实际产品诊断在 2026-09-10 记录多次 `StartupFailed`，稳定 code 为通用 `initialization-failed`，没有 `viewer-not-ready` 或 `webview2-missing`。此前 UI WIP 的 `ShellView` 空引用崩溃属于已保存到独立分支的另一问题，不属于当前 `master` 或本计划。
- 当时保存的数据根同时存在 `<root>/Terraria_Wiki_zh/data.db` 与 `<root>/Terraria_Wiki/Terraria_Wiki.db`。两者都是可只读打开但内容不同的数据库：前者约 3.78 GB，含 4390 页面、29774 Redirect、26237 Asset；后者约 3.58 GB，含 4320 页面、28497 Redirect、25914 Asset，另留有 WAL/SHM。`Manager.db` quick check 为 `ok`，活动 Wiki 的默认目标目录是 `Terraria_Wiki_zh`。
- `PrepareTargetAsync` 发现旧目录和目标目录都含数据库后调用 `MigrateLegacyDataDirectory`；后者按既有安全规则抛出 `data-directory-conflict`，防止覆盖任一数据集。该 `WikiCompatibilityException` 随后被初始化边界包装成通用 `initialization-failed`，恢复界面因而无法说明真实冲突，用户观察为“Viewer 无法启动”。
- 用户之后切换到宿主管理的新默认数据根，Runtime 可以启动；该根当前只有默认占位页且 `IsPageDownloaded=false`，所以 `MainWindow` 按契约进入数据管理引导并在导航 Viewer 前返回。这是空库流程，不是 Viewer transport 故障。
- 两套旧数据的创建来源仅凭现有时间戳不能可靠确定。它们内容不同，任何自动按新旧、大小、数量或路径选择并删除另一套的做法都可能造成数据丢失，必须保持失败关闭。

已核对的事实：

1. `MainWindow.StartAsync` 先等待 `WindowsClientRuntime.CreateAsync`，再启动 Viewer 服务、绑定 Runtime 并执行 `session.Navigate`。首页真正开始导航前已经发生框架/窗口构造和数据库准备。
2. `WindowsClientRuntime.CreateAsync` 依次执行 WebView2 probe、单实例/数据根锁、设置与数据根解析、`ActiveWiki.InitializeAsync`、设置读取。当前计时只覆盖较粗的累计阶段，不能直接把全部耗时归因于 SQLite 或 Viewer。
3. `ActiveWikiUseCases.InitializeAsync` 先初始化 Manager、检查全部已注册 Wiki 的未完成导入日志、修复设置，再准备活动 ContentDb、执行 `DatabaseService.Init`，最后刷新三张表的数量并写回 Manager。
4. `PrepareTargetAsync` 对现有数据库进入旧目录/数据库家族处理、迁移日志恢复、迁移前 checkpoint 和锚点迁移预检。checkpoint 仅在 WAL/SHM 文件存在时执行，不能写成每次启动都必定执行。
5. `MigrateAnchorDataSafelyCoreAsync` 已在复制前读取默认首页检查旧链接标记。无需迁移时不复制整库、不执行迁移产物完整校验；这是已归档优化的成果，本次不得回退。它仍会单独打开只读连接、读取首页内容进行判断。
6. `DatabaseService.Init` 每进程/每连接初始化时设置 WAL、确保表/FTS/种子；`_initialized` 仅是内存状态。这些不是整库完整性检查；已有表不等于每次重建整张表或重建搜索索引。
7. `RefreshBookStatisticsCoreAsync` 依次统计 WikiPage、WikiRedirect、WikiAsset，读取文件大小并保存 WikiBook。下载、更新等生命周期已有统计刷新调用，Application 另有 `Data.GetStatisticsAsync`；正常首屏不应重复依赖三项总数计算。
8. `IsPageDownloaded` 是独立持久字段，不由上述三次计数推导。启动空库引导使用该字段；移除统计前仍需验证旧包、失败下载、缺失文件等情形，不得改变引导行为。
9. 当前 Viewer 在首页 DOM 写入后报告 Ready，15 秒是超时上限，非固定等待。AI 启动任务在后台进行，不是首页显式等待条件。
10. `MainWindow` 已区分 WebView2、数据根与 Application 初始化恢复入口，但兼容异常的原始稳定 code 未完整穿过 Application；Viewer 状态回调也只显示通用失败文案，现有诊断无法直接区分数据准备、loopback bind、静态资产和 DOM Ready 阶段。

已有 Windows 历史记录（仅用于确定调查方向，不作为本次基线）：

| 制品/记录 | Runtime 就绪，进程启动起算 | Viewer 服务就绪 | 首页 DOM 就绪 |
|---|---:|---:|---:|
| `artifacts/phase11-final-validation-v3/smoke-installed.json`，local.22 | 2083 ms | 2103 ms | 2311 ms |
| 同目录 `smoke-portable.json`，local.22 | 3510 ms | 3534 ms | 3761 ms |

记录使用合成数据，版本早于当前源码；不能用于估计真实大库提速或证明当前瓶颈。推断：前置准备值得优先计时；尚未证明迁移预检或统计分别耗时多少。

## 目标与完成定义

- 健康当前格式库、仅有旧目录的可迁移库、空库和明确冲突库得到不同且稳定的启动结果；数据兼容失败不得再误报为 Viewer transport 失败。
- 当旧目录与目标目录都含有效但不同的数据时，应用快速返回稳定 `data-directory-conflict`，保留两套数据库、WAL/SHM 和 Manager，不自动选择、删除、覆盖、合并或复制数 GB 数据；现有恢复页提供不泄漏完整路径的准确说明和数据根重选入口。
- Core 的 `WikiCompatibilityException` code 在 Application/Windows 边界保持为安全、可分类的错误；产品诊断记录阶段与 allowlist code，不记录完整路径、底层异常、页面内容或数据库细节。
- 正常再次启动不在首屏前重复统计三张表，也不为“可能迁移”无条件执行迁移专属准备。
- 完整校验保留在导入、真正迁移和显式维护等对应边界，不增加启动整库校验。
- 正常数据库使用最小必要打开/兼容判定；仅在首次接入旧库、真实版本升级或数据替换需要时进入完整兼容处理。
- 未完成导入/迁移恢复仍在消费者读取数据库前完成；旧数据实际需要迁移时不得先显示未经迁移的正文。
- 不影响活动 Wiki 选择、空库引导、Search/Read/资源、数据管理统计刷新和下载/导入/更新结果。
- Release build、直接运行、未来 publish/安装形态继续使用同一 Viewer 启动逻辑和资产；本变更不得以专属 Debug 分支掩盖 Release 问题。
- 记录同机、同版构建配置、同份隔离数据的修改前后首屏指标；必须证明重复工作已消除且无性能回退，不能仅凭测试通过宣称“秒开”。若变化未超过测量噪声，继续定位或明确标为未达到提速目标。

## 前置条件

- Phase 00–10 已完成，Phase 11 仍有公开发布门槛；本变更不依赖发布或重新打包。
- 计划合并时当前 `master` HEAD 为 `85e6d21`，工作区干净；Wiki 搜索页未完成实现已保存在独立分支 `preserve-wiki-search-page-implementation` 的 `1e5ad81`，不得混入本变更。
- 用户仅要求创建计划。后续获实施授权后，先检查 Git 状态并建立只含已确认范围的恢复点，再改核心路径。
- 性能复测使用合成数据库；代表性大库需有可安全使用的隔离副本。不得为了测试对生产原库初始化、迁移、checkpoint 或下载。
- 冲突回归必须使用临时小型数据库或按完整数据库家族制作的隔离副本；现有两套真实数据库只允许只读盘点，未经用户单独确认不得重命名、移动、复制、checkpoint、初始化或删除。
- 不调用真实 AI provider，不产生付费请求；不安装新依赖。

## 范围与非目标

范围内：分段计时、启动阶段错误分类、旧/目标目录冲突的失败关闭与现有恢复入口、首屏前统计刷新消除、现有兼容入口的轻量快速路径、必要的状态与回归契约。

非目标：

- 不修改或实施 Wiki 搜索页计划，不变更 Shell 布局、其他页面、搜索算法、Viewer HTML/CSS/JS/资源或数据包格式。
- 不新增数据冲突向导、数据库合并器或第二套数据管理页面；本轮只复用现有数据根恢复页和选择入口，提供准确状态。若未来要求在应用内选择两套不同数据库并执行重命名/迁移，须先补充独立可恢复数据操作设计并由用户确认。
- 不改退出策略、AI 启动策略、WebView2 引擎、HTTP 缓存/动态端口或 NativeWebView 生命周期。
- 不删除兼容支持、导入 hash/格式校验、故障恢复、数据根锁、消费者 pause lease 或取消传播。
- 不为本任务新增 Registry、启动框架、后台调度系统或公共诊断接口；不重构 Core/Application 分层。
- 不默认新增表、字段、schema version、文件指纹服务或长期缓存。若持久迁移记录确有必要，先在本计划补充最小设计、兼容及失效证据，再实施。
- 不安装、发布、上传或重建安装包，不扩大平台范围。

## P0 / 兼容性影响

- 搜索、FTS5、Redirect、WikiAsset、收藏和数据包字节语义保持原样。
- 同一根中的旧/目标数据库都视为用户数据；冲突时保持原字节与文件家族，不以默认 `DataFolder`、修改时间、大小或记录数量推断哪套可以丢弃。
- 保留所有注册 Wiki 的未完成导入日志检查；无日志快速返回，不能因只打开一个 Wiki 而跳过其他 Wiki 的恢复契约。
- 保留未完成锚点迁移恢复；恢复日志优先级高于任何“已检查”状态。
- WAL/SHM 不得简单删除或忽略。只有确认需要文件交换的迁移才执行相应 checkpoint/一致性处理；正常打开交由原 SQLite 初始化路径处理，具体顺序须经 WAL 故障测试证明。
- 持久“已检查”只代表某项迁移规则已完成，不能代表数据库永远完整或任意替换后仍可信。
- 正常冲突检测只检查布局与必要元数据，不扫描或比较整库内容；真正的数据选择、迁移或替换仍需用户明确决定和可恢复操作。

## 方案与待决问题

### 1. 建立启动阶段与故障基线

- 复用现有 WindowsLifecycleTimings/smoke 输出，增加有限阶段：最早托管入口、窗口构造/Opened、WebView2 probe、数据根与锁、Manager 初始化/恢复、设置、兼容准备、Content 初始化、统计、Viewer 服务、导航发起、DOM Ready。
- 计时实现保持内部、可选且低开销；默认不写每条 SQL，不记录数据库路径、内容、查询词或密钥。跨层计时优先内部回调/现有测试可见性，不增加公共 ports。
- 区分累计时间与阶段耗时，注意重叠后台任务；不得把“进程到 Runtime 就绪”写成纯数据库耗时，也不得把 DOM Ready 写成所有图片完成。
- 以现有 Release 成功 smoke、空库、仅旧目录、仅目标目录、旧/目标并存和 WAL busy 建立可重复 fixture；记录失败发生在数据准备、Viewer server、WebView 导航还是 DOM Ready，避免继续用一个“Viewer 启动失败”覆盖全部阶段。

### 2. 保留兼容错误并让目录冲突快速失败

- `LegacyWikiCompatibility` 继续负责识别 `data-directory-conflict`、数据库家族冲突、orphan sidecar、checkpoint 和恢复日志问题；不在 Windows 层复制判断算法。
- `ActiveWikiUseCases.InitializeAsync` 对安全的 `WikiCompatibilityException` 映射为同 code 的 `ApplicationUseCaseException`，而不是统一包装为 `initialization-failed`；未知异常仍使用通用安全 code，不向 Presentation 暴露底层异常或完整路径。
- 目录冲突应在 checkpoint、锚点迁移、Content 初始化和统计之前确定并返回；不得因为性能优化把冲突当“无需迁移”继续打开，也不得只因目标目录可读就静默忽略旧目录。
- Windows 恢复状态按阶段处理：数据布局/兼容错误进入现有数据根恢复页，说明检测到两套 Wiki 数据且均未修改，并允许重选已准备好的有效根；只有静态资产、loopback bind 或 WebView/DOM Ready 失败才称为 Viewer 故障。
- `ProductDiagnostics` 只记录 allowlist 阶段与稳定 code，例如 `data-directory-conflict`，不记录两个目录名、文件大小、记录数量或内部异常。诊断写入失败继续不阻止启动。

### 3. 移除健康启动的统计等待

- 正常 Initialize 使用 Manager 已保存的数量快照，不调用 `RefreshBookStatisticsAsync` 重算三张表。
- 保留数据操作完成后的既有统计刷新与显式 `Data.GetStatisticsAsync`。优先复用这些入口，不默认加每次启动的后台扫描。
- 统计只作为上次保存的快照，不用过期计数决定能否 Read/Search。需要即时统计的原调用方继续得到刷新结果；若当前页面要求新鲜值，沿用该页已有刷新路径，不新增 UI。
- 覆盖应用外替换数据库、旧包计数不一致和异常中断：正常读取不被旧计数阻断，显式刷新可纠正快照。若某个调用方依赖启动精确计数，先在该调用边界收敛最小方案并更新本计划。

### 4. 收紧迁移准备时机

- 复用原迁移判定和原 staging/backup/journal 算法，只调整进入时机，不写第二套迁移。
- 检查顺序候选：未完成操作恢复 → 最小旧目录/格式判定 → 必要时迁移预检 → 确认需要迁移才执行 checkpoint、staging 和完整校验 → 正常 Content 初始化。
- 当前迁移方法在预检前拒绝 sidecar；因此不能只移动 Application 中的一行 checkpoint。须在原 Core 内形成能读取一致 WAL 数据的轻量预检，真正迁移前再执行原排他/一致性保护并重新确认条件，防止判定与交换之间失效。
- 旧目录恢复、预检不确定、未知格式或失败仍走原安全路径/稳定错误，不把异常当“已兼容”。

### 5. “检查一次”的实现选择

- 用户期望是已处理数据再次启动走快速路径；不采用全局永久布尔值，也不在每次启动对整库 hash 来证明它没变。
- 首选先测轻量预检的实际成本，尝试复用必要打开过程中已取得的信息，避免额外连接/重复首页读取。如果成本可忽略，不为省一次轻量读取引入持久状态系统。
- 如果仍需跨启动跳过预检，实施前必须形成小型设计：迁移规则版本、与目标数据库的绑定方式、原子完成记录，以及首次旧库接入、下载/更新/导入、手动文件替换、热切换、删除重建、失败恢复的失效矩阵。
- 不只用路径或文件长度/时间戳当可靠身份；数据库正常写入也不能导致每次重新全量检查。不要未经核对占用 `PRAGMA user_version` 等可能属于旧数据库的字段。
- 记录缺失/不可信时回退原轻量判定；失败迁移不得写成功标记，恢复日志不得被标记遮蔽。若无法以小改动证明安全，保留轻量判定并记录理由，而非宣称已经彻底“只检查一次”。

### 6. 保留必要初始化与空库语义

- 本轮不直接删除 WAL 配置、缺表/FTS 初始化和默认数据种子。它们属于正常打开/首次建库的职责，先单独计时，不能把 `CREATE IF NOT EXISTS` 当成索引重建。
- 若这些步骤成为主要瓶颈，先补充可审查的快路径设计和旧库/缺表回归再扩大对应步骤；不以取消必要初始化换取速度。
- `IsPageDownloaded=false` 时继续在启动 Viewer server 后进入数据管理引导，不导航占位首页；状态必须说明 Wiki 内容尚未下载，而不是声称 Viewer 无法启动。下载或导入完成后仍复用同一 Viewer/Reader 重绑定路径。

待决项：当前源码各阶段真实耗时；迁移判定是否值得持久化；统计快照消费者是否全部允许延后刷新。两套真实旧数据库最终保留哪一套属于用户数据处置决定，不由本计划自动推断；本计划只保证软件准确失败、原样保留并提供安全恢复入口。这些均不阻塞计划修订，也不授权源码实施或真实数据改写。

## 文件变更

本轮仅 MODIFY 本 plan；下列为后续实施候选，不代表全部必须修改：

| 文件 | 候选动作与职责 |
|---|---|
| `src/Terraria_Wiki.Windows/Program.cs` | 必要时增加最早托管入口计时；Velopack hook 顺序保持不变 |
| `src/Terraria_Wiki.Windows/MainWindow.cs` | 扩展已有启动计时和 smoke 输出，按阶段呈现数据恢复或 Viewer 故障；不改页面布局或关闭流程 |
| `src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs` | 记录 Runtime 准备的子阶段 |
| `src/Terraria_Wiki.Windows/Hosting/ProductDiagnostics.cs` | 在既有 allowlist 内增加必要启动阶段/稳定错误；保持路径、内容和底层异常脱敏 |
| `src/Terraria_Wiki.Application/UseCases/ActiveWikiUseCases.cs` | 保留兼容错误 code、消除首屏统计重算、收紧迁移准备调用；保留选择/恢复/回滚 |
| `src/Terraria_Wiki.Core/Compatibility/LegacyWikiCompatibility.cs` | 复用原布局冲突与迁移判定，调整迁移专属准备顺序与快速路径，不自动处置冲突数据 |
| `src/Terraria_Wiki.Core/Services/DatabaseService.cs` | 默认 KEEP；仅必要内部计时/连接复用候选，禁止顺带删除 schema/FTS 初始化 |
| `src/Terraria_Wiki.Application/UseCases/WikiDataUseCases.cs`、`src/Terraria_Wiki.Core/Data/WikiDataLifecycle.cs` | 默认 KEEP；复用已有显式统计与数据变更后刷新 |
| `src/Terraria_Wiki.Core/Packages/WikiPackageTransferService.cs` | 默认 KEEP；若持久记录方案获证实，才在现有导入事务边界做最小失效接线 |
| `tests/Terraria_Wiki.Application.Tests/ActiveWikiTests.cs`、`WikiDataUseCaseTests.cs` | 兼容 code 传播、启动、统计快照、刷新、切换和恢复回归 |
| `tests/Terraria_Wiki.Core.Tests/LegacyWikiCompatibilityTests.cs`、`DatabaseServiceTests.cs`、`WikiPackageTransferTests.cs` | 两目录冲突不修改、WAL、一致性、无需迁移、必要迁移及导入恢复回归 |
| `tests/Terraria_Wiki.Windows.Tests/WindowsClientSmokeFixtureTests.cs`、`ProductizationBoundaryTests.cs` | 必要计时、故障分类、诊断脱敏和现有 smoke 证据接线 |
| `openspec/specs/application-use-cases/spec.md` | 实施后明确兼容错误与启动统计快照/刷新语义；保留迁移前置与全部 import 恢复契约 |
| `openspec/specs/wiki-data-compatibility/spec.md` | 实施后明确并存数据失败关闭；仅在确认持久迁移记录等持续契约变化时增加相应内容 |
| `openspec/specs/windows-presentation/spec.md`、`openspec/specs/windows-productization/spec.md` | 实施后同步数据错误/Viewer 错误分类和诊断 allowlist；不增加页面或发布渠道 |

KEEP：其他页面、Wiki 搜索 ViewModel、Viewer 与资源、AI/Agent、配置密钥、依赖、打包。Master Architecture/P0/Phase 不变；README 仅在有实际结果时按需增加状态入口。

## 执行步骤

- [ ] 获得实施授权后更新状态；检查并隔离既有改动，建立 Git 恢复点。
- [ ] 建立仅旧目录、仅目标目录、两者并存且内容不同、空库和 WAL busy 的小型 fixture；证明当前冲突稳定复现且测试不接触真实生产库。
- [ ] 添加最小分段计时，在 Windows 同一开发 Release 输出/隔离数据上记录修改前性能与故障阶段基线。
- [ ] 保留兼容稳定 code，修正 Windows 数据恢复/Viewer 故障分类与诊断脱敏；证明冲突时所有数据库家族字节不变。
- [ ] 审查计数消费者，消除正常首屏前统计重算；验证显式刷新及数据变更后更新，保存阶段证据。
- [ ] 在原兼容实现中收紧迁移专属准备顺序；补充 WAL/无需迁移/故障恢复回归。
- [ ] 根据预检实际耗时决定是否需要持久“检查一次”记录；先完成绑定/失效/回滚设计，再实施必要最小改动，或记录保留轻量判定的证据。
- [ ] Windows 复测健康大库、空库和冲突 fixture；确认健康首页收益来自取消重复工作，空库不误报，冲突不启动 Viewer 或修改数据。
- [ ] 执行相关与全量 .NET 回归、Windows Release build；更新持续契约及本计划证据。
- [ ] 检查 diff、数据副本/临时文件、进程及 Git 状态，记录实际收益和限制，只提交本变更，不 push/打包。

## 验证与回归计划

### 静态与自动化

- `git diff --check`，检查变更范围、无新依赖/平台、Application 不泄漏 SQLite API。
- 两目录并存 fixture 在初始化前后核对 Manager、两个数据库及其 WAL/SHM 的文件清单、长度与 SHA-256，断言返回 `data-directory-conflict` 且没有自动删除、改名、checkpoint、合并或选库。
- 验证 `data-directory-conflict`、`database-family-conflict`、`database-checkpoint-failed`、未知初始化异常、Viewer 静态资产缺失、loopback bind 失败、WebView2 缺失和 DOM Ready 超时各自得到正确阶段/code/恢复动作；诊断不含路径和底层异常。
- 使用行为计数/现有测试夹具证明：正常重复启动不统计三张表、无需迁移不创建 staging/整库副本或执行完整校验；不以脆弱源码字符串匹配代替行为证明。
- 首次空库、当前格式库、旧锚点库、旧目录/家族、缺失首页、预检取消/失败、WAL 中最新数据、checkpoint busy、未完成 import/anchor journal、恢复失败均有安全结果。
- 如新增持久记录，覆盖同路径替换旧库、旧包覆盖新库、删除重建、失败/中断、记录损坏和规则版本变化，证明不会误跳过迁移。
- 验证三项计数不控制首屏可用性，显式统计纠正旧值，下载/更新/导入/删除后计数及 IsPageDownloaded 保持正确。
- 沿用现有热切换、rollback、pause lease、锁、Search/Redirect/资源、数据包完整性回归。

### Windows 11 x64 性能与功能

- 实际 Windows .NET 10 Release build，要求 0 warning / 0 error；先定向 Core/Application/Windows 相关 tests，再全量 solution tests。
- 同机同输出形态、相同隔离数据/配置、相同窗口与主题，修改前后分别至少 5 次正常重启；记录首次与后续运行，不将第一次简单称为已清系统缓存的冷启动。
- 分别记录窗口出现、Runtime 就绪、导航开始、DOM Ready 和人工观察到的可用首页。报告中位数及最小/最大值，不把 5 次样本称为可靠 p95，不混比 installed 与 portable。
- 基础测量禁用真实 provider 请求；若测 AI 已配置时的竞争，使用现有非付费 fake 模式并单列结果。
- 用真实大库只读来源制作的隔离副本时，在计时循环外完成必要副本准备，保持前后初始条件一致；核对原件未变化。不对使用中的 WAL 数据直接复制单个 db 文件。
- 首页实际可阅读，内部导航/搜索/资源可用；空库仍进入既有数据管理引导。UI 尚未就绪不得因调整计时提前标为成功。
- 当前 Release 输出和后续 Release build 都核对唯一 `ViewerAssets`、WebView2 probe 与 loopback smoke；直接 build 输出通过不外推为未来 Setup/portable 已重建或已验证。
- 缺少目标环境或安全大库副本时明确留下未验证项，不承诺固定“1 秒内”。

## 风险与回退

- 迁移前预检读到不一致 WAL 内容：在原 Core 边界使用一致连接语义，真实迁移仍在排他与恢复保护下重新确认；无法证明时保留原顺序。
- 两套有效库被错误自动选中：明确禁止按时间、大小、数量或默认目录猜测；冲突保持失败关闭，真实处置必须先备份并由用户选择。
- 稳定兼容 code 暴露底层信息：只传递既有 allowlist code 和安全文案；原始异常只保留在内存异常链，不进入产品日志或普通 UI。
- 统计快照陈旧：保留真实刷新入口与数据变更后的更新，不以陈旧数字阻塞读取。不得把删除统计计算变成丢失统计功能。
- 标记失效错误：默认不引入持久标记；若采用，失效矩阵和故障测试是实施门槛，未知状态走兼容回退。
- 测量噪声或优化错对象：先分段计时；如果主要耗时在框架加载而非数据准备，报告证据并调整本计划，不静默转成 UI/框架重写。
- 回退独立代码提交即可恢复原启动顺序；基础方案不改变 schema/数据包。若后续引入持久记录，必须先记录旧版本可忽略性及清除/失效方案，不能依赖破坏原库回退。

## 进度记录

- 2026-09-09：完成启动调用链、兼容预检、统计/数据操作入口、相关 spec/ADR 与现有测试核对。
- 2026-09-09：创建本 draft，区分已确认重复工作与尚未测量的瓶颈；仅规划，未开始源码实施。
- 2026-09-10：诊断当前 Release 的“Viewer 无法启动”：资产和 WebView2 正常，隔离 smoke 通过；真实失败来自旧/目标数据目录并存，经 Application 包装后只留下 `initialization-failed`。
- 2026-09-10：按用户要求把启动故障可靠性、错误分类和恢复提示并入本 plan，后续与统计/迁移性能优化一同实施；仍未授权修改业务源码或真实数据库。

## 发现、偏差与决策

- 纠正：当前不是每次启动完整校验整库；已归档变更已消除无需迁移时的整库复制。
- 纠正：2026-09-10 的 Release 故障不是 Viewer 资产漏包、WebView2 缺失或 loopback transport 固有失败，而是 Content 数据布局冲突；当前 UI/诊断分类不够精确。
- 发现：真实旧根中的两套数据库均可只读打开且记录数量不同，不能视为等价副本；现有失败关闭是正确数据安全边界，需修正的是错误传播、恢复说明和健康路径开销。
- 决策：保留轻量恢复检查；取消重复工作与删除数据保护不是同一目标。
- 决策：先建立阶段计时并修正数据/Viewer 故障分类，再处理首屏统计和迁移准备；不把 HTTP 缓存、AI 延迟启动或退出优化混入本 change。
- 决策：本 change 不自动解决两套不同真实数据库的取舍；只保证冲突快速、准确、无损并可通过现有数据根入口恢复。
- 决策：不根据一句“检查一次”预建数据库版本体系；可靠失效设计和测量收益优先于新增持久状态。
- 当前 Application spec 要求迁移在 Content 使用前完成，本方案保持该顺序；只允许确认无需迁移的库走快路径，不绕开既有契约。

## 验证结果

- 规划阶段已读取相关源码、spec/ADR、历史 smoke 记录和测试；历史 local.22 数字不属于本轮新测量。
- 2026-09-10 使用当前已有 Release 输出和隔离合成数据执行一次 Windows smoke：成功；Runtime Ready 1923 ms、Viewer server Ready 1936 ms、DOM Ready 2136 ms，CSS/JS/图片/音频请求与退出清理通过。该结果只证明当前 Viewer/打包输入可工作，不代表真实大库性能已优化。
- 真实旧根仅通过 SQLite immutable/read-only 盘点 Manager 与两套 Content 数据库；未运行初始化、迁移、checkpoint、下载、重命名或删除。Manager quick check 为 `ok`，两套 Content 记录数量不同。
- 本轮仅修改本计划；`git diff --check -- openspec/changes/optimize-windows-startup/plan.md` 已通过，候选源码与测试文件路径均已核对存在。
- 未执行源码 build/tests、系统化修改前后性能测量、真实数据操作或任何业务源码修改；没有已实现提速结论。

## 完成摘要

启动可靠性诊断已与原首屏性能计划合并，实施状态仍为 draft。后续交付必须同时证明健康路径重复工作被消除、冲突路径准确且不改数据、空库与 Viewer 故障不再混淆，并记录前后计时、兼容/恢复回归和剩余瓶颈；不能以计划完成代替优化完成。
