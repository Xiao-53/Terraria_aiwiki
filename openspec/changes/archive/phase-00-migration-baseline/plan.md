# Phase 00 — MAUI 迁移基线与轻量宿主 PoC

- 状态：completed
- Phase：00 — Migration Baseline & Host Spike
- 执行环境：WSL Codex CLI 负责阅读、测试代码和文档；Windows 负责 .NET SDK、Avalonia、WebView2、运行与发布验证；本 Phase 不安装 Android/MAUI 工具链
- 创建日期：2026-08-20
- 最后更新：2026-08-23
- 范围：现有源码/数据基线、Avalonia Windows 垂直切片、宿主决策门槛
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`docs/adr/0001-use-avalonia-client.md`；`openspec/specs/wiki-data-compatibility/spec.md`；`openspec/specs/local-wiki-viewer/spec.md`；`openspec/specs/wiki-query-boundary/spec.md`

## 背景与当前证据

用户已确定 Windows 新版本不继续使用 MAUI，并明确不愿为当前 Windows 开发安装 MAUI workload、Android SDK、JDK、Emulator 等大型工具链。本 Phase 的任务不是重新讨论 MAUI，而是在不安装这些组件的前提下，证明首选替代宿主能承载项目最危险、最有代表性的能力。产品当前目标仅为 Windows，未来只考虑 Android；除此之外的平台均为非目标，WSL/Linux 只是开发执行环境。

### 已读取的源码基线

当前源码仓库：`terraria-wiki-offline/`，基线提交 `a1dd89981d7604006ec91955d4d19e93cb933ce1`。计划生成时该仓库 `git status --short` 无输出。

已核对：

- `Terraria_Wiki/Terraria_Wiki.csproj`：单一 `Microsoft.NET.Sdk.Razor` MAUI 项目，目标含 Android/iOS/MacCatalyst/Windows；直接引用 MAUI Controls、MAUI BlazorWebView、sqlite-net、HtmlAgilityPack、LuYao.TlsClient。
- `MauiProgram.cs`、`App.xaml.cs`、`MainPage.xaml(.cs)`：DI、数据库路径、Web server、窗口/生命周期和平台 WebView 集成都在 MAUI 宿主中。
- `Components/Models/DatabaseModels.cs`、`CommonModels.cs`：现有数据库、搜索、数据包和 UI/bridge 模型。
- `Components/Services/DatabaseService.cs`：SQLite 表、WikiSearchIndex FTS5、搜索、分页、历史、收藏、资源元数据和数据库切换。
- `Components/Services/DataService.cs`：页面/Redirect 获取、增量更新、失败重试、HTML 清理、搜索文本、资源、导入导出、包校验和维护；核心算法与 MAUI/global state 混合。
- `LocalWebService.cs` 与 `Resources/Raw/Web/**`：loopback Viewer、数据库 WikiAsset 路由和现有 HTML/CSS/JS。
- `AppState.cs`、`AppService.cs`、`FileHelper.cs`、`LocalizationService.cs`、`LogService.cs`、`LegacyUpgradeHandler.cs`：可复用逻辑与 MAUI 路径、设置、文件对话、通知、生命周期混合。
- `Components/**/*.razor`：旧 Presentation 直接访问 `App.ContentDb`、`App.DataManager` 等静态服务。
- `WindowHelper.cs`、`KeyboardService.cs`、`BurnInProtectionService.cs`、`Platforms/**`：旧 MAUI/目标平台实现。

静态统计仅用于定位耦合，不作为重构配额：`DataService.cs` 中约有 109 处对 `App.ManagerDb`/`ContentDb`/`AppStateManager` 等静态入口的访问；数据库模型没有 MAUI API；`WindowHelper`、`MainPage` 和平台目录明显属于旧宿主。

### 已确认事实

