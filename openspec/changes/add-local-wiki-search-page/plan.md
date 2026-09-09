# 添加本地 Wiki 搜索页面入口

- 状态：draft；等待用户批准后实施
- Phase：无；独立 Windows Presentation 变更
- 执行环境：WSL2 Codex CLI 负责阅读、编辑、计划维护与静态检查；Windows 11 x64 负责 .NET/Avalonia 构建、测试与真实窗口验收
- 创建日期：2026-09-09
- 最后更新：2026-09-09
- 范围：Windows Avalonia Shell 导航、本地 Wiki 搜索页面、共享 Reader 编排、Presentation 回归与持续文档
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`

## 背景与当前证据

用户要求增加一个可自行输入关键词检索的本地 Wiki 界面，并先完成现有工作区提交和实施规划。当前 Git 基线为 `fbc0c88`，工作区在创建本计划前干净；此前未提交内容已按治理历史、UI 文案和单模型配置拆成独立提交，没有 push。

已确认事实：

- 当前 Windows Presentation 契约和 Shell 对外显示“AI 问答”“收藏”“Wiki 资料库”“数据管理”“设置”五个左侧入口；顶部“打开 Wiki”只打开或恢复共享 Reader，不切换主页面。
- `ShellSection.Wiki`、`IsWikiSelected`、`IsWikiVisible`、`WikiView` 和 `WikiViewModel` 已存在，Shell 也已挂载隐藏的 `WikiView`，但没有 `NavigateWikiCommand`、导航方法或可见导航按钮；`PresentationBoundaryTests` 还明确断言 Shell 不包含“Wiki 阅读导航”。
- `WikiViewModel` 已通过 `WindowsClientRuntime.Application.Query.SearchAsync` 使用 Application 查询边界，具备 180 ms 输入防抖、旧请求取消、无结果与安全错误文本，并通过 `OpenSearchResultAsync` 在现有 Reader 中打开结果。
- 当前 Search 契约已经固定标题、Redirect、正文 FTS5、排序、去重、摘要和 `MaxResults` 行为；无需修改 SQLite、索引、Core 或 Application 公共接口。
- 当前 Windows Presentation 只有一个 `NativeWebView` 和一个 `ViewerWebViewSession`。1100 DIP 及以上使用内联 Reader，低于 1100 DIP 使用右侧覆盖 Reader；设置页临时隐藏 Reader。
- 当前 `WikiView` 仍是 Phase 08 留下的 360 DIP 固定宽度、带独立卡片边界的内部搜索切片，搜索列表与状态共用一行；它能表达基本功能，但不符合当前标准页面的连续布局、完整状态和 900×600 响应式验收要求。
- 进行中的 Phase 11 和 AI 会话 Token 变更不需要为本功能改动发布、Agent、Sidecar、会话存储或 Wiki Tool 协议。

拟定产品决定：左侧新增独立第六个“Wiki”入口；顶部“打开 Wiki”语义保持不变；“Wiki 资料库”继续承担全部页面/对话历史及标题筛选，不与本地全文检索合并。该方案优先复用现有隐藏页面，避免复制搜索或 Reader。

## 目标与完成定义

1. 左侧导航提供清楚、可键盘访问、带非纯颜色选中提示的“Wiki”入口；点击后切换到本地 Wiki 搜索主页面，不取消正在进行的 Chat 回答。
2. 页面允许用户在搜索框输入词条或关键词，通过现有 Application Search 查询当前活动的本地 Wiki；不访问在线 Wiki，也不直接访问 SQLite/Core。
3. 搜索结果展示页面标题、匹配类型、可选 Redirect 和纯文本摘要；点击结果后由唯一共享 Reader 打开页面。
4. 页面明确覆盖初始、搜索中、有结果、无结果、数据源不可用、失败和过期请求状态；旧请求不得覆盖新查询。
5. 1280×820 宽窗口中搜索页与现有 Reader 并排可用；900×600 或低于 1100 DIP 时搜索页保持完整，点击结果后 Reader 使用既有覆盖层并可由 Esc、遮罩或关闭按钮收起。
6. 顶部“打开 Wiki”仍只控制 Reader，不切换当前主页面；收藏、资料库、数据管理、设置和 Chat 现有导航语义不变。
7. 全应用仍只创建一个 `NativeWebView` 和一个 `ViewerWebViewSession`，不新增依赖、数据库、索引、Viewer 内容或平台支持。
8. Windows Release XAML 编译、相关 Presentation 行为/边界测试、全量 .NET 回归和真实 Windows Light/Dark、默认/最小窗口验收取得实际证据后，才标记完成。

## 前置条件

- Phase 00–10 已完成；本功能复用其 Application Search、Windows Shell 和 Viewer/Reader 结果。
- Phase 11 虽仍在收尾，但本功能不依赖公开发布参数、安装器重建或外部渠道验证。
- 当前恢复点为 `fbc0c88`；实施前重新确认 Git 状态，计划和实现只提交当前变更。
- 用户批准本计划后才进入 `in-progress`；生成本计划不自动授权开始实现。
- 不需要真实 AI API Key、付费模型请求、联网 Wiki 请求或 Android 工具链。

## 范围与非目标

范围内：

- Windows 左侧 Wiki 导航入口、选中状态、Automation ID/Name 和 Shell 状态转换。
- 现有 `WikiView` 的标准页面布局、搜索框、结果列表、轻量加载/状态表达和可访问性。
- `WikiViewModel` 中为真实 UI 状态所需的最小内部属性或状态修正；保留当前防抖、取消和安全错误处理。
- 搜索结果到唯一共享 Reader 的现有接线与宽窄布局回归。
- Windows Presentation 持续契约、README/项目当前状态和相关自动化测试。

非目标：

- 不修改搜索匹配、排序、分词、FTS5、SQLite schema、数据包、同步或下载实现。
- 不合并或删除“Wiki 资料库”，不改变其全部页面、对话历史、分页和标题筛选。
- 不把顶部“打开 Wiki”改成主页面导航，不创建第二个 Reader/WebView/Viewer session。
- 不修改 Local Viewer HTML/CSS/JS、loopback transport、Application 公共接口、AI/Wiki Tool/Sidecar 协议或会话存储。
- 不新增 UI 框架、MVVM 包、图标包、字体、图片、在线搜索、搜索建议、查询历史或快捷键体系。
- 不实施 Android、Web、Linux 桌面或其他平台界面，不重建安装包或公开 Release。

## P0 / 兼容性影响

- 手动搜索是 P0。实现只复用 `WikiQueryUseCases.SearchAsync`，不得绕过 Application 或改变现有七级 Search 行为。
- 离线阅读继续使用当前活动 Wiki、动态 loopback Viewer 与唯一 Reader；点击结果只改变 Reader 当前页面和内存返回栈。
- 活动 Wiki 切换后的 `RefreshAfterWikiDataChange` 继续清空旧查询状态，避免旧数据源结果残留。
- Chat、AI、收藏、资料库和 Viewer 共用同一 Application/Core 数据；不得建立 UI 专用数据副本。
- 本变更没有数据迁移或不可逆操作。回退只需恢复单独的 Presentation/文档提交，不影响本地 Wiki 数据。

## 方案与待决问题

### 1. 导航与 Reader 语义

- 在 Chat 后增加独立“Wiki”rail 按钮，使用与现有导航一致的 `rail-nav`、选中指示、已有 PathIcon 语言、Tooltip、`AutomationProperties.AutomationId="WikiNavigation"` 和准确 Automation Name。
- `ShellViewModel` 增加 `NavigateWikiCommand` 与 `NavigateWiki()`。进入页面时把主区域切到 `ShellSection.Wiki`；宽屏恢复现有 Reader 工作区，窄屏保持搜索页完整并在选择结果后再显示覆盖 Reader。
- 保留 `OpenWikiReaderCommand`：顶部按钮仍可从 Chat、收藏等页面打开 Reader，不把用户强制切到搜索页。
- 离开 Wiki 页面不取消 Chat 活动回答；设置页独占和 Reader 恢复行为保持现有契约。

### 2. 页面布局与视觉

- 把 `WikiView` 从固定 360 DIP 卡片改为标准 24 DIP 页面边距和连续内容布局；页面标题使用“本地 Wiki”，不添加重复副标题。
- 搜索字段提供永久可见标签、清楚 placeholder 和 Automation Name；沿用实时防抖检索，不额外增加与自动搜索重复的主按钮。
- 结果使用轻量连续列表与分隔线，不制作逐项灰卡；标题为主层级，匹配类型、Redirect 和摘要为次级信息。
- 搜索中使用轻量进度反馈；状态文本与结果列表分行，避免当前叠放；空状态只说明无结果和可执行的下一步。
- 页面 XAML 只使用现有 `DynamicResource` token 和共享样式。若 `wiki-search-pane` 在改造后无引用，才从共享 selector 中做同范围清理，不新增第二套 style。

### 3. 搜索状态与并发

- 保留当前 180 ms 防抖、CancellationToken 和请求身份检查。
- 用最小内部状态或等价布尔属性区分初始、搜索中、成功有结果、成功无结果、不可用和失败，避免不可用/失败时同时显示“没有找到结果”。
- 清空输入时清空旧结果并回到初始提示；活动数据源变化时继续取消请求并重置查询。
- Search 结果 DTO、匹配文案和打开页面身份继续使用现有模型；不改变 Application/Core 错误内容脱敏。

### 4. 文档与测试

- 更新 Windows Presentation spec，把五入口改为六入口，并将 Wiki Search 从内部能力改为可见主页面；同时保留顶部 Wiki 按钮只控制 Reader 的约束。
- README 和 `openspec/project.md` 只更新当前导航/能力状态，不复制计划细节；Master Architecture 的 P0、Phase 和平台方向不变，因此不修改。
- 将当前“不得出现 Wiki 阅读导航”的静态断言改为验证新入口、Automation、单 Viewer 和顶部按钮语义；增加 Shell 导航/Reader 状态与搜索页面状态行为回归。

替代方案：

- 把全文搜索做成“Wiki 资料库”第三个页签：会混合全文检索与全部页面/对话历史两类不同任务，也浪费已存在的独立 `WikiView`，不采用。
- 让顶部“打开 Wiki”切换主页面：与已确认的 Reader 快捷按钮语义冲突，并会改变 Chat/收藏上下文，不采用。
- 新写搜索服务或 Viewer：复制现有 P0 能力和数据边界，不采用。

待决问题：无阻塞项。页面最终间距、图标路径和状态文案在实现时以现有 token/style、真实 XAML 编译和 Windows 窗口检查收敛，不改变上述行为方案。

## 文件变更

- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml`：增加第六个 Wiki rail 入口，复用已挂载的 `WikiView`，保留顶部 Reader 按钮。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs`：增加 Wiki 导航 command/method，明确页面与 Reader 状态转换。
- **MODIFY** `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiView.axaml`：把隐藏的固定宽搜索切片整理为标准响应式页面和独立状态区域。
- **MODIFY IF REQUIRED** `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiViewModel.cs`：增加最小 UI 状态区分和可访问结果名称；不改变查询边界。
- **MODIFY IF PROVEN UNUSED** `src/Terraria_Wiki.Windows/Presentation/Styles/Controls.axaml`：移除仅供旧搜索卡片使用的 selector；不新增颜色或主题体系。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationFlowTests.cs`：覆盖 Wiki 导航、Reader 宽窄行为、旧请求取消和各搜索终态。
- **MODIFY** `tests/Terraria_Wiki.Windows.Tests/PresentationBoundaryTests.cs`：验证六入口、Automation、顶部按钮语义、无 raw hex 和唯一 Reader/WebView。
- **MODIFY** `openspec/specs/windows-presentation/spec.md`：更新持续导航与本地 Wiki 搜索页面契约。
- **MODIFY** `README.md`、`openspec/project.md`：更新当前产品入口和实现状态摘要。
- **KEEP** `src/Terraria_Wiki.Application/**`、`src/Terraria_Wiki.Core/**`、`src/Terraria_Wiki.Windows/Viewer/**`、`src/Terraria_Wiki.Windows/Presentation/Reader/**`、`agent/**`、SQLite/schema/data package 和 Viewer assets：现有能力已满足本需求。

