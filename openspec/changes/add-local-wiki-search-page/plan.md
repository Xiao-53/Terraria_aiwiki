# 新增全宽本地 Wiki 浏览搜索页面

- 状态：draft；等待用户批准后实施
- Phase：无；独立 Windows Presentation 变更
- 执行环境：WSL2 Codex CLI 负责阅读、编辑、计划维护与静态检查；Windows 11 x64 负责 .NET/Avalonia 构建、测试与真实窗口验收
- 创建日期：2026-09-09
- 最后更新：2026-09-09
- 范围：Windows Avalonia 左侧 Wiki 导航、全宽本地 Wiki 搜索/浏览页面、唯一 Viewer 布局编排、Presentation 回归与持续文档
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`

## 背景与当前证据

用户要求增加本地 Wiki 界面，并在查看当前 Windows 实机界面后两次澄清最终形态：左侧新增“Wiki”导航；进入后是一个占满右侧工作区的本地 Wiki 搜索与浏览页面，不采用“左侧搜索结果 + 右侧 Reader”的分栏；AI、收藏、Wiki 资料库、数据管理、设置以及这些页面现有的 Reader 行为全部不动。

已确认事实：

- 当前 Shell 对外只有“AI 问答”“收藏”“Wiki 资料库”“数据管理”“设置”五个左侧入口，右上“打开 Wiki”只在当前主页面旁打开或恢复共享 Reader。
- `ShellSection.Wiki`、`IsWikiSelected`、`IsWikiVisible`、`WikiView` 和 `WikiViewModel` 已存在，Shell 已挂载隐藏的 `WikiView`，但没有导航 command/button；边界测试还明确断言不存在 Wiki 导航。
- `WikiViewModel` 已通过 `WindowsClientRuntime.Application.Query.SearchAsync` 使用当前活动本地 Wiki，具备 180 ms 防抖、旧请求取消、结果映射和安全错误处理；无需新增搜索服务。
- 搜索结果当前通过 `OpenSearchResultAsync` 打开唯一 `ReaderPaneViewModel`，全应用只有一个 `NativeWebView` 和一个 `ViewerWebViewSession`。
- 普通页面现有 Reader 规则已确认：宽度不小于 1100 DIP 时内联分栏，较窄时使用右侧覆盖层；设置页暂时隐藏 Reader。用户要求这些行为保持不变。
- 当前隐藏 `WikiView` 是 360 DIP 搜索切片，只适合与右侧 Reader 并排，不符合本次全宽 Wiki 页面要求。
- 进行中的 Phase 11 与 AI 会话 Token 变更不需要为本功能改动打包、Agent、Sidecar、会话存储或 Wiki Tool 协议。

当前 Git 恢复基线为 `3641997`。本计划替换该提交中的第一版 draft；第一版错误地把 Wiki 页面定义为搜索列表与右侧 Reader 分栏，现已由用户明确否决。

## 目标与完成定义

1. 左侧导航新增独立“Wiki”入口，保持现有五个入口的位置、命令和业务功能不变。
2. 选择 Wiki 后，右侧整个工作区进入本地 Wiki 模式；不显示 AI/收藏/资料库等主页面，也不显示普通页面的左右 Reader 分栏。
3. Wiki 模式提供本地搜索框和全宽结果列表；输入词条或关键词后只通过现有 Application Search 查询当前活动 Wiki。
4. 选择结果后，Wiki 正文在同一个全宽工作区内打开，不在结果列表右侧另开 Reader；搜索结果与正文是同一 Wiki 页面内的两个状态。
5. Wiki 正文继续使用唯一 NativeWebView、现有页面/资源/内部链接、收藏、返回和本地 Viewer transport，不创建第二个 Viewer。
6. 用户可以从正文回到当前搜索结果或重新搜索；切换到其他左侧导航后，对应页面恢复进入 Wiki 前的正常布局和 Reader 状态。
7. 右上“打开 Wiki”在非 Wiki 页面继续只打开共享 Reader，1100 DIP 宽窄行为、Chat 状态、活动回答、收藏、资料库、数据管理和设置均不改变。
8. 搜索覆盖初始、搜索中、有结果、无结果、不可用、失败和过期请求；错误不泄漏路径或底层异常。
9. Windows Release XAML 编译、相关/全量 .NET tests 和真实 Windows Light/Dark、1280×820、900×600 验收通过后，才标记完成。

## 前置条件

- Phase 00–10 已完成；本功能复用既有 Search、Reader 和 Viewer。
- Phase 11 仍在收尾，但本功能不依赖公开发布输入或安装器重建。
- 用户批准修订后的计划后才进入 `in-progress`；本轮只修订规划，不开始业务源码实现。
- 实施前确认 Git 状态和恢复点，只提交本变更，不 push。
- 不需要真实 AI API Key、付费请求、在线 Wiki 请求或 Android 工具链。

## 范围与非目标

范围内：

- 左侧 Wiki rail 入口、选中状态、Automation ID/Name 和 Shell 导航命令。
- Wiki 选中时的全工作区搜索状态与正文浏览状态。
- 复用同一个 ReaderHost/NativeWebView 的布局切换，以及离开 Wiki 后恢复普通页面布局。
- 现有 Wiki 搜索页面的全宽结果布局、状态和可访问性。
- Windows Presentation 持续契约、README/项目当前状态和相关回归。

非目标：

- 不采用“搜索结果在左、Reader 在右”的 Wiki 页面布局。
- 不修改其他五个页面的 XAML、ViewModel、用户流程或可见文案，除非编译证明 Shell 接线必须做最小适配。
- 不改变非 Wiki 页面右上“打开 Wiki”、宽屏分栏、窄屏覆盖、关闭、Esc 或设置页隐藏行为。
- 不合并或删除“Wiki 资料库”；它仍负责全部页面和对话历史，不承担正文全文检索页面。
- 不修改搜索排序/匹配、FTS5、SQLite schema、数据包、同步、下载、Application 公共接口或 Viewer 内容协议。
- 不新增第二个 Reader/WebView/Viewer session，不移动 Viewer backend，不升级依赖。
- 不增加在线搜索、查询历史、建议服务、UI 框架、图标包、Android 或其他平台实现。

## P0 / 兼容性影响

- 手动搜索与离线阅读是 P0。Search 继续使用 `WikiQueryUseCases.SearchAsync`，正文继续使用现有 Application/Viewer 边界。
- Wiki 模式只改变 Presentation 布局和状态，不改变数据、页面身份、Redirect、资源或内存返回栈语义。
- 同一个 `ReaderHost` 在普通页面保持现有分栏/覆盖位置，在 Wiki 页面占满工作区；不得复制或把 NativeWebView 放入第二个 visual parent。
- 进入 Wiki 前记录普通页面 Reader 状态，离开时恢复，避免新页面影响 Chat、收藏等既有行为。
- 活动数据源变化继续取消搜索并清空旧结果；AI、资料库和 Viewer 仍读取同一数据。
- 无数据迁移和不可逆操作；回退单独 Presentation 提交即可恢复现状。

## 方案与待决问题

### 1. 左侧导航

- 在现有主 rail 中增加“Wiki”按钮，使用 `rail-nav`、非纯颜色选中指示、现有 PathIcon 语言、Tooltip、`WikiNavigation` Automation ID 和准确 Automation Name。
- `ShellViewModel` 增加 `NavigateWikiCommand` / `NavigateWiki()`；其他五个导航命令不改变业务语义。
- Wiki 入口与“Wiki 资料库”通过图标、Tooltip 和页面标题区分：前者搜索并阅读正文，后者浏览页面清单与对话历史。

### 2. 全宽 Wiki 页面状态

- Wiki 搜索态：隐藏普通 `MainSurface`、Reader splitter 和 overlay；`WikiView` 跨越整个工作区，显示搜索栏、状态和全宽结果列表。
- Wiki 阅读态：搜索结果退出可见区域，同一个 `ReaderHost` 跨越整个工作区显示所选页面；不保留左右分栏。
- Wiki 阅读头部提供返回搜索/重新搜索的真实入口，同时保留页面内部返回和收藏；不得通过覆盖 NativeWebView 的浮层实现，以避开原生 WebView airspace 问题。
- 从 Wiki 切换到其他 rail 入口时恢复进入前的普通 Reader 状态和现有响应式列；Chat 活动回答不取消。
- 非 Wiki 页面继续使用当前 `MainSurface + GridSplitter + ReaderHost` 布局，右上“打开 Wiki”命令与可见行为不变。

### 3. 搜索行为

- 保留现有 180 ms 防抖、CancellationToken、请求身份检查、Search DTO 和匹配文案。
- 用最小内部状态区分初始、搜索中、结果、无结果、不可用、失败和阅读正文，避免错误状态同时显示“没有找到结果”。
- 清空输入回到搜索初始态；选中结果进入阅读态；从阅读态返回时保留查询和结果，用户可继续选择或修改搜索。
- 搜索结果展示标题、匹配类型、可选 Redirect 和纯文本摘要；不改变 Core highlight/排序语义。

### 4. 视觉与响应式

- 搜索态采用 24 DIP 页面边距、完整可用宽度和连续列表，不使用固定 360 DIP、嵌套卡片或逐项阴影卡。
- 页面标题使用“本地 Wiki”，不增加重复副标题；搜索字段有永久标签、placeholder、焦点和 Automation Name。
- 结果和正文都使用整个工作区；900×600 时不退回普通 Reader 覆盖层，也不依赖水平滚动。
- 只使用现有语义 token/style。若旧 `wiki-search-pane` 改造后无引用，才做同范围清理。

### 5. 文档与测试

- 更新 Windows Presentation spec：五入口改为六入口；新增全宽 Wiki 搜索/阅读模式；普通页面 Reader 与顶部按钮契约保持原文语义。
- README 和 `openspec/project.md` 只更新当前入口/能力摘要；Master Architecture 的 P0、Phase 和平台方向不变。
- 替换“不得出现 Wiki 导航”的旧断言，增加 Wiki 搜索态/阅读态、单 Viewer、普通页面布局恢复和其他导航不变回归。

替代方案：

- Wiki 页面采用左搜索、右 Reader：用户明确否决，不采用。
- 只让右上“打开 Wiki”进入全屏：用户明确要求新增左侧 Wiki 导航，不采用。
- 为全宽页面创建第二个 NativeWebView：违反唯一 Viewer 契约，不采用。
- 把全文搜索塞入“Wiki 资料库”：职责不同且用户要求独立页面，不采用。

待决问题：无阻塞项。具体 Shell Grid 行列切换采用保持 `ReaderHost` 单一父级的最小实现，并由 XAML 编译和真实 Windows 窗口验证收敛。

## 文件变更

- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml`：增加 Wiki rail；为 Wiki 搜索态和全宽 Reader 态提供互斥布局，普通页面布局保持原样。
- **MODIFY IF REQUIRED** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml.cs`：只扩展现有布局计算，使单一 ReaderHost 在 Wiki/普通模式间切换位置，不重建控件。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs`：增加 Wiki 导航、搜索/阅读模式和普通 Reader 状态保存/恢复。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiView.axaml`：实现全宽搜索页和结果列表，不承载第二个 Viewer。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiViewModel.cs`：增加最小搜索/阅读状态和返回搜索命令，保留查询实现。
- **MODIFY IF REQUIRED** `src/Terraria_Wiki.Windows/Presentation/Reader/ReaderView.axaml`：只在 Wiki 全宽阅读态显示“返回搜索/重新搜索”入口；普通 Reader 头部行为不变。
- **MODIFY IF PROVEN UNUSED** `src/Terraria_Wiki.Windows/Presentation/Styles/Controls.axaml`：清理旧固定搜索卡 selector，或增加可复用且完整交互态的最小语义 class。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationFlowTests.cs`：覆盖 Wiki 导航、全宽搜索/阅读切换、结果返回和普通 Reader 状态恢复。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationBoundaryTests.cs`：覆盖六入口、唯一 Viewer、Wiki 全宽布局和其他页面边界。
- **MODIFY** `openspec/specs/windows-presentation/spec.md`、`README.md`、`openspec/project.md`：同步持续契约与当前状态。
- **KEEP** Application、Core、Viewer backend、Agent、SQLite/schema、数据包和 Viewer assets。

