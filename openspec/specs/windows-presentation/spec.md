# Windows Presentation 能力契约

- 状态：implemented；2026-09-05 本地完整会话、整段收藏与对话历史已接入
- 适用范围：Avalonia Windows Presentation、Chat、收藏、Wiki 资料库、数据管理、设置、Wiki Search、共享 Reader 与安全 Markdown；不外推 Android
- 来源：Master Architecture v0.6、Phase 08、已归档 Phase 09 计划、ADR 0001、ADR 0010、Application/Viewer/Windows AI Runtime 能力契约

## 信息架构与响应式布局

- 应用默认进入 AI 问答，左侧显示可用的“AI 问答”“收藏”“Wiki 资料库”“数据管理”和“设置”五个导航入口；顶部 Wiki 按钮只打开或恢复共享 Reader，不替换当前主页面。
- Windows Shell 使用固定窄导航栏、标题区和细描边工作区。初始窗口为 1280×820 DIP，最小窗口为 900×600 DIP。
- 宽度不小于 1100 DIP 时，默认由 AI 画布与同一个 Wiki Reader 并排组成；两侧可用细拖柄调整，但主内容和 Reader 均有硬性最小宽度。拖动分隔条不会自动关闭 Reader；关闭只由 Reader 叉号执行，顶部 Wiki 按钮可重新打开。
- 宽度小于 1100 DIP 时，默认只显示 AI 画布。点击可信来源或进入 Wiki 阅读时，Reader 作为右侧覆盖层出现，可用 Esc、关闭按钮或遮罩收起。
- Chat/收藏/资料库/数据管理/设置/Wiki 导航不取消活动回答；当前完整会话在内存中保留，退出应用才统一取消和清理。设置页临时独占主工作区并隐藏共享 Reader，离开设置后按进入前的 Reader 状态恢复，不销毁或复制 Viewer。

## 视觉系统与交互状态

- Windows Presentation 继续使用现有 Avalonia FluentTheme 12.1.1，并采用 Compact 密度；不增加第二套主题框架或图标依赖。
- 自定义视觉值由 Light/Dark 语义 token 统一提供，至少覆盖 Terraria 品牌色、应用背景、导航、表面、次级表面、描边、次级文本、危险状态、圆角和 elevation。页面 XAML 不直接散落 hex 色值。
- Shell 使用 72 DIP 左侧导航栏、64 DIP 顶栏、清晰的图标加文本标签和非纯颜色的选中指示；主内容使用 24 DIP 常规边距。列表与结果区优先连续内容和轻量分隔，只在输入、确认和覆盖层等需要边界的区域使用圆角或阴影。
- 可点击图标和发送/停止控件保持不小于 44×44 DIP 的命中区。自定义导航、分段、列表、来源、Wiki 和 composer 按钮必须定义 pointer-over、pressed、focus-visible 与 disabled 反馈；颜色不能是唯一的选中或状态信号。
- Chat 空状态只展示品牌图形、标题和说明，用户从底部 composer 直接输入问题，不展示预设问题按钮。收藏与 Wiki 资料库页头的刷新操作复用同一 Terraria 浅色次级按钮样式和刷新图标，不使用默认中性灰色按钮。回答、工具轨迹、来源、Token 与终态保持清楚但不过度堆叠卡片。宽屏 Reader 与主内容齐平，只有窄屏覆盖 Reader 保留 elevation。

## Chat 交互