## 执行步骤

- [ ] 开始实施前确认用户批准计划、Git 状态和恢复点；把计划状态改为 `in-progress`。
- [ ] 在 `ShellViewModel` 和 `ShellView.axaml` 接入第六个 Wiki 导航；验证选中状态、顶部按钮语义、Chat 不取消和 900×600 rail 容量。
- [ ] 重排 `WikiView`，接通初始/加载/结果/空/错误/不可用状态；按需最小调整 `WikiViewModel`，保留现有防抖和取消。
- [ ] 补充/修订 Presentation flow 与 boundary 测试；确认结果仍在唯一共享 Reader 中打开，宽窄切换不复制 Viewer。
- [ ] 更新 Windows Presentation spec、README 和项目基线；不修改 Master Architecture、ADR 或范围外 Phase。
- [ ] 在 WSL 执行 diff、边界和临时文件检查；在 Windows 执行 Release build、定向及全量 .NET tests。
- [ ] 在真实 Windows 窗口检查 Light/Dark、1280×820、900×600、内联/覆盖 Reader、键盘焦点和代表性标题/Redirect/正文搜索。
- [ ] 更新计划的进度、发现和验证结果；检查最终 Git 状态，只提交本变更并保留可回退的独立提交，不 push。

## 验证与回归计划

