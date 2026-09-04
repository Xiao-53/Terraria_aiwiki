# Windows Presentation 能力契约

- 状态：implemented；Phase 11 产品更新、凭据与恢复状态已接入既有 Settings/Shell
- 适用范围：Avalonia Windows Presentation、Chat、收藏、Wiki 资料库、数据管理、设置、Wiki Search、共享 Reader 与安全 Markdown；不外推 Android
- 来源：Master Architecture v0.6、Phase 08、已归档 Phase 09 计划、ADR 0001、Application/Viewer/Windows AI Runtime 能力契约

## 信息架构与响应式布局

- 应用默认进入 AI 问答，左侧显示可用的“AI 问答”“收藏”“Wiki 资料库”“数据管理”和“设置”五个导航入口；顶部 Wiki 按钮只打开或恢复共享 Reader，不替换当前主页面。
- Windows Shell 使用固定窄导航栏、标题区和细描边工作区。初始窗口为 1280×820 DIP，最小窗口为 900×600 DIP。
- 宽度不小于 1100 DIP 时，默认由 AI 画布与同一个 Wiki Reader 并排组成；两侧可用细拖柄调整，但主内容和 Reader 均有硬性最小宽度。拖动分隔条不会自动关闭 Reader；关闭只由 Reader 叉号执行，顶部 Wiki 按钮可重新打开。
- 宽度小于 1100 DIP 时，默认只显示 AI 画布。点击可信来源或进入 Wiki 阅读时，Reader 作为右侧覆盖层出现，可用 Esc、关闭按钮或遮罩收起。
- Chat/收藏/资料库/数据管理/设置/Wiki 导航不取消活动回答；Chat 的当前单轮结果在内存中保留，退出应用才统一取消和清理。

## 视觉系统与交互状态

- Windows Presentation 继续使用现有 Avalonia FluentTheme 12.1.1，并采用 Compact 密度；不增加第二套主题框架或图标依赖。
- 自定义视觉值由 Light/Dark 语义 token 统一提供，至少覆盖 Terraria 品牌色、应用背景、导航、表面、次级表面、描边、次级文本、危险状态、圆角和 elevation。页面 XAML 不直接散落 hex 色值。
- Shell 使用 72 DIP 左侧导航栏、64 DIP 顶栏、清晰的图标加文本标签和非纯颜色的选中指示；主内容使用 24 DIP 常规边距。列表与结果区优先连续内容和轻量分隔，只在输入、确认和覆盖层等需要边界的区域使用圆角或阴影。
- 可点击图标和发送/停止控件保持不小于 44×44 DIP 的命中区。自定义导航、分段、列表、来源、Wiki 和 composer 按钮必须定义 pointer-over、pressed、focus-visible 与 disabled 反馈；颜色不能是唯一的选中或状态信号。
- Chat 空状态只展示品牌图形、标题和说明，用户从底部 composer 直接输入问题，不展示预设问题按钮。收藏与 Wiki 资料库页头的刷新操作复用同一 Terraria 浅色次级按钮样式和刷新图标，不使用默认中性灰色按钮。回答、工具轨迹、来源、Token 与终态保持清楚但不过度堆叠卡片。宽屏 Reader 与主内容齐平，只有窄屏覆盖 Reader 保留 elevation。

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

## 数据管理与迁移

- 左侧“数据管理”只经 Application `ActiveWikiUseCases` 与 `WikiDataUseCases` 列出/切换 Wiki、刷新统计，并执行页面/资源下载、更新、失败项重试或清理、数据库整理、资源删除和整库删除；Presentation 不直接访问 SQLite 或复制 Core 生命周期实现。
- 下载、更新、重试和维护等长操作显示页内进度、当前动作与取消入口。失败只显示不含本机路径的安全状态，不阻塞 Chat、资料库和离线设置访问。
- 切换 Wiki、清理失败项、删除资源、删除整库和导入覆盖必须先显示目标与影响范围，再经页内确认调用既有 Application 用例。活动数据变化后刷新搜索、收藏和资料库；需要时让同一个 Viewer/Reader 重新绑定动态 loopback，不创建第二个会话。
- Windows `.pkg` 导入先调用 `InspectPackageAsync`，保存 inspection token 并显示元数据；用户确认后才调用 `ImportPackageAsync`。0.4 之前的数据包必须明确提示兼容迁移并传递 legacy consent，包格式与既有 staging/backup/rollback 不变。
- Windows 导出由原生保存文件选择器决定目标。若目标已存在，宿主先在同目录写唯一临时包，完整成功后才替换目标；失败或取消清理临时文件并保留旧目标。
- 当前 Wiki 尚未下载页面时，Runtime 仍可进入数据管理和设置；Reader 保持关闭并提示先下载或导入，不把无内容状态升级为整应用启动失败。
- 没有已保存数据根时，Windows 宿主不得自动打开系统文件夹选择器；它在 `%LocalAppData%\Terraria AI Wiki\WikiData` 创建默认数据根并把绝对路径交给现有 Application/Core，由既有初始化流程创建 `Manager.db` 与活动 Wiki 数据库。已有有效保存根与显式命令行根优先；已有无效保存根不得被默认根静默覆盖，而应直接进入数据管理页恢复。
- 数据管理页始终显示当前数据库文件夹并保留更改入口。更改时可选择包含有效 `Manager.db` 的已有数据根，或选择空文件夹作为新数据根；非空且不含有效数据库的目录必须拒绝。取消、选错或后续数据库初始化失败都保留页面和再次选择入口，只有路径验证通过后才保存并重新组合 Runtime。

