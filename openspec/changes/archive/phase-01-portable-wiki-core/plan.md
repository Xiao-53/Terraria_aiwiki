# Phase 01 — Portable Wiki Core

- 状态：completed
- Phase：01 — Portable Wiki Core
- 执行环境：WSL Codex CLI 负责编辑、静态检查与计划维护；Windows `dotnet.exe` 负责 Core-only restore/build/test；不安装 MAUI/Android workload
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：现有 Wiki P0 后端的单一 portable Core、旧 MAUI 兼容 façade、数据库/数据包兼容与旧数据原位复用
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`docs/adr/0001-use-avalonia-client.md`；`docs/adr/0002-portable-core-and-data-root.md`；`openspec/specs/wiki-data-compatibility/spec.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/local-wiki-viewer/spec.md`

## 背景与当前证据

用户已批准按已确认计划实施 Phase 01，并选择：完整 P0 Core、生产代码进入内层 `terraria-wiki-offline/` 源码仓库、Windows 旧数据根目录原位复用。

当前内层源码基线为提交 `a1dd89981d7604006ec91955d4d19e93cb933ce1`，实施开始时 `git status --short` 无输出。现有 MAUI 单项目把数据库、抓取、包处理、平台路径、设置、弹窗和全局 `App.*` 混合在一起；已确认数据库模型本身无 MAUI 引用，`DatabaseService` 仅因搜索设置读取 `Preferences` 耦合 MAUI，`DataService` 则包含约 1700 行混合逻辑。Phase 00 已在无 workload 的 Windows 11 x64 环境验证 sqlite-net/FTS5、真实 WAL 数据快照、3.58 GB golden `.pkg`、LuYao TLS 与 Avalonia 宿主门槛。

本 Phase 以现有 specs 和 Phase 00 oracle 为兼容基线。事实与限制：

- 旧源码没有独立自动化测试项目，必须先建立 Core-only 测试面。
- 当前搜索 SQL 的质量问题属于 Phase 02，本 Phase 只锁定并迁移语义。
- 当前导入器没有完整的路径包含、长度/数量/总量、重复路径和替换回滚保护；生产 Core 必须满足已确认的数据兼容 spec。
- WSL 没有 Linux-native `dotnet`，但可调用 Windows `C:\Program Files\dotnet\dotnet.exe`；其结果记录为 Windows 进程验证。
- 当前环境按架构约束不安装 MAUI workload，因此旧 MAUI 宿主不宣称实际 build 通过；通过 Core-only 编译、兼容调用测试和静态调用点审查控制风险。

## 目标与完成定义