### WSL / 静态

- `git diff --check`，检查计划内文件和无意生成物。
- 搜索 Presentation 依赖，确认仍只有一个 `<NativeWebView` 和一个 `new ViewerWebViewSession(`。
- 确认 Presentation 不新增 SQLite、Core 数据库服务、loopback transport 或在线 Wiki 调用。
- 检查页面 XAML 无 raw hex，导航/结果项有准确 Automation Name，旧 `wiki-search-pane` 只有在零引用时才删除。
- 核对持续 spec 与实现一致：六入口、顶部 Reader 快捷按钮、宽窄阈值和唯一 Viewer。

### Windows 11 x64

- 使用 Windows .NET 10 对 `Terraria_Wiki.Core.slnx` 执行 Release build，要求 0 warning / 0 error，并实际编译 Avalonia XAML。
- 先运行 `PresentationFlowTests` 与 `PresentationBoundaryTests` 定向回归，再运行全量 .NET tests；不发 AI 请求、不下载 Wiki、不改变真实数据。
- 使用隔离设置或合成数据启动真实客户端，检查 Light/Dark、1280×820、900×600、1100 DIP 两侧分界、rail 可用空间和焦点可见性。
- 搜索至少覆盖标题、Redirect、正文与无结果；快速连续输入验证过期结果不覆盖；点击结果验证页面、返回、收藏和关闭 Reader。
- 从 Chat/收藏页点击顶部“打开 Wiki”，确认主页面不被替换；进入独立 Wiki 导航后确认搜索页可见且 Chat 状态保留。