## 设置

- 左侧“设置”加载并显式保存 Application 的全部强类型偏好：语言、应用主题、Wiki 内容主题、鼠标侧键返回、搜索上限、页面/资源下载并发数和失败重试次数；验证与持久化仍由 `ApplicationSettingsUseCases` 负责。
- 保存成功后，应用主题、Viewer 内容主题和鼠标侧键返回在当前 Windows Runtime 生效；搜索和下载参数由后续 Application 调用读取。若持久化成功但实时应用失败，页面必须如实提示下次启动生效，不把已保存结果误报为失败。
- 当前 Windows Presentation 以简体中文交付，同时保留并可保存既有语言偏好；本阶段不承诺完整英文界面。
- 设置页使用“常规设置 / AI 配置”二级菜单，不增加第六个全局导航。AI 配置只公开 DeepSeek 官方 API 与自定义 OpenAI-compatible 两种 provider，并按 Windows AI Runtime spec 编辑同一个 `ai-runtime.json` v3。
- DeepSeek 预设固定官方 endpoint 与 Responses API，只允许选择当前受支持的 Flash/Pro 模型；自定义项显式填写 endpoint、model 与 Responses / Chat Completions。Python 只读显示安装目录中的内置 Runtime；API key 不回显并保存到 Windows Credential Manager，提供保留、替换和显式清除；思考强度提供服务默认、关闭、低、高、最大五档。
- AI 保存统一验证并原子写入后，显式重启当前 AI Runtime；Chat 继续经既有状态事件收到结果。保存或重启失败只能显示安全页内状态，不弹阻塞对话框、不泄漏 key/provider 原始响应，也不影响常规设置与离线能力。
- AI 配置与 Credential Manager 不依赖 Wiki 数据库 Runtime 才可编辑。没有有效数据根时 API Key 输入、provider 载入与保存仍可用；保存成功后明确提示 AI 将在选定有效数据根后启动，不把暂时无法重启误报成凭据保存失败。
- 常规页同级“关于与更新”区域显示应用版本、安装/portable 形态、Windows/WebView2 状态、unsigned 告知和有界诊断。只有用户点击后才检查公开 GitHub stable Releases；检查、可用、下载、取消、待重启、失败与禁用均为页内状态，失败不影响离线功能。
- 启动错误只显示当前适用的恢复动作：数据根失败自动进入数据管理页并使用页内选择/重选，WebView2 缺失提供 Microsoft 官方获取入口，其他失败保留重试；不得让无关按钮暗示错误恢复方式。

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

首次启动自动创建宿主管理的默认数据根，不弹出选择器；创建或数据库初始化失败时不提供无效的通用重试，而是进入数据管理页提供“选择/更改 Wiki 数据文件夹”。空文件夹可由既有 Application/Core 初始化，非空错误目录、无效数据库或取消都不替换已保存路径，页面保持可操作。WebView2 缺失时提供“重试”和 Microsoft 官方获取入口；其他启动失败只保留适用的安全恢复动作。兼容旧数据根时，若内容数据库遗留 WAL/SHM，必须通过 SQLite checkpoint 安全合并后再执行既有迁移；不得直接删除 sidecar。

若增加多轮、自动会话历史或同步、第二个 Viewer、可点击模型链接、Markdown HTML/WebView、窗口状态持久化、Windows 产品化或 Android UI，必须建立独立变更并更新相应能力契约。