1. 在内层源码仓库建立单一 `net10.0` Core project 和不包含 MAUI 的测试/solution，Windows CLI 可 restore/build/test。
2. Core 包含数据库、查询/阅读、抓取更新、内容处理、资源、失败重试、维护、数据包和旧数据迁移能力，且无 UI/平台/global-state 引用。
3. 旧 MAUI 项目通过 `ProjectReference` 使用同一份 Core 实现；旧 Razor、Viewer transport 和平台交互保持兼容 façade，不复制 Core。
4. 现有数据库表/字段/种子/FTS5、Search/Read 语义和 `.pkg` 字节格式有自动回归证据。
5. Core 只接收宿主显式传入的数据根；沿用 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db`，不自动复制用户数据。
6. 数据包和旧数据迁移在临时副本/唯一 staging 上验证，失败可恢复，真实数据库和 golden 原件前后 hash 不变。

## 前置条件

- Phase 00 已完成并归档；ADR 0001 已接受 Avalonia 首选方向。
- 用户已批准本 Phase 的完整范围与三项关键选择。
- 真实数据和 golden 只允许显式、只读输入；任何 SQLite 初始化、迁移、导入替换或导出都在临时副本执行。
- NuGet 依赖使用已验证版本，不在本 Phase 升级或替换。

## 范围与非目标

范围：持久化模型、SQLite/FTS5、Search/Read、页面与 Redirect 获取、增量更新、HTML 清理、搜索文本、资源、失败重试、维护、导入导出、旧数据迁移、显式数据路径和旧 MAUI façade。

非目标：Search Fix、Application 用例层、Avalonia Presentation/生产 Viewer Adapter、AI、Windows Runtime/打包、Android、旧 MAUI 退休、其他产品平台。Phase 00 experiment 保持独立 oracle，不改造成生产代码。

## P0 / 兼容性影响

- 表名、字段、SQLite 映射、种子数据、FTS5 表和现有搜索 SQL默认保持不变；任何发现的搜索缺陷只记录给 Phase 02。
- 数据库源文件集合先作 `.db/-wal/-shm` 稳定字节快照；SQLite 只打开临时快照/副本。
- `.pkg` 保持 `WIKIDATA`、little-endian Int32 metadata 长度、camelCase、`mD5`、payload 顺序和 MD5 兼容字段。
- 导入 staging 与目标在同一数据根，完整校验后才交换；目标先改名为备份，失败恢复。
- destructive maintenance 先关闭活动连接并使用可恢复改名；旧 Viewer 在 active import/delete 前由宿主 façade 暂停，完成后恢复。
- Core 不访问 `App.*`、MAUI `Preferences`/`FileSystem`、弹窗、picker、WebView 或目标平台 API。

## 方案与待决问题

已确认方案：

- 生产位置：`terraria-wiki-offline/src/Terraria_Wiki.Core/`；测试位置：`terraria-wiki-offline/tests/Terraria_Wiki.Core.Tests/`。
- 一个 Core assembly，避免提前拆分多个基础 project；旧 MAUI project 通过 `ProjectReference` 消费。
- 移动的旧持久化类型暂保留 `Terraria_Wiki.Models` / `Terraria_Wiki.Services` namespace；新增 contracts 使用 `Terraria_Wiki.Core` 子 namespace。
- Core 直接依赖 `sqlite-net-pcl 1.11.285` 和 `HtmlAgilityPack 1.12.4`；LuYao TLS 通过 `IWikiHttpTransport` 的旧宿主实现保留，不进入 Core 依赖。
- Core 提供显式 options、progress、cancellation、稳定 error code；旧 façade 映射 Preferences、本地化、日志、弹窗和文件选择。
- 数据包默认限额与 Phase 00 oracle 一致：metadata 16 MiB、文件数 100,000、单文件 8 GiB、总 payload 16 GiB、copy buffer 1 MiB、拒绝尾随字节。

无阻塞待决问题。若代码证据要求改变数据库/包语义、依赖版本、Phase 边界或数据根策略，先暂停并更新本计划/ADR。

## 文件变更

- **ADD** Core-only solution、`src/Terraria_Wiki.Core/**` 与 `tests/Terraria_Wiki.Core.Tests/**`。
- **MOVE/MODIFY** 旧持久化模型、`DatabaseService`、纯文件/批处理/HTML/数据迁移逻辑进入 Core；旧路径不保留重复实现。
- **MODIFY** MAUI project reference、composition root、`DataService`/`AppService`/`LegacyUpgradeHandler` 和少量直接调用点，使其成为 façade。
- **KEEP** Razor Presentation、Viewer 静态资产、LocalWebServer transport、平台代码和旧宿主生命周期，除接入 Core 所需的最小调用修改。
- **ADD/MODIFY** Phase 01 plan、ADR 0002；完成后更新 Master Architecture 的已决问题以及 README/project 当前状态。
- **KEEP** Phase 00 experiment 与 golden 原件；只作为独立验证输入。

## 执行步骤

- [x] 建立 plan、ADR、Core/test projects 和 Core-only solution；验证无 workload restore/build 基线。
- [x] 迁移持久化模型与 DatabaseService；把搜索上限改为显式 options，保持 SQL；补数据库 schema/CRUD/FTS5/分页/历史/收藏/资源测试。
- [x] 增加稳定 Search/Read result，覆盖 Redirect、锚点、未找到、取消与数据源不可用；旧调用通过 façade 映射。
- [x] 提取显式 `WikiDataPaths`、operation options/progress/error、HTTP transport 和下载/更新/重试/维护生命周期；用 fake transport 做确定性回归。
- [x] 实现安全 package inspect/import/export、同卷 staging/backup/rollback、旧数据库/WAL 迁移；独立 oracle 与真实 golden 验证保留在最终验收步骤。
- [x] 将旧 MAUI `DataService` 收窄为 UI/platform façade，接入 Core 并移除重复源码/直接依赖。
- [x] 执行静态依赖门、Windows Core-only build/test、真实数据库稳定快照和 golden 包兼容验收；记录实际结果。
- [x] 对抗审查范围、数据安全、回滚和测试缺口；修复后复验。
- [x] 更新 README/project/architecture 当前状态，计划标记 completed；完成变更才归档。

## 验证与回归计划

### WSL / 静态

- `rg` 检查 Core 不含 MAUI、Avalonia、Razor、WebView、`App.*`、`Preferences`、`FileSystem`、平台条件编译。
- 检查 Core public surface 不暴露 picker、弹窗、SQLite 表给未来 transport；检查旧 UI 的直接调用仍只通过兼容 façade/现有数据库服务。
- 检查 Git diff、换行、临时文件、真实数据和包未进入版本控制。

### Windows Core-only

- 使用 Windows `dotnet.exe` restore/build/test Core-only solution；不安装/调用 MAUI/Android workload。
- 数据库 fixture：Manager/Content schema、种子、迁移、WAL、FTS5 trigram、Search、Read、CRUD、backup、分页、资源。
- 同步 fixture：namespace 分页、Redirect、时间比较、HTML/图片/音频、304、失败重试、取消和并发互斥。
- package fixture：正常、恶意路径、重复/冲突、长度/数量/总量、hash、截断、尾随、旧版确认、未知 Wiki、swap/rollback。
- 真实网络 smoke：Windows 临时数据库上最小 MediaWiki 与 LuYao 路径；网络失败记录为明确外部状态，不替代确定性测试。

### 真实兼容输入

- 对显式真实数据库先捕获稳定 `.db/-wal/-shm` 快照；新 Core 仅在临时副本读取/初始化/迁移，源文件集合 hash 不变。
- 新生产 reader 在临时 staging 验证 golden `.pkg`；新 writer 输出由独立 Phase 00 oracle 验证；原件 SHA-256 前后保持 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`。
- 所有 staging、backup 和临时导出在验收后只清理已核对的临时目标。

## 风险与回退

- 大范围移动造成旧 UI 编译风险：保留旧 namespace/类型名和 façade，分切片移动，每片 Core-only 测试通过后才删除原实现。
- 搜索语义漂移：锁定现有 fixture 结果，不在本 Phase调整 SQL/排序；差异回退到迁移前实现并记录给 Phase 02。
- WAL/数据库损坏：不直接对源运行 SQLite；迁移和导入前关闭活动连接，目录交换保留备份，失败恢复。
- 大包磁盘/内存压力：全程流式、显式上限、同卷 staging、提前检查声明大小；不把整个 payload/数据库读入内存。
- Core 过度抽象：只增加已由真实宿主边缘要求的 paths、HTTP、progress/options/error contracts；不建设 Registry、事件总线或 Application。
- 无 MAUI workload：不宣称旧宿主实际 build；若后续必须证明，需在独立已安装环境执行，不改变当前工具链约束。

## 进度记录

- 2026-08-24：用户批准实施已确认的完整 Phase 01 计划；建立 change，状态转为 `in-progress`。
- 2026-08-24：新增 ADR 0002、单一 Core/test projects 与 `Terraria_Wiki.Core.slnx`；Windows `dotnet.exe restore` 和 Release build 通过，0 warnings / 0 errors，未安装或调用 MAUI workload。
- 2026-08-24：完成 SQLite/Search/Read、抓取更新/资源/重试/维护、严格 `.pkg` reader/writer、原子 import/export 与旧数据兼容层；Core-only Release 测试 47/47 通过。
- 2026-08-24：旧 MAUI `DataService` 与 `LegacyUpgradeHandler` 已收窄为宿主 façade，Search/Read 接入 `WikiReader`，删除旧数据库服务与持久化模型重复源码；按用户要求在此小阶段结束后暂停。
- 2026-08-24：首轮对抗审查后补齐 package commit/recovery 状态、完整 P0 schema 检查、共享数据操作门闩、delete/import/旧锚点迁移的可恢复边界、真实数据兼容测试入口；回归增至 61/61。
- 2026-08-24：末轮审查补齐可验证的数据库家族恢复日志、缺失/非法 journal phase 与身份拒绝、孤立 sidecar 安全清理、导出 staging 失败释放门闩、失败列表与旧宿主数据库写入互斥、完整 schema/FTS 结构验证，以及删除操作的不可取消提交边界；最终 Windows Release build 为 0 warnings / 0 errors，测试 74/74。
- 2026-08-24：完成真实 WAL 数据、3.58 GB golden package、生产 Core reader/writer、独立 Phase 00 oracle 与 LuYao 网络 smoke 验收；源文件 hash 前后不变，显式临时副本与 staging 已清理。Phase 01 状态转为 `completed` 并归档；未进入 Phase 02。

## 发现、偏差与决策

- 已确认生产 Core 位于内层源码仓库、采用单 project、旧数据原位复用；详细后果进入 ADR 0002。
- Visual Studio 通用 ignore 规则会误忽略 `src/Terraria_Wiki.Core/Packages/`；已加入精确反向规则，使生产 package 源码可跟踪，而 `.pkg` 数据文件仍不进入内层源码 Git。
- SQLite 数据映射暂保留原 namespace 和 public 类型是有意兼容选择；物理提取不与 API 大规模重命名合并。
- lifecycle、package transfer、旧数据迁移和旧宿主直接数据库写入共享同一个 `WikiDataOperationGate`；导入、锚点迁移和数据库家族改名各使用固定且严格校验的恢复日志。目录改名后的删除提交不可被调用方取消，必须完成或回滚。
- 数据库家族恢复日志记录 version、transaction id、已完成移动数、有序 source/destination、文件长度和 WAL/SHM SHA-256；恢复只接受与 journal phase 相符的已完成前缀。安全锚点迁移拒绝任何 WAL/SHM sidecar，不把主库与不明 sidecar 重新配对。
- 导入和安全锚点迁移在交换前使用同一个完整 P0 schema validator，覆盖 Page、Redirect、History、Favorite、Asset 的对象类型、字段、主键、必需索引，以及真实 FTS5 virtual table、trigram 声明和可执行 `MATCH`，而不是只检查代表性表或 SQL 注释。
- Windows 实际测试从 WSL 调用 `dotnet.exe`；真实输入通过显式环境变量进入 opt-in 测试/runner，普通测试不会发现或打开用户数据。

## 验证结果

### 自动化与静态边界

- Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，0 warnings / 0 errors。
- Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：74/74 通过，0 skipped。
- Core 依赖静态扫描：未发现 MAUI、Avalonia、Razor、WebView、`App.*`、`Preferences`、平台 `FileSystem`、Windows/Android/iOS API、平台条件编译、loopback、Vector 或 Embedding 引用；Core project 仅为 `net10.0`，生产依赖仍是 `HtmlAgilityPack 1.12.4` 与 `sqlite-net-pcl 1.11.285`。
- `git diff --check`：退出码 0；旧工作树文件仅报告既有 CRLF→LF 提示，无 whitespace error。内层源码 Git 未跟踪 `.pkg`、`.db`、`-wal` 或 `-shm` 实物，Core `Packages/*.cs` 可正常跟踪。

### 真实数据库与数据包

- 独立 Phase 00 oracle 读取 `phase-00-inputs/packages/Terraria_Wiki.pkg`：成功；3,584,607,183 bytes、427-byte metadata、3 个 payload 文件、全部 MD5 通过。原件 SHA-256 前后均为 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`。
- 生产 Core reader 对同一 golden package 完整解包并执行最终严格 schema/FTS validator：opt-in 测试 1/1 通过（18 秒）；源 hash 不变，staging 自动清理。
- 真实 `Manager.db/-wal/-shm` 稳定快照与独立 runner：成功；主文件 SHA-256 前后均为 `b333748ca0f827dc81b77d4cd31724612c9089bc1ddb93a3375861e01357bd5d`，源文件集合稳定。
- 真实内容 `Terraria_Wiki.db/-wal/-shm` 稳定快照与独立 runner：成功；主文件 SHA-256 前后均为 `ef5dcc4797c89058bb7302fc67c732ed4992e734e8a1d415e15165be7240b06d`；FTS5 trigram、代表性 Page/Redirect/Asset、临时副本 CRUD 与 backup 均通过。
- 生产 Core 在保留的真实临时副本上完成 legacy database-family 规范化、初始化、查询和 backup：opt-in 测试 1/1 通过；未修改真实源。
- 生产 Core writer 生成 1,393-byte fixture，独立 Phase 00 oracle 验证 camelCase metadata、路径、MD5 和 payload 全部通过；SHA-256 前后均为 `10439955ecb42c099b0f927eae8fcd9cc6037e5b4952a9bd7233a0ce8a90c952`。
- LuYao/MediaWiki Windows 网络 smoke：HTTP 200、1,850 bytes、LuYao `1.2.0.0`，耗时 3,435 ms。
- 验收证据保存在 Phase 00 experiment 的 gitignored `artifacts/phase-01-*.json`；约 11 GB 的真实数据库临时 runner 目录和 Core writer 临时 fixture 已在核对目标后删除，golden 与真实源大小/hash 复核保持不变。

### 未执行的目标平台验证

- 按既定约束未安装 MAUI/Android workload，未 build/run 旧 MAUI 宿主；因此不宣称旧 MAUI、Avalonia 生产客户端、Windows app runtime 或 Android 已通过。Phase 01 的实际平台结论只到 Windows 普通 .NET 10 Core build/test 与真实兼容输入验证。

## 完成摘要

Phase 01 已完成：内层源码仓库现在只有一份可移植 `net10.0` Wiki Core，覆盖 SQLite/FTS5、Search/Read、抓取更新、内容/资源、失败重试、维护、严格 package transfer 和旧数据兼容；旧 MAUI 通过 façade 和同一 Core 复用能力。数据根继续原位沿用，破坏性路径具备互斥、校验、同卷 staging/backup 和恢复日志。自动化、真实 WAL 数据、golden package、独立 writer/oracle 与网络 smoke 均给出实际通过证据且未改变真实源。

本 Phase 不包含 Search Fix、Application、Avalonia 生产宿主、AI、Windows Runtime 或 Android；这些后续阶段均未开始。按用户要求，Phase 01 归档后立即停止。