- Chat 使用单一可滚动 transcript 连续展示当前会话全部轮次。应用每次启动固定进入空白新对话，不自动恢复最近历史；用户从历史或收藏打开后恢复完整 transcript 并可继续追问。
- 每次请求只向 Agent 发送当前会话最近 4 个已回答或证据不足终态的 user/assistant 纯文本，总预算 24000 字符；失败、取消、pending、工具、来源、HTML、thinking 和原始消息图不发送。完整会话只在本机显示与保存。
- Enter 必须在多行 TextBox 消费换行前通过预览路由发送，Shift+Enter 保留换行；若 `TextPresenter.PreeditText` 表示中文输入法仍在组词，Enter 只交给输入法确认候选，不触发发送。回答期间禁止并发发送，只提供停止；问题被接受后立即清空底部输入框，已提交文本保留为当前 transcript 的新轮次并随整段会话保存，输入区继续明确提示键盘行为。
- 回答期间的停止图标必须在圆形 composer 按钮内双向居中。Chat 右上角整段收藏与“新对话”按钮占用独立布局栏；两者满足 44×44 DIP 命中区和 Automation Name。新对话在回答时禁用，其他时间清空 transcript/context 但保留未发送草稿并重新聚焦；空白会话不写记录。
- 每轮可见结果包含问题、最多每 75 ms 刷新的流式 Markdown、Wiki 工具进度、可信来源、Token 花费和安全状态。回答区用小型“AI 结论”标签与 Markdown 正文标题区分；可信来源使用等宽整行按钮，标题、详情与打开箭头保持一致对齐。Token 花费显示总输入、缓存输入、未缓存输入、输出与合计 token，不显示模型请求数、工具调用数或美元估算，并与完成状态分别使用轻量信息条。思考中使用轻量状态面板、进度条和紧凑工具轨迹；首个正文增量后显示独立回答区，完成后工具轨迹降为次级摘要。
- AI Disabled/Failed 必须留在 Chat 内显示固定安全错误、重试 AI 和打开 Wiki；不弹阻塞对话框、不自动切页，离线 Search/Reader 保持可用。
- 问题接受后先保存 pending，完成、证据不足、取消或失败后保存终态；崩溃遗留 pending 加载为 interrupted，且不进入模型历史。存储失败只降级本地历史，当前内存 Chat 与离线 Wiki 仍可用。

## 收藏

- 左侧收藏页组合两类现有能力：Windows 宿主会话库中显式收藏的完整 Chat 会话，以及 Application `WikiLibraryUseCases` 提供的 Wiki 收藏。Presentation 不直接访问 SQLite。
- Chat 会话采用 `%LocalAppData%\Terraria AI Wiki\conversations\index.json` 与独立 `<guid>.json` 版本化宿主 JSON；临时写入、flush、原子 replace 与 backup 后才发布。最多保留 100 个未收藏会话，最旧者自动淘汰；收藏不自动淘汰，最多 200 个，单会话最多 100 轮。
- 旧 `%LocalAppData%\Terraria AI Wiki\chat-favorites.json` 只在新 index 不存在时迁移为单轮已收藏会话；新会话文件与 index durable 后才完成，旧文件原样保留。取消收藏只改变状态，会话继续保留在历史。
- Wiki 收藏继续使用现有数据源和 schema。Reader 标题栏星标当前由宿主明确打开的 Wiki 页面；收藏项在唯一共享 Reader 中打开，并可从收藏页移除。
- 会话与收藏不跨设备同步，不进入诊断或发布物；Wiki 收藏继续保持原行为。

## Wiki 资料库

- 左侧“Wiki 资料库”在同一主页面内提供“全部页面”和“对话历史”。全部页面只经 Application；对话历史只经同一宿主会话库，因此当前 Wiki 数据源不可用时仍可使用。Presentation 不直接访问 SQLite 或 Core。
- 全部页面按标题过滤并通过 `PageRequest` 每批加载 100 项；对话历史按首问标题在本地筛选、按更新时间倒序显示标题、时间、轮数与收藏状态。输入变化、切换或刷新必须取消过期请求。
- 对话可直接打开完整 transcript。逐项删除（包括收藏会话）必须页内确认；删除当前会话后 Chat 进入空白新对话。清空只删除未收藏会话并须页内确认，收藏会话保留。
- Wiki 页面阅读不再写持久 WikiHistory；旧表/旧行只为数据兼容保留。Reader 的内存返回栈、`SaveToTabHistory`、`WikiBackAsync` 和鼠标侧键返回保持不变。

## 数据管理与迁移