## 执行步骤

- [ ] 用户批准修订计划后将状态改为 `in-progress`，确认 Git 状态和恢复点。
- [ ] 接入左侧 Wiki 导航和 Shell 状态；先用行为测试锁定其他五个入口及普通 Reader 状态不变。
- [ ] 把现有 `WikiView` 改为全宽搜索态，补齐异步/错误/空状态和可访问性。
- [ ] 接通搜索结果到全宽单一 ReaderHost，并实现返回搜索且保留查询结果。
- [ ] 实现 Wiki/普通页面布局切换；验证同一个 NativeWebView 不重建、不重挂第二父级。
- [ ] 更新 Presentation flow/boundary 回归与持续文档。
- [ ] 执行 WSL 静态检查、Windows Release build、定向/全量 .NET tests 和真实窗口验收。
- [ ] 更新计划进度与验证结果，只提交当前变更，不 push。

## 验证与回归计划

### WSL / 静态

- `git diff --check`；核对只修改计划内文件。
- 确认源码仍只有一个 `<NativeWebView` 和一个 `new ViewerWebViewSession(`。
- 确认 Presentation 不新增 SQLite/Core 数据服务、在线 Wiki、第二 transport 或依赖。
- 检查 Wiki XAML 无 raw hex，导航/搜索/返回操作有 Automation Name，交互 style 状态完整。
- 对比持续 spec，确认普通页面顶部按钮、Reader 宽窄阈值和设置页行为未被改写。

