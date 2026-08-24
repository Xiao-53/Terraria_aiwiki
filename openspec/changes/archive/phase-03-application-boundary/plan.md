# Phase 03 — Application Boundary

- 状态：completed
- Phase：03 — Application Boundary
- 执行环境：WSL Codex CLI 负责阅读、编辑、静态检查和计划维护；Windows `dotnet.exe` 负责 portable solution restore/build/test；不安装 MAUI/Android workload
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：单一 `net10.0` Application、五组具体用例、两个宿主 ports、独立测试与持续契约
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/specs/application-use-cases/spec.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/wiki-data-compatibility/spec.md`；`openspec/specs/local-wiki-viewer/spec.md`；`docs/adr/0002-portable-core-and-data-root.md`

## 背景与当前证据

Phase 01 已建立唯一 `net10.0` Core，Phase 02 已在同一 Core 固定七级 Search。实施前 Windows Core Release 测试为 89/89；Phase 02 九个源码/测试文件已独立提交为内层 `4567cf8`，未 push。外层入口、架构、项目基线、ADR、能力规范与 Phase 00–02 记录已独立提交为 `dd0127a`，排除了 `.codex`、临时 Search 探针、Phase 00 真实输入、experiment 和无关未跟踪治理归档。

真实代码事实：

- `ManagerDbService` 与 `ContentDbService` 是 Core 中的持久化实现；`WikiReader` 提供稳定 Search/Read，当前 `WikiReadResult` 仍含持久化 `WikiPage`，因此 Application 必须映射 DTO。
- `WikiDataLifecycle` 已复用下载、资源下载、更新、失败重试/清理、VACUUM、资源/整库删除和统计；`WikiPackageTransferService` 已复用 Inspect/token/legacy confirmation/import/export/rollback。
- lifecycle、package 和旧宿主直接写操作共享 `WikiDataOperationGate`；Application 热切换和 library 写操作必须使用同一实例，不能建立第二把互不知情的门闩。
- 旧宿主初始化先打开 Manager、恢复未完成 import，再选活动 Wiki；`LegacyUpgradeHandler` 在 ContentDb 打开前处理旧目录与数据库家族，但 anchor 数据迁移仍是旧 Presentation 生命周期中的独立步骤。Application 初始化将三种迁移统一放到打开 ContentDb 前。
- 当前设置键和值域来自 `LocalizationService`、`ThemeService`、`AppState`、`Settings.razor` 和 `DataService`；Application 不读取 MAUI `Preferences`，由 port 映射。
- 页面、历史、收藏的现有 SQL 已提供稳定排序和分页；Application 负责验证 `PageRequest`、映射 DTO，并保留 Core 的过滤/总数语义，不把日期分组或本地化带入用例层。

## 目标与完成定义

1. 增加唯一 Application project 和独立 test project，并扩展 `Terraria_Wiki.Core.slnx`；Application 仅引用 Core、无新增运行时包。
2. `ApplicationSettingsUseCases`、`ActiveWikiUseCases`、`WikiQueryUseCases`、`WikiLibraryUseCases`、`WikiDataUseCases` 覆盖计划内设置、活动 Wiki、查询阅读、library 和数据生命周期。
3. 公共 API 只暴露 Application DTO、两个 Application ports，以及可安全复用的 Core Search/progress/summary/package inspection 契约；不暴露 SQLite、`DatabaseService` 或五种持久化表模型。
4. 初始化、热切换、取消、回滚、消费者 pause lease、分页/过滤、历史/收藏写入、data/package 两阶段流程都有自动化证据。
5. Windows restore、Release build、全量 test 通过；89 个现有 Core 测试不回归；静态与反射边界门通过。
6. Phase 04 只需实现两个 Application ports 并组合 Application；Phase 05 可直接使用相同 Query/Read 用例。

## 前置条件

- [x] Phase 00–02 已完成并归档。
- [x] Windows Core Release 89/89 复验通过。
- [x] Phase 02 内层独立提交 `4567cf8`，未 push。
- [x] 外层相关治理基线独立提交 `dd0127a`，未 push。
- [x] 用户明确要求按已提供计划实施 Phase 03。
- 真实数据库和 golden package 只作为显式只读输入；所有写入、导入、导出和迁移只对临时/合成数据执行。

## 范围与非目标

范围：Application 工程/测试、五组具体用例、强类型设置与 DTO、两个宿主 ports、共享状态/门闩编排、solution、持续 spec 和 Phase 状态文档。

非目标：修改 Core 生产实现/schema/FTS5/Search 排序/包字节格式；迁移旧 MAUI/Razor UI 或 `DataService`；Avalonia、Viewer transport、AI、日志/更新、文件选择、设置平台 Adapter、Windows 发布、Android 或其他平台。

## P0 / 兼容性影响

- 所有查询和数据维护委托给现有 Core；Application 不复制 SQL、抓取、同步、package 或 Viewer backend。
- 热切换、活动库 import、整库删除和旧数据迁移同时受共享 Core gate 与消费者 pause lease 保护。
- Search 继续返回 Core `SearchResultItem`，七级排序和高亮语义不变；Read 映射为无持久化泄漏 DTO，AI Read 不写历史。
- import 保留 inspection token 与显式 legacy confirmation；export 可指定任一已注册 Wiki；破坏性命令只作用于活动 Wiki。
- 公共 API 兼容门禁止 `DatabaseService`、SQLite connection 和持久化表模型。

## 方案与待决问题

- 新增一个公开组合根，内部持有唯一 Manager/Content 数据库、Core gate、lifecycle、package service 和活动快照；五组具体用例共享该状态。
- Application 构造只接收绝对 data root、`IApplicationSettingsStore`、`IWikiDataConsumerCoordinator`、现有 Core `IWikiHttpTransport` 和可选 `TimeProvider`；不新增额外宿主 port。
- 设置加载对非法旧值按字段修复并回写；主动保存先整体验证再写。语言仅允许现有 `zh-cn`/`en-us`，应用主题允许 `auto/light/dark`，内容主题允许 `auto/original`。
- `PageRequest` 采用 offset `>=0`、limit `1..200`、可选 filter；Application 使用现有 Core 参数化 API 并映射 `PageSlice<T>`。
- 编排异常使用稳定 string code 的 `ApplicationUseCaseException`；Core 异常原样传播，取消永远传播为 `OperationCanceledException`。
- 无阻塞待决问题。若实现必须修改 Core 生产语义、引入依赖或增加平台假设，则先记录偏差并停止扩展。

## 文件变更

- **ADD** `src/Terraria_Wiki.Application/**`：公共 DTO、ports、异常、组合根、共享状态与五组用例。
- **ADD** `tests/Terraria_Wiki.Application.Tests/**`：设置、初始化/切换、Query/Read、Library、Data/package 和边界测试。
- **MODIFY** `Terraria_Wiki.Core.slnx`：加入 Application 与 Application tests，不新增 solution。
- **ADD** `openspec/specs/application-use-cases/spec.md` 与本计划；完成后计划移入 archive。
- **MODIFY（完成时）** `README.md`、`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`：只更新状态和入口。
- **KEEP** Core 生产源码、旧 MAUI/Razor、schema、依赖、Viewer、真实输入和 Phase 00 experiment。

## 执行步骤

- [x] 复读治理、相关 specs/ADR/前置 Phase、Core 配置/入口/实现/模型/测试和旧宿主调用方。
- [x] 复跑 89 个 Core 测试；分别建立内层 Phase 02 与外层治理基线提交。
- [x] 创建 `in-progress` 活计划和 application-use-cases 持续契约。
- [x] 建立 Application/test projects、公共契约、ports、组合根与设置用例；执行定向测试。
- [x] 实现初始化/热切换、Query/Read 与 Library；覆盖回滚、分页和确定性时间。
- [x] 实现 lifecycle/package 数据用例；覆盖 fake HTTP、临时 SQLite、消费者协调与 import/export。
- [x] 增加静态/反射边界门；执行 Windows restore、Release build 和全量 test。
- [x] 对显式真实输入执行只读 Application Search/Read 与 golden Inspect；复核源 hash。
- [x] 更新入口/架构/项目状态，记录验证，完成并归档计划；分别创建内层/外层 Phase 03 提交，不 push。

## 验证与回归计划

### Windows portable solution

- 设置默认、合法保存、非法存储修复、非法主动保存拒绝、活动 ID 独立保存。
- 初始化选择与迁移顺序；同 ID 幂等、未知 ID、占用、取消、初始化/设置失败、消费者 pause/resume 和旧源回滚。
- Search 默认/显式 limit、七级语义；Read Redirect/anchor/not found/unavailable/cancel；asset 读取。
- 页面/历史/收藏分页、过滤、总数、稳定排序；同日 history update、默认页跳过、清空、favorite 幂等。
- fake HTTP 与临时 SQLite 覆盖 lifecycle；package 覆盖 token、legacy confirmation、active/non-active、pause 和 Core rollback。
- `dotnet.exe restore`、Release build、全量 test；现有 Core 89 个测试继续通过。

### 静态与反射

- Application 无 MAUI、Avalonia、Razor、WebView、`App.*`、`Preferences`、平台 `FileSystem`、SQLite API、条件编译、Vector/Embedding。
- Application project 仅 `net10.0`、仅引用 Core、无 PackageReference。
- public surface 不出现 `DatabaseService`、SQLite connection、`WikiBook`、`WikiPage`、`WikiHistory`、`WikiFavorite`、`WikiAsset`。
- `git diff --check`、生成物/临时文件/真实数据追踪检查、两个 Git 状态与提交范围检查。

### 真实输入

- 仅在已有显式环境变量/路径可安全复用时，用稳定临时快照执行 Application Search/Read；源数据库家族 hash 不变。
- golden `.pkg` 只通过 Application Inspect；前后 SHA-256 保持 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`。
- 若当前会话没有可复现的真实数据库路径，则明确记录未执行，不发现或猜测用户数据位置。

## 风险与回退

- 切换失败可能使 ContentDb 与活动快照不一致：在 pause + gate 内保存旧路径/快照/设置，任何目标初始化或设置失败都重新打开旧路径并恢复设置；回滚失败使用独立稳定 code。
- Core gate 是 non-blocking：Application 不等待占用操作，统一返回稳定 `operation-in-progress`，避免 UI 假死和嵌套死锁。
- 迁移/导入/删除触碰数据目录：仅调用已有可恢复 Core 实现；Application 不增加文件交换算法。
- 非活动 export 需要临时 ContentDb：Application 创建后在 finally 关闭；输出路径/覆盖策略仍由调用方负责。
- 测试使用临时根并自动释放；真实输入只读且前后 hash；任何失败可回退到 Phase 02 源码提交和外层治理基线提交。

## 进度记录

- 2026-08-24：用户提供完整实施计划并要求在新上下文实施；计划视为 approved。
- 2026-08-24：按规定重读治理、spec/ADR、Phase 00–02 和 Core/旧宿主真实代码；Windows Core Release 89/89 通过。
- 2026-08-24：内层 Phase 02 提交 `4567cf8`；外层相关治理基线提交 `dd0127a`；两者均未 push。计划转为 `in-progress`。
- 2026-08-24：新增单一 Application 与 test project、组合根、公共 DTO、两个 ports 和五组用例；设置/初始化/切换、Query、Library 与 Data 定向回归增至 54/54。
- 2026-08-24：补充内部编排门，确保 lifecycle/package 在选择活动快照到取得 Core 数据门闩之间不会与热切换交错；实际运行中的下载会让切换稳定返回 `operation-in-progress`。
- 2026-08-24：Windows restore、Release build、Core 89/89 与 Application 54/54 全部通过；3.58 GB golden 通过 Application Inspect，验证 payload 的临时快照完成 Application Search/Read，原包 hash 不变且临时目录清理。
- 2026-08-24：内层 Phase 03 提交为 `6d619b3`；README、Master Architecture 和项目基线已更新，计划完成并归档，未 push。

## 发现、偏差与决策

- 外层治理文件此前大多未跟踪；基线提交只纳入本 Phase 会修改/引用的 13 个文件，排除了临时、真实数据、experiment 和无关归档。
- 外层 `.git` 在当前 sandbox 中只读，Git 索引/提交通过用户批准的受控提升完成；普通治理文件内容仍在工作区内编辑。
- 未新增 ADR：单一 Application、显式 data root 与平台 ports 已由 Master Architecture 和 ADR 0002 决定。
- Core 的 non-blocking `WikiDataOperationGate` 保护实际数据变更，但 lifecycle 在内部才取得 lease；Application 增加一个纯内部编排 semaphore，把“读取活动快照 → 调用 Core”与热切换串行化，并保持忙碌时稳定拒绝。它不是新 port，也不改变 Core 语义。
- 真实验收没有打开或修改正在使用的生产数据库：golden 先由 Application Inspect 校验，再由 Core reader 解到 Windows 临时目录形成稳定真实快照；所有迁移和 SQLite 打开只发生在该临时副本，原 `.pkg` 前后 hash 相同。

## 验证结果

- 实施前 Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：89/89 通过，0 skipped。
- Phase 02 `git diff --check`：无 whitespace error；仅旧工作树 CRLF→LF 提示。提交范围严格为计划记录的 8 个修改文件和 1 个新增测试。
- Windows `dotnet.exe restore Terraria_Wiki.Core.slnx --nologo`：通过，所有项目最新。
- Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，0 warnings / 0 errors。
- Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：Core 89/89、Application 54/54，通过，0 skipped。
- Application 测试覆盖设置默认/修复/拒绝、活动 ID、迁移/切换/回滚/占用/取消、Search/Read/asset、library 分页/过滤/总数/写入、fake HTTP lifecycle、统计/VACUUM/删除以及 active/non-active package 流程。
- 静态与反射门：Application 只定义两个 public host ports 和五组用例；project 仅 `net10.0` + Core `ProjectReference`，无 `PackageReference`；源码无 MAUI、Avalonia、Razor、WebView、`App.*`、平台 Preferences/FileSystem、SQLite API 或条件编译；公共 API 无数据库服务或持久化表模型。
- opt-in Windows 真实验收：`RealApplicationCompatibilityTests` 实际运行 33 秒，通过 1/1。Application Inspect 得到 3,584,607,183 bytes、427-byte metadata、3 个文件；临时真实快照上的 Search `镐` 非空、Read `1.0` found；原包 SHA-256 前后均为 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`，临时根已清理。
- 内层最终 `git diff --cached --check` 通过；Phase 03 只新增 Application/test 并修改既有 solution，未修改 Core 生产实现、旧 MAUI、schema、依赖或数据格式，未跟踪 `.db/.pkg/.pak/-wal/-shm`。

## 完成摘要

Phase 03 已完成。内层现在提供一个只引用 Core 的 `net10.0` Application，通过五组具体用例覆盖设置、活动 Wiki、Search/Read/asset、页面/历史/收藏和完整数据生命周期；两个宿主 ports 分别承接设置存储与活动数据消费者暂停。公共 API 不泄漏持久化实现，热切换和破坏性流程可回滚并稳定处理占用与取消。

现有 89 个 Core 测试与新增 54 个 Application 测试全部通过，真实 golden 的 Application Inspect 与临时快照 Search/Read 也已通过且原件 hash 不变。内层代码提交为 `6d619b3`，外层治理将以本归档计划和状态更新形成独立 Phase 03 提交；均不 push。Phase 04 可实现两个 ports、提供现有 Core HTTP transport 并组合 `WikiApplication`，Phase 05 可直接消费同一 `WikiQueryUseCases`。
