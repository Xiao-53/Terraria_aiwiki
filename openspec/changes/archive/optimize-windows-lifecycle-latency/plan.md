# 优化 Windows 启停延迟

- 状态：archived
- Phase：无；Phase 09 期间的独立 Windows Runtime 性能变更
- 执行环境：WSL Codex CLI；Windows 11 x64 Release 构建、测试与实机计时
- 创建日期：2026-09-02
- 最后更新：2026-09-02
- 范围：Windows Avalonia Host、AI Sidecar 生命周期、Local Viewer/WebView 生命周期与 smoke 计时
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/windows-ai-runtime/spec.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`docs/adr/0006-use-stdio-python-sidecar.md`

## 背景与当前证据

用户实测启动约 6 秒、关闭约 8 秒，并要求两者均在 3 秒内。源码基线为 `0e6c923`，内层仓库干净。`MainWindow.StartAsync` 当前在创建 WebView session 和导航前同步等待 `EnableAiAsync`；关闭串行等待 WebView session、AI、Viewer 和 Application。AI 有多段顺序超时，Viewer client drain 无全局期限且响应写入使用 `CancellationToken.None`。

## 目标与完成定义

- 正常 Windows 11 x64 Release 环境中，进程创建至 Viewer 首页完成并可交互不超过 3000ms；AI 可继续后台启动。
- 关闭请求至 Avalonia 进程及所属 Python 进程全部退出不超过 3000ms。
- 点击 Reader 操作不会在 Viewer 尚未就绪时静默丢失。
- 保持数据库、Application/Core 公共 API、Viewer 安全边界和单 WebView 不变。

## 前置条件

- Phase 00–08 已完成；Phase 09 当前切片不依赖本变更。
- Windows 11 x64、WebView2、有效数据根和兼容 Python 环境用于目标验证。

## 范围与非目标

范围内：生命周期顺序、取消/超时、内部 ViewerReady 握手、内部 smoke 时序和直接测试。

非目标：数据库或 Viewer 内容重写、内置 Python、安装器、Android、AI 协议字段或业务能力变化。

## P0 / 兼容性影响

继续由同一 Application/Viewer 使用原数据；数据库只在所有消费者退出后释放。Viewer origin/source/envelope 校验不放宽。强制结束只作用于当前 Windows Host 创建并持有的 Python 进程树。

## 方案与待决问题

- Viewer 启动后立即组合 session、导航和绑定 UI；AI 由 Runtime 后台启动并单独发布状态。
- Wiki Viewer 首页完成初始 DOM 写入后发送内部 `ViewerReady`；就绪前 Reader 命令不可执行并显示明确状态。
- Runtime 用生命周期 CTS 和同步门统一 AI 启动、重试、切根与释放竞态。
- 关闭先取消，WebView stop 最多 500ms；AI 与 Viewer 并行停止。AI 优雅退出总期限 1.5s，超时杀进程树并限时收尾；Viewer 关闭活动 socket 并最多排空 1s。
- 3 秒是正常运行 SLO；缺失 WebView2、数据损坏、外部锁或磁盘故障必须显式报错，但不计入性能验收。

## 文件变更

- MODIFY Windows `MainWindow` / `WindowsClientRuntime`：启动重排、后台 AI、并行收尾与计时。
- MODIFY AI process/client/runtime：单一关闭期限和有界进程/stream 收尾。
- MODIFY Viewer server/session/host assets：就绪握手、可取消写入、socket 跟踪与有界停止。
- MODIFY Windows tests 与持续 specs；KEEP Application/Core/SQLite schema/公共协议。

## 执行步骤

- [x] 增加生命周期时序和 ViewerReady 内部契约及测试。
- [x] 重排启动，让 Viewer 可交互不再等待 AI。
- [x] 收紧 AI、Viewer 和 WebView 关闭边界并并行释放消费者。
- [x] 执行定向/全量测试、Windows Release 构建和连续 5 次目标机计时。
- [x] 更新持续规范、计划结果并独立提交源码和治理变更。

## 验证与回归计划

- WSL：静态边界、`git diff --check`、无生成物或 secret。
- Windows：Release build/tests；后台 AI/关闭竞态、拒绝退出 Sidecar、停滞 Viewer client、唯一 WebView。
- 实机：连续 5 次进程创建→ViewerReady ≤3000ms；AI ready/starting/活动回答三种关闭场景中关闭→全部进程退出 ≤3000ms。

## 风险与回退

后台启动可能与重试/退出竞争，以内部 gate/CTS 和所有权检查阻止晚到结果。强杀只在优雅期限后使用且限定所属进程树。可整体回退到源码 `0e6c923`。

## 进度记录

- 2026-09-02：完成代码与规范调查，确认同步 AI 启动、串行退出和无界 Viewer client drain 为主要耗时边界；开始实施。
- 2026-09-02：完成后台 AI、ViewerReady、幂等导航、有界 socket/Sidecar 收尾和并行消费者释放；Windows 实机连续五轮达到目标。

## 发现、偏差与决策

- 用户将“打开”定义为 Viewer 可交互，将“关闭”定义为主程序和所属 Python 进程全部退出。
- 保留 15 秒业务操作故障期限，不把 3 秒 SLO 误作所有设备的硬故障超时。
- 实机首次诊断发现页面已经完成导航且 `ViewerReady` 已处理，但 Viewer Ready 状态回调又对同一 Base URI 导航，替换了等待中的完成源；去掉重复导航并使 session 导航幂等后，等待超时和过早点击竞态同时消失。
- AI 配置解析也从 `WindowsClientRuntime.CreateAsync` 移到 Viewer 导航后的后台路径，避免本机 Python/config 探测阻塞离线首屏。

## 验证结果

- Windows 11 x64 Release build：成功，0 warning / 0 error。
- 全量 Release tests：Core 91、Application 54、WikiTools 28、Windows 85，共 258/258 通过。
- 连续五轮正式 WinExe smoke：Search、Redirect/anchor、返回、主题、CSS/JS、数据库图片/音频和退出清理全部成功；进程创建至 `ViewerReady` 为 1950、1992、2612、1988、1972 ms，均小于 3000 ms。
- 同五轮 smoke 关闭清理为 57、54、54、56、58 ms；额外等待 AI 后台运行 7 秒再发送正常窗口关闭，主进程 1692 ms 退出，所属 Python 残留为 0。
- 自动化覆盖拒绝响应的活动 Sidecar 在单一优雅期限后只强杀所属进程树，以及停滞 Viewer client 的有界停止。

## 完成摘要

Windows Viewer 首屏不再等待 AI，只有 DOM 已写入才开放 Reader 命令；同 URI 重复状态不会重启导航。退出统一取消后并行停止 AI/Viewer，所有 I/O 和子进程收尾均有上限，再释放数据库。目标机证据满足打开、关闭均不超过 3 秒。
