# 新增参考图风格的全宽本地 Wiki 搜索页面

- 状态：archived；首版功能已实现但真实窗口视觉验收未通过，单顶栏修订未实施；用户于 2026-09-12 要求结束本 change，后续由新 plan 继续优化
- Phase：无；独立 Windows Presentation 变更
- 执行环境：WSL2 Codex CLI 负责阅读、编辑、计划维护与静态检查；Windows 11 x64 负责 .NET/Avalonia 构建、测试与真实窗口验收
- 创建日期：2026-09-09
- 最后更新：2026-09-12
- 范围：保留已完成的 Windows Avalonia 左侧 Wiki 入口与全宽搜索/阅读三态；按首次真实窗口反馈把 Wiki 专用工具栏合并进现有 Shell 顶栏，并只在独立 Wiki 模式隐藏重复 Reader 标题栏；复用原搜索、Reader 命令和唯一 Viewer，修订完成后再次交由用户验证再合并。
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`

## 背景与当前证据

用户要求增加本地 Wiki 界面，并在查看当前 Windows 实机界面后两次澄清最终形态：左侧新增“Wiki”导航；进入后是一个占满右侧工作区的本地 Wiki 搜索与浏览页面，不采用“左侧搜索结果 + 右侧 Reader”的分栏；AI、收藏、Wiki 资料库、数据管理、设置以及这些页面现有的 Reader 行为全部不动。

已确认事实：

- 首版实施前 Shell 对外只有“AI 问答”“收藏”“Wiki 资料库”“数据管理”“设置”五个左侧入口，右上“打开 Wiki”只在当前主页面旁打开或恢复共享 Reader；提交 `f73d49e` 已增加第六个“Wiki”入口。
- 首版实施前 `ShellSection.Wiki`、`IsWikiSelected`、`IsWikiVisible`、`WikiView` 和 `WikiViewModel` 已存在但入口隐藏；提交 `f73d49e` 已完成导航和首页/搜索/正文三态，当前修订只收敛这些状态的原生顶栏。
- `WikiViewModel` 已通过 `WindowsClientRuntime.Application.Query.SearchAsync` 使用当前活动本地 Wiki，具备 180 ms 防抖、旧请求取消、结果映射和安全错误处理；无需新增搜索服务。
- 搜索结果当前通过 `OpenSearchResultAsync` 打开唯一 `ReaderPaneViewModel`，全应用只有一个 `NativeWebView` 和一个 `ViewerWebViewSession`。
- 普通页面现有 Reader 规则已确认：宽度不小于 1100 DIP 时内联分栏，较窄时使用右侧覆盖层；设置页暂时隐藏 Reader。用户要求这些行为保持不变。
- 首版实施前隐藏的 `WikiView` 是 360 DIP 搜索切片；提交 `f73d49e` 已改为全宽连续结果列表，本轮不再修改它。
- 进行中的 Phase 11 与 AI 会话 Token 变更不需要为本功能改动打包、Agent、Sidecar、会话存储或 Wiki Tool 协议。

当前 Git 恢复基线为 `3641997`。本计划替换该提交中的第一版 draft；第一版错误地把 Wiki 页面定义为搜索列表与右侧 Reader 分栏，现已由用户明确否决。

2026-09-09 本次修订依据：用户提供 `C:\Users\Xiao\Desktop\Snipaste_2026-09-09_19-56-45.png`，明确要求“按照类似图片的效果添加一个 wiki 本地搜索页面，搜索逻辑不要重写，不要动其余实现与功能”。该图片是视觉参考，不作为新功能清单；可见结构为左侧窄导航、顶部左侧返回/主页/收藏与标题、右侧搜索框、下方 Terraria 横幅和大块连续内容区域。截图中的置顶等按钮不构成新增功能授权。截图是会话附件，不是构建依赖；下面的结构说明作为持久验收依据。

最终视觉依据为用户随后提供的 `C:\Users\Xiao\Desktop\Snipaste_2026-09-09_20-03-15.png`：有数据时直接展示完整本地 Wiki 首页，包含原站横幅、欢迎区、推荐资源包、游戏简介及后续内容。前一张“请先下载数据”仅代表空库情形；不得把它做成正常首页，也不以原生横幅加留白替代真实首页内容。

2026-09-12 首次真实窗口反馈依据为 `C:\Users\Xiao\Desktop\Snipaste_2026-09-12_17-31-17.png`。截图确认首页、唯一 Viewer 与本地数据均已正确显示，但顶部连续出现三层相近信息：64 DIP 应用顶栏中的“Terraria AI Wiki”、独立 Wiki 工具栏中的“Terraria Wiki / 本地搜索”，以及 Reader 标题栏中的“Terraria Wiki / Terraria Wiki”。用户认为三个抬头挤在一起丑陋，要求先规划把中间 Wiki 工具栏合并进应用顶栏、把本地搜索移到最上方，并删除下方无必要的重复标题栏。

本次反馈的职责判断：网页内 Terraria Logo 与首页正文属于真实本地 Wiki 内容，不是重复原生抬头，不修改；Reader 标题栏中的页面返回和收藏仍有真实行为，不能随视觉栏一起静默删除，因此只在独立 Wiki 模式隐藏该标题栏，并把正文需要的返回/收藏命令迁到最上方的 Wiki 上下文操作。非 Wiki 页面仍保留原 Reader 标题栏、关闭按钮和宽窄行为；Reader 底部状态栏继续承担加载、成功和安全错误反馈，不属于本次要求删除的抬头栏。

补充源码证据：`WikiViewModel.Query` 已驱动 180 ms 防抖，`SearchCommand` 已支持立即查询；`SearchCoreAsync` 已处理取消、请求身份、结果映射和安全错误。新页面直接绑定这些能力，不改造查询流程。当前 `IsEmpty` 不能独立区分失败/不可用与零结果，因此新视图优先直接展示现有 `StatusMessage`，不再叠加硬编码“没有找到结果”。现有 `assets/windows/Viewer/Web/Terraria_Wiki_zh/index.html` 已提供 Logo/站点样式及动态正文容器；首页内容由本地数据提供，并非写死在该模板。`MainWindow.cs` 已使用活动 Wiki 的 `DefaultPageTitle` 设置首页，`Reader.OpenAsync` 是既有阅读入口；`SetHomePageAsync` 只设置 Reader 元信息，不能当作实际导航。实施时从现有活动 Wiki 配置取首页身份，经现有打开路径导航，不硬编码“首页”或另建查询接口。

## 目标与完成定义

1. 左侧导航新增独立“Wiki”入口，保持现有五个入口的位置、命令和业务功能不变。
2. 选择 Wiki 后，右侧整个工作区进入本地 Wiki 模式；不显示 AI/收藏/资料库等主页面，也不显示普通页面的左右 Reader 分栏。
3. Wiki 模式只保留一层原生顶栏：沿用现有 64 DIP Shell 顶栏及唯一“Terraria AI Wiki”产品标题，把 Wiki 主页/返回搜索/页面返回/收藏等按状态出现的操作与本地搜索放到同一栏；删除独立 `WikiToolbar` 行，不再新增“Terraria Wiki”原生标题。搜索框在首页、结果和阅读状态均可访问，直接绑定现有 Query/SearchCommand，只通过现有 Application Search 查询当前活动 Wiki。
4. 初次进入 Wiki 时通过唯一 Viewer 展示当前活动 Wiki 的本地首页；输入搜索后切到全宽结果，选择结果后在同一区域显示正文。首页、结果和正文为三个页面展示状态，不另开 Reader 分栏。
5. Wiki 首页和正文继续使用唯一 NativeWebView、现有页面/资源/内部链接、收藏、返回和本地 Viewer transport；独立 Wiki 模式隐藏 Reader 自带标题栏，但正文状态仍能从最上方调用现有 Reader 返回与收藏命令，不创建第二个 Viewer。
6. 用户可以从正文回到当前搜索结果或重新搜索；切换到其他左侧导航后，对应页面恢复进入 Wiki 前的正常布局和 Reader 状态。
7. 右上“打开 Wiki”在非 Wiki 页面继续只打开共享 Reader，1100 DIP 宽窄行为、Chat 状态、活动回答、收藏、资料库、数据管理和设置均不改变。
8. 搜索覆盖初始、搜索中、有结果、无结果、不可用、失败和过期请求；错误不泄漏路径或底层异常。
9. Windows Release XAML 编译、相关/全量 .NET tests 和真实 Windows Light/Dark、1280×820、900×600 验收通过后，才标记完成。

## 前置条件

- Phase 00–10 已完成；本功能复用既有 Search、Reader 和 Viewer。
- Phase 11 仍在收尾，但本功能不依赖公开发布输入或安装器重建。
- 原全宽 Wiki 计划已获批准并由 `f73d49e` 实施；本次单顶栏视觉修订须用户批准后才修改 XAML/C#，当前轮只更新计划。
- 实施前确认 Git 状态和恢复点，只提交本变更，不 push。
- 不需要真实 AI API Key、付费请求、在线 Wiki 请求或 Android 工具链。

## 范围与非目标

范围内：

- 左侧 Wiki rail 入口、选中状态、Automation ID/Name 和 Shell 导航命令。
- Wiki 选中时的全工作区搜索状态与正文浏览状态。
- 复用同一个 ReaderHost/NativeWebView 的布局切换，以及离开 Wiki 后恢复普通页面布局。
- 现有 Wiki 搜索页面的全宽结果布局、状态和可访问性。
- 独立 Wiki 模式的单顶栏信息层级：移除 `WikiToolbar` 布局行，把搜索与必要上下文操作合并到现有 Shell 顶栏；隐藏重复 Reader 标题栏并迁移其必要命令。
- Windows Presentation 持续契约和相关回归；README/项目基线已准确描述六入口与全宽 Wiki 能力，本次纯视觉收敛无需重复更新。

非目标：

- 不采用“搜索结果在左、Reader 在右”的 Wiki 页面布局。
- 不修改其他五个页面的 XAML、ViewModel、用户流程或可见文案；必要接线只限 Shell 和 Wiki 页面。编译问题不作为扩大范围的理由。
- 不改变非 Wiki 页面右上“打开 Wiki”、宽屏分栏、窄屏覆盖、关闭、Esc 或设置页隐藏行为。
- 不全局删除或改版共享 Reader 标题栏；Chat 来源、收藏、资料库和数据管理打开 Reader 时仍显示原返回、页面身份、收藏和关闭操作。
- 不删除 Reader 底部状态栏；加载、打开结果和安全错误仍须可观察。
- 不合并或删除“Wiki 资料库”；它仍负责全部页面和对话历史，不承担正文全文检索页面。
- 不修改搜索排序/匹配、FTS5、SQLite schema、数据包、同步、下载、Application 公共接口或 Viewer 内容协议。
- 不新增第二个 Reader/WebView/Viewer session，不移动 Viewer backend，不升级依赖。
- 不增加在线搜索、查询历史、建议服务、UI 框架、图标包、Android 或其他平台实现。
- 不重写或抽取 `WikiViewModel` 搜索流程，不改 `SearchCoreAsync`、180 ms 防抖、取消/请求身份判断、结果映射、查询参数和数据源重置逻辑；不添加第二份搜索状态机、客户端过滤/排序或分页。
- 不照搬截图中的置顶、旧浏览历史、同步和主题切换入口，不更换现有 Shell 外观、默认启动页面或空库启动引导。
- 不清理共享样式、修复相邻历史问题、重新打包或实施其他 change。

## P0 / 兼容性影响

- 手动搜索与离线阅读是 P0。Search 继续使用 `WikiQueryUseCases.SearchAsync`，正文继续使用现有 Application/Viewer 边界。
- Wiki 模式只改变 Presentation 布局和状态，不改变数据、页面身份、Redirect、资源或内存返回栈语义。
- 同一个 `ReaderHost` 在普通页面保持现有分栏/覆盖位置，在 Wiki 页面占满工作区；不得复制或把 NativeWebView 放入第二个 visual parent。
- 进入 Wiki 前仅保存恢复布局所需的 Reader 开关/展示状态；离开后按原导航规则恢复。共享 Viewer 继续保留最近实际阅读内容及原内存返回栈，不承诺恢复独立的旧正文、滚动位置或历史快照，不新建页面缓存或第二套返回栈。
- 活动数据源变化继续取消搜索并清空旧结果；AI、资料库和 Viewer 仍读取同一数据。
- 无数据迁移和不可逆操作；回退单独 Presentation 提交即可恢复现状。

## 方案与待决问题

### 1. 左侧导航

- 在现有主 rail 中增加“Wiki”按钮，使用 `rail-nav`、非纯颜色选中指示、现有 PathIcon 语言、Tooltip、`WikiNavigation` Automation ID 和准确 Automation Name。
- `ShellViewModel` 增加 `NavigateWikiCommand` / `NavigateWiki()`；其他五个导航命令不改变业务语义。
- Wiki 入口与“Wiki 资料库”通过图标、Tooltip 和页面标题区分：前者搜索并阅读正文，后者浏览页面清单与对话历史。

### 2. 全宽 Wiki 页面状态

- Wiki 首页态：同一个 ReaderHost 占满工作区，使用现有活动 Wiki 的 DefaultPageTitle 与阅读入口显示真实本地首页及原站横幅/正文/资源。只在必要时导航，不因布局更新反复打开首页。
- Wiki 搜索态：在现有工作区内让 `WikiView` 占满可用宽度；普通 Reader splitter 和 overlay 不显示。若 WikiView 仍在 `MainSurface` 内，不隐藏它的父级；只切换互斥子视图和 Grid 占位。
- Wiki 阅读态：搜索结果退出可见区域，同一个 `ReaderHost` 跨越整个工作区显示所选页面；不保留左右分栏。
- 删除 Shell 工作区内独立的 `WikiToolbar` 行；Wiki 搜索和上下文操作合并进现有 64 DIP 顶栏，工作区从顶栏下方直接开始首页、结果或正文，不再多占一层垂直空间。
- 独立 Wiki 首页/正文隐藏 Reader 自带 48 DIP 标题栏，消除重复页面标题、位置文本和空闲关闭位。Reader 底部状态条保留；非 Wiki 页面 Reader 标题栏完全保持原样。
- Reader 返回与收藏行为不删除：仅在 Wiki 正文态以 44×44 DIP 图标按钮出现在最上方，继续直接绑定 `Reader.BackCommand` 与 `Reader.ToggleFavoriteCommand`。主页和“返回搜索结果”继续使用原 Shell command，语义与 Viewer 返回明确区分；首页不显示无意义的返回/收藏操作。
- 从 Wiki 切换到其他 rail 入口时恢复进入前的普通 Reader 状态和现有响应式列；Chat 活动回答不取消。
- 非 Wiki 页面继续使用当前 `MainSurface + GridSplitter + ReaderHost` 布局，右上“打开 Wiki”命令与可见行为不变。

### 3. 搜索行为

- 原样复用现有 180 ms 防抖、CancellationToken、请求身份检查、Search DTO 和匹配文案；搜索图标及 Enter 直接调用现有 SearchCommand，不另写事件处理查询路径。
- 页面只消费 Query、IsAvailable、IsSearching、HasResults、Results 和 StatusMessage。状态提示独占一行，不覆盖结果；不再用 IsEmpty 叠加通用零结果文案。Shell 只增加首页/结果区域与正文区域的显示编排，不接管搜索内部状态。
- 清空输入后由 Shell 切回本地首页；搜索结果清理由 Query 既有逻辑完成。选中结果进入阅读态；从阅读态返回搜索时保留查询和结果，用户可继续选择或修改搜索。
- 阅读中修改非空查询时切回现有结果区，仍由 Query 原 setter 发起搜索；返回搜索不额外查询。“主页”清空查询并经 Reader.OpenAsync 打开活动 Wiki 的 DefaultPageTitle；不调用在线接口，也不改共享返回栈语义。
- 搜索结果展示标题、匹配类型、可选 Redirect 和纯文本摘要；不改变 Core highlight/排序语义。

### 4. 视觉与响应式

- 参考图布局（只在 Wiki 选中时出现，保留应用现有外层 Shell）：

  ```text
  现有左侧导航 │ Terraria AI Wiki  [按状态：主页 / 返回搜索 / 页面返回 / 收藏]  [输入关键词搜索…  搜索]
               ├──────────────────────────────────────────────────────────────────
               │ 首页：唯一 Viewer 显示原站横幅与本地首页正文
               │       欢迎区 → 推荐资源包 → 游戏简介 → 其余已有内容
               │ 结果：全宽连续列表，标题 / 匹配类型 / Redirect / 摘要
               │ 正文：唯一 WebView + 原状态条（不再显示重复 Reader 标题栏）
  ```

- 顶栏左侧继续只显示产品标题“Terraria AI Wiki”，不再增加原生“Terraria Wiki”标题；网页自身 Terraria Logo 保留。右侧使用单组紧凑上下文图标与搜索输入，placeholder 为“输入关键词搜索…”，搜索框与图标保留稳定 Automation ID/Name。可见“本地搜索”文字可省略，因为占位符、搜索图标和 Automation Name 已表达用途，避免再次制造标题噪音。
- Wiki 首页态只显示标题和搜索；搜索结果态增加主页；正文态按可用性显示主页、返回搜索结果、Viewer 返回和收藏。各操作使用现有图标语言、ToolTip 和准确 Automation Name，不把页面返回与返回搜索混为一个命令。
- 首页横幅、欢迎区、推荐资源包、游戏简介和页面链接全部来自现有 Viewer 模板与本地首页 HTML；不在 Avalonia 中重画或硬编码这些版块，不修改 HTML/CSS/JS/图片。实际首页版本随用户已有数据而定，截图文字不是固定数据契约。
- 搜索结果使用原生全宽列表；首页和正文使用同一个 WebView。原站横幅是否出现在正文、尺寸、滚动和主题表现全部沿用现有 Viewer，不另加或隐藏网页装饰。不同活动 Wiki 继续使用各自首页与站点资源，不强行套中文 Terraria 内容。
- 有首页数据时直接显示完整首页；仅在已有宿主状态明确表示空库时提示“请先下载或导入数据”，通过现有导航进入数据管理。缺少单独首页、零搜索结果、初始化中和数据源不可用沿用现有错误/状态，不伪造欢迎内容、不自动下载、不改首次启动引导。
- 搜索内容区采用 24 DIP 内边距、完整可用宽度和连续列表，去除 WikiView 的固定 360 DIP 宽度；不新增卡片阵列、重复说明或推荐词条。
- 结果和正文都使用整个工作区；900×600 时不退回普通 Reader 覆盖层，也不依赖水平滚动。
- 1280×820 时顶栏保持单行；900×600 时搜索输入允许在剩余宽度内收缩到可用下限，上下文操作使用 44×44 DIP 图标并只按当前状态出现，不隐藏搜索或产生水平滚动。若真实窗口证明单行仍不足，再只让搜索区在同一顶栏容器内响应式重排，不恢复独立第二栏。首页/正文沿用原 Viewer 响应式和主题同步，不为截图修改网页尺寸或样式。

### 5. 文档与测试

- 更新 Windows Presentation spec：五入口改为六入口；新增全宽 Wiki 搜索/阅读模式；普通页面 Reader 与顶部按钮契约保持原文语义。
- README 和 `openspec/project.md` 只更新当前入口/能力摘要；Master Architecture 的 P0、Phase 和平台方向不变。
- 替换“不得出现 Wiki 导航”的旧断言，增加 Wiki 搜索态/阅读态、单 Viewer、普通页面布局恢复和其他导航不变回归。
- 增加单顶栏静态/行为回归：Shell 不再包含 `WikiToolbar` 独立工作区行或原生“Terraria Wiki”标题；搜索仍绑定原命令；Wiki 正文顶栏能调用现有 Reader 返回/收藏；非 Wiki Reader 标题栏与关闭行为不变。

替代方案：

- Wiki 页面采用左搜索、右 Reader：用户明确否决，不采用。
- 只让右上“打开 Wiki”进入全屏：用户明确要求新增左侧 Wiki 导航，不采用。
- 为全宽页面创建第二个 NativeWebView：违反唯一 Viewer 契约，不采用。
- 把全文搜索塞入“Wiki 资料库”：职责不同且用户要求独立页面，不采用。
- 直接连同 Reader 标题栏删除返回/收藏：视觉更空，但会静默丢失已承诺的正文行为，不采用；迁移命令到顶栏。
- 全局删除 Reader 标题栏：会改变 Chat 来源、收藏和资料库等未授权页面，不采用；只在独立 Wiki 模式隐藏。

待决问题：无阻塞项。当前计划把用户所说“下面那一栏”解释为截图中紧邻 Wiki 工具栏的 Reader 标题栏；Reader 底部“已打开/正在打开/失败”状态条继续保留。如用户希望连底部状态反馈也取消，需要另行明确其替代表达，不能直接让错误不可见。

## 文件变更

- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml`：删除独立 `WikiToolbar` 工作区行；把 Wiki 搜索、主页、返回搜索、Reader 返回和收藏按状态合并到现有 Shell 顶栏；普通页面顶栏和 Wiki 预览保持原样。
- **MODIFY IF REQUIRED** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs`：只增加 Wiki 专用 Reader chrome/顶栏可见性派生属性；不改变现有三态、搜索或普通 Reader 状态恢复。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderView.axaml`：只在独立 Wiki 模式隐藏 48 DIP Reader 标题栏；非 Wiki Reader 标题栏与底部状态栏保持原样。
- **KEEP** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml.cs`：`WikiSearchInput` 名称和 Enter 接线保持，工作区仍只重排同一个 ReaderHost。
- **KEEP** `src/Terraria_Wiki.Windows/MainWindow.cs`、`Presentation/Wiki/WikiView.axaml`：首页接线、全宽结果列表和状态不因顶栏收敛改变。
- **KEEP** `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiViewModel.cs`：Query、SearchCommand、搜索流程和结果映射原样复用；页内导航状态放在 Shell 的 Wiki 专属接线中。
- **KEEP** `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderPaneViewModel.cs` 与共享 `Styles/Controls.axaml`、`Styles/ThemeResources.axaml`：直接复用现有命令、图标按钮样式和 token，不调整通用行为或清理样式。
- **KEEP** `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj` 与 `assets/windows/`：首页及横幅由现有 Viewer 加载，不新增资源引用、依赖或构建/发布修改。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationFlowTests.cs`：覆盖 Wiki 导航、全宽搜索/阅读切换、结果返回和普通 Reader 状态恢复。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationBoundaryTests.cs`：覆盖六入口、唯一 Viewer、Wiki 全宽布局和其他页面边界。
- **MODIFY** `openspec/specs/windows-presentation/spec.md`：把 Wiki 独立工具栏契约收敛为共享 Shell 单顶栏和上下文 Reader chrome；**KEEP** `README.md`、`openspec/project.md`，因为入口、能力和项目状态没有变化。
- **KEEP** Application、Core、Viewer backend、Agent、SQLite/schema、数据包和 Viewer assets。

## 执行步骤

- [x] 用户批准修订计划后将状态改为 `in-progress`，确认 Git 状态和恢复点。
- [x] 接入左侧 Wiki 导航和 Shell 状态；先用行为测试锁定其他五个入口及普通 Reader 状态不变。
- [x] 接入参考图风格的 Wiki 工具栏、现有 Viewer 本地首页和全宽结果区；直接绑定现有搜索与状态属性，补齐可访问性，不修改搜索 ViewModel 或重画首页内容。
- [x] 接通搜索结果到全宽单一 ReaderHost，并实现返回搜索且保留查询结果。
- [x] 实现 Wiki/普通页面布局切换；验证同一个 NativeWebView 不重建、不重挂第二父级。
- [x] 更新 Presentation flow/boundary 回归与持续文档。
- [x] 执行 WSL 静态检查、Windows Release build、定向/全量 .NET tests。
- [x] 用户完成首轮真实 Windows 首页检查：功能与内容成立，但三层重复抬头导致视觉验收未通过。
- [x] 更新计划进度与验证结果，只提交当前变更，不 push；用户验收后再合并。
- [x] 记录 2026-09-12 首次真实窗口反馈并完成单顶栏修订计划；本轮不修改 XAML/C#。
- [ ] 用户批准本次单顶栏修订方案。
- [ ] 删除独立 `WikiToolbar` 行，把本地搜索与上下文操作合并到现有 64 DIP Shell 顶栏；验证 1280×820 与 900×600 不拥挤。
- [ ] 独立 Wiki 首页/正文隐藏 Reader 标题栏，并在正文态从顶栏继续提供现有返回/收藏；普通页面 Reader chrome 不变。
- [ ] 更新 Presentation flow/boundary 回归与持续 spec；执行 Windows Release build、定向/全量 tests 和第二轮真实窗口验收。
- [ ] 将修订作为独立提交保留在当前功能分支，不合并、不 push；用户验收通过后再关闭 change。

## 验证与回归计划

### WSL / 静态

- `git diff --check`；核对只修改计划内文件。
- 确认源码仍只有一个 `<NativeWebView` 和一个 `new ViewerWebViewSession(`。
- 确认 Presentation 不新增 SQLite/Core 数据服务、在线 Wiki、第二 transport 或依赖。
- 对照 `f73d49e` 检查：`WikiViewModel`、`ReaderPaneViewModel`、Application/Core、其他五个页面、共享样式、csproj 和 Viewer 资产均无修改；`ReaderView.axaml` 只允许增加独立 Wiki 模式的标题栏可见性，不改命令与底部状态。
- 检查 Wiki XAML 无 raw hex，导航/搜索/返回操作有 Automation Name，交互 style 状态完整。
- 对比持续 spec，确认普通页面顶部按钮、Reader 宽窄阈值和设置页行为未被改写。

### Windows 11 x64

- Windows .NET 10 Release build：9 个项目，要求 0 warning / 0 error，并编译 Avalonia XAML。
- 定向运行 `PresentationFlowTests`、`PresentationBoundaryTests`，再运行全量 .NET tests；不发 AI 请求、不下载 Wiki、不修改真实数据。
- 1280×820 与 900×600、Light/Dark：点击 Wiki rail 后搜索页占满工作区；选择结果后正文仍占满工作区，没有左右分栏或覆盖 Reader。
- 使用已含首页的本地测试数据，对照 2026-09-12 反馈图验收：应用顶栏只保留一个产品标题并包含搜索，下面直接开始首页/结果/正文；不再出现独立 `WikiToolbar` 或 Reader 重复标题栏。原站横幅、欢迎区、推荐资源包、游戏简介按本地 HTML 实际呈现，链接沿用既有处理。检查初次进入和主页按钮、首页→搜索→正文→返回搜索、900×600 单顶栏收缩、Enter/Tab、焦点，以及空库/首页缺失时的真实状态；不为获得截图数据自动下载 Wiki。
- 标题、Redirect、正文、无结果和快速连续输入；验证结果排序沿用 Application、旧请求不覆盖新结果、错误安全。
- 从正文返回搜索保留查询/结果；内部链接、Viewer 返回、收藏和状态条可用。
- 分别从 Chat、收藏、资料库、数据管理进入/退出 Wiki，确认原页面状态与 Reader 开关恢复；设置页和顶部“打开 Wiki”行为不变。
- 验证整个过程中只有同一个 NativeWebView/Viewer session，窗口退出清理不回归。

### 非目标环境

- Android 和其他平台不构建、不验证、不宣称支持。
- WSL 结果不得记作 Windows 客户端通过。

## 风险与回退

- 单一 NativeWebView 全宽切换可能触发 visual parent、尺寸或 airspace 问题：保持 `ReaderHost` 在同一 Shell 父级，只切换 Grid 占位/可见性，不复制或动态重建控件。
- Wiki 模式可能污染普通页面 ReaderState：进入前保存、离开时恢复，并用每个现有入口的状态回归约束。
- 搜索态与阅读态切换可能丢失查询/结果：状态保留在现有 `WikiViewModel`，返回搜索不重新请求，除非活动数据源已变化。
- 新 rail 在 900×600 或高缩放下可能拥挤：实际检查 56×56 命中区和全部入口，不隐藏主功能。
- `Wiki` 与“资料”含义可能混淆：Wiki 使用搜索/浏览语义图标与 Tooltip，资料保持书库/历史语义。
- 旧测试明确禁止 Wiki 导航：只替换被新需求推翻的断言，其他五入口和唯一 Viewer 不变量继续保留。
- 单顶栏在正文态同时承载上下文操作和搜索，900×600 可能拥挤：使用仅按状态出现的 44×44 图标和可收缩搜索输入；实机不足时只在同一顶栏容器内响应式重排，不恢复第二栏。
- 隐藏 Wiki Reader 标题栏可能意外影响普通 Reader：可见性必须只由独立 Wiki 选中态控制，并以 Chat 来源、收藏、资料库、数据管理及宽窄 Reader 回归锁定。
- 回退只恢复独立 Presentation/文档提交；无数据、schema 或包格式迁移。

## 进度记录

- 2026-09-09：提交现有工作区内容、UI 文案和单模型配置，创建第一版 draft 计划。
- 2026-09-09：用户以实机截图指出当前没有 Wiki 入口，并否决“左搜索 + 右 Reader”方向。
- 2026-09-09：用户最终澄清：新增左侧 Wiki 导航；该页面全宽完成本地搜索与正文浏览；其他页面和功能全部不动。
- 2026-09-09：重写本计划为全宽 Wiki 搜索/阅读模式；下一步等待用户批准，不实施源码。
- 2026-09-09：按用户新参考图补充工具栏、右侧搜索、首页横幅、连续内容区和窄窗布局；收紧为搜索 ViewModel、共享 Reader 与其他页面实现全部 KEEP，仅修订此 plan。实施仍未开始。
- 2026-09-09：用户进一步以 20:03:15 截图确认有数据首页效果；最终方案改为现有 Viewer 的完整本地 Wiki 首页，取消原生横幅/留白首页和新增图片资源引用。新增入口、首页/搜索/正文编排仍为唯一实施范围。
- 2026-09-12：用户批准实施。当前 `HEAD` 为 `24ee9d9`；建立 `checkpoint/pre-add-local-wiki-search-page-20260912` 保存点，并从同一提交创建 `feature/add-local-wiki-search-page`。实施前已有的 AI change 归档移动和 `package-core-source-rar` 未跟踪目录保持原样，不纳入本变更提交。
- 2026-09-12：接入第六个“Wiki”入口、页内工具栏和首页/搜索/正文三态；活动 Wiki 的 `DefaultPageTitle` 由现有 Windows 宿主传入 Shell，页面搜索仍原样复用 `WikiViewModel`，正文仍使用唯一 ReaderHost。
- 2026-09-12：增加 Wiki 模式与普通 Reader 状态恢复回归，同步 Presentation spec、项目基线和 README。Windows Release build 与 349 项全量 .NET tests 已通过；等待用户真实窗口验收，未合并、未 push。
- 2026-09-12：用户提供首次真实窗口截图，确认功能链路可用但拒绝三层重复抬头；要求先计划把 Wiki 工具栏并入 Shell 顶栏、把本地搜索移到最上方并删除下方重复标题栏。本轮只更新 plan，提交 `f73d49e` 的实现保持不动。
- 2026-09-12：用户决定归档本 change，并将另建新 plan 继续优化。已记录但尚未执行的单顶栏修订、第二轮构建/测试和真实窗口复验均不在本 change 内完成；功能分支保持未合并、未 push。

## 发现、偏差与决策

- 发现：搜索、结果模型和 Viewer 均已存在；主要工作是新导航和单 Viewer 的两种 Shell 布局，而不是后端开发。
- 决策：Wiki 页面内部使用“本地首页 ↔ 全宽搜索结果 ↔ 全宽正文”，首页与正文复用唯一 Viewer，不使用搜索/Reader 分栏。
- 决策：普通页面继续采用既有共享 Reader 分栏/覆盖；新布局只在 `ShellSection.Wiki` 生效。
- 决策：保持一个 ReaderHost/NativeWebView/Viewer session，通过同父级布局切换复用。
- 决策：不触碰其他五个页面的功能与视觉，不进入相邻重构。
- 决策：本次使用 terraria-ui 的语义 token、轻量列表、状态/键盘及 Windows 验收要求；用户参考图决定 Wiki 首页横幅与工具栏形态。Skill 中旧 `terraria-wiki-offline/src/` 路径按根治理规则解析为当前根级 `src/`，不恢复旧 checkout。
- 发现：Local Viewer spec 仍记有 `RecordVisitAsync`，与当前项目基线所述旧浏览历史停止读写不一致；本计划不恢复历史写入，也不顺带修订该独立文档问题。
- 发现：保存分支 `preserve-wiki-search-page-implementation` 提供了单 Reader 布局草稿，但基于旧代码线且未把 `DefaultPageTitle` 接入 Shell，也遗漏部分派生可见性通知；本次只手工复用布局思路并在当前基线上补齐，未 cherry-pick 该提交。
- 决策：`WikiViewModel`、`ReaderPaneViewModel`、共享样式和 Viewer 资产保持零修改；主页显式操作和进入 Wiki 时通过现有 `Reader.OpenAsync` 导航，布局重算不触发重复导航。
- 发现：首次真实截图中的三层拥挤来自两个原生标题层加一个 Reader chrome，不是 Viewer 首页内容问题；实际本地首页、资源和宽度均已达到功能目标。
- 决策：收敛为一个原生 Shell 顶栏；网页 Terraria Logo 作为正文品牌内容保留。独立 Wiki 模式隐藏 Reader 标题栏但迁移其正文返回/收藏命令，普通页面 Reader 与底部状态反馈不变。

## 验证结果

- 规划基线 Windows Release build 已通过 9 个项目、0 warning / 0 error；该证据不包含计划中的新功能。
- 旧计划记载的规划阶段验证只作为实施前基线，不作为本次新页面的完成证据。
- 2026-09-12 Windows `.NET 10` Release solution build：9 个项目成功，0 warning / 0 error；包含 Avalonia XAML 编译。
- 2026-09-12 定向 `PresentationFlowTests|PresentationBoundaryTests`：38/38 通过。
- 2026-09-12 Windows 全量 `.NET` tests：349/349 通过（Core 97、Application 56、WikiTools 45、Windows 151）。
- 2026-09-12 WSL 静态检查：`git diff --check` 通过；源码仍只有一个 `<NativeWebView` 和一个 `new ViewerWebViewSession(`；计划要求 KEEP 的 `WikiViewModel`、Reader、共享 styles、Application/Core、Viewer assets 与项目依赖均无改动。Skill 目录未提供独立 validator 脚本，按 `terraria-ui` 检查表完成源码/自动化检查。
- 未验证：真实 Windows 窗口的 Light/Dark、1280×820、900×600、实际本地首页内容与交互；按用户要求交由用户验收后再合并。本轮未运行 publish/安装器，不产生付费 AI、在线 Wiki 或下载请求。
- 2026-09-12 首次真实窗口：用户截图确认本地首页与 Viewer 正常显示；视觉验收未通过，原因是三个 Terraria Wiki 相关抬头连续堆叠。单顶栏修订尚未实施、构建或验证。

## 完成摘要

首版实现与自动化验证位于 `feature/add-local-wiki-search-page` 的 `f73d49e`，保存点为 `checkpoint/pre-add-local-wiki-search-page-20260912`。首次真实窗口功能链路成立但视觉验收未通过；单顶栏修订没有实施、构建或复验，功能分支未合并、未 push。用户于 2026-09-12 主动结束并归档本 change，因此不标记为 `complete`，后续优化由新 plan 承接。