- Existing Wiki Core 不是独立 project，但相当一部分逻辑本身使用普通 C#/.NET API。
- SQLite 表、FTS5、包格式、数据目录和 Viewer 是 P0，不能用重新实现来验证新框架。
- 现有源码没有独立自动化测试 project；`Components/Pages/Test.razor` 是应用内测试页面，不是可重复测试套件。
- `DatabaseService` 默认以 `ReadWrite | Create` 打开数据库，初始化会修改 journal mode、建表和写入种子；其搜索方法仍直接读取 MAUI `Preferences`。因此“旧库只读兼容”和“现有服务可移植”必须分开验证。
- 当前数据包导入直接把 metadata 中的相对路径与临时目录组合，未证明规范化后的输出仍位于临时根内；Phase 00 必须使用独立安全 parser 验证异常包，不调用生产替换流程。
- 当前代码导入/导出扩展名为 `.pkg`，旧仓库 README 仍写 `.pak`；历史文件是否只是扩展名差异需要真实样本确认。
- 现有 Viewer server 固定使用 loopback 端口，启动异常只写调试日志，并依赖 MAUI 包资源 API 和全局活动 Wiki；新宿主必须验证端点、健康状态、生命周期与来源限制。
- Windows x64 环境可调用 `C:\Program Files\dotnet\dotnet.exe`，已安装 .NET SDK 10.0.400，以及 Microsoft.NETCore.App、Microsoft.AspNetCore.App、Microsoft.WindowsDesktop.App 10.0.11；`dotnet workload list` 未列出已安装 workload。
- 已安装 Avalonia Templates 12.1.1；PoC 已实际 restore `Avalonia`/`Avalonia.Desktop`/`Avalonia.Themes.Fluent` 12.1.1、`Avalonia.Controls.WebView` 12.0.1、`sqlite-net-pcl` 1.11.285 及测试包。
- 已安装 WebView2 Runtime 151.0.4129.93，运行文件存在；NuGet v3 endpoint 的直接访问返回 HTTP 200。
- WSL 2.7.11.0 与 Ubuntu 24.04.4（WSL2）可用，WSL Codex CLI 0.148.0 可用；WSL 没有 Linux-native `dotnet`，但可以调用 Windows `dotnet.exe`。本 Phase 的 Windows 客户端验证必须在 Windows 进程中执行，Linux-native SDK 不是当前产品目标的前置条件。
- 当前没有 MAUI/Android workload、Android SDK、JDK、Emulator 或 `global.json`；C 盘约有 568.46 GB 可用空间。
- 已发现并在用户授权后显式验证现有 `Manager.db` 与约 3.34 GB 的 `Terraria_Wiki.db`。最终 runner 从不以 SQLite 打开源库，而是把 `.db`、`.db-wal`、`.db-shm` 作为一个字节级输入集合，确认前后哈希稳定后，仅在 Windows 本地临时快照/可写副本上执行 checkpoint、读取、FTS5、CRUD 和 backup；两套源文件集合均保持不变。
- 用户提供 golden `phase-00-inputs/packages/Terraria_Wiki.pkg`：3,584,607,183 bytes，SHA-256 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`。它是 `WIKIDATA` + little-endian Int32 JSON 长度，实际 JSON 字段为 camelCase，哈希字段为 `mD5`；包含 3 个 payload（failed_pages.txt、res.txt、Terraria_Wiki.db）。独立 oracle 已流式验证并清理临时 staging，未修改原件；独立临时 writer 生成的新包也通过同一 oracle。未取得 `.pak` 历史样本，该可选历史扩展名不作为当前 `.pkg` 能力门槛的替代证据。

### 实施前推断及最终结果

- 已安装工具足以用 Windows CLI restore/build/run/publish Avalonia PoC，且无 MAUI/Android/JDK/Emulator workload；已确认。
- `sqlite-net-pcl` 在新宿主中的 provider 初始化、SQLite 3.53.3 和 FTS5 trigram 保持代表性现有行为；已由 fixture 与真实快照确认。
- Avalonia NativeWebView 可以承载现有 Local Viewer 的代表性垂直切片，动态 loopback 是已验证基线；Phase 00 不需要改为资源映射。
- `LuYao.TlsClient` 1.2.0.0 已在 Windows .NET 10 进程中完成实际 HTTPS 请求；不再仅依赖兼容表推断。

## 目标与完成定义

本 Phase 只完成“选择宿主所需的最小证据”，不实施正式产品迁移。

完成条件：

1. 形成可复现的 P0 源码、数据格式、样本、兼容 oracle 和旧行为基线，并与 capability specs 对齐。
2. 在不安装 MAUI/Android/JDK/Emulator 的 Windows 环境创建 Avalonia 垂直切片。
3. 分别验证旧数据库只读检查、可写副本初始化/迁移、FTS5 trigram、现有 Viewer 页面/资源/消息和最小发布产物。
4. 记录现有与新增工具、PoC restore/download/落盘体积、构建命令、运行环境和所有失败。
5. 用独立于新 writer/reader 的兼容 oracle 验证 golden 旧包与新临时导出包，并验证异常包不会写出临时根目录。
6. 对 ADR 0001 作出一个明确验证结果：通过并补充证据，或以新 ADR supersede 并启动 WPF 回退计划。
7. PoC 不修改用户生产数据库、不改变 `.pkg` 格式、不进入完整 UI/AI 开发。

## 前置条件

- 用户批准执行本 Phase；当前 draft 只是一份计划，不代表已授权 restore NuGet 包、复制真实数据库或创建 PoC。
- 使用已发现的代表性数据库时，先在用户批准后创建独立副本并记录原件/副本哈希；若不能使用该数据，再生成结构等价的最小 fixture。
- 用户已提供至少一个由当前旧应用导出的 `.pkg` golden 样本；如能取得旧 README 指向的历史 `.pak`，同时保留原件。任何真实包都先计算哈希，不在原件上写入。
- 用户确认最低 Windows 版本和 CPU 架构；Phase 00 的最小默认建议是当前 Windows x64 加一套干净 Windows x64 环境。若产品需要 Windows 10 或 ARM64，必须另外准备相应环境并纳入矩阵。
- 准备一套无 .NET SDK、Avalonia 模板和项目开发依赖的干净 Windows 环境，可使用 Windows Sandbox、虚拟机快照或独立测试机，用于发布产物 smoke test。
- PoC 放在外层工作区的新实验目录，不污染 `terraria-wiki-offline/` 基线；具体目录在执行开始时记录，生产目录结构由 Phase 01 决定。
- 当前 WebView2 Runtime 已可用；Phase 00 仍需验证目标机的 Runtime 检测与分发策略，不扩展为 Visual Studio/MAUI 安装。

### 执行前准备状态

| 项目 | 当前状态 | 谁来准备 |
|---|---|---|
| .NET 10 SDK / Windows runtimes | 已就绪 | 无需用户操作 |
| Avalonia 模板 | 已就绪 | 无需用户操作 |
| WebView2 Runtime | 本机及 Sandbox Available/Missing 均已验证 | Evergreen 前置检测策略已记录 |
| NuGet 网络 | 已验证可访问 | 无需用户操作 |
| 代表性 Wiki 数据库 | Manager/Content 已经一致快照验证；源集合不变 | 无需用户操作 |
| 当前 `.pkg` golden | 已提供并由独立 oracle 验证 | `phase-00-inputs/packages/Terraria_Wiki.pkg` 原件只读 |
| 历史 `.pak` | 可选但未发现；不阻塞当前 `.pkg` 契约 | 未来若出现样本则另行显式验证 |
| 干净 Windows x64 环境 | Windows Sandbox 已实测；无 SDK/`dotnet` 命令 | Phase 00 自动脚本可复现 |
| Windows 最低版本 / 架构矩阵 | 本 Phase 明确只验当前 Windows 11 x64 | Windows 10 / ARM64 若进入产品范围需另行授权和验证 |

### 本次执行假设与授权边界

- 2026-08-20 用户要求“按计划完成第一阶段的步骤”，本次按 Roadmap 的首个阶段 Phase 00 执行，视为批准本 Phase 计划及其安全、可回退的本机探针。
- 当前验证矩阵暂按现有 Windows 11 x64 主机执行；这不等于确认 Windows 10 或 ARM64 产品支持，额外矩阵仍待用户另行确认。
- 已发现的真实数据库原件只允许只读打开和前后哈希；任何初始化、FTS 写入、CRUD 和 backup 都只允许针对临时副本。
- 用户已提供旧应用导出的 `.pkg` golden，先计算并记录原件 SHA-256，再由独立 oracle 流式验证；`.pak` 历史样本仍缺失，不据此扩展格式兼容范围。
- PoC 固定放在 `experiments/phase-00-host-spike/`，不得修改 `terraria-wiki-offline/**`；运行时数据库副本、发布目录和日志均为可删除生成物，不提交真实 Wiki 内容。

## 范围与非目标

### 范围

- 静态源码/调用关系和数据格式基线。
- 最小 Avalonia Windows app 或实验 project。
- sqlite-net/SQLite provider/FTS5 代表性运行验证。
- NativeWebView + 现有 Viewer 静态资产 + 一个数据库资源请求 + JS↔C# 消息。
- 独立数据包兼容/安全 parser：只读 metadata 和 payload，验证路径、长度、数量、总大小和 hash，不替换生产数据。
- Windows 文件选择、目录选择、剪贴板、外链和主题/窗口可行性探针。
- `dotnet publish` 与干净 Windows 启动探针。
- WPF + Blazor Hybrid 只在 Avalonia 硬门槛失败后做对照 PoC。

### 非目标

- 不重做 Chat、Wiki、数据管理或设置 UI。
- 不正式提取/移动 Existing Wiki Core；该工作属于 Phase 01。
- 不修复搜索质量；仅验证 FTS5 能力，搜索修复属于 Phase 02。
- 不接入 Python AI、Agent 或 transport。
- 不安装 Android SDK/JDK/Emulator，不创建 Android target。
- 不创建或验证 iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 或其他范围外产品平台。
- 不删除、重命名、格式化或升级原 MAUI 源码。
- 不提前决定 Viewer loopback 与资源映射的最终实现。

## P0 / 兼容性影响

本 Phase 不改变生产 P0 数据或格式。保护措施：

- 真实数据库原件只做只读检查并核对前后 SHA-256；任何初始化、迁移、CRUD、backup 或 FTS 写入只在独立可恢复副本上执行。
- 数据包原件只读；所有解析/导出写入在新建临时目录进行，不以当前应用数据目录为目标。
- FTS5 测试必须记录 `sqlite_version()`、`pragma compile_options`、trigram 建表和查询结果。
- 数据包 oracle 必须独立于新生产 writer/reader，检查 header、metadata、路径包含、长度/数量/总量边界、文件 hash、截断和尾随数据策略；PoC 导出只写临时目录。
- Viewer 只请求测试数据库/fixture 中的页面和资源。
- 任何 provider、包版本或数据库行为差异都只记录为发现，不在本 Phase 迁移生产数据。

## 方案与待决问题

### 首选 PoC

验证最小链路：

```text
Avalonia Window
→ Search test use case
→ existing/linked DatabaseService or behavior-equivalent test harness
→ representative SQLite + FTS5

Avalonia NativeWebView
→ existing Local Viewer HTML/CSS/JS
→ loopback test server or local resource mapping
→ one WikiPage + one WikiAsset
→ one JS↔C# message
```

PoC 可以为测试目的链接少量现有源码或建立最小 harness，但不得复制一份准备长期演进的 Wiki Core。PoC 通过后，正式移动方式只在 Phase 01 计划中确定。

### 硬门槛与决策

| 门槛 | 通过证据 | 失败处理 |
|---|---|---|
| 轻量工具链 | 安装清单没有 MAUI/Android/JDK/Emulator；CLI build/run/publish | 若 Avalonia 本身要求不接受的大型工具链，拒绝 |
| SQLite/FTS5 | 原件只读检查 hash 不变；副本上 trigram、查询、CRUD/backup 成功 | 检查 provider；限定时间仍失败则拒绝 |
| Viewer | 页面、CSS/JS、数据库图片、内部链接、消息桥、端点/健康检查和关闭恢复成功 | loopback 是可接受基线；资源映射只作可选对照；仍失败则拒绝 |
| 数据包 | golden 旧包和临时新包通过独立 oracle；异常路径/长度包被安全拒绝 | 不改格式；不调用生产替换；记录阻塞并拒绝迁移 |
| 发布 | 干净 Windows 启动，WebView2 策略明确 | 评估 framework-dependent/self-contained；仍失败则拒绝 |

### 待决问题

- Avalonia 具体稳定版本和 NativeWebView 包版本：执行时根据 .NET 10 支持矩阵选择，不在计划中预先锁死。
- Windows 数据根目录：测试旧目录原位读取与新目录迁移两种方式，Phase 01 决定。
- Viewer transport：先把 loopback 作为完整基线验证，端点不得继续作为 Core 常量，启动失败必须可观察；只有 NativeWebView 在目标平台能实际提供响应内容时才评估资源映射。
- 发布形态：framework-dependent 与 self-contained 的体积、启动、WebView2 依赖分别记录，不在 PoC 直接决定商店/安装器。
- `LuYao.TlsClient`：只做一个实际请求/加载探针；若失败，作为 Phase 01 专门兼容问题，不在 PoC 重写网络层。

## 文件变更

以下是候选，不代表当前已获准创建：

- **KEEP** `terraria-wiki-offline/**`：只读 brownfield 基线。
- **ADD candidate** 独立 PoC/fixture 目录：具体路径在执行前确认，完成后决定删除或保留为测试资产。
- **ADD candidate** SQLite/FTS5 characterization tests：优先成为 Phase 01 可继承测试。
- **ADD candidate** Viewer smoke fixture：复用现有 `Resources/Raw/Web/**` 和 `wwwroot/js/iframe-bridge.js`，不复制长期分叉版本。
- **ADD candidate** 数据包独立兼容/安全 checker 与 golden fixtures：不引用新生产 writer/reader，不执行目标目录替换。
- **MODIFY candidate** `docs/adr/0001-use-avalonia-client.md`：记录验证通过证据；若失败则标记 superseded 并关联新的回退 ADR。
- **MODIFY candidate** 本计划：实时记录安装、命令、体积、结果、偏差和决策。
- **KEEP** 所有数据库表、`.pkg` 字节格式和旧数据迁移逻辑。

本次执行收敛后的精确范围：

- **ADD** `experiments/phase-00-host-spike/Directory.Build.props`、solution/project 文件与说明：隔离 Phase 00 PoC，不定义 Phase 01 生产目录。
- **ADD** `experiments/phase-00-host-spike/src/Phase00.Probes/**`：sqlite-net/FTS5、只读数据库、临时副本、动态 loopback Viewer、数据包 oracle 和 Windows 平台探针。
- **ADD** `experiments/phase-00-host-spike/src/Phase00.ProbeRunner/**`：从 Windows CLI 对显式指定的数据库/包样本运行只读哈希、临时副本和 oracle 探针并生成 JSON 证据。
- **ADD** `experiments/phase-00-host-spike/src/Phase00.HostSpike/**`：Avalonia + NativeWebView 最小 Windows 壳与可记录结果的 smoke 模式。
- **ADD** `experiments/phase-00-host-spike/tests/Phase00.Probes.Tests/**`：数据库最小 fixture、数据包正常/恶意输入、Viewer transport 生命周期和来源限制测试。
- **ADD** `experiments/phase-00-host-spike/scripts/**`：Windows CLI 的 restore/build/test/run/publish 与证据收集入口。
- **ADD** `experiments/phase-00-host-spike/.gitignore`：排除 `bin/`、`obj/`、真实数据库副本、publish 和运行日志。
- **MODIFY** 本计划与 `docs/adr/0001-use-avalonia-client.md`：仅在取得实际验证证据后记录结果；硬门槛未齐全时不将 ADR 记为完全通过。

## 执行步骤

- [x] 基线锁定：已记录旧源码提交 `a1dd89981d7604006ec91955d4d19e93cb933ce1`、P0 表/FTS5/包格式/Viewer 路由；本轮未修改 `terraria-wiki-offline/**`。
- [x] 样本准备：已记录 golden 包大小/哈希/字段和临时 fixture；真实数据库仅经一致字节快照进入临时验证，`.pak` 历史样本仍缺失但不阻塞当前 `.pkg` 契约。
- [x] 工具链复核：Windows .NET SDK 10.0.400、Avalonia templates 12.1.1、WebView2 Runtime 151.0.4129.93、约 564 GB 可用空间；无 MAUI/Android/JDK/Emulator/workload。
- [x] Avalonia 最小启动：Windows CLI restore/build/publish 成功；开发机 framework-dependent HostSpike 启动并创建 NativeWebView，最初缺少 manifest supportedOS 的问题已修复。
- [x] SQLite/FTS5 探针：sqlite-net-pcl 1.11.285 在 fixture 与真实 Manager/Content 一致快照上实测 SQLite 3.53.3、`ENABLE_FTS5`、trigram、读取、临时 CRUD 和 backup；源 `.db/-wal/-shm` 集合前后不变。
- [x] Viewer 探针：动态 `127.0.0.1:0` loopback 完成 health/static/DB image/audio/404/HEAD/host/method/path、端口冲突、重复 start-stop-close；NativeWebView smoke 通过 Redirect/anchor 数据、内部导航/返回响应、主题、history、C#↔JS request/response、非法消息拒绝和临时目录清理。
- [x] 平台探针：人工实测文件/目录选择、剪贴板写入后读回、系统浏览器外链、主题和窗口关闭；取消/错误均有明确 outcome/error 分支，未把按钮存在等同于验收通过。
- [x] 数据包探针：独立 oracle 流式解析 golden 包，验证 427-byte camelCase/`mD5` metadata、3 payload、声明大小与 MD5、截断/尾随/路径/重复/长度/数量/总量恶意输入；临时 staging 已清理，未执行生产替换。
- [x] 发布探针：framework-dependent（约 157 MB）和 self-contained win-x64（约 234 MB）发布及开发机 smoke 通过；无 `dotnet` 命令的 Windows Sandbox 中，WebView2 Available 完整 smoke 与 Missing 可观察失败路径均通过。
- [x] 决策门：所有 Phase 00 宿主选择硬门槛均取得 Windows 实测证据；ADR 0001 的 Avalonia + NativeWebView 方向通过，Phase 01 可在独立计划授权后开始。
- [x] 收尾：临时 package/database staging 自动清理，源数据库集合与 golden 前后哈希一致；生成物均 gitignored，最终验收后清理；未修改 `terraria-wiki-offline/**`。

## 验证与回归计划

### WSL / 平台无关

- 静态检查 Core 候选的 MAUI/Avalonia/平台引用。
- 解析 `.pkg` header、metadata 和 file hash 的只读测试；对可取得的 `.pak` 历史样本做同样检查。
- 数据包路径包含、长度/数量/总量、重复路径、截断与临时目录无逃逸测试。
- 数据模型、HTML 清理、URL/文件名与搜索输入的 characterization tests。
- WSL/Linux-native `dotnet` 不作为 Windows 客户端验证；需要 .NET 的 Windows 探针由 Windows `dotnet.exe` 执行并明确记录进程环境。

### Windows

- `dotnet --info`、`dotnet workload list` 与安装目录/磁盘占用证据。
- CLI restore/build/run/publish；不以 IDE 成功代替命令行证据。
- WebView2 availability、Viewer 交互、loopback 端点/冲突/生命周期、文件系统/剪贴板/外链/窗口探针。
- SQLite/FTS5、旧数据库、数据包和 Local Viewer 的代表性运行验证。
- 在未安装开发工具的 Windows 用户环境执行发布产物 smoke test。

### Android

本 Phase 不执行。不得把 Avalonia 文档中的 Android 支持表当作本项目 Android 已通过；实际验证属于 Phase 13。

## 风险与回退

| 风险 | 保护/回退 |
|---|---|
| PoC 误改真实数据 | 原件只读、写入副本/fixture、前后哈希、临时目录、原件不作为输出目标 |
| 数据库“只读测试”实际触发初始化写入 | 原件只用只读连接；初始化、迁移和 CRUD 只对独立副本执行 |
| sqlite native provider/FTS5 不一致 | 记录版本与 compile options；不迁移生产数据；失败触发宿主决策 |
| 数据包路径或长度声明逃逸临时目录 | 使用独立有边界的 parser；先验证再写入；不调用当前生产替换流程 |
| Viewer 依赖 MAUI 特定 WebView 行为 | loopback 作为完整基线；资源映射只作可选对照；失败回退 WPF，不重写 Viewer |
| 固定端口、静默启动失败或 listener 泄漏 | Adapter 管理端点和健康状态；验证冲突、多实例、前后台和关闭恢复 |
| 工具链实际仍很重 | 记录真实安装清单和磁盘增量；超过用户接受范围即不接受方案 |
| NativeWebView/API 变化 | PoC 锁定已验证版本；后续升级必须重跑 Viewer/发布回归 |
| 发布产物只在开发机工作 | 强制干净环境 smoke test；失败不得进入 Phase 01 |
| PoC 演变成第二套 Core | 只保留 characterization tests；生产迁移必须移动/提取现有实现 |

回退不修改原 MAUI 基线。Avalonia 验证失败时，创建独立 WPF + Blazor Hybrid PoC，复用相同数据库/Viewer/发布 fixture，使结论可比较，并用新 ADR supersede 当前决策。

## 进度记录

- 2026-08-20：完成治理文件、v0.5、项目基线、源码入口/服务/模型/Razor/Viewer/平台代码盘点。
- 2026-08-20：重新审计系统环境，确认 Windows .NET 10 SDK、Avalonia templates、WebView2、NuGet 与 WSL2/Codex CLI 可用；确认未安装 MAUI/Android/JDK/Emulator。
- 2026-08-20：发现代表性现有 Wiki 数据库；未发现 `.pkg` / `.pak` golden，干净 Windows 发布验证环境与 Windows 支持矩阵仍待准备。
- 2026-08-20：完成候选比较，推荐 Avalonia + NativeWebView；WPF + Blazor Hybrid 作为硬门槛失败回退。
- 2026-08-20：创建本 draft；尚未获准实施 Phase 00，未创建 PoC、未修改业务源码。
- 2026-08-20：用户批准按计划执行首个 Roadmap 阶段；计划转为 `in-progress`，执行矩阵暂按现有 Windows 11 x64，PoC 范围固定为外层 `experiments/phase-00-host-spike/`。
- 2026-08-20：执行前复核确认源码仓库仍为 `a1dd89981d7604006ec91955d4d19e93cb933ce1` 且状态无输出；Windows .NET SDK 10.0.400、无 workload、Avalonia templates 12.1.1、WebView2 Runtime 候选与代表性数据库位置仍可用。
- 2026-08-20：创建 Phase 00 disposable PoC：独立 `Phase00.Probes`、Windows `Phase00.ProbeRunner`、Avalonia `Phase00.HostSpike`、xUnit tests 和 PowerShell scripts；未修改旧源码、依赖锁定外文件或生产数据。
- 2026-08-20：Windows `dotnet restore -r win-x64`、Release solution build、framework-dependent/self-contained publish 成功；首次 Host 启动暴露 manifest supportedOS 缺失，补充 Windows 10/11 compatibility manifest 后 NativeWebView 可创建。
- 2026-08-20：20 个 probes tests 全部通过；fixture runner 成功输出 SQLite/FTS5、package、Viewer 和 bridge JSON。Provider 实测 SQLite 3.53.3，FTS5/trigram 可用。
- 2026-08-20：以独立 oracle 流式校验 golden 包；3,584,607,183 bytes、3 files、payload MD5 与声明一致，runner JSON 记录的输入 SHA-256 前后保持 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`，临时 3.58 GB staging 已清理。
- 2026-08-20：开发机 NativeWebView smoke 通过并自动退出：same-origin WebMessageReceived、PageRedirectAsync、GetRedirectedTitleAndAnchorAsync、SaveToTabHistory、DB 图片、内部 Alias、ChangeTheme 均有 JSON 证据；尚未在干净 Windows 环境复测。
- 2026-08-20：Windows PowerShell `restore.ps1`、`build.ps1`、`test.ps1`、`publish.ps1` 和 `run-smoke.ps1` 均实际执行成功；smoke JSON `success=true`、`historyCount=1`、`darkTheme=true`。关闭时 WebView2 输出一次 `Chrome_WidgetWin_0` unregister 1412 诊断，但进程退出码为 0。
- 2026-08-20：runner 增加显式 `--keep-temp true` forensic 选项；验证后使用 Windows PowerShell 删除了已核对路径的临时 fixture 目录，默认运行仍自动清理。
- 2026-08-20：复核 runner 输出边界；SQLite FTS 命中证据改为只输出标题与正文 UTF-8 字节数，不将正文写入 JSON；Release 构建、20 个测试和 fixture runner 复跑成功。
- 2026-08-20：发布物 smoke 复跑成功；发现并修复 Host fixture 临时根未在窗口关闭时清理的问题，随后扫描 Windows `%TEMP%` 无残留 `phase00-host-*` 目录。
- 2026-08-23：主 agent 执行独立验收。隔离临时输出下 Windows Release build 成功（0 errors）、20/20 tests 通过、framework-dependent/self-contained publish 与两种发布物 NativeWebView smoke 均通过；golden `.pkg` 前后 SHA-256 一致。
- 2026-08-23：真实 Content/Manager 数据的 `.db`/`.db-wal`/`.db-shm` 先做前后哈希和临时一致快照；Windows provider 在本地临时快照上完成真实表读取、Content FTS5/trigram、CRUD 与 backup。现有 runner 直接对活动 WAL 库执行时因只复制主 `.db` 报 `database is locked`，该路径不得作为通过证据。
- 2026-08-23：用户授权继续完成 Phase 00。收尾范围锁定为 WAL 一致快照、独立临时 writer/oracle、Viewer/平台/WebView2/TLS 最小探针、干净 Windows smoke 和人工平台验收；不进入 Phase 01，不修改 `terraria-wiki-offline/**`。
- 2026-08-23：SQLite probe 改为先稳定复制 `.db/-wal/-shm`，SQLite 只打开临时 snapshot；最终 runner 对真实 Manager/Content 均返回 `success=true`、`sourceFilesUnchanged=true`，临时副本 CRUD/backup 通过。
- 2026-08-23：fixture writer 与 oracle 的 DTO/序列化完全分离；完整 Release solution build 0 errors，27/27 tests 与 fixture runner 通过。对抗审查发现并修复输出路径可覆盖输入/WAL/SHM 的漏洞；四个受保护目标均返回 exit 2 且内容不变。
- 2026-08-23：两种开发机发布物 NativeWebView smoke 通过；Windows Sandbox 在无 `dotnet` 环境分别验证 WebView2 Missing 明确错误和只读 runtime 输入下的 Available 完整 Viewer smoke，两次 host exit code 均为 0，期望状态均匹配。
- 2026-08-23：用户完成人工平台验收：文件/目录选择、剪贴板写后读回、系统浏览器外链、Dark 主题、窗口与 transport 清理均成功。LuYao.TlsClient 1.2.0.0 实际 HTTPS 请求返回 HTTP 200、1850 bytes。
- 2026-08-23：Phase 00 决策门关闭；计划改为 `completed`，同步 ADR、README 与项目基线。Phase 01 尚未开始，不把 PoC 当作生产客户端实现。

## 发现、偏差与决策

- v0.5 把基于 MAUI 的新 Presentation 与 Windows Runtime 写为既定方向，与用户新前提冲突；已通过 v0.6 重新规划。
- 旧 Razor 的高复用价值不足以覆盖 WPF 没有 Android 路线的长期成本；Presentation 本来就是重建范围。
- Avalonia 可以降低当前 Windows 工具链负担，但不能消除未来 Android SDK/JDK；路线通过阶段隔离而不是假装依赖不存在。
- NativeWebView 官方支持 Windows/Android不等于当前 Viewer 已兼容；Viewer 是 Phase 00 硬门槛。
- NativeWebView 的通用请求事件不应被假定为可提供本地响应；在 API 实测前，loopback 是正式候选而不是临时失败方案。
- `.pak` 与当前 `.pkg` 的关系没有样本证据；Phase 00 不作额外承诺，未来若取得样本则以独立变更验证。
- golden 样本纠正了原先 metadata 命名假设：旧源代码的默认 camel-case 序列化实际产生 `id/title/.../files`，`MD5` 属性实际字段名为 `mD5`；oracle 使用兼容大小写的读取语义并严格验证 canonical lowercase hash。
- `Avalonia.Controls.WebView` 12.0.1 在 Windows .NET 10 Host 中可编译、创建 WebView2 native child window 并触发 `WebMessageReceived`；缺少 supportedOS manifest 会在运行时阻止 native child window 创建，已补齐并纳入测试记录。
- `sqlite-net-pcl` 1.11.285 provider 在 Windows .NET 10 中实际启用 SQLite 3.53.3/FTS5；临时副本 CRUD 与 `VACUUM INTO` backup 不触碰 fixture/source 原件。
- 开发机 NativeWebView smoke 首次运行暴露 C#→iframe `postMessage` 误将对象编码为字符串；修正为同源对象消息后，4 条有效 WebMessage 和自动退出证据通过。
- runner 的 SQLite 搜索证据不保留正文，避免对显式真实数据库运行时将 Wiki 内容写入 JSON；fixture/Viewer smoke 仍可在临时 fixture 内验证页面 HTML 和资源行为。
- Host smoke 的 SQLite fixture 现在绑定窗口生命周期，关闭后 best-effort 删除临时根；发布物 smoke 仍只使用临时 fixture，不触碰用户数据库。
- 真实数据库使用 WAL；`File.Copy(source.db, copy.db)` 不能形成一致快照，也不能证明副本可迁移。探针必须把主库及 WAL 状态作为一个输入整体处理，在源文件前后哈希稳定时只对本地临时快照执行 checkpoint、CRUD 和 backup。
- sqlite-net 的 `ReadOnly` 打开仍可能更新现有 `-shm` 协调文件；后续探针不得先直接打开生产源，而应先做字节级一致快照，再在临时快照上运行 SQLite 查询。
- Windows SQLite provider 在 WSL UNC 临时路径上不能可靠取得 SQLite 文件锁；Windows 运行验证的可写数据库工作目录必须使用 Windows 本地临时目录，WSL `/tmp` 只保存不需要 SQLite 锁的证据或副本。
- fixture writer 与 package oracle 现在使用独立 DTO/序列化实现；writer → oracle 闭环和恶意包边界均进入自动化测试。
- runner 在进入普通错误 JSON 写入路径前拒绝把输出指向 package 输入、数据库主文件或其 `-wal/-shm` sidecar，避免“报告失败”本身破坏被保护输入。
- Windows 发布策略在 Phase 00 收敛为：应用可使用 self-contained .NET `win-x64`，Viewer 仍依赖系统 Evergreen WebView2；宿主启动时必须检测并报告缺失。固定 runtime 目录只作为 Sandbox Available 验证输入，不是已决定的产品捆绑方案；安装器/bootstrap 细节属于 Windows Productization。
- PoC 证明的是宿主选择所需的代表性 Viewer 垂直切片，不宣称已实现 Phase 01 之后的全部生产 bridge/API；完整 Adapter 接管仍须遵守后续 Phase 和 P0 回归边界。

## 验证结果

本轮已获得的实际证据（均由 Windows `C:\Program Files\dotnet\dotnet.exe` 进程执行，WSL 只负责调用/读取）：

- `terraria-wiki-offline/` 仍为基线提交且状态无输出；本轮没有修改业务源码。
- `dotnet restore -r win-x64 Phase00.HostSpike.sln` 成功；依赖为 Avalonia 12.1.1、Avalonia.Controls.WebView 12.0.1、sqlite-net-pcl 1.11.285、Microsoft.NET.Test.Sdk 17.13.0、xunit 2.9.3、xunit.runner.visualstudio 3.0.2。
- Release solution build 成功，0 errors；有分析器 warning（MD5 是旧格式兼容所需、可释放 Host 字段等），不影响编译。
- 27 个 `Phase00.Probes.Tests` 全部通过：独立 writer/oracle、包正常/恶意输入、大小/数量/总量/hash/截断/尾随/路径边界，SQLite 无 WAL/活动 WAL/Manager 结构/源变化、FTS5/page/redirect/asset/pagination/backup，Viewer image/audio/health/static/DB resource/404/500/method/host/path/来源限制/重复 start-stop/端口冲突/history。
- fixture runner 成功；输出包含 SQLite 3.53.3、`ENABLE_FTS5`、trigram、独立新包、Viewer 和 bridge 证据，source SHA 前后相同。
- golden runner：`dotnet run --project src/Phase00.ProbeRunner -- package --package phase-00-inputs/packages/Terraria_Wiki.pkg --output artifacts/golden-package.json` 成功；读取 3,584,607,183 bytes，metadata 427 bytes，3 files，payload 3,584,606,744 bytes，MD5 与声明一致；原件 SHA-256 前后为 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`，staging 已清理。
- 真实 Manager runner：主库 SHA-256 前后均为 `b333748ca0f827dc81b77d4cd31724612c9089bc1ddb93a3375861e01357bd5d`；一致快照包含 WAL/SHM，源文件集合不变，真实表读取、临时 CRUD 和 backup 均成功。
- 真实 Content runner：主库 SHA-256 前后均为 `ef5dcc4797c89058bb7302fc67c732ed4992e734e8a1d415e15165be7240b06d`；一致快照包含 WAL/SHM，FTS5/trigram、真实表读取、临时 CRUD 和 backup 均成功。该哈希只标识本次活动数据时点，不是发行包常量。
- framework-dependent 与 self-contained `win-x64` publish 成功，输出目录约 157 MB / 234 MB，静态 Viewer 资产均在生成物中。
- `scripts/restore.ps1`、`build.ps1`、`test.ps1`、`publish.ps1`、`run-smoke.ps1` 均通过 Windows PowerShell 实际执行；run-smoke 生成的 JSON 与直接 exe smoke 一致。
- 开发机两种发布物 NativeWebView smoke 均自动退出码 0、`success=true`；图片/音频实际请求、Redirect、内部导航/返回、主题、history、C#↔JS request/response、非法消息忽略、transport stop/restart/conflict/close 和临时根清理全部为 true，WebView2 151.0.4129.93。
- Windows Sandbox Missing：Windows NT 10.0.26100 x64、`dotnetCommand=false`、WebView2 不存在；self-contained Host 打开/关闭且准确记录明确 not-found，`expectationMet=true`。这证明缺失前置项可观察，不是 Viewer success。
- Windows Sandbox Available：同一无开发工具环境只读映射 WebView2 151.0.4129.93 后，完整 NativeWebView smoke `success=true`、`expectationMet=true`、host exit 0。固定 runtime 只用于验证，不作为提交或分发资产。
- 平台人工记录：文件和目录 picker 均完成选择，剪贴板写入/读回为 true，系统浏览器外链请求成功，Dark 主题应用成功，窗口关闭、transport stop/restart/close 和临时根清理均为 true；取消与异常分支在 Adapter 中返回独立 outcome/error。
- LuYao.TlsClient 1.2.0.0 通过实际 HTTPS 请求：`https://terraria.wiki.gg/api.php` 返回 HTTP 200、1850 bytes；证据不保存响应正文或 query。Build 仍报告该包的 `alpine-x64` RID warning，Windows 运行成功但该依赖的后续升级/替代不是 Phase 00 范围。
- 输出保护回归：package 输入、数据库 `.db`、`-wal`、`-shm` 四种 unsafe output 均在错误 evidence 写入前返回 exit 2，输入内容不变。

未验证但不阻塞本 Phase 的项目：没有历史 `.pak` 样本，因此不对该旧 README 扩展名作格式承诺；Windows 10/ARM64 不在本轮已授权矩阵；完整生产 Viewer Adapter、安装器/bootstrap、签名/商店与 Android 行为属于后续 Phase。Phase 00 只关闭已定义的 Windows 11 x64 宿主选择门槛。

## 完成摘要

Phase 00 已完成。Avalonia + NativeWebView 在已授权的 Windows 11 x64 矩阵通过工具链、真实 SQLite/WAL/FTS5、独立数据包 oracle、新临时 writer、代表性 Viewer、平台人工路径、两种发布、干净 Windows/WebView2 有无路径和 LuYao TLS 门槛；ADR 0001 的首选宿主方向得到实测支持。PoC 仍是可删除的 characterization 资产，不是生产客户端；没有修改 Existing Wiki Core、生产数据库、`.pkg` 字节格式或 Phase 顺序。Phase 01 尚未开始，必须另行建立/批准计划。
