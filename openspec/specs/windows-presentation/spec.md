# Windows Presentation 能力契约

- 状态：implemented baseline；Phase 08 Windows 11 x64 验证通过
- 适用范围：Avalonia Windows Presentation、Chat、Wiki Search、共享 Reader 与安全 Markdown；不外推 Android
- 来源：Master Architecture v0.6、Phase 08、ADR 0001、Application/Viewer/Windows AI Runtime 能力契约

## 信息架构与响应式布局

- 应用默认进入 AI 问答，只显示可用的“AI 问答”和“Wiki 阅读”两个导航入口；收藏、历史、全部页面、数据管理、设置和 Wiki 切换属于后续 Phase，不显示无功能占位入口。
- Windows Shell 使用固定窄导航栏、标题区和细描边工作区。初始窗口为 1280×820 DIP，最小窗口为 900×600 DIP。
- 宽度不小于 1100 DIP 时，默认由 AI 画布与同一个 Wiki Reader 并排组成；Reader 可关闭，关闭后 AI 扩展为完整工作区。
- 宽度小于 1100 DIP 时，默认只显示 AI 画布。点击可信来源或进入 Wiki 阅读时，Reader 作为右侧覆盖层出现，可用 Esc、关闭按钮或遮罩收起。
- Chat/Wiki 导航不取消活动回答；Chat 的当前单轮结果在内存中保留，退出应用才统一取消和清理。

## Chat 交互

- 每次问题都是独立单轮：发送新问题前清除旧正文、工具状态、来源和 usage，不向 Agent 传递或持久化历史。
- Enter 发送，Shift+Enter 换行；回答期间禁止并发发送，只提供停止。空状态提供可发现但不占用主画布的示例问题入口。
- 可见结果包含当前问题、最多每 75 ms 刷新的流式 Markdown、Wiki 工具进度、可信来源、usage 和安全状态。
- AI Disabled/Failed 必须留在 Chat 内显示固定安全错误、重试 AI 和打开 Wiki；不弹阻塞对话框、不自动切页，离线 Search/Reader 保持可用。

## 安全 Markdown

- 生产依赖固定使用 Markdig 1.3.2，启用 `DisableHtml`、CJK emphasis 和 pipe table；Markdig 只产生 AST，不生成 HTML。
- 原生 Avalonia 控件显式渲染标题、段落、强调、列表、引用、代码块、分隔线和表格；可见 Markdown 上限与协议一致为 32 KiB。
- 模型 HTML 作为安全文本或忽略；图片不加载；Markdown 链接、自动链接、远程链接和任意 URI 不创建可点击导航。
- 唯一可导航链接是宿主从 typed answer 建立的可信 Wiki 来源按钮。

## Wiki 与共享 Reader

- Chat 来源必须按 `resolvedTitle#anchor` 打开；不得使用 requested title 代替已解析标题。
- Wiki 阅读使用 180 ms debounce 搜索、取消前一请求并安全显示无结果或数据源不可用状态；结果在同一个 Reader 打开。
- 整个 Windows Presentation 只创建一个 `NativeWebView` 和一个 `ViewerWebViewSession`。宽窄切换只改变该 Reader 的布局与可见性，不移动、复制或启动第二个 Viewer。
- Reader 继续使用既有 Viewer 协议、内部链接、主题、访问记录、返回行为和资源链路；Presentation 不直接访问 Core、SQLite 或 Viewer backend。

## 变更边界

Application/Core 公共 API、Python Agent、NDJSON v1、`ai-runtime.json`、SQLite schema、Wiki 数据包和 Viewer 内容协议不因 Presentation 改变。新增状态模型保持在 Windows assembly 内部；不得把 UI 状态升级为公共持久模型或 Application port。

若增加多轮、会话持久化、Phase 09 导航、第二个 Viewer、可点击模型链接、Markdown HTML/WebView、窗口状态持久化或 Android UI，必须建立独立变更并更新相应能力契约。
