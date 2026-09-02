# Windows Presentation 能力契约

- 状态：implemented baseline；Phase 09 收藏/Reader 与 Wiki 资料库切片已通过 Windows 11 x64 Release build/tests，资料库人工 UI 验收待执行
- 适用范围：Avalonia Windows Presentation、Chat、收藏、Wiki 资料库、Wiki Search、共享 Reader 与安全 Markdown；不外推 Android
- 来源：Master Architecture v0.6、Phase 08、Phase 09 进行中计划、ADR 0001、Application/Viewer/Windows AI Runtime 能力契约

## 信息架构与响应式布局

- 应用默认进入 AI 问答，左侧显示可用的“AI 问答”“收藏”和“Wiki 资料库”三个导航入口；顶部 Wiki 按钮只打开或恢复共享 Reader，不替换当前 Chat/收藏/资料库主页面。数据管理和设置仍不显示无功能占位入口。
- Windows Shell 使用固定窄导航栏、标题区和细描边工作区。初始窗口为 1280×820 DIP，最小窗口为 900×600 DIP。
- 宽度不小于 1100 DIP 时，默认由 AI 画布与同一个 Wiki Reader 并排组成；两侧可用细拖柄调整，但主内容和 Reader 均有硬性最小宽度。拖动分隔条不会自动关闭 Reader；关闭只由 Reader 叉号执行，顶部 Wiki 按钮可重新打开。
- 宽度小于 1100 DIP 时，默认只显示 AI 画布。点击可信来源或进入 Wiki 阅读时，Reader 作为右侧覆盖层出现，可用 Esc、关闭按钮或遮罩收起。
- Chat/收藏/资料库/Wiki 导航不取消活动回答；Chat 的当前单轮结果在内存中保留，退出应用才统一取消和清理。

## 视觉系统与交互状态

- Windows Presentation 继续使用现有 Avalonia FluentTheme 12.1.1，并采用 Compact 密度；不增加第二套主题框架或图标依赖。
- 自定义视觉值由 Light/Dark 语义 token 统一提供，至少覆盖 Terraria 品牌色、应用背景、导航、表面、次级表面、描边、次级文本、危险状态、圆角和 elevation。页面 XAML 不直接散落 hex 色值。
- Shell 使用 72 DIP 左侧导航栏、64 DIP 顶栏、清晰的图标加文本标签和非纯颜色的选中指示；主内容使用 24 DIP 常规边距。列表与结果区优先连续内容和轻量分隔，只在输入、确认和覆盖层等需要边界的区域使用圆角或阴影。
- 可点击图标和发送/停止控件保持不小于 44×44 DIP 的命中区。自定义导航、分段、列表、来源、Wiki 和 composer 按钮必须定义 pointer-over、pressed、focus-visible 与 disabled 反馈；颜色不能是唯一的选中或状态信号。
- Chat 空状态直接展示可点击示例问题；回答、工具轨迹、来源、Token 与终态保持清楚但不过度堆叠卡片。宽屏 Reader 与主内容齐平，只有窄屏覆盖 Reader 保留 elevation。

## Chat 交互

- 每次问题都是独立单轮：发送新问题前清除旧正文、工具状态、来源和 Token 花费，不向 Agent 传递或持久化历史。
- Enter 必须在多行 TextBox 消费换行前通过预览路由发送，Shift+Enter 保留换行；若 `TextPresenter.PreeditText` 表示中文输入法仍在组词，Enter 只交给输入法确认候选，不触发发送。回答期间禁止并发发送，只提供停止；已提交问题保留在底部只读输入栏，不清空也不另建右上角问题卡，输入区继续明确提示键盘行为。
- 回答期间的停止图标必须在圆形 composer 按钮内双向居中。Chat 右上角收藏按钮占用独立布局栏，当前问题、流式 Markdown、最终回答及其滚动内容不得进入或覆盖该区域。
- 可见结果包含当前问题、最多每 75 ms 刷新的流式 Markdown、Wiki 工具进度、可信来源、Token 花费和安全状态。回答区用小型“AI 结论”标签与 Markdown 正文标题区分；可信来源使用等宽整行按钮，标题、详情与打开箭头保持一致对齐。Token 花费只显示输入、输出与合计 token，不显示模型请求数、工具调用数或美元估算，并与完成状态分别使用轻量信息条。思考中使用轻量状态面板、进度条和紧凑工具轨迹；首个正文增量后显示独立回答区，完成后工具轨迹降为次级摘要，避免大面积空白与厚重卡片堆叠。
- AI Disabled/Failed 必须留在 Chat 内显示固定安全错误、重试 AI 和打开 Wiki；不弹阻塞对话框、不自动切页，离线 Search/Reader 保持可用。
- 完成的当前单轮可由 Chat 右上角星标显式收藏或取消收藏。普通问答仍不自动保存；从收藏页打开快照只恢复问题、最终安全 Markdown 和可信来源，不向 Agent 重放或传递历史。

