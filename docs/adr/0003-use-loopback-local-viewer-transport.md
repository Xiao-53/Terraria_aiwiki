# ADR 0003：Windows Local Viewer 使用受限动态 loopback transport

- 状态：accepted；Phase 04 Windows 11 x64 生产客户端验证通过
- 日期：2026-08-24
- 决策范围：Windows Local Viewer transport、NativeWebView 消息边界与生命周期；不外推 Android
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/local-wiki-viewer/spec.md`、`openspec/changes/archive/phase-04-windows-client-foundation/plan.md`

## 背景

旧 MAUI 宿主用固定 `127.0.0.1:55000` 的 `HttpListener` 提供 Viewer 静态内容和数据库资源，并允许 `Access-Control-Allow-Origin: *`。实现直接依赖 MAUI package API、全局活动 Wiki 和 ContentDb，启动失败只写调试日志，不能直接进入新的 Windows Host。

Phase 00 已证明 Avalonia NativeWebView 能从动态 loopback 加载现有 HTML/CSS/JS、图片和音频，并完成同源消息链路。Phase 04 需要把该结论收敛为生产 Adapter，同时保持 Core/Application 公共契约和旧 Viewer 资产不变。

## 决策

Windows Local Viewer 采用宿主管理的受限 IPv4 loopback transport：

1. 每次启动绑定 `127.0.0.1:0`，由操作系统分配端口；不使用固定端口或公开网卡。
2. HTTP/1.1 `Host` 必须精确等于当前 `127.0.0.1:<port>`，只接受 GET/HEAD；拒绝 absolute target、反斜线、冒号、NUL、路径穿越和活动 Wiki 之外的站点目录，不返回宽松 CORS。
3. build/publish 从旧项目的 `Resources/Raw/Web/**` 和 `wwwroot/js/iframe-bridge.js` 直接复制唯一 Viewer 资产；新 Host 只增加一个安全 outer page，不复制或修改站点内容。
4. `/src/*` 只调用 Application `Query.ReadAssetAsync`；页面和 Redirect 只调用 `Query.ReadAsync`；页面成功读取后显式调用 `Library.RecordVisitAsync`。
5. outer page 只转发精确同 origin、精确 iframe `contentWindow`、`req/res` 类型和合法 request ID 的消息；C# 再校验 source、origin、类型、ID 和方法。未知方法及 Phase 04 未支持的新标签、图片剪贴板和完整右键菜单返回错误，不静默成功。
6. HTTP(S) 外链由 bridge 校验后交给系统浏览器。Local Viewer 不获得任意文件 URI 或其他 scheme 能力。
7. Adapter 实现 `IWikiDataConsumerCoordinator`：pause 时先停止 listener；lease 释放后仅在宿主仍存活时按 Application 当前活动 Wiki 重启。窗口退出先取消任务和 WebView session，再停止 transport、Dispose Application 并释放数据锁。
8. Windows 首屏只有在 Redirect 已完成且正文 DOM 已写入后才用内部 `ViewerReady` 把 Reader 标为可交互；同一 Base URI 的重复 Ready 状态不得再次导航。退出时 WebView stop 最多等待 500 ms，transport 主动关闭活动 socket 并最多排空 1 秒，避免半开请求拖住窗口关闭。

## 验证证据

Phase 04 在 Windows 11 x64 实际完成：

- Release restore/build 和 Core 89/89、Application 54/54、Windows 28/28 tests；transport tests 覆盖动态端口、重复启停、冲突、Host/方法/路径、MIME、404/500、无 CORS、pause/resume 和关闭。
- 正式 build、framework-dependent publish、self-contained publish 三种 WinExe 均运行 NativeWebView smoke；每种都实际完成 Search、Redirect/anchor、返回、主题，并请求站点 JS/CSS、数据库图片和音频，退出清理为 true。
- 2026-09-02 的 Windows Release 生命周期回归连续 5 次完成相同 smoke：进程创建到 `ViewerReady` 为 1950–2612 ms，关闭清理为 54–58 ms；停滞 client 的自动化测试也验证了有界停止。
- framework-dependent 与 self-contained `win-x64` 输出约 158 MB / 235 MB；两者都直接包含旧 Viewer 资产。系统 WebView2 Evergreen 仍是前置项，没有随 .NET self-contained 产物捆绑。
- smoke 只使用合成 data root；既有 golden `.pkg` SHA-256 复核仍为 `d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa`。

## 后果

正面后果：

- 不需要修改 Core、Application 公共 API、SQLite、数据格式或旧 Viewer 内容。
- 动态端口消除固定端口冲突，listener 生命周期和失败可被宿主观察与重试。
- Viewer、手动搜索和未来 AI 继续使用同一活动数据源；数据库读取不泄漏到 Presentation。
- 同源 outer page 与 C# 双重校验收紧旧 bridge 的 `postMessage('*')` 边缘。

负面后果与约束：

- Windows Host 运行期间存在一个仅 loopback 可达的 HTTP listener，必须持续维护 Host、方法、路径和消息校验回归。
- self-contained .NET 不包含 WebView2；缺失 Runtime 时 Host 只报告可操作错误，由 Phase 11 决定安装/bootstrap。
- 本决策只由 Windows 11 x64 验证支持。Android Viewer transport 必须在 Phase 13 目标环境独立决定和验证，不能直接复制 Windows listener 假设。

## 被否决方案

- 固定 loopback 端口：增加实例/端口冲突，且没有必要。
- 继续使用旧 `LocalWebService`：依赖 MAUI、全局状态、固定端口和宽松 CORS，并绕过 Application。
- 当前改用自定义 scheme 或 WebView 资源映射：Phase 00 未证明其对相对 URL、媒体和消息链路有更小风险；动态 loopback 已取得完整项目证据。
- 复制或重写 Viewer 内容：会产生第二份资产和回归面，违背 P0 复用边界。