- 左侧“数据管理”只经 Application `ActiveWikiUseCases` 与 `WikiDataUseCases` 列出/切换 Wiki、刷新统计，并执行页面/资源下载、更新、失败项重试或清理、数据库整理、资源删除和整库删除；Presentation 不直接访问 SQLite 或复制 Core 生命周期实现。
- 下载、更新、重试和维护等长操作显示页内进度、当前动作与取消入口。失败只显示不含本机路径的安全状态，不阻塞 Chat、资料库和离线设置访问。
- 切换 Wiki、清理失败项、删除资源、删除整库和导入覆盖必须先显示目标与影响范围，再经页内确认调用既有 Application 用例。活动数据变化后刷新搜索、收藏和资料库；需要时让同一个 Viewer/Reader 重新绑定动态 loopback，AI 继续通过同一个 Application 查询边界读取新内容，不创建第二个会话或要求重启应用。
- Windows `.pkg` 导入先调用 `InspectPackageAsync`，保存 inspection token 并显示元数据；用户确认后才调用 `ImportPackageAsync`。0.4 之前的数据包必须明确提示兼容迁移并传递 legacy consent，包格式与既有 staging/backup/rollback 不变。
- Windows 导出由原生保存文件选择器决定目标。若目标已存在，宿主先在同目录写唯一临时包，完整成功后才替换目标；失败或取消清理临时文件并保留旧目标。
- 当前 Wiki 尚未下载页面时，Runtime 仍可进入数据管理和设置；启动完成后自动选择数据管理页，Reader 保持关闭，并明确提示应用不会自动联网、用户可执行“下载页面与资源”或导入本地数据包。无内容状态不得升级为整应用启动失败。
- 没有已保存数据根时，Windows 宿主不得自动打开系统文件夹选择器；它在 `%LocalAppData%\Terraria AI Wiki\WikiData` 创建默认数据根并把绝对路径交给现有 Application/Core，由既有初始化流程创建 `Manager.db` 与活动 Wiki 数据库。已有有效保存根与显式命令行根优先；已有无效保存根不得被默认根静默覆盖，而应直接进入数据管理页恢复。
- 数据管理页始终在带边框的单行只读区域显示当前数据库文件夹，显示层使用 `PathSegmentEllipsis` 折叠中间目录、悬停显示完整路径并允许选择复制；完整值仍用于保存和重组。右侧保留次级“更改文件夹”入口。更改时可选择包含有效 `Manager.db` 的已有数据根，或选择空文件夹作为新数据根；非空且不含有效数据库的目录必须拒绝。取消、选错或后续数据库初始化失败都保留页面和再次选择入口，只有路径验证通过后才保存并重新组合 Runtime。

## 设置

- 左侧“设置”加载并显式保存 Application 的全部强类型偏好：语言、应用主题、Wiki 内容主题、鼠标侧键返回、搜索上限、页面/资源下载并发数和失败重试次数；验证与持久化仍由 `ApplicationSettingsUseCases` 负责。
- 保存成功后，应用主题、Viewer 内容主题和鼠标侧键返回在当前 Windows Runtime 生效；搜索和下载参数由后续 Application 调用读取。若持久化成功但实时应用失败，页面必须如实提示下次启动生效，不把已保存结果误报为失败。
- 当前 Windows Presentation 以简体中文交付，同时保留并可保存既有语言偏好；本阶段不承诺完整英文界面。
- 设置页使用“常规设置 / AI 配置”二级菜单，不增加第六个全局导航。AI 配置只公开 DeepSeek 官方 API 与自定义 OpenAI-compatible 两种 provider，并按 Windows AI Runtime spec 编辑同一个 `ai-runtime.json` v3。
- DeepSeek 预设固定官方 endpoint 与 Responses API，只允许选择当前受支持的 Flash/Pro 模型；自定义项显式填写 endpoint、model 与 Responses / Chat Completions。“模型服务”是唯一 AI 配置区域，合并提供商字段、API Key、思考强度和数据发送说明；普通用户界面不显示 Python 版本或路径。API key 保存到 Windows Credential Manager，不回显真实值或长度；已有凭据以固定长度遮蔽标记和“已保存”文字显示，空底层输入仍表示保留，输入新值表示替换，并另有显式清除操作。思考强度提供服务默认、关闭、低、高、最大五档。
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
- Reader 继续使用既有 Viewer 协议、内部链接、主题、内存返回行为和资源链路；Presentation 不直接访问 Core、SQLite 或 Viewer backend。

## 变更边界

Core、`ai-runtime.json`、SQLite schema、Wiki 数据包和 Viewer 内容协议不因 Presentation 改变。会话状态/存储模型保持在 Windows assembly 内部；不得把它升级为 Application port。Python Agent history 与 NDJSON v2 的对应边界分别由 AI Backend 和 Windows AI Runtime specs 管理。

首次启动自动创建宿主管理的默认数据根，不弹出选择器；创建或数据库初始化失败时不提供无效的通用重试，而是进入数据管理页提供“选择/更改 Wiki 数据文件夹”。空文件夹可由既有 Application/Core 初始化，非空错误目录、无效数据库或取消都不替换已保存路径，页面保持可操作。WebView2 缺失时提供“重试”和 Microsoft 官方获取入口；其他启动失败只保留适用的安全恢复动作。兼容旧数据根时，若内容数据库遗留 WAL/SHM，必须通过 SQLite checkpoint 安全合并后再执行既有迁移；不得直接删除 sidecar。

若增加云同步、超过 4 轮的模型上下文、模型生成历史摘要、第二个 Viewer、可点击模型链接、Markdown HTML/WebView、窗口状态持久化或 Android UI，必须建立独立变更并更新相应能力契约。