### Windows 11 x64

- Windows .NET 10 Release build：9 个项目，要求 0 warning / 0 error，并编译 Avalonia XAML。
- 定向运行 `PresentationFlowTests`、`PresentationBoundaryTests`，再运行全量 .NET tests；不发 AI 请求、不下载 Wiki、不修改真实数据。
- 1280×820 与 900×600、Light/Dark：点击 Wiki rail 后搜索页占满工作区；选择结果后正文仍占满工作区，没有左右分栏或覆盖 Reader。
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
- 回退只恢复独立 Presentation/文档提交；无数据、schema 或包格式迁移。

## 进度记录

- 2026-09-09：提交现有工作区内容、UI 文案和单模型配置，创建第一版 draft 计划。
- 2026-09-09：用户以实机截图指出当前没有 Wiki 入口，并否决“左搜索 + 右 Reader”方向。
- 2026-09-09：用户最终澄清：新增左侧 Wiki 导航；该页面全宽完成本地搜索与正文浏览；其他页面和功能全部不动。
- 2026-09-09：重写本计划为全宽 Wiki 搜索/阅读模式；下一步等待用户批准，不实施源码。

## 发现、偏差与决策

- 发现：搜索、结果模型和 Viewer 均已存在；主要工作是新导航和单 Viewer 的两种 Shell 布局，而不是后端开发。
- 决策：Wiki 页面内部使用“全宽搜索态 ↔ 全宽阅读态”，不使用搜索/Reader 分栏。
- 决策：普通页面继续采用既有共享 Reader 分栏/覆盖；新布局只在 `ShellSection.Wiki` 生效。
- 决策：保持一个 ReaderHost/NativeWebView/Viewer session，通过同父级布局切换复用。
- 决策：不触碰其他五个页面的功能与视觉，不进入相邻重构。

## 验证结果

- 规划基线 Windows Release build 已通过 9 个项目、0 warning / 0 error；该证据不包含计划中的新功能。
- `terraria-ui` Skill validator 已通过。
- 当前静态证据确认：Wiki 搜索实现存在但无导航；Shell/Reader 只有一个 NativeWebView/Viewer session。
- 修订计划尚未实施，没有新 Wiki 页面或目标窗口通过结果。

## 完成摘要

不适用：状态为 draft。用户批准后才实施；完成时必须记录实际 Windows build/tests、全宽 Wiki 实机验收、其他页面无回归证据和最终提交。