### 非目标环境

- Android 和其他产品平台不构建、不验证、不宣称支持。
- WSL 静态检查不得记为 Windows 客户端 build/run 通过。

## 风险与回退

- 第六个 rail 入口可能在 900×600 或高缩放下拥挤：沿用 56×56 导航尺寸并在真实最小窗口检查；若空间不足，只调整 rail 分组间距，不隐藏主功能或改变全局尺寸。
- Wiki 与“Wiki 资料库”名称可能混淆：前者明确为本地全文搜索/阅读，后者保持页面清单和对话历史；通过图标、Tooltip 和页面标题区分，不合并业务。
- Reader 状态转换可能让窄屏导航后立即遮住搜索页，或宽屏保持关闭：为 `NavigateWiki()` 的宽窄行为建立明确测试，进入页面时宽屏恢复工作区，窄屏只在打开结果后覆盖。
- 现有 `IsEmpty` 会把错误零结果与正常无结果混在一起：实施时用最小状态拆分，并保留安全错误、不泄漏路径或底层异常。
- 导航变更可能破坏 Phase 09 的五入口静态断言：只更新已被新需求替代的断言，继续验证其余五入口、顶部按钮和唯一 Viewer 不变量。
- 回退不涉及数据：恢复本变更独立提交即可回到五入口和隐藏 Wiki 搜索页；Application/Core、数据和 Viewer 无需迁移。

## 进度记录

- 2026-09-09：按用户要求审计并提交全部现有合法未提交文件；构建产物继续由 `.gitignore` 排除，未 push。
- 2026-09-09：发现 `.codex/config.toml` 与根 `AGENTS.md` 单模型规则冲突；以 `fbc0c88` 关闭多代理默认配置并提交，角色历史文件保留。
- 2026-09-09：读取 UI Skill/规范、架构/项目基线、Search/Viewer/Presentation specs、ADR 0003、Shell/Wiki/Reader/样式/直接调用方及测试，确认复用现有隐藏页面是最小方案。
- 2026-09-09：创建本 draft 计划；下一步等待用户批准，不开始业务源码实现。

## 发现、偏差与决策

- 发现：本需求不是从零新增搜索能力；现有实现已完成 Application 接线、防抖取消、结果模型和共享 Reader 打开，只缺可见入口与当前标准下的页面/状态整理。
- 发现：当前持续 spec 与自动化明确禁止 Wiki 主导航，因此实现必须同步更新契约和对应断言，不能只改 XAML。
- 决策：新增第六个独立 Wiki 入口，保持顶部按钮和 Wiki 资料库职责不变；这是对当前用户需求的最小、可回退表达。
- 决策：不新增依赖、公共接口、数据库或 Viewer；优先 `Reuse → Fix`，不进入 Wrap/Extract/Rewrite。
- 偏差：归档 Phase 08 记录引用的旧内层提交 `2a07b3c` 不在当前独立仓库对象库中；本计划只依赖当前源码、持续契约和已迁移归档记录，不以缺失对象补全实现事实。

## 验证结果

- 规划前 Windows Release build：9 个项目成功，0 warning / 0 error。该结果验证当前基线与已提交 UI 文案，不代表计划中的 Wiki 导航已经实现。
- `terraria-ui` Skill validator：通过。
- 当前实现静态证据：一个 `NativeWebView`、一个 `ViewerWebViewSession`；`WikiView` 已挂载但无导航 command/button；现有 Wiki Presentation tests 覆盖防抖取消、无结果/不可用和结果打开回调。
- 本计划尚未实施，因此没有新导航、搜索页面或真实 Windows UI 的通过结果。

## 完成摘要

不适用：计划状态为 draft。用户批准后才实施；完成时必须记录实际代码、Windows 构建/测试、真实窗口验收、未验证事项和最终提交。
