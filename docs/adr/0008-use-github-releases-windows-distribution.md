# ADR 0008：Windows 仅通过 GitHub Releases 分发普通安装器与 portable

- 状态：accepted；Phase 11 本地候选链路已验证，公开发布仍需仓库、版本与软件许可证输入
- 日期：2026-09-04
- 决策范围：Windows 发行、安装、更新、Runtime 前置和支持基线；不外推 Android
- 关联：`openspec/changes/phase-11-windows-productization/plan.md`、`docs/release/windows.md`

## 背景

Phase 10 后 Windows 客户端可 self-contained publish，但没有普通用户安装、portable、应用更新或发布清单。用户明确取消 Microsoft Store、Store identity/publisher、Store 更新、MSIX 首要格式与商业代码签名，并指定 GitHub Releases 为应用二进制和后续更新的唯一来源。

## 决策

1. 唯一产品输入是非 single-file、非 trimmed、非 AOT 的 self-contained `win-x64` 目录。Velopack 1.2.0 从同一目录生成每用户 `Setup.exe`、portable zip、full/delta package 与 `win-x64-stable` index。
2. 稳定 `packId` 为 `TerrariaAIWiki.Windows`，与 `%LocalAppData%\Terraria AI Wiki` 用户配置目录分离。安装、repair、更新和卸载不得删除 Host 设置、收藏、AI provider/credential 或外部 Wiki data root。
3. 应用更新只读取编译时确认的公开 `https://github.com/<owner>/<repo>` 正式 Releases，不内置 GitHub token，不查看 draft/prerelease，不在启动时静默检查或下载。检查、下载、取消与重启均由用户显式触发；失败降级为继续使用当前离线版本。
4. Setup 声明 Evergreen WebView2 官方 bootstrap；.NET 与 CPython 随应用 self-contained。portable 缺少 WebView2 时只打开 Microsoft 官方页面，不携带 Fixed Version Chromium。
5. Windows 11 x64 是正式支持基线。Windows 10 22H2 x64 只有同一制品完成独立低成本矩阵后才可标为 best-effort；不得增加 Win10 专属包或依赖分支。x86/ARM64 和其他桌面平台不在范围。
6. Phase 11 制品明确 unsigned。SHA-256、源码和 GitHub provenance 只用于完整性与可审计性，不能替代 Authenticode，也不能要求用户全局关闭 SmartScreen、Smart App Control 或企业策略。
7. 发布脚本固定 CPython/uv/Velopack 输入，使用 NuGet lock，生成 payload/dependency/release manifest、SHA-256、notices 和 unsigned 状态。缺少项目所有者批准的软件许可证、tracked source 不干净或元数据不一致时默认拒绝形成可公开发布候选。
8. 仓库不包含自动上传或发布动作。GitHub Release 外部写入必须另获明确授权；已有 release asset 不得用不同字节覆盖。

## 选择理由

Velopack 用一个受控工具覆盖普通安装、portable 和完整包更新，避免并行维护自制 updater、MSIX 与另一套安装器。GitHub Releases 适合免费、公开源码项目的可审计二进制分发，客户端读取公开 feed 不需要保存 token。完整目录替换也适合同时更新 .NET、Python、Agent 与 Viewer，避免混合版本。

## 后果与约束

- 未签名文件可能被 Windows 警告或阻止，每个版本都需要清楚告知并发布校验和。
- GitHub outage、匿名 API 限流、仓库转移或缺少 release asset 会让更新不可用，但不得阻止当前版本启动。
- delta 只是重建目标 full package 的输入，不能直接交给 updater 当作完整包 apply；下载器必须重建并校验完整目标包后再替换 `current`。
- portable 需要用户可写目录才能原位更新；不可写时保留当前版本并引导手动下载新的 portable。
- 公开源码不自动决定软件许可证。Wiki 文本、媒体、Viewer 与依赖保持各自许可；发布门禁不能用“非商业”意图代替权利核对。

## 被否决方案

- Microsoft Store/MSIX/App Installer：用户明确取消，且会引入 Store identity、publisher 和第二更新渠道。
- 自制安装器与 updater：重复解决事务更新、快捷方式、卸载、repair 和 feed 校验，维护面更大。
- Fixed Version WebView2：显著增加 Chromium 体积与安全更新责任；Evergreen 已符合当前策略。
- framework-dependent 或运行期下载 Python：使终端依赖开发/runtime 环境并削弱可重复发布。
- 自签名证书或引导关闭系统保护：不能建立可验证的公众信任，且增加安全风险。
