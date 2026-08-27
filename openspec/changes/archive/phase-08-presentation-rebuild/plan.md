# Phase 08 — Presentation Rebuild

- 状态：completed
- Phase：08 — Presentation Rebuild
- 执行环境：WSL2 Codex CLI 实施与静态检查；Windows 11 x64 执行 .NET/Avalonia build、test、run、publish 与人工验收
- 创建日期：2026-08-27
- 最后更新：2026-08-28
- 范围：Windows Avalonia Presentation 的 Shell、Chat、Wiki、共享 Reader、安全 Markdown、Presentation 测试与持续治理文档
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/ai-backend/spec.md`、`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户提供了已批准的 Phase 08 实施方案并要求在新上下文直接执行。前置 Phase 00–07 已归档；当前恢复基线是内层源码 `d328a04`、外层治理 `bd80734`。内层 Git 工作树干净；外层存在既有未跟踪 `.agents/`、`.codex/`、实验、历史架构与归档 change 等内容，本阶段全部保持排除。

真实源码确认：

- `src/Terraria_Wiki.Windows/MainWindow.cs` 是 866 行代码构造的三列临时壳，同时承担启动/关闭、搜索、AI 呈现、Viewer 会话和 smoke。
- `WindowsClientRuntime` 已组合 Application、唯一 Viewer、WikiTools 与 Python Sidecar，AI Disabled/Failed 不阻断离线能力。
- `ViewerWebViewSession` 已负责唯一 `NativeWebView` 的精确页面、锚点、主题、内部历史与返回命令，不需要第二个 Viewer 或内容协议变化。
- `WikiQueryUseCases.SearchAsync` 与 `ReadAsync` 已提供 Presentation 所需的稳定边界；Search 结果包含标题、Redirect、纯文本摘要与 match/highlight，Presentation 不需要访问 Core/SQLite。
- `AiAgentAnswer` 已提供最终 Markdown、可信 `resolved_title`/anchor 来源、tool trace 与 usage；流式事件的 delta 拼接契约和 32 KiB 上限已在 Phase 07 固定。
- Windows project 当前锁定 Avalonia 12.1.1、NativeWebView 12.0.1，没有 MVVM/DI/Markdown 包；旧品牌资产位于 `Terraria_Wiki/Resources/AppIcon/logo.svg` 与 `appicon.ico`。

## 目标与完成定义

1. 默认启动进入 AI 问答，只显示“AI 问答”和“Wiki 阅读”两个导航项；窗口保持 1280×820、最小 900×600。
2. Chat 支持独立单轮问题、Enter 发送/Shift+Enter 换行、流式安全 Markdown、工具轨迹、可信来源、usage、停止、重试与 AI 故障隔离；导航不取消活动回答。
3. 点击可信来源后由唯一共享 Reader 打开 `resolvedTitle#anchor`；Wiki 搜索结果使用同一个 Reader，不创建第二个 Viewer。
4. Reader 在宽度不小于 1100 DIP 时并排显示，窄窗口作为右侧覆盖层，可用 Esc/关闭按钮收起；Wiki 页面保持 320 DIP 搜索栏。
5. Markdig 1.3.2 仅解析 AST，禁用 HTML；原生 Avalonia 控件渲染标题、段落、强调、列表、引用、代码与表格。HTML、图片、Markdown/自动链接、远程或恶意 URI 均不可执行/导航。
6. Application/Core、Python Agent、NDJSON v1、`ai-runtime.json`、SQLite schema、Viewer 内容协议、旧 MAUI 和 P0 数据生命周期均不变。
7. Windows Release restore/build、全部既有与新增测试、合成数据/fake Sidecar 链路、两种 `win-x64` publish smoke 通过；目标尺寸/主题/缩放人工检查有真实记录。
8. 新增持续 `windows-presentation` spec，更新 ADR 0001、README、项目状态与架构 Phase 表；源码与治理分别提交，计划完成后归档，不 push。

## 前置条件

- [x] Phase 00–07 已完成并归档。
- [x] 用户明确授权直接实施本 Phase。
- [x] 双 Git 基线与既有未跟踪内容边界已确认。
- [x] Runtime、Application、Viewer、AI typed answer 与现有 Presentation/test 证据已读取。
- [x] Markdig 1.3.2 可由锁定 Windows/NuGet 工具链恢复，并与 `net10.0-windows` 构建兼容。