## 收藏

- 左侧收藏页组合两类现有能力：Windows 宿主保存的显式 Chat 单轮快照，以及 Application `WikiLibraryUseCases` 提供的 Wiki 收藏。Presentation 不直接访问 SQLite。
- Chat 收藏采用版本化宿主 JSON，默认位于 `%LocalAppData%\Terraria AI Wiki\chat-favorites.json`；最多 200 项，问题、Markdown 与来源沿用受限长度，写入失败只影响收藏功能。
- Wiki 收藏继续使用现有数据源和 schema。Reader 标题栏星标当前由宿主明确打开的 Wiki 页面；收藏项在唯一共享 Reader 中打开，并可从收藏页移除。
- 收藏不是多轮会话或自动历史，不改变 AI 独立单轮契约，也不跨设备同步。

## Wiki 资料库

- 左侧“Wiki 资料库”在同一主页面内提供“全部页面”和“历史记录”两个内页，只经 Application `WikiLibraryUseCases` 访问当前活动 Wiki；Presentation 不直接访问 SQLite 或 Core。
- 两个内页均按标题过滤并通过 `PageRequest` 分批加载，每批 100 项；继续加载沿用当前过滤和 offset。输入变化、切换内页或刷新必须取消过期请求，旧结果不得覆盖新筛选结果。
- 全部页面显示标题和更新时间；历史记录保持 Application 返回的最新优先顺序并按日期分组。空列表、加载失败和数据源不可用使用页内状态，不修改本地数据。
- 清空历史必须先显示页内二次确认，只调用既有 `ClearHistoryAsync`；失败保留当前列表并显示安全状态，不影响页面、收藏或 Wiki 内容。
- 点击全部页面或历史条目在唯一共享 Reader 中打开对应标题；窄屏沿用既有 Reader 覆盖层，宽屏沿用既有分栏，不创建第二个 Viewer。

## 安全 Markdown

- 生产依赖固定使用 Markdig 1.3.2，启用 `DisableHtml`、CJK emphasis 和 pipe table；Markdig 只产生 AST，不生成 HTML。
- 原生 Avalonia 控件显式渲染标题、段落、强调、列表、引用、代码块、分隔线和表格；可见 Markdown 上限与协议一致为 32 KiB。
- 模型 HTML 作为安全文本或忽略；图片不加载；Markdown 链接、自动链接、远程链接和任意 URI 不创建可点击导航。
- 唯一可导航链接是宿主从 typed answer 建立的可信 Wiki 来源按钮。

## Wiki 与共享 Reader

- Chat 来源必须按 `resolvedTitle#anchor` 打开；不得使用 requested title 代替已解析标题。
- 顶部 Wiki 按钮只控制 Reader 可见性；Reader 内继续使用既有 Viewer 页面导航和搜索。原生 Wiki 搜索状态仍保持内部能力，但顶部按钮不得把主内容切换到该页面。
- 整个 Windows Presentation 只创建一个 `NativeWebView` 和一个 `ViewerWebViewSession`。宽窄切换只改变该 Reader 的布局与可见性，不移动、复制或启动第二个 Viewer。
- Reader 继续使用既有 Viewer 协议、内部链接、主题、访问记录、返回行为和资源链路；Presentation 不直接访问 Core、SQLite 或 Viewer backend。

## 变更边界

Application/Core 公共 API、Python Agent、NDJSON v1、`ai-runtime.json`、SQLite schema、Wiki 数据包和 Viewer 内容协议不因 Presentation 改变。新增状态模型保持在 Windows assembly 内部；不得把 UI 状态升级为公共持久模型或 Application port。

启动失败时必须同时提供“重试”和“重新选择 Wiki 数据文件夹”，后者验证并替换已保存路径后重新组合 Runtime。兼容旧数据根时，若内容数据库遗留 WAL/SHM，必须通过 SQLite checkpoint 安全合并后再执行既有迁移；不得直接删除 sidecar。

若增加多轮、自动会话历史或同步、数据管理/设置/导入导出/维护等 Phase 09 其余 P0 页面、第二个 Viewer、可点击模型链接、Markdown HTML/WebView、窗口状态持久化或 Android UI，必须建立独立变更并更新相应能力契约。
