# Phase 04 — Windows Client Foundation

- 状态：completed
- Phase：04 — Windows Client Foundation
- 执行环境：WSL Codex CLI 负责阅读、编辑、静态检查和计划维护；Windows 11 x64 `dotnet.exe` 负责 restore/build/test/run/publish；不安装 MAUI/Android workload
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：正式 Avalonia Windows WinExe、Windows host adapters、Local Viewer transport/bridge、最小搜索阅读壳、独立测试和持续契约
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/specs/application-use-cases/spec.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/local-wiki-viewer/spec.md`；`docs/adr/0001-use-avalonia-client.md`；`docs/adr/0002-portable-core-and-data-root.md`；`docs/adr/0003-use-loopback-local-viewer-transport.md`

## 背景与当前证据

用户已明确要求按已提供的 Phase 04 计划实施。实施前外层 HEAD 为 `89bc28b`，内层 HEAD 为 `6d619b3`；内层工作树干净，Core 89/89 与 Application 54/54 是 Phase 03 已记录基线。外层存在 `.codex/`、experiments、Phase 00 inputs 和若干治理归档等未跟踪内容，本 Phase 不修改、不暂存它们。

真实代码证据：

- `WikiApplication` 构造只需要绝对 data root、`IApplicationSettingsStore`、`IWikiDataConsumerCoordinator`、Core `IWikiHttpTransport` 与可选 `TimeProvider`；公开五组用例，不需要修改公共契约。
- `ActiveWiki.InitializeAsync` 在打开 Manager 后恢复 import、选择活动 Wiki，并在消费者 pause lease 内切换 ContentDb；`Query.SearchAsync`、`ReadAsync`、`ReadAssetAsync` 和 `Library.RecordVisitAsync` 足以承载本 Phase 搜索与 Viewer。
- 旧 `LocalWebService` 固定 55000、允许宽松 CORS、直接访问活动数据库和 MAUI package API，不能直接进入新宿主；Phase 00 已验证的 `TcpListener` 动态 `127.0.0.1:0`、Host/GET/HEAD/路径限制和 NativeWebView 消息链路可作为局部生产化起点。
- 旧 Viewer 页面位于 `Terraria_Wiki/Resources/Raw/Web/**`，bridge 位于 `Terraria_Wiki/wwwroot/js/iframe-bridge.js`；正式项目可在 build/publish 时从这些唯一资产直接复制，不维护第二份 Viewer 内容。
- Phase 00 已在 Windows 11 x64 锁定 Avalonia 12.1.1 与 `Avalonia.Controls.WebView` 12.0.1，并证明需要 Windows supportedOS manifest；默认依赖系统 Evergreen WebView2。

## 目标与完成定义

1. 内层新增单一 `src/Terraria_Wiki.Windows` WinExe 与 `tests/Terraria_Wiki.Windows.Tests`，加入 portable solution；不修改 Core/Application 公共 API、旧 MAUI project、schema、搜索或包格式。
2. Windows host 以 JSON 原子保存 Application 设置、活动 Wiki ID 和已确认 data root；data root 严格按 CLI、有效保存值、文件夹选择器顺序解析，拒绝缺少 `Manager.db`、不可访问、相对或取消路径。
3. 命名单实例 lease、按规范化 data root 派生的 lease 和旧 `Terraria_Wiki` 进程检测在挂载前阻止并发；所有失败可在启动错误态重试，退出释放。
4. 唯一 `WikiApplication` 使用两个 Windows ports 与 `HttpClientWikiHttpTransport` 组合；最小壳完成初始化、搜索取消、结果选择、页面打开和明确错误/取消状态。
5. 生产 Viewer 以动态 loopback 只提供必要静态文件和 `Query.ReadAssetAsync` 资源；bridge 只用 `Query.ReadAsync`/`Library.RecordVisitAsync`，处理 Redirect/anchor、临时返回、主题和受限 HTTP(S) 外链，未知/未支持方法返回可观察错误。
6. Windows restore、Release build、143 个既有测试和新增 Windows tests 通过；framework-dependent/self-contained `win-x64` publish 成功；实际 run/smoke、资源链路和输入 hash 结果如实记录。
7. Windows 实测确认后新增 Viewer transport ADR、更新 Local Viewer spec、README、Master Architecture 与项目基线，完成并归档计划；内外层分别显式提交且不 push。

## 前置条件

- [x] Phase 00、03 已完成并归档；Avalonia/NativeWebView 和 Application 边界已有实际证据。
- [x] 用户明确授权实施 Phase 04。
- [x] 已确认内外层恢复基线和 Git 状态；内层无用户未提交改动。
- 真实生产数据库与 golden 原件只读；任何启动/写入 smoke 只使用 Windows 临时副本或合成 data root。

## 范围与非目标

范围：Windows WinExe、内部 Windows settings/data-root/instance/process adapters、Application 组合、最小 Avalonia 搜索阅读壳、NativeWebView loopback transport/bridge、相关测试、build/publish 资产复制、Viewer ADR/spec 和 Phase 状态文档。

非目标：AI、Phase 08 正式视觉/导航、Phase 09 历史/收藏/数据管理/设置 UI、新标签页、图片剪贴板与完整右键菜单、安装器、签名、捆绑 WebView2、Windows 10/ARM64、Android、Core/Application 公共契约或旧 MAUI 修改。

## P0 / 兼容性影响

- 数据原位使用，host 不创建空 data root、不复制数据库；测试和 smoke 不写生产数据或 golden 原件。
- Viewer 页面、CSS/JS 与公共资源从旧项目唯一来源复制；数据库图片/音频只经 Application asset DTO。
- Search/Read/Redirect 语义完全沿用 Application/Core；页面成功读取后由 Presentation 显式记录访问。
- 单实例、data-root lease、旧进程检测和 Viewer pause 在共享数据源切换前保护旧/新宿主不并发挂载。
- 不改变 SQLite、FTS5、数据库表、数据包字节、抓取、同步或旧宿主行为。

## 方案与待决问题

- settings 使用同目录 temp + replace/backup；主文件损坏时仅从有效 backup 恢复，取消和异常不生成部分 JSON。
- data-root lease 使用从规范化绝对路径 SHA-256 派生的 Windows named mutex，避免在生产 data root 写锁文件；进程检测对任意旧 `Terraria_Wiki` 进程保守阻止挂载。
- Viewer 基于 Phase 00 `TcpListener` 实现局部生产化，Host 必须精确匹配实际 `127.0.0.1:<port>`，无 CORS；静态路径限定在复制输出根，`/src/*` 委托 Application。
- outer host page 精确校验同 origin 与指定 iframe `contentWindow` 后才向 NativeWebView 转发；C# 再校验 source、origin、`req/res` 和非空 request ID。
- WebView2 缺失由 NativeWebView 初始化/导航错误转为可重试状态；不在本 Phase 下载或捆绑 Runtime。
- Windows 实测已确认正式壳、两种 publish、NativeWebView bridge 与 Viewer 资源；Android、Windows 10/ARM64、WebView2 安装/bootstrap 仍留在计划外对应 Phase。

## 文件变更

- **ADD** `terraria-wiki-offline/src/Terraria_Wiki.Windows/**`：WinExe、settings/data-root/locking/process adapters、Viewer transport/bridge/coordinator、Avalonia app/window、manifest 与 outer host asset。
- **ADD** `terraria-wiki-offline/tests/Terraria_Wiki.Windows.Tests/**`：host settings、data-root、locks/process、Viewer transport/coordinator/bridge、边界和组合 smoke tests。
- **MODIFY** `terraria-wiki-offline/Terraria_Wiki.Core.slnx`：加入正式客户端和 tests。
- **KEEP** Core、Application、旧 `Terraria_Wiki/**` 源文件和 project；旧 Viewer 资产仅作为 build/publish 输入。
- **ADD** `docs/adr/0003-use-loopback-local-viewer-transport.md`。
- **MODIFY** `openspec/specs/local-wiki-viewer/spec.md`、`README.md`、`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`。

## 执行步骤

- [x] 建立 Windows/test projects、settings/data-root/single-instance/data-root lease/process detection；以定向 tests 验证原子恢复、优先级、拒绝、取消、冲突与释放。
- [x] 实现 Application 组合和 pause coordinator；验证初始化失败、pause/resume、退出顺序与边界扫描。
- [x] 实现 loopback transport、Application asset source 和严格 outer host；验证动态端口、重复启停、Host/方法/路径/MIME/404/500/HEAD/无 CORS/清理。
- [x] 实现 bridge 与 NativeWebView session；验证 origin/source/type/id、Read/Redirect/anchor、history/back/theme、HTTP(S) 外链、未知/未支持方法和取消。
- [x] 实现最小 Avalonia shell、搜索取消、选择打开、启动/错误/重试/WebView2 状态和退出清理。
- [x] 执行 Windows restore/build/full test、run 和 framework-dependent/self-contained publish smoke；检查静态资产、临时文件与输入 hash。
- [x] 根据实测补 ADR/spec 和状态文档，完成 Git diff 审查；内层已提交，外层治理记录随本计划归档后提交，不 push。

## 验证与回归计划

- WSL：`rg` 静态边界、`git diff --check`、资产路径和实际 diff/生成物检查；不宣称 Windows build/run。
- Windows 11 x64：portable solution restore、Release build、全量 test；正式 WinExe 实际启动；两种 `win-x64` publish 和产物 smoke。
- 自动化：保留 Core 89/89、Application 54/54；新增 Windows tests 覆盖 settings/data-root/locks/process、transport/coordinator/bridge、边界与组合。
- smoke：合成或 golden 解出的临时 data root，验证搜索、页面、CSS/JS、图片、音频、内部链接、返回和退出；原件前后 SHA-256 不变。
- Android、Windows 10/ARM64：不在本 Phase 验证矩阵，不作可用声明。

## 风险与回退

- 数据并发：新/旧进程和同 root lease 在 Application 初始化前检查；异常退出由 named mutex 进程所有权自动释放。
- settings 损坏：同卷替换与 backup 恢复；无法恢复时保留明确启动错误，不猜测 data root。
- loopback/bridge：严格 origin/Host/path/method；stop 后不接收请求，pause 释放只在宿主存活时恢复。
- NativeWebView/WebView2：沿用已验证版本与 manifest；缺失 Runtime 可观察，不下载依赖。
- 任何实现若要求改 Core/Application 公共契约或旧宿主，则停止并重新规划；代码可回退至内层 `6d619b3`，治理可回退至外层 `89bc28b`。

## 进度记录

- 2026-08-24：用户提供已批准实施计划并要求在新上下文完成 Phase 04；按 `INIT.md` 重读入口、架构、项目基线、全部相关 specs/ADR、Phase 00/03 记录和真实 Application/Viewer/PoC 代码。
- 2026-08-24：确认外层/内层恢复基线分别为 `89bc28b`/`6d619b3`，内层干净，外层无关未跟踪内容保持排除；计划转为 `in-progress`。
- 2026-08-24：新增正式 Windows/test projects、host settings、data-root resolver、命名单实例/data-root leases、旧进程检测、Application 组合和 13 个第一批 host tests；Windows 定向 tests 通过。
- 2026-08-24：新增动态 loopback、Application asset source、pause coordinator、严格 outer host、bridge/session 和最小 Avalonia 搜索阅读壳；Windows tests 增至 28/28。
- 2026-08-24：首次正式 run 暴露 NativeWebView script 必须回到 Avalonia UI thread；统一封装 UI-thread 调度后，正式 build 和两种 publish 的 smoke 全部通过。
- 2026-08-24：Windows restore/build、Core 89/89、Application 54/54、Windows 28/28、三种 WinExe smoke 与 golden hash 复核通过；新增 ADR 0003、更新 Viewer spec 和项目状态。
- 2026-08-24：内层 Phase 04 代码提交为 `601063d`，未 push；计划完成并归档，外层治理记录随后独立提交。

## 发现、偏差与决策

- Phase 00 的 transport 可复用算法和测试证据，但正式实现必须去除直接 SQLite source，改为 Application `ReadAssetAsync`，因此不直接引用 outer experiment project。
- 旧 `iframe-bridge.js` 使用 `postMessage('*')`；正式 outer host 必须把它限制在同 origin 且精确 iframe source，C# 端进行第二次协议校验。旧资产本身保持不改。
- Application 初始化本身会获取 pause lease；coordinator 通过延后 enable 和活动 Wiki provider 避免初始化期间提前启动 Viewer，并能在未来切换释放 lease 后按当前 Wiki 重启。
- 采用 named mutex 而不是 data-root 锁文件，避免在生产数据目录增加宿主文件；data-root 名称由规范化绝对路径 SHA-256 派生。
- `Manager.db` 除存在/可读外还检查 SQLite header 与最低文件长度，避免空白占位文件被 Core 当作新 data root 初始化。
- 首次 Windows run 的 iframe 请求能到达 C#，但 background continuation 上调用 NativeWebView 导致响应超时；所有 script 调用改由 Avalonia UI dispatcher 执行后，build 与两个 publish smoke 均通过。这是 Phase 00 characterization 进入正式异步 Application 链路后出现的实现偏差，不改变 transport 决策。
- `Chrome_WidgetWin_0` unregister 1412 在每次自动关窗后仍输出一次 Chromium 诊断；与 Phase 00 已知现象一致，进程退出码、smoke 和 listener/Application 清理均成功，不计为产品操作失败。
- diagnostic `--host-settings` / `--smoke-log` 只用于隔离可复现 Windows smoke；正常 data-root 解析仍严格遵循 `--data-root`、有效保存值、文件夹选择器。

## 验证结果

- Windows `dotnet.exe restore Terraria_Wiki.Core.slnx --nologo`：通过，6 个项目均最新。
- Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，0 warnings / 0 errors。
- Windows 全量 `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：Core 89/89、Application 54/54、Windows 28/28，共 171/171，0 skipped。
- Windows tests 覆盖 JSON 原子保存/backup 恢复/取消、data-root CLI/保存值/picker 优先级与拒绝、selection cancel、named mutex 冲突/释放、旧进程拒绝、严格项目边界、Viewer 动态端口/重复启停/冲突/Host/GET/HEAD/路径/MIME/404/500/无 CORS、pause/resume/host-dead、bridge origin/source/type/id/重复 ID、Redirect/anchor/Read/asset/visit/history/back/theme、HTTP(S) 外链、未知/未支持方法和取消。
- Windows 正式 Release build WinExe smoke：通过；Search 1、站点 JS 1、CSS 1、数据库图片 1、音频 3、临时 history 返回后 0、退出清理 true。
- Windows framework-dependent `win-x64` publish：通过，干净输出约 158 MB；发布物执行同一 smoke 并通过，Viewer 资产存在。
- Windows self-contained `win-x64` publish：通过，干净输出约 235 MB；发布物执行同一 smoke 并通过，Viewer 资产存在。两者仍使用系统 Evergreen WebView2。
- smoke data root 是 Windows 临时合成库，不是生产数据库；`phase-00-inputs/packages/Terraria_Wiki.pkg` 只读 SHA-256 复核为 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`，与既有基线一致。
- WSL `rg` 边界扫描和内层 `git diff --cached --check` 通过；项目只引用 Application，只有组合根使用 Core HTTP transport，无 MAUI/Razor/SQLite API/持久化 service 或全局 `App.*` 访问。
- 未验证：Windows 10、ARM64、Android、WebView2 缺失机器的 Phase 04 新实测、安装器/bootstrap、签名和发布渠道；其中 WebView2 缺失路径已有 Phase 00 实测，产品化仍留 Phase 11。

## 完成摘要

Phase 04 已完成。内层新增正式 Avalonia Windows Foundation，以原子 host settings、显式 data-root 选择、单实例/data-root leases 和旧进程检测安全组合唯一 `WikiApplication`；最小壳可搜索并按原始标题打开 Viewer。生产 Viewer 复用旧站点资产，通过受限动态 loopback 和严格双层 bridge 校验只调用 Application Read/Asset/Library，支持页面、资源、Redirect/anchor、临时返回、主题和 HTTP(S) 外链，并对未支持功能返回错误。

Windows restore、0-warning Release build、171/171 tests、正式 run 与两种 `win-x64` publish smoke 均通过。Core、Application 公共 API、旧 MAUI、SQLite/schema、搜索、包格式和生产数据均未修改；内层提交为 `601063d`，未 push。当前交付仍是 Phase 04 最小壳，不包含 AI、正式 Presentation/P0 管理 UI、安装器或 Android。