## 范围与非目标

范围内：Windows 内部 Presentation 状态/view models、同步/异步 command、XAML Shell/Chat/Wiki/Reader/Markdown 控件、共享 Reader 编排、输入键盘行为、搜索 debounce/取消、流式 Markdown 节流、Presentation 自动化测试、品牌 linked resources、持续 spec/ADR/入口状态。

非目标：多轮与聊天持久化、会话列表、收藏/历史/全部页面/数据管理/设置/Wiki 切换入口、凭据 UI、Markdown WebView、第二个 Viewer、窗口状态持久化、安装器、内置 Python、WebView2 bootstrap、Android、其他平台、MAUI 退休、Core/Application/协议/schema/Viewer 内容改造。

## P0 / 兼容性影响

- Search/Read 继续通过 `WikiQueryUseCases`；AI 继续通过现有 WikiTools/Application 使用同一数据，Presentation 不触碰 SQLite。
- 复用同一 `NativeWebView`、`ViewerWebViewSession`、Viewer server、Bridge/history/theme，不复制 Viewer backend 或资产。
- AI Disabled/Failed 只在 Chat 内显示安全错误、重试与打开 Wiki，启动和离线搜索阅读不被自动切页或阻塞弹窗影响。
- 新问题清除旧单轮结果但不传历史；页面导航只改变内存可见状态，不取消活动回答。退出应用才统一取消。
- 无数据库、设置 schema、包格式或用户数据迁移；回退仅需恢复 Phase 08 源码提交。

## 方案与待决问题

- 使用 XAML `MainWindow` + `SplitView` Shell，并拆出 Chat/Wiki/Reader/Markdown `UserControl`；`MainWindow` code-behind 只保留 Runtime 启停、唯一 Viewer 创建/销毁和 smoke 编排。
- 使用 Windows assembly 内部 `INotifyPropertyChanged` view models 与小型 commands；不增加 CommunityToolkit、ReactiveUI、DI 容器或 Presentation project。
- 共享 `ReaderPaneViewModel` 管理来源/搜索打开、模式与宽窄布局；唯一 `NativeWebView` 在固定 Reader 控件中创建一次。
- Markdig 使用 `MarkdownPipelineBuilder().DisableHtml()` 并启用 CJK emphasis 与 pipe table extension；只消费 AST，链接/图片降级为安全可选择文本，宿主来源按钮是唯一导航入口。
- 流式累计已由协议脱敏的 Markdown，最多每 75 ms 发布一次可见 block 集合；completed 立即最终解析并核对最终正文，不改变 delta 拼接。
- Avalonia 的控件模板、键盘路由和 XAML 编译细节需要由 Windows build/test 收口；如果框架 API 与假设不符，保持上述行为并采用最小等价实现，记录偏差。

## 文件变更

