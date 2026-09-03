# Phase 11 — Windows Productization

- 状态：in-progress；用户已于 2026-09-04 明确授权开始实施，公开 GitHub 仓库地址、首发版本和软件许可证范围在对应发布步骤前关闭
- Phase：11 — Windows Productization
- 执行环境：WSL Codex CLI 负责阅读、编辑、计划维护和平台无关检查；Windows 11 x64 负责 .NET/Avalonia、内置 Python、安装器、portable、升级、修复、卸载和目标平台验证；Windows 10 22H2 x64 仅执行低成本兼容性门槛
- 创建日期：2026-09-03
- 最后更新：2026-09-04
- 范围：Windows 错误恢复、内置 Python Runtime、Credential Manager、Evergreen WebView2 前置处理、版本化 self-contained 发布、普通 Windows 安装程序、portable zip、GitHub Releases 更新与发布准备、发布诊断和许可告知
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`docs/adr/0006-use-stdio-python-sidecar.md`、`assets/windows/README.md`

## 背景与当前证据

### 已确认事实

- Phase 00–10 已完成；Phase 07 和 Phase 10 是本阶段的正式前置。当前 Windows 11 x64 基线为 Release build 0 warning / 0 error、275/275 .NET tests，以及 framework-dependent / self-contained 两种 `win-x64` publish smoke 通过。
- `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj` 当前固定 `net10.0-windows10.0.17763.0` 与 `win-x64`，复制 Viewer runtime、host/bridge 和 AgentRuntime 源文件，但没有产品版本、publish profile、安装包、升级、发布脚本或渠道配置。
- 当前仓库没有 Git remote、GitHub release workflow、release tag 或已经确定的公开仓库地址。应用内更新源和外部发布操作不能凭空填写或在本计划阶段执行。
- self-contained publish 约 267 MiB，可在没有 .NET SDK/runtime 的 Windows 环境启动；它仍依赖系统 Evergreen WebView2。`WebView2RuntimeProbe` 缺失时在 Application 初始化前失败，现有 Shell 只有通用重试和选择数据操作。
- AI Runtime 当前使用用户指定的外部 `python.exe`。发布物只复制 launcher、Agent 源码、`pyproject.toml` 与 `uv.lock`；设置页仍要求用户选择已安装依赖的 Python 3.12–3.14。
- `ai-runtime.json` v2 当前明文保存 `pythonExecutable`、`apiKey` 与 provider。Host 只把 key 注入所属 Sidecar 环境且不会写入协议/日志，但磁盘 secret 尚未进入 Windows Credential Manager。
- stdio NDJSON v1、单活动问题、取消/完成竞态、故障隔离、WikiTools 动态 loopback 与唯一 Viewer 生命周期已有自动化和真实 Windows 证据。本阶段替换 Runtime 部署、secret 来源与外层发布机制，不重写 Agent、协议或 Wiki Tool transport。
- host settings、Chat 收藏和 Wiki 数据都位于产品二进制之外；Wiki data root 仍是用户选择的 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db`。安装、升级、修复和卸载不得迁移或删除这些数据。
- 当前应用自己的默认配置目录是 `%LocalAppData%\Terraria AI Wiki`。若普通安装器也使用同一目录作为安装根，修复或卸载可能删除配置，因此安装器内部 ID/安装根必须采用不同且稳定的名称。
- `assets/windows/README.md` 记录了受控资产来源和许可风险：上游没有独立 `LICENSE`；官方 Terraria Wiki 可许可的文本为 CC BY-NC-SA 4.0，图像和音频应按各文件页面判断；现有快照还含 Calamity Wiki CC BY-NC-SA 3.0 提示和 Viewer.js MIT 标头。
- 当前 tracked 工作树没有用户已有修改；现存未跟踪治理归档、`.codex/`、历史架构和 `experiments/` 不属于本阶段，不得删除、暂存或夹带。

### 用户于 2026-09-03/04 确认的产品决策

- 应用二进制、版本发布和后续应用更新只通过 GitHub Releases 分发，不建设其他应用发布渠道。
- 优先交付由 self-contained `win-x64` payload 生成的普通 Windows `Setup.exe`，并同时提供 portable zip。Phase 11 不实施 MSIX；Microsoft Store、Store package identity、Store publisher 和 Store 更新工作已从范围删除。
- 首发正式支持 Windows 11 x64。Windows 10 x64 只有在现有依赖兼容、无需独立代码路径且完整验证成本很低时，才增加兼容声明；ARM64/x86 不在范围。
- 暂不配置商业代码签名证书。首发制品按未签名发布，必须如实说明 SmartScreen、Smart App Control 和企业策略可能警告或阻止运行；校验和不能替代 Authenticode。
- 保持 Evergreen WebView2、内置 CPython Runtime 和 Windows Credential Manager，并保留安装、升级、修复、卸载与 P0 回归测试。
- 产品意图是免费、公开源码、非商业，并遵守官方 Terraria Wiki 内容的 CC BY-NC-SA 4.0 署名、非商业和相同方式共享要求。

### 2026-09-03/04 核对的外部权威证据

- [.NET 发布文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/)区分 framework-dependent 与 self-contained；后者携带目标 Runtime，适合没有预装 .NET 的终端。
- [Velopack Packaging Overview](https://docs.velopack.io/packaging/overview)可从现有 publish 目录生成 Windows `Setup.exe`、portable zip、full/delta update package 与 release index；[GitHub update source](https://docs.velopack.io/integrating/update-sources)可直接读取公开仓库的 GitHub Releases，但无 token 访问受 GitHub 匿名速率限制。
- [Velopack Windows 说明](https://docs.velopack.io/packaging/operating-systems/windows)的默认 `Setup.exe` 是无需提升权限的每用户安装，安装到 `%LocalAppData%\{packId}`，更新时整体替换 `current` 目录；持久数据必须放在安装根之外。
- [Velopack bootstrap 文档](https://docs.velopack.io/packaging/bootstrapping)明确支持 `webview2` 前置，并在安装与新增依赖的更新前检查/安装它；self-contained .NET payload 不应重复声明 .NET Runtime 前置。
- [Velopack GitHub Actions 文档](https://docs.velopack.io/distributing/github-actions)提供 `download → pack → upload github` 流程；CLI 与应用库应固定同一版本，上传 release 需要 `contents: write`，公开仓库客户端读取不需要内置 token。
- [GitHub Releases 文档](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)允许为 tag 附加二进制资产；单个资产须小于 2 GiB。当前 payload 体积在该上限内，但实际安装器、portable 与 update package 大小仍需构建记录。
- [WebView2 分发文档](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)建议优先使用自动安全更新的 Evergreen Runtime；应用 Runtime 本身仍由 Microsoft 官方分发，不属于项目的应用版本渠道。
- [Python Windows 文档](https://docs.python.org/3/using/windows.html#the-embeddable-package)说明 embeddable package 用于被另一应用携带的隔离 Runtime；它不带 pip，第三方依赖必须在构建时准备，不能在用户启动时临时安装。
- Windows `CredWriteW` / `CredReadW` 为当前用户提供 generic Credential Manager 存储；credential blob 上限为 2560 bytes，读取后的原生 buffer 必须用 `CredFree` 释放。当前 4096 字符的 key 上限必须改为按实际编码后的 byte length 验证。
- [Microsoft SmartScreen 说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)指出未签名文件每个新版本都从零积累文件信誉，用户可能看到“Windows 已保护你的电脑”，企业策略或 Windows 11 Smart App Control 还可能直接阻止运行。
- [Windows 10 release information](https://learn.microsoft.com/en-us/windows/release-health/release-information)确认普通 Windows 10 22H2 已于 2025-10-14 结束支持；因此即使兼容测试通过，也只能给出有边界的 best-effort 兼容声明，不能把它与 Windows 11 正式支持等同。
- [官方 Terraria Wiki 版权页](https://terraria.wiki.gg/wiki/Terraria_Wiki%3ACopyrights)只把其可依法许可的文本置于 CC BY-NC-SA 4.0，图像和音频按各文件页面确定；[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)还要求适当署名、许可证链接、修改说明、非商业和相同方式共享。
- [Creative Commons FAQ](https://creativecommons.org/faq/)不建议将 CC 许可证直接用于软件；[OSI Open Source Definition](https://opensource.org/osd)也不把限制商业用途的许可证认定为开源。因此公开发布前必须明确区分“公开源码、免费、非商业”的产品描述、软件源码许可证和 Wiki/第三方内容许可证。

### 方案假设与 PoC 门槛

- 正式 payload 保持 self-contained、非 single-file、非 trimmed、非 AOT 的 `win-x64` 目录形态。当前 Avalonia、NativeWebView、ASP.NET shared framework、SQLite native provider、Python native wheels和外部 Viewer/Agent 文件没有裁剪/AOT证据。
- 优先采用单一 Velopack 依赖同时承担普通安装、portable 和 GitHub Releases 更新，避免并行维护安装器与自定义 updater。只有 PoC 证明其无法满足子进程清理、修复、portable 更新或数据保留时，才停止并由用户重新选择技术。
- Velopack 的 stable channel 暂定为 `win-x64-stable`；最终内部 `packId` 必须稳定且不得等于 `Terraria AI Wiki` 配置目录名。它只是普通安装器内部 ID，不是 Store package identity。
- 内置一个精确 patch 版本和 SHA-256 的 CPython x64 embeddable runtime，只展开 `uv.lock` 的生产依赖。具体 patch 版本在实施时按官方可用制品与真实 smoke 固定，不写成浮动 `latest`。
- 安装版与 portable 都从公开 GitHub Releases 检查 stable 更新，不在客户端内置 GitHub token；draft 和 prerelease 默认不可见。首版只提供用户显式触发的检查、下载和重启，不做静默后台或强制更新。
- 普通安装器的“修复”定义为重新运行相同或更新版本的官方 `Setup.exe`，恢复完整受控 payload 后重新验证；该语义、用户数据保留和损坏文件恢复必须先用两版本 PoC 证明。

## 目标与完成定义

1. 从唯一版本化 self-contained `win-x64` payload 生成普通每用户 `Setup.exe`、portable zip、stable update package/index、SHA-256 和发布清单；终端不需要 .NET SDK、外部 Python、uv/pip 或仓库源码。
2. `Setup.exe` 完成非管理员安装、快捷方式/卸载注册、首次启动、覆盖安装式修复和干净卸载；portable 解压即可运行且不注册安装项。两种形态使用同一产品二进制。
3. 安装版与 portable 的显式“检查更新”只读取已配置公开 GitHub 仓库的正式 Releases；显示新版本和安全摘要，用户确认后下载、应用并重启。网络、限流、损坏资产或取消只影响更新，不影响当前版本和离线 P0。
4. 应用携带精确锁定、可追溯且带 hash/许可清单的 CPython embeddable runtime 与冻结生产依赖；Sidecar 继续使用 NDJSON v1、现有 Agent 源码和同一个 WikiTools/Application 边界。
5. 正常产品配置不保存 Python 路径或明文 API key。API key 进入当前 Windows 用户的 generic Credential Manager；`ai-runtime.json` 升级为严格 v3，只保存非 secret provider 配置。
6. v2 → v3 首次迁移可重试且不丢 key：只有 Credential Manager 写入与 v3 durable replace 成功后，明文才从主配置消失；失败时保留可恢复的旧配置、当前离线能力和安全页内状态。
7. Evergreen WebView2 是安装前置：`Setup.exe` 通过官方 bootstrap 检查/安装，Host probe 继续作为修复兜底。portable 在 Runtime 缺失时提供明确的 Microsoft 官方获取/修复动作，不捆绑 Fixed Version Chromium。
8. 版本、Git tag、commit、公开仓库 URL、installer ID、制品命名、update channel、SHA-256、Runtime provenance、第三方 notices、内容署名、隐私/数据发送说明和发布 runbook 有单一事实源。
9. 设置页不再要求普通用户选择 Python；在既有“常规设置 / AI 配置”中显示内置 Runtime、凭据和更新状态，并在既有区域提供版本、检查更新与安全诊断。不得增加第六个全局导航或阻塞式成功弹窗。
10. 增加有界、隐私安全的本地诊断，只记录版本、稳定事件/错误 code、Runtime/update 状态和耗时；不记录问题/回答、Wiki HTML、API key、bearer、provider 原始响应、完整用户路径或数据库内容。
11. 干净 Windows 11 x64 环境完成安装、首次启动、无外部 Runtime smoke、内置 Python AI smoke、更新、修复、卸载、重装和 portable 回归；全过程保留 host settings、Chat 收藏、Credential Manager secret 与外部 Wiki data root。
12. 发布物如实标记为 unsigned，安装说明展示 GitHub tag、SHA-256 校验和与预期 Windows 警告；不能把 hash、GitHub 来源或开源说明描述成代码签名。
13. 发布说明正确区分项目自有源码、官方 Terraria Wiki 文本、逐文件媒体、Calamity 内容、Viewer.js 和所有 .NET/Python 第三方组件的许可与署名；没有权利证据的资产不得被宣称为已清权。
14. Windows Release build/tests、Python frozen 回归、P0 Viewer/Search/Read/数据管理 smoke、安装/更新/修复/卸载测试、目标窗口验收和发布审计均有实际证据后，Phase 11 才可完成。

## 前置条件

- [x] Phase 00–10 已完成，Phase 07 Runtime 与 Phase 10 独立产品源码基线可回归。
- [x] 发行渠道、首要制品、Windows 11 x64、暂不商业签名、Evergreen WebView2、内置 Python、Credential Manager 和非商业方向已由用户确认。
- [x] 用户已于 2026-09-04 批准本计划并授权开始实施；该授权不包含外部 GitHub 发布、付费 provider 请求或其他未列明外部写入。
- [ ] 用户提供并确认公开 GitHub 仓库的 `owner/repo` 和 remote；公开仓库是无内置 token 更新的必要条件。当前本地仓库没有 remote。
- [ ] 用户确认首个版本号及 tag 规则，默认候选为 SemVer 2.0 与 `v<version>`；现有发布资产不得被重写为不同内容。
- [ ] 用户确认软件源码许可证的精确文本和适用范围。若坚持把项目自有代码置于 CC BY-NC-SA 4.0，须明确接受 CC 不建议用于软件、非 OSI 开源及与第三方软件许可证分离的后果；本计划不代替权利人作不可撤销授权。
- [ ] 逐项核对 Viewer/Wiki/游戏素材、Calamity 内容、项目图标和第三方生产依赖的实际许可/署名；用户的非商业意图不能替代来源方授权。
- [ ] Windows 构建机具备 .NET 10 SDK 和经版本/hash 固定的 Velopack、CPython、Python wheels 与 WebView2 bootstrap 输入。安装终端不得需要开发工具。
- [ ] 实施开始前重新检查 tracked/untracked 状态；触碰核心 Runtime/配置前建立只包含已批准范围的可恢复 Git 保存点。
- [ ] 任何真实 provider 请求和 GitHub draft/prerelease/stable 上传均在执行前取得对应明确授权；GitHub token 只由受控本机环境或 Actions `GITHUB_TOKEN` 提供，不写入仓库、参数日志或客户端。

## 范围与非目标

### 范围内

- Windows `win-x64` self-contained 产品 payload、普通每用户安装器、portable zip、单一 Velopack stable channel、GitHub Releases 检查/下载/更新和安装/修复/卸载验证。
- Windows 内置 CPython embeddable runtime、冻结生产依赖、安装目录零写入策略、Runtime provenance/hash/license 与 smoke。
- Windows Credential Manager adapter、严格 v3 配置、v2 安全迁移、设置/Chat 状态和 secret redaction 回归。
- Evergreen WebView2 的安装前置、portable 缺失恢复、现有 Runtime probe 与实际官方链接。
- 与产品化直接相关的页内恢复、版本、更新与诊断状态；遵循 `terraria-ui` Skill 和现有 Fluent Terraria token/style。
- 安全本地诊断、release manifest/checksum/notices/runbook、GitHub release workflow，以及实际完成后必要的 specs/ADR/README/项目状态同步。

### 非目标

- Microsoft Store、Store package identity、Store publisher、Store listing/审核/更新、MSIX、App Installer 和其他应用发布渠道；Phase 11 不保留相关 PoC、清单、签名或验证工作。
- 商业代码签名证书、开发自签名公开分发、签名服务接入或把校验和冒充签名。未来签名属于单独授权的后续变更。
- 同时维护 Velopack 之外的 Inno Setup/WiX/MSI、自定义 updater、后台常驻更新服务、静默强制更新或额外 CDN/网站 feed。
- Android、ARM64、x86、Linux/macOS/iOS/Web 或其他产品平台；不得安装 MAUI/Android workload、SDK、JDK 或 Emulator。
- Phase 12 Toolset、多轮/自动 Chat 历史、云账户/同步、provider 在线发现、API key 联网校验、Vector/Embedding、Reader/HTML 精简或 AI 质量重做。
- SQLite schema、FTS5、Wiki 抓取/更新算法、`.pkg` 格式、NDJSON v1、Wiki Tool JSON、Viewer 内容协议或 Application/Core 公共 API 变化。
- 为缩小体积启用 trimming、NativeAOT、ReadyToRun 或 single-file；不借产品化批量升级现有业务依赖。
- 自动删除 Wiki data root、host settings、Chat 收藏或 Credential Manager secret。卸载后的保留与手动清理方式必须文档化。
- Windows 10 LTSC/ESU 的长期支持承诺。可选门槛只评估普通 Windows 10 22H2 x64 的 best-effort 兼容性。

## P0 / 兼容性影响

- 安装、更新、修复和卸载只操作应用 payload、安装器元数据和快捷方式，不枚举、复制、搬动或删除保存的数据根。数据库写入继续只来自现有 Application/Core 用例。
- 安装器 `packId` 与安装根不得等于或包含现有 `%LocalAppData%\Terraria AI Wiki` 用户配置根；更新/repair/uninstall 测试必须用 canary 文件证明 settings、favorites 和 AI 配置未被删除。
- Velopack 更新会整体替换 `current`，因此 Viewer、Agent、CPython、.NET 与 native DLL 必须作为一个版本原子交付；设置、诊断和下载缓存不得写入 `current`。
- 更新/修复前必须通过现有生命周期有界关闭 Viewer、WikiTools 和所属 Python 子进程。不得按进程名终止其他 Python，也不得遗留 loopback listener 或文件锁。
- 内置 Runtime 只替代外部 Python 部署，不改变 Agent、PydanticAI 2.31、Search → Read → Answer、来源/usage/error 或 NDJSON v1。用 `-B -I -u -X utf8` 或等价策略证明安装目录没有 `.pyc`、cache、日志或依赖自更新。
- Credential Manager 使用独立、稳定且不依赖安装目录的 target name；不把 provider URL、key 或用户名写进 target。native credential buffer 必须释放，公开错误只使用稳定 code。
- v3 降级到 Phase 10 二进制时，旧客户端不能读取 Credential Manager。可接受回退边界是 P0 离线 Wiki 可用、AI 要求重新配置；不得为了无缝降级保留明文 secret 副本。
- WebView2 bootstrap 或修复失败不得修改 Wiki 数据。AI、凭据或内置 Python 故障仍只禁用 AI；Viewer 缺失进入可操作恢复状态，数据管理和设置保持可达。
- portable 默认从用户可写目录运行并使用安装目录之外的现有数据/配置；从只读介质运行时不得承诺原位更新，必须安全失败并引导用户下载新 portable。
- GitHub 检查失败、匿名限流、release 缺少 index、hash 不匹配、断网或用户取消都保留当前版本；不得在校验完成前替换应用目录。
- 未签名制品在部分 Windows 11 或企业设备上可能无法运行。这是用户已接受的首发限制，必须进入支持矩阵、安装说明和实际验证记录，不能通过关闭安全功能规避测试。

## 方案与待决问题

### 推荐的单一产品化底座

1. **发布 payload**：新增唯一 `win-x64` self-contained Release profile；显式关闭 trim/AOT/single-file，固定版本输入。framework-dependent 只留开发回归，不作为正式制品输入。
2. **普通安装器与 portable**：固定同一版本的 Velopack C# library 和 `vpk` CLI，从同一个 publish 目录生成 `Setup.exe`、portable zip、full package、可选 delta 与 `releases.win-x64-stable.json`。不生成 MSI/MSIX。
3. **目录隔离**：显示名继续为 `Terraria AI Wiki`；另选不会碰撞现有用户数据根的稳定 installer ID，例如 `TerrariaAIWiki.Windows`。最终 ID 在第一个公开 Release 前写入 ADR/runbook，发布后不随意改变。
4. **GitHub 更新**：应用使用 public `GithubSource(<owner/repo>, token: null, prerelease: false)`；设置页提供显式“检查更新”。发现更新后显示版本与 GitHub release notes 链接，用户确认才下载和重启。更新进度、取消、限流和失败均使用固定安全状态。
5. **GitHub 发布**：tag 必须精确对应产品版本和已验证 commit。Actions 只在受控 tag/workflow dispatch 上运行 Windows build/test/publish/package；先生成 draft 并完成资产审计，用户明确授权后再发布 stable。workflow 使用最小 `contents: write`，依赖与 `vpk` 固定版本。
6. **发布资产**：至少包含版本化 `*-Setup.exe`、`*-Portable.zip`、Velopack full package、stable index、`SHA256SUMS.txt`、`release-manifest.json`、许可/notices 和安装说明。delta 是可删优化，不是首发正确性的依赖。
7. **repair/uninstall**：重新运行相同/更新 `Setup.exe` 作为 repair/reinstall 路径；卸载从 Windows Installed Apps 进入。两者只能清理 installer ID 对应的安装根，保留产品数据根与 credential。应用内不建设第二套 repair engine。
8. **内置 Python**：选定一个受支持的 CPython 3.14.x x64 embeddable patch，记录官方下载 URL、SHA-256 和 PSF license；依据 `uv.lock` 只展开生产 wheels 到隔离 `site-packages`，不携带 pip、uv、venv、tests 或 cache。
9. **只读 Runtime**：生产 `python.exe` 和 launcher 从 `AppContext.BaseDirectory` 下固定相对位置解析；普通配置不含 Python path。开发/测试的外部 Python 只通过显式 diagnostic override 或 factory 注入，不进入用户设置或正式契约。
10. **凭据与迁移**：新增 Windows 内部 credential store 小边界，直接包装 `CredWriteW` / `CredReadW` / `CredDeleteW` / `CredFree`，测试用内存 fake。v3 JSON 只保存 version/provider；UI 不回显真实 key，空输入保留已有 key，替换与显式清除分开。
11. **迁移顺序**：严格验证 v2 → 写 Credential Manager → 写 durable v3 临时文件 → 原子替换主文件 → 重启 Runtime。v3 replace 前失败保留可重试 v2；v3 成功后 credential 成为唯一 key 来源。
12. **Evergreen WebView2**：`vpk pack --framework webview2` 或当时官方等价机制负责安装前置，应用不额外声明 .NET Runtime。Host 缺失态保留 probe，portable 提供官方获取/修复入口，不下载未知 URL或捆绑 Fixed Version。
13. **诊断与 UI**：复用现有 startup error、Chat 和 Settings，不新增全局导航。AI 设置移除 Python picker，显示 credential/bundled runtime；常规设置或同级“关于与诊断”区域显示版本、更新、WebView2 与日志入口。覆盖初始、检查中、可用、无更新、下载中、待重启、失败、取消和禁用状态。
14. **发布清单**：为每个候选记录 version/tag/commit/RID、installer ID/channel、Python/WebView2 策略、payload/hash、unsigned 状态和 notices；制品检查拒绝 `.venv`、`__pycache__`、`.pyc`、tests、数据库、`.pkg`、secret、证书、构建缓存和绝对用户路径。
15. **许可表达**：根级说明将“产品自有源码许可证”“官方 Wiki 文本 CC BY-NC-SA 4.0”“逐文件媒体/游戏资产”“Calamity 3.0 内容”“MIT/PSF/.NET/NuGet/PyPI 第三方”分开；发布页不得暗示 wiki.gg、Re-Logic 或 Calamity 团队背书。

### 安装技术选择

| 方案 | 结论 | 理由与门槛 |
|---|---|---|
| Velopack `Setup.exe` + portable（优先） | 进入 PoC | 一个工具覆盖普通安装、portable、GitHub Releases feed 与更新；需证明 child process/文件锁、repair、配置目录隔离、portable update 和 WebView2 bootstrap |
| Inno Setup + 独立 updater | 不实施 | 安装器成熟，但要增加第二套 GitHub feed/updater、portable 更新和更多生命周期代码，不符合当前最小范围 |
| WiX/MSI | 不实施 | 面向管理员/每机部署的能力不是首发需求，会增加 elevation、安装数据库和额外发布矩阵 |

如果 Velopack PoC 出现无法局部修复的硬阻断，停止实施并记录最小复现、数据/更新影响和替代成本；不得静默引入第二安装器。

### 尚待用户确认

1. 公开 GitHub 仓库的 `owner/repo` 和 remote URL；它会成为应用内 stable update source。
2. 首个公开版本与 tag，例如 `0.1.0` / `v0.1.0`；是否先发布一个 GitHub prerelease 做安装/更新候选验收。
3. 软件源码许可证的精确选择与范围。Wiki 内容遵循 CC BY-NC-SA 4.0 已确定，但这不能自动替代软件许可证或覆盖第三方许可。
4. 是否允许最终候选用内置 Python执行一次有预算上限、无自动重试的真实 provider 验收；若不允许，Phase 11 可用 fake provider 与既有 Phase 07 实证完成 Runtime 产品化回归，但不宣称当前发行版已重新实测在线模型。

## 文件变更

以下是基于当前真实入口的候选清单；精确文件名在实施阅读直接调用方后固定。

- **MODIFY** `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：self-contained 产品 profile 输入、版本元数据、Velopack library、内置 Runtime 和 notices 复制；保留 Viewer/Agent 单一来源及现有业务依赖版本。
- **ADD candidate** `src/Terraria_Wiki.Windows/Packaging/**`：固定 release inputs/hash、Windows PowerShell publish/package/verify 脚本和 release manifest 模板；不创建第二产品 project。
- **ADD candidate** `.github/workflows/windows-release.yml`：公开仓库确认后增加 tag/manual 触发的 Windows build/test/package/draft-release 流程；不在 pull request 上发布。
- **MODIFY** `src/Terraria_Wiki.Windows/Program.cs` 或实际最早入口：在任何 Avalonia/Runtime 初始化前执行 Velopack hook dispatcher，保证 install/update/uninstall fast hook 不启动正常应用。
- **MODIFY** `src/Terraria_Wiki.Windows/AI/{AiRuntimeConfiguration.cs,AiRuntimeConfigurationStore.cs,AiSidecarProcess.cs,WindowsAiRuntime.cs}`：v3 非 secret 配置、内置 Runtime 定位、Credential Manager 来源、只读 Python flags 和稳定恢复错误。
- **ADD candidate** `src/Terraria_Wiki.Windows/AI/WindowsCredentialStore.cs`：最小 Win32 generic credential adapter；不引入跨平台公共接口。
- **ADD/MODIFY candidate** `src/Terraria_Wiki.Windows/Updates/**` 与 `src/Terraria_Wiki.Windows/Hosting/{WindowsClientRuntime.cs,WebView2RuntimeProbe.cs}`：GitHub stable update、版本/portable 检测、WebView2 恢复、安全诊断和生命周期接线；具体拆分服从当前调用关系，不预建框架。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Settings/{SettingsView.axaml,AiSettingsViewModel.cs}` 与必要的现有状态文件：移除用户 Python picker，显示 credential/bundled-runtime/version/update/diagnostic 状态；复用现有样式，不增加全局入口。
- **MODIFY only if required** `src/Terraria_Wiki.Windows/Presentation/Styles/Controls.axaml`：仅在现有语义 class 无法表达新状态时增加可复用 class；页面不写 raw hex。
- **MODIFY/ADD** `tests/Terraria_Wiki.Windows.Tests/**`：v2→v3、credential fake/native、内置 Runtime、只读进程参数、GitHub update 状态、hook/生命周期、WebView2 恢复、诊断脱敏、version/package boundary 与 UI 状态。
- **MODIFY only if packaging proves necessary** `agent/sidecar_launcher.py`：只做 embeddable path/只读启动兼容；Agent 业务、协议与 provider 边界不变，相应 Python tests 同步覆盖。
- **ADD** `openspec/specs/windows-productization/spec.md`：完成后固化 Windows 安装、portable、GitHub 更新、Runtime、secret、诊断和发布契约。
- **MODIFY** `openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/local-wiki-viewer/spec.md` 与 `docs/adr/0006-use-stdio-python-sidecar.md`：只更新实际完成的 v3、内置 Runtime、Credential Manager、WebView2 和 UI 恢复事实。
- **ADD candidate** `docs/adr/0008-use-github-releases-windows-distribution.md`：记录普通 self-contained 安装器 + portable、Velopack、GitHub Releases、Windows 11 x64、unsigned 和更新长期决策。
- **ADD candidate** `docs/release/windows.md`、`LICENSE`/分范围许可文件、`NOTICE.md`、`THIRD_PARTY_NOTICES.md`、隐私与支持说明：只在用户确认软件许可证和权利范围后建立；不把第三方内容重新授权为项目许可证。
- **MODIFY** `README.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`：仅在实际通过门槛后更新 Phase 状态和已经收敛的架构开放问题。
- **KEEP** Application/Core/WikiTools 公共契约、SQLite/schema/FTS5、数据包、Wiki data root、Viewer 内容/bridge、Android 暂缓状态和全部无关未跟踪文件。

## 执行步骤

- [ ] **关闭剩余输入并建立恢复点**：记录 GitHub `owner/repo`、首发版本/tag、软件许可证范围和 provider 验收选择；重新检查 Git 状态，建立只含已批准范围的 Git 保存点；计划转 `approved`，实施开始时转 `in-progress`。
- [ ] **制作可删除的 Velopack PoC**：用当前 self-contained publish 生成不同 installer ID 的 unsigned `Setup.exe`、portable 和两版本 local feed；验证普通用户安装、hook 首行执行、Viewer/WikiTools loopback、Python child process、文件选择器、外链、single-instance、配置目录隔离、portable 和卸载。PoC 不进入产品 Runtime；硬阻断先更新计划。
- [ ] **证明修复与更新语义**：损坏安装目录中的受控 DLL/Viewer/Python 文件后重跑同版本 `Setup.exe`，确认 payload 恢复且外部 settings/favorites/data root/credential canary 不变；完成 N→N+1、本地中断和回退测试，确认 child process 全部关闭且无混合版本。
- [ ] **固定 release inputs 与版本**：建立唯一 `win-x64` product publish/profile、SemVer/tag/assembly 映射、installer ID、`win-x64-stable` channel、制品命名、官方 input URL/hash 和 payload allowlist；明确不 trim/AOT/single-file。
- [ ] **构建封闭式 Python payload**：从精确官方 embeddable ZIP 与 `uv.lock` 生成生产 `site-packages`，移除 pip/uv/tests/cache，记录文件/hash/license；Host 从安装目录定位并完成 fake provider、TLS/CA/native wheel 与安装目录零写入 smoke。
- [ ] **迁移 Credential Manager 与 v3**：实现最小 adapter、byte limit/redaction、v2 可重试迁移、v3 原子保存、replace/preserve/clear 语义及故障注入 tests；确认 key 不在 JSON、命令参数、协议、日志、crash、制品或 Git。
- [ ] **接入 GitHub stable 更新**：实现 public GithubSource、显式检查、下载/取消/确认重启、portable/installed 分支和安全错误；draft/prerelease 默认忽略，不内置 token，不在应用启动时静默下载。
- [ ] **收口 WebView2、恢复与诊断**：为安装器配置官方 Evergreen bootstrap；portable 和 Host probe 提供官方获取/修复动作；增加有界脱敏诊断、版本/Runtime/update 状态，只局部调整 Settings/Shell。
- [ ] **生成 release 制品和自动化**：固定 Velopack tool/library 版本，在 Windows 生成 Setup、portable、full package/index、checksums、manifest 与 notices；GitHub repo 确认后加入最小权限 workflow，默认先产生 draft，不自动公开 stable。
- [ ] **扩充自动化与静态边界**：运行全部既有测试和新增 productization tests；扫描 P0/API/schema/protocol 未变化、安装目录无写入、payload allowlist、secret/license/provenance、版本/channel 和恢复不变量。
- [ ] **执行干净 Windows 11 安装矩阵**：在无 .NET/Python/uv/pip/开发工具的干净 Windows 11 x64 完成安装→首次启动→数据根选择/恢复→Viewer/Search→内置 AI fake→退出；覆盖 WebView2 已有/缺失、AI key 缺失、credential failure、Runtime 损坏、repair、卸载与重装。
- [ ] **执行 GitHub Releases 升级矩阵**：获得外部发布授权后，用目标 GitHub repo 的独立 RC channel/prerelease 验证 N→N+1、release index/full package/hash、断网/限流/取消/损坏下载、restart 和 portable update；不得拿 local feed 代替最终渠道证据。
- [ ] **执行 Windows 10 低成本门槛**：仅在不新增依赖/代码路径的前提下，用 Windows 10 22H2 x64 干净环境运行同一 Setup/portable/upgrade/repair/P0 矩阵。全部通过才写“best-effort compatible”；任一平台差异或维护成本即保持 Windows 11 only。
- [ ] **执行 P0 与 UI 验收**：Windows Release build/tests、目录 publish、安装/portable smoke、代表性旧数据副本启动、Viewer assets/bridge、Wiki 数据管理和 AI 故障隔离；UI 检查 Light/Dark、1280×820、900×600、宽/窄 Reader、键盘/焦点和新增异步状态。
- [ ] **执行候选发布审计**：验证 unsigned 状态、SmartScreen/Smart App Control 告知、制品 SHA-256、许可/署名/notices、隐私/support、安装/更新/修复/卸载说明；如获付费请求授权，再执行一次有限真实 provider 验收。
- [ ] **同步持续文档并完成 Phase**：把实际决策写入 ADR/spec，把可复现命令写入 runbook，只更新 README/项目基线/架构状态；检查 diff、生成物、包、日志、进程和未跟踪范围，Windows 证据齐全后标记 completed、归档并创建只含 Phase 11 的 Git 提交，不自行 push 或发布。

## 验证与回归计划

### WSL / 平台无关

- `git diff --check`、tracked/untracked/生成物检查；确认未夹带既有治理归档、实验、`.codex/`、数据库、`.pkg`、WAL/SHM、key、证书或构建输出。
- `rg` 边界扫描：Windows Productization 不向 Core/Application 加平台依赖；Presentation 不访问 SQLite；只有一个 NativeWebView/Viewer session；NDJSON/Wiki Tool/Viewer 协议字段不变；没有 Store/MSIX/Android/MAUI/非目标平台实现。
- Python source 用 frozen lock 运行 ruff、format check、strict mypy、pytest；release payload 另做 allowlist/hash/license 检查，不把 source venv 当作 bundled runtime 证据。
- release inputs 必须来自 HTTPS 官方来源、精确版本和 SHA-256；脚本在 hash 不匹配、未知文件、版本不一致、dirty source 或缺少 notices 时失败关闭。
- workflow 静态审计 trigger、最小权限、固定 tool/action 版本和 secret 边界；客户端源码/制品不得包含 GitHub write token。

### Windows 开发环境

- 根 solution restore、Release build 0 warning / 0 error、全量 .NET tests；记录实际项目/测试数量，不沿用计划中的 275 作为未来结果。
- self-contained `win-x64` product publish 包含唯一 ViewerAssets、Agent、embedded Python、notices 和版本信息；不包含外部 Python path、开发工具、test/cache、数据库或 secret。
- bundled Python handshake、取消→再次提问、Search→Read→多段 answer→sources/usage、crash/retry、退出进程树清理和安装目录零写入通过；fake provider 先提供无外网可重复证据。
- native Credential Manager 测试使用测试专属 target，覆盖 missing/read/write/replace/delete、2560-byte 上限、Unicode、访问失败、v2 migration 各断点和清理；不得枚举或修改用户其他凭据。
- Velopack hooks 必须在正常 Avalonia/Runtime 组合前运行；install/update/uninstall fast callback 不显示 UI、不读取 Wiki 数据、不启动 Viewer/WikiTools/Python。
- WebView2 probe、installer bootstrap、portable 获取入口和重试使用 Microsoft 官方来源；应用不以管理员身份运行，不执行 shell 字符串或未知下载。

### 干净 Windows 11 / 安装、portable 与更新

- 在干净 Windows 11 x64 标准用户环境使用 `Setup.exe` 而非 publish 目录完成 Start Menu 启动、single instance、LocalAppData 隔离、Credential Manager、文件选择、外链、loopback Viewer/WikiTools、内置 Python 和卸载。
- WebView2 缺失机验证 installer bootstrap；portable 缺失场景验证安全说明和官方动作。断网时不得把 online bootstrap 冒充离线安装。
- N→N+1 更新保留外部 Wiki data root、host settings、Chat 收藏和 key；应用 payload 完整替换且不混合旧 DLL/Python wheels。更新失败/中断后当前稳定版本仍可启动。
- repair 恢复刻意损坏的 payload；卸载后没有 app/Sidecar/listener 进程、快捷方式或安装目录残留，外部 Wiki 数据、用户设置和 credential 按文档保留。
- portable 在新的用户可写目录解压运行，不创建 Installed Apps 项；GitHub update 后仍从 portable 根启动。只读目录更新安全失败且不损坏当前文件。
- GitHub stable 客户端忽略 draft/prerelease，处理 404/403 rate limit、缺少 index、超时、断网、hash 不匹配、取消和更新后重启；任何失败不阻止离线应用启动。
- 代表性合成数据完成 Search、Viewer JS/CSS/image/audio、Reader/历史、数据管理和退出清理；可用时以真实旧数据临时副本验证兼容，不读写用户生产原件。

### Windows 10 条件门槛

- 仅测试 Windows 10 22H2 x64；记录 OS build、ESU 状态、WebView2、Setup/portable 和应用版本，并明确该 OS 已结束普通支持。
- 复用完全相同的 `win-x64` 制品和验证矩阵，不增加 Win10 专属包、分支或依赖。任何 workaround、降级依赖或独立发布维护都使门槛失败。
- 通过只允许发布说明写“best-effort compatible with Windows 10 22H2 x64”；Windows 11 x64 仍是唯一正式支持基线，不能把 manifest target 当作兼容证据。

### UI / 可访问性

- 新状态沿用语义 token/style，无页面 raw hex、无第二主题/图标依赖、无第六全局导航；设置内容在 820 DIP 宽度内自然换行。
- 更新 UI 覆盖 idle/checking/up-to-date/available/downloading/ready/cancelled/failed，凭据/WebView2/bundled Runtime 也有可用、缺失、失败与重试状态；过期异步结果不得覆盖新检查或退出状态。
- Light/Dark、1280×820、900×600、1100 DIP 宽窄分界、Reader 开/关、实际可用缩放、Tab 顺序、focus-visible、disabled、Automation Name 和至少 44×44 DIP 图标命中区实际检查。
- 所有错误采用页内安全状态；重试不清除仍可用内容，不弹阻塞式成功对话框，不泄漏路径、provider 响应、release token 或底层异常。

### 发布、安全与许可

- 验证 SemVer/tag/assembly/manifest/channel 一致、tag 指向已验证 commit、Release 资产完整且单个小于 GitHub 2 GiB 上限；release index 只引用同一正式版本的 GitHub 资产。
- secret scan 覆盖源码、配置、stdout/stderr、诊断、crash、smoke JSON、release manifest、Setup、portable、update package 和 Git diff；不得含 API key、MCP bearer、GitHub write token、证书私钥、问题/回答、HTML 或用户绝对路径。
- 明确验证最终 EXE/installer 没有 Authenticode 签名，并把这一事实及 Windows 警告写入 Release notes；不得生成或分发自签根证书，不引导用户全局关闭 SmartScreen/Smart App Control。
- notices 覆盖 CPython、生产 PyPI wheels、NuGet/.NET/Avalonia/NativeWebView、Viewer.js/handy-scroll、官方 Terraria Wiki 文本、逐文件媒体和 Calamity 3.0 提示；自动清单不能替代授权。
- 检查产品描述只使用已确认术语：免费、公开源码、非商业。软件许可证未达到 OSI 定义时不宣称“OSI-approved open source”；CC BY-NC-SA 4.0 的署名、修改和链接信息必须随源码与二进制可达。
- GitHub upload、Release publish 和真实 provider 请求分别记录实际授权、执行者、环境与结果；未执行项必须明确标注，不能用本地 feed/fake provider 代替最终渠道证据。

## 风险与回退

- **许可/术语风险**：CC BY-NC-SA 4.0 适合 Wiki 内容但官方不建议用于软件，且 NC 与 OSI 开源定义不兼容。公开发布前精确确定项目代码许可证和措辞；第三方许可保持原样，不进行 blanket relicensing。
- **素材权利风险**：Wiki 文本许可不自动覆盖媒体、Terraria 商标/游戏素材、Calamity 3.0 内容或项目图标。未完成逐项核对时只能把候选包标为未清权，不宣称公开发行就绪。
- **安装根碰撞**：若 installer ID 映射到现有配置根，repair/uninstall 会有数据删除风险。PoC 前即使用不同 ID，并以 canary/failure injection 验证；发现碰撞不得发布。
- **Velopack 集成风险**：整体替换 `current`、fast hook、文件锁和 repair 语义可能与长驻 Python/loopback 生命周期冲突。先做可删除两版本 PoC；硬阻断时回到 Phase 11 父提交并请求重新选型。
- **portable 更新风险**：只读位置、杀毒软件或外部锁可能阻止原位替换。校验后才应用，失败保留旧版并提供 GitHub Release 手动下载，不放宽任意目录权限。
- **GitHub 单点渠道风险**：GitHub outage、匿名限流、仓库改名/转移或 release 资产遗漏会使检查更新失败。客户端降级为继续运行当前版本并打开固定 Releases 页面；repo 迁移必须单独设计兼容窗口。
- **未签名风险**：每个新版本重新积累文件信誉，SmartScreen/Smart App Control/企业策略可能拦截。当前回退是继续提供 hash、源码和透明说明，不伪造信任；正式签名若未来需要，另建变更。
- **Windows 10 生命周期风险**：普通 22H2 已 EOL。即使技术兼容也只作 best-effort，若测试或维护不是近乎零成本则不加入首发矩阵。
- **内置 Python 供应链**：Runtime、native wheels 或 CA bundle 可能缺失/过期。精确 pin+hash+notices，构建期展开，运行期禁止安装；安全更新随完整应用版本发布。
- **凭据迁移风险**：JSON 与 Credential Manager 不是同一事务。采用“先 credential、后 durable v3”并做断点故障注入；成功前不删除 v2。旧版回退只保证离线 P0，AI 需重新配置。
- **WebView2 在线前置风险**：首次安装/repair 在 Runtime 缺失时可能需要访问 Microsoft。断网时安全失败并给出 standalone 官方方案说明，不改用 Fixed Version 或未知镜像。
- **更新中断/混合 payload**：使用完整 package/hash 和版本目录替换，测试 N→N+1、文件锁与中断恢复；Python/Agent/.NET/Viewer 一起发布，不允许局部在线更新。
- **诊断泄漏/膨胀**：只用 allowlist 事件、稳定 code 和严格轮换；任何字符串先脱敏。日志失败不阻断 Runtime，用 canary secret/path 证明不落盘。
- **代码回退**：回到 Phase 11 父提交；安装版用保留的旧 GitHub Release 完整安装器执行 repair/reinstall。没有 SQLite/schema/package 迁移，Wiki 数据不需回滚；v3 降级按已记录限制重新配置 AI。

## 进度记录

- 2026-09-03：用户要求规划 Phase 11；读取入口、Master Architecture、项目基线、计划规范、相关 specs/ADR、Phase 07/10 记录，以及 Windows project、Runtime/config/Sidecar、启动恢复、Settings/Shell、测试和资产来源。
- 2026-09-03：形成第一版 MSIX/Store 倾向草案；只创建计划，未修改业务源码、下载 Runtime、生成安装包或发布外部制品。
- 2026-09-04：用户明确取消 Store、Store identity/publisher 和 Store 更新工作，改为 GitHub Releases only、普通 self-contained `win-x64` installer + portable、Windows 11 x64、暂不商业签名，并确认继续 Evergreen WebView2、内置 Python、Credential Manager 与完整产品化回归。
- 2026-09-04：重新核对项目上下文、`terraria-ui` Skill、Velopack/GitHub/Microsoft/Python/Creative Commons/OSI 官方资料，重写计划为单一 Velopack + GitHub Releases 路线；未进入实施。
- 2026-09-04：用户明确授权开始实施 Phase 11；计划转为 `in-progress`。首步为只使用本地 feed 的 Velopack 安装/portable/修复/更新 PoC，外部 GitHub 发布仍等待单独授权与仓库地址。

## 发现、偏差与决策

- **已决定**：应用二进制与更新只来自 GitHub Releases；Phase 11 交付普通 `Setup.exe` 和 portable zip，不实施 Store/MSIX/App Installer。
- **已决定**：Windows 11 x64 是正式首发支持；Windows 10 22H2 x64 只走低成本、同制品、完整实测的 best-effort 门槛。
- **已决定**：Phase 11 不配置商业签名。unsigned 是已知发布限制，必须测试并公开说明，不得以 hash 或自签名替代。
- **已决定**：保留 Evergreen WebView2、内置 CPython Runtime、Windows Credential Manager，以及安装/更新/修复/卸载/P0 回归。
- **已决定**：不升级业务依赖、不改变 P0/协议/schema、不恢复 MAUI/Android 工具链、不并行建设第二安装器或第二更新渠道。
- **发现**：Velopack 默认安装在 `%LocalAppData%\{packId}`，而当前用户配置位于 `%LocalAppData%\Terraria AI Wiki`；installer ID 必须与配置目录分离，否则 repair/uninstall 可能删除用户配置。
- **发现**：GitHub public GithubSource 不需要客户端 token，但匿名 API 有每 IP 60 requests/hour 限制；显式检查和安全降级是首版必要边界。
- **发现**：普通 Windows 10 22H2 已 EOL；源码 target 的 17763 API 下限仍不等于支持或兼容证据。
- **发现**：当前 `apiKey` 允许最多 4096 字符，而 generic credential blob 上限为 2560 bytes；v3 必须按编码后的 byte length 验证并更新持续 spec。
- **许可冲突**：用户的“免费、开源、非商用”意图不能同时精确对应 OSI 开源定义；CC 官方也不建议把 CC 许可证用于软件。本计划采用“免费、公开源码、非商业”作为尚未选定软件许可证前的准确措辞，并保持 Wiki/第三方许可分层。
- **治理偏差**：`.agents/skills/terraria-ui/SKILL.md` 的必读源码路径仍指向已退休的 `terraria-wiki-offline/src/...`，与根 `AGENTS.md`、`INIT.md` 和 Phase 10 的根级 `src/...` 事实冲突。本计划遵循更高层当前治理；修正 Skill 不属于本次产品化规划授权。

## 验证结果

当前只完成规划期读取与静态核对：

- 已确认 Phase 00–10 状态、Phase 11 架构边界、持续契约、ADR、Windows project、Runtime/config、Settings/Shell 和资产许可事实。
- 已确认当前仓库没有 remote/release automation，tracked 工作树没有用户已有修改，既有未跟踪范围保持不变。
- 已核对 .NET self-contained、Velopack Setup/portable/GitHub source/WebView2 bootstrap、GitHub Releases、CPython embeddable、Credential Manager、Windows unsigned 风险、Windows 10 生命周期与 CC/OSI 许可边界的权威资料。
- 已对计划执行 `git diff --check` 和 Store/MSIX 残留扫描；结果记录在本次规划交付中。
- 未执行 build/test/publish、Python/Velopack 下载、installer PoC、GitHub upload、安装/升级/repair/uninstall、真实 provider 或 UI 验收；这些都属于计划获批后的实施门槛。

## 完成摘要

Phase 11 尚未实施。本 draft 已按用户决定收敛为：从唯一 self-contained `win-x64` payload 生成普通每用户 `Setup.exe` 与 portable zip，以公开 GitHub Releases 作为唯一应用发布和更新来源，继续使用 Evergreen WebView2、内置 CPython 与 Windows Credential Manager，并完整验证安装、更新、修复、卸载、错误恢复和 P0。Windows 11 x64 是正式首发支持，Windows 10 22H2 x64 只作低成本 best-effort 门槛，首发制品明确 unsigned。开始实施前仍需确认公开 GitHub `owner/repo`、首发版本和软件源码许可证范围；计划批准不等于授权外部发布或付费请求。
