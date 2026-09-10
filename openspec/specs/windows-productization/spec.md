# Windows Productization 能力契约

- 状态：implemented locally；Phase 11 公开发行仍等待仓库坐标、正式版本、项目软件许可证和权利审计
- 适用范围：Avalonia Windows 11 x64 安装、portable、更新、Runtime 前置、诊断与发行；不外推 Android 或其他桌面平台
- 来源：Master Architecture v0.6、Phase 11、ADR 0008、Windows AI Runtime/Presentation/Local Viewer specs

## 发行形态与支持范围

- 唯一产品输入是 self-contained `win-x64` 目录；不得启用 trim、AOT 或 single-file。安装终端不依赖 .NET SDK/runtime、外部 Python、pip 或 uv。
- Velopack 1.2.0 从同一 payload 生成每用户 `Setup.exe`、portable zip、full/delta update package 与 `win-x64-stable` index。稳定 pack ID 是 `TerrariaAIWiki.Windows`，不得与 `%LocalAppData%\Terraria AI Wiki` 用户配置目录重合。
- GitHub Releases 是应用二进制与后续更新的唯一渠道。Microsoft Store、Store identity/publisher/update、MSIX、App Installer 和其他应用渠道不属于当前产品。
- Windows 11 x64 是唯一正式支持基线。Windows 10 22H2 x64 只有同一制品在独立低成本完整矩阵通过后才可声明 best-effort；不得为它增加专属包、依赖或代码路径。x86、ARM64 与其他桌面系统不在范围。
- Phase 11 不配置 Authenticode。发行页、runbook 与包内 notice 必须明确 unsigned 以及 SmartScreen、Smart App Control 或企业策略可能警告/阻止；SHA-256 与源码 provenance 不得描述成代码签名。

## 安装、portable 与数据保留

- Setup 在当前用户范围安装、注册开始菜单入口和卸载项；portable 解压到用户可写目录即可运行，不注册 Installed Apps 或安装快捷方式。两者执行同一应用二进制与 Viewer/Agent/Python payload。
- Host settings、完整 Chat 会话库、保留用于回退的旧 `chat-favorites.json`、`ai-runtime.json`、Windows Credential Manager secret 与外部 Wiki data root 必须位于安装根之外。安装、覆盖安装式 repair、N→N+1、卸载和重装不得删除或改写这些数据。会话库固定在 `%LocalAppData%\Terraria AI Wiki\conversations`。
- repair 定义为重新运行相同或更高可信版本的完整 Setup；它恢复受控 DLL、Viewer、Agent 与 Python 文件。delta 只能用于重建并校验目标 full package，不能直接作为完整包 apply。
- 更新、repair 或退出前必须走统一 Runtime 清理：停止回答，关闭所属 Python 进程树、WikiTool loopback 与唯一 Viewer transport。不得扫描或结束不属于本应用的进程。

## Runtime 前置与更新

- .NET 与 CPython 随应用携带。CPython/uv/production lock、官方源 URL 与 SHA-256 必须固定；运行期不得下载或安装 Python 依赖，安装目录不得生成 bytecode、数据库、配置或 secret。
- Windows 默认依赖 Evergreen WebView2。Setup 使用官方 bootstrap；portable 和 Host probe 缺失时只提供 Microsoft 官方获取/修复入口，不捆绑 Fixed Version Chromium，不从未知镜像下载。WebView2 mutable profile/cache 位于 `%LocalAppData%\Terraria AI Wiki\WebView2`，不得污染或锁定安装/portable `current` payload。
- 更新客户端只接受编译时合法的公开 `https://github.com/<owner>/<repo>`，不内置 token，忽略 draft/prerelease，不降级，不在启动时静默检查或下载。用户显式执行检查、下载、取消与重启应用。
- 更新失败、断网、匿名限流、资产缺失、取消或校验失败只影响更新功能；当前离线 Search、Viewer、数据管理和设置保持可用。缺少合法仓库元数据的本地构建必须明确禁用更新。

## 诊断、安全与发行门禁

- 本地诊断只记录 allowlist 事件、应用版本、稳定错误 code、Windows/WebView2/Runtime/update 状态和必要耗时；启动耗时按固定 allowlist 阶段记录累计毫秒与阶段毫秒，任意未知错误 detail 必须丢弃。不记录问题、回答、Wiki HTML、完整用户路径、API key、bearer、provider 原始响应或底层异常。
- 诊断默认最多保留当前与前一文件，每个不超过 512 KiB。日志目录失败不得阻止应用启动或关闭。
- 发行脚本必须使用 NuGet lock，验证 Velopack/CPython/uv 版本和 CPython hash，生成 payload、Python、依赖与 release manifests、notices 和 `SHA256SUMS.txt`，拒绝 test/cache/bytecode/数据库/证书私钥/开发路径或疑似 secret。
- 公开候选必须来自干净且已验证的 commit，SemVer、tag、assembly、repository、channel 与 manifest 一致，单个 GitHub asset 小于 2 GiB。缺少项目所有者批准的软件许可证、权利证据或必要元数据时必须失败关闭，不能标为 publication-ready。
- 项目自有软件、官方 Terraria Wiki 可许可文本、逐文件媒体、Calamity 内容、Viewer 和 .NET/Python 依赖保持分层许可与署名；任何 blanket notice 都不能替代来源方授权。
- 仓库内发布脚本只生成本地候选。GitHub upload/publish 和真实 provider 请求是独立外部写入，必须另获用户明确授权；不得覆盖已有 Release asset 为不同字节。

## 验收场景

1. Windows Release build/tests 和 Python frozen checks 通过；self-contained payload 包含唯一 Viewer、Agent、内置 Python 与 notices，不含开发工具、数据库、secret 或缓存。
2. Setup 完成安装、开始菜单启动、P0 Search/Viewer/assets/bridge/AI fake smoke、同版本 repair、N→N+1、重装和干净卸载；外部 settings/conversations/legacy-favorites/data/credential canary 保持不变。
3. Portable 从新目录启动同一 P0 与内置 Runtime，不创建安装注册；更新上下文正确识别 portable，失败不破坏当前 payload。
4. Credential Manager、严格 v3、WebView2 缺失恢复、诊断轮换/脱敏、更新全部状态与进程清理均有自动化证据。
5. UI 在 Light/Dark、1280×820 与 900×600 下保持既有五导航、页内异步状态、键盘/焦点和可读布局，不增加新的全局入口。
6. 最终 Setup、portable、full/delta/index、manifests、notices 与 checksum 相互一致，应用和安装器 unsigned 状态与文档一致。

## 变更规则

改变应用发布渠道、pack ID/update channel、支持 OS/架构、安装范围、代码签名策略、Runtime 固定方式、GitHub token 边界、更新交互、数据保留或诊断内容时，必须建立独立变更并更新 ADR 0008 与本 spec。Android 发行不得继承 Windows 路径、Velopack、WebView2、Credential Manager 或 GitHub update 实现假设。