- MODIFY `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：Markdig 1.3.2、XAML/品牌 linked resources、Windows icon。
- MODIFY `src/Terraria_Wiki.Windows/App.axaml`：Fluent 主题与克制品牌资源/样式。
- REPLACE/MODIFY `src/Terraria_Wiki.Windows/MainWindow.cs`；ADD `MainWindow.axaml`：Shell 与 lifecycle/smoke 分离。
- ADD `src/Terraria_Wiki.Windows/Presentation/**`：内部 view models、commands、Chat/Wiki/Reader/Markdown 控件与 XAML。
- KEEP `Hosting/WindowsClientRuntime.cs`、`Viewer/**`、`AI/**`、Application/Core、Agent、协议、数据与旧 MAUI；只有编译证明所需时才作最小 Presentation 接线修正。
- ADD/MODIFY `tests/Terraria_Wiki.Windows.Tests/**`：Presentation 状态、搜索、共享 Reader、Markdown 安全/边界、键盘/响应式/automation 回归。
- ADD `openspec/specs/windows-presentation/spec.md`。
- MODIFY `docs/adr/0001-use-avalonia-client.md`、`README.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`；完成后归档本计划。

## 执行步骤

- [x] 读取治理、架构、相关 specs/ADR、Phase 07 记录以及真实 Windows/Application/Viewer/AI 源码与测试；确认双仓库恢复基线。
- [x] 建立 Presentation 内部状态、commands、安全 Markdown AST/block renderer 及单元测试。
- [x] 实现 XAML Shell、Chat、Wiki、唯一共享 Reader、品牌资源与响应式宽窄布局。
- [x] 将 Runtime/Viewer/AI/Search 接入 view models，保留 smoke 与退出清理；验证 AI 故障隔离、单轮、取消和页面往返。
- [x] 执行 Windows restore/build/full tests，并修复所有 warning/error；运行合成数据与 fake Sidecar 自动 smoke。
- [x] 执行两种 `win-x64` publish 离线 smoke；完成 1280×820/窄窗、浅/深色和当前目标环境缩放的实际人工检查，并明确记录未执行的精确 100%/150% 项。
- [x] 更新持续 spec、ADR、README、项目基线和架构 Phase 表。
- [x] 检查 diff、secret、生成物、临时文件和双 Git 状态；源码/治理分别提交，归档计划，不 push。

## 验证与回归计划

### WSL / 静态

- `git diff --check`、依赖与 source boundary 扫描；确认无 SQLite/Core direct access、无第二个 `NativeWebView`/`ViewerWebViewSession`、无 MAUI/Android/后续 Phase 入口。
- 检查 XAML/资源路径、Markdown parser 只使用 AST、HTML/图片/链接无执行路径、32 KiB 边界与节流状态可确定测试。

### Windows 11 x64

- 使用 Windows .NET 10 CLI 执行 solution restore、Release build、全部 tests；要求 0 warning / 0 error / 0 skipped（显式环境 gate 除外需逐项说明）。
- 使用合成 data root 与 fake Sidecar 验证：AI 流式 → 来源点击 → Reader 精确 title/anchor → Wiki 搜索/打开 → 返回 Chat 保留结果；AI Disabled/Failed 不影响 Wiki。
- 正式 WinExe 在 1280×820 与窄窗口、浅/深色、100%/150% 缩放下人工检查布局、文本选择、键盘/焦点、automation name 与 Viewer。
- framework-dependent/self-contained `win-x64` publish 均执行离线 smoke，检查 ViewerAssets、linked 品牌资产、AgentRuntime、退出清理与 AI 故障降级；不发真实 Provider 请求。

### 非目标平台

Android 与其他产品平台不构建、不验证、不宣称支持。WSL 检查不得记为 Windows 目标平台通过。

## 风险与回退

- XAML/NativeWebView 宿主：控件只能有一个 visual parent；Reader 固定拥有唯一 WebView，Shell 只改变 Reader pane 的列/overlay 可见性，不搬移/复制控件实例。
- Markdown 安全：Markdig 扩展可能产生链接、HTML 或图片 AST；renderer 使用显式允许列表，未知 block/inline 只取纯文本或忽略，不生成 HTML、URI 或图片请求。
- 流式成本：75 ms 节流避免每 delta 重建；最终事件立即重建。32 KiB 是现有协议上限，不另行截断或改变 typed answer。
- UI 可测性：业务状态和编排放入不依赖视觉树的内部 view models/controllers；XAML 只绑定，减少需要真实 WebView2 的单元测试面。
- 恢复点固定为内层 `d328a04`、外层 `bd80734`。不做 schema/数据迁移；Phase 08 独立提交可整体回退。

## 进度记录

- 2026-08-27：读取 `INIT.md`、README、Master Architecture、项目基线、计划规范、相关持续 specs、ADR 0001、Phase 07 完成记录及 Windows/MainWindow/Runtime/Viewer/Application/AI/test 真实实现。确认前置完成和双仓库恢复点，创建本计划并进入 in-progress。
- 2026-08-27：完成第一轮 Presentation 实现与自动化验证；用户否决第一轮视觉方案，工作暂停，未提交源码或治理变更。
- 2026-08-28：用户提供两张修订效果图，明确默认 AI 全宽画布与来源打开后的 AI/Reader 分栏状态。以效果图替换第一轮视觉基准，继续实施；能力、协议和 Phase 边界不变。
- 2026-08-28：用户进一步明确宽屏默认打开 AI/Reader 双栏，另一张单画布效果图代表窄窗口。实现 `Home` Reader 状态：宽屏默认显示、窄屏默认隐藏，可信来源/Wiki 在窄屏按需覆盖；最终宽/窄、浅/深色真实窗口均完成检查。
- 2026-08-28：最终源码以 `2a07b3c`（`feat(windows): rebuild Phase 08 presentation`）提交；内层工作树干净。临时 venv、发布目录、合成数据和验收截图已删除，计划进入 completed 并归档。

## 发现、偏差与决策

- 当前唯一 Viewer 已天然支持 `title#anchor`，来源导航无需新增 Application API 或 Viewer 协议。
- 当前 AI typed answer/stream contract 已提供 Phase 08 所需全部数据；Presentation 只需节流解析与安全显示，不修改 Sidecar。
- 旧品牌 SVG 已含绿色 `#97bf5c`、深青 `#41675c`、棕色 `#a97448/#705448`；Presentation 以 linked resource 复用，避免复制或重绘。
- 用户确认的视觉基准是固定窄图标栏、简洁标题区、细描边大画布、贴底胶囊输入框；宽屏默认显示近似等宽的 Chat/共享 Reader 双栏，关闭 Reader 后 Chat 恢复全宽；窄屏默认只显示 Chat，可信来源按需覆盖 Reader。
- 效果图中的收藏、历史、数据、设置、同步和主题图标属于后续能力示意；Phase 08 仍只显示可用的 AI/Wiki 入口，不创建无功能占位入口。
- Avalonia 12 的基础 `Image` 不直接解码 SVG，且本阶段只允许新增 Markdig 生产依赖。`logo.svg` 仍作为 linked resource 保留，实际窗口与导航显示同一现有 `appicon.ico`，没有复制、转换或新增 SVG renderer。
- Windows 捕获环境的实际缩放为 225%；最终布局在该更高缩放下通过，但没有改动系统显示设置，因此不把精确 100%/150% 人工检查写成已执行。

## 验证结果

- Windows .NET 10 Release build：成功，0 warning / 0 error。
- 完整自动化：237/237，0 failed / 0 skipped（Core 89、Application 54、WikiTools 28、Windows 66）。
- Windows Python 3.12 无外网跨语言集成：`AiCrossLanguageIntegrationTests` 1/1，通过 Search → Read → 分段回答、来源和 usage。
- 最终 framework-dependent `win-x64` 发布：172 MiB；合成数据离线 smoke 成功，Search 1、JS 1、CSS 1、image 1、audio 3、history 0、退出清理 true。
- 最终 self-contained `win-x64` 发布：277 MiB；同一离线 smoke 成功，Search 1、JS 1、CSS 1、image 1、audio 4、history 0、退出清理 true。
- 两种最终发布均包含 Markdig、ViewerAssets 和 9 个 AgentRuntime 文件；不包含虚拟环境或 Python cache。
- AI 生产 Sidecar + 本地 fake Provider 的取消后再次提问链路，以及 FD/SC 发布中的流式、工具、来源、usage 和退出清理已通过；未发起真实付费 Provider 请求。
- 人工窗口：最终 1280×820 宽屏默认 AI/Reader 双栏、900×800 窄屏单 AI 画布、浅色与深色、Reader/WebView、文本/输入和关闭行为均通过；目标显示器为 225% 缩放。精确 100%/150% 未执行。
- 静态边界：只有一个 `NativeWebView` 和一个 `ViewerWebViewSession`；Presentation 未引入 Core/SQLite 访问、第二 Viewer、HTML 输出、可点击模型 URI 或 Phase 09 导航；diff/secret 检查无异常。

## 完成摘要

Phase 08 已把 Phase 07 代码构造临时壳重建为用户确认视觉基准下的 AI-first Windows Presentation：默认宽屏为 AI/唯一共享 Reader 双栏，窄屏为单 AI 画布并按需覆盖 Reader；Chat、Wiki Search、安全 Markdown、可信来源与 Runtime 故障隔离均通过 Windows 实测。Application/Core、Python Agent、协议、SQLite、Viewer 内容和旧 MAUI 未改变。源码与治理分别提交，下一阶段仍是 Phase 09 P0 UI Takeover；本阶段未 push。
