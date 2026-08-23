# Phase 02 — Search Fix

- 状态：completed
- Phase：02 — Search Fix
- 执行环境：WSL Codex CLI 负责编辑、静态检查和计划维护；Windows `dotnet.exe` 负责 Core-only Release build/test；不安装 MAUI/Android workload
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：现有 SQLite FTS5 trigram 搜索查询、稳定 Search API、旧手动搜索面板和定向回归
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/wiki-data-compatibility/spec.md`；`docs/adr/0002-portable-core-and-data-root.md`

## 背景与当前证据

Phase 01 已在内层源码仓库独立提交为 `0fbddeb`（`feat: complete portable wiki core phase 01`），未 push；提交前 Windows Core Release 测试 74/74 通过，`git diff --check` 通过，未跟踪生产数据库、WAL、SHM 或 `.pkg` 实物。

现有搜索实现位于 `src/Terraria_Wiki.Core/Services/DatabaseService.cs`。它删除单双引号，只按 ASCII 空格拆词，未转义 LIKE 的 `%`/`_`，把标题与 Redirect 合并为同一优先级，在最终去重排序前预截断，并把正文固定为 `LIMIT 50`。Redirect 未验证目标页面，FTS 未验证对应页面，FTS snippet 直接生成 HTML `<mark>`。`WikiSearchOptions` 只校验正数；`SearchResultItem` 没有稳定匹配类型和高亮区间。

旧 `SearchResultPanel.razor` 不取消已开始的查询，也没有请求版本；旧请求可能覆盖新请求。它把 Redirect 结果直接导航到目标，绕开别名 Read 解析，并通过 `MarkupString` 渲染数据库摘要。`TopBar.razor` 还会静默拦截单引号。

FTS 表必须原样保留：`WikiSearchIndex(Title UNINDEXED, PlainContent, tokenize='trigram')`。SQLite trigram 的少于 3 个 Unicode 字符的全文查询不会匹配；LIKE 索引退化不得被用来引入正文全表短词扫描。

## 目标与完成定义

1. `WikiReader.SearchAsync` 固定七级顺序：精确标题、精确 Redirect、标题前缀、Redirect 前缀、标题包含、Redirect 包含、正文 FTS；同级顺序完全确定。
2. 多词为 AND；精确/前缀使用去除首尾并把任意 Unicode 空白折叠为单个 ASCII 空格的完整查询；包含要求每个词出现。大小写沿用 SQLite `LIKE` 语义，不另建 Unicode collation。
3. `%`、`_`、反斜线、单双引号及其他符号作为字面输入；所有用户值和最终上限参数化。
4. 去重和完整排序后才应用 `MaxResults`；允许范围固定为 `1..100`，正文不再固定 50。
5. 仅长词进入 FTS；全部词短于 3 个 Unicode scalar 时不查正文，混合长短词由长词选出 FTS 候选并在候选正文校验短词。
6. 陈旧 FTS 行与无法解析到真实页面的 Redirect 不返回。
7. `SearchResultItem.Snippet` 始终为纯文本；`WikiSearchMatchKind` 提供七种稳定类型；`Highlights` 以 .NET UTF-16 字符串索引表示合法、非重叠区间。
8. 旧搜索面板实现取消与请求版本，最后一次输入获胜；取消、无结果和数据源错误互不混淆；点击 Redirect 别名，沿用 Read 边界；不再使用 `MarkupString`。
9. Windows Core Release 定向与全量测试通过，0 warnings / 0 errors；10,000 行临时 fixture 预热后的精确标题、精确 Redirect、正文查询在当前 Windows 开发机各小于 2 秒且最多返回 100 条。

## 前置条件

- Phase 00、Phase 01 已完成；Phase 01 已独立提交且内层工作树干净。
- 用户已明确批准本 Phase 的方案和实施；Phase 03 未获授权。
- 普通测试只使用临时 SQLite fixture；不发现、复制或打开生产 SQLite/WAL，不运行 3.58 GB golden 包。
- 现有依赖版本、schema、tokenizer、数据根和 `.pkg` 格式保持不变。

## 范围与非目标

范围：Core 搜索 SQL/查询预处理/结果 DTO，旧 MAUI Razor 搜索面板的安全输出与竞态修复，现有 Core-only 测试，查询能力契约和 Phase 状态文档。

非目标：Read 语义改造、Application、Avalonia 生产客户端、AI Adapter/Backend、Viewer transport、索引重建、schema/tokenizer 变更、Vector/Embedding、数据包、同步生命周期、生产数据验证、MAUI 退休、Phase 03 或后续 Phase。

## P0 / 兼容性影响

- 只读取现有 `WikiPage`、`WikiRedirect`、`WikiSearchIndex`，不迁移或重建数据库。
- 手动 UI 与未来 AI 继续通过同一个 `WikiReader.SearchAsync` 使用同一数据源。
- Redirect 结果保留别名身份；点击后由现有 `ReadAsync` 解析目标与锚点。
- `Title`、`RedirectTo`、`Snippet` 字段保留；新增匹配类型和高亮集合是向后兼容扩展，但 `Snippet` 从可能含 HTML 标记收紧为纯文本。
- 最终行为若有问题，可整体回退 Phase 02 未提交 diff；Phase 01 提交提供明确锚点。

## 方案与待决问题

- 查询输入先折叠 Unicode 空白；不删除标点，不对数据库内容做新 normalization，避免改变既有页面身份。
- LIKE 使用显式 escape 字符并参数化精确、前缀和逐词包含模式。
- Redirect CTE 先拆出 `ToTarget` 的页面部分并 join `WikiPage`；FTS 候选也 join `WikiPage`。
- 六种标题/Redirect 候选与可选正文候选先 `UNION ALL`，再用窗口排序按显示标题跨来源去重；最终才 `LIMIT ?`。
- FTS snippet 使用每次查询唯一的非 HTML 起止标记；Core 解析标记并返回纯文本与 UTF-16 高亮区间。UI 只用 Razor 编码文本并显式分段生成 `<mark>`。
- 非正文结果的摘要继续从对应页面现有 FTS 纯文本截取；高亮集合可为空。
- 无阻塞待决问题。若实现要求改变 schema/tokenizer、引入依赖或抽取 Application/UI 框架，则暂停并回报，不自行扩展。

## 文件变更

- **MODIFY** `terraria-wiki-offline/src/Terraria_Wiki.Core/Services/DatabaseService.cs`：输入规范化、参数化七级候选、去重/排序/limit、短词策略、纯文本 snippet 解析。
- **MODIFY** `terraria-wiki-offline/src/Terraria_Wiki.Core/Models/DatabaseQueryModels.cs`：`WikiSearchMatchKind`、高亮范围、内部查询行与 `SearchResultItem` 扩展。
- **MODIFY** `terraria-wiki-offline/src/Terraria_Wiki.Core/Query/WikiQuery.cs`：`MaxResults 1..100`。
- **MODIFY** `terraria-wiki-offline/Terraria_Wiki/Components/Pages/SearchResultPanel.razor` 及 CSS：取消/请求版本/错误状态、别名点击、编码分段高亮。
- **MODIFY** `terraria-wiki-offline/Terraria_Wiki/Components/Layout/TopBar.razor`：移除单引号拦截。
- **MODIFY** `terraria-wiki-offline/Terraria_Wiki/Resources/Raw/Languages/{zh-cn,en-us}.json`：搜索数据源错误文本。
- **MODIFY/ADD** `terraria-wiki-offline/tests/Terraria_Wiki.Core.Tests/**`：Search 定向、安全、取消、错误和 10,000 行性能 fixture。
- **MODIFY** `openspec/specs/wiki-query-boundary/spec.md`：固化 Phase 02 持续查询契约。
- **MODIFY** `README.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`：完成后更新当前 Phase 状态。
- **ADD → MOVE** 本计划完成后移到 `openspec/changes/archive/phase-02-search-fix/plan.md`。
- **KEEP** schema、FTS tokenizer、`.pkg`、同步/导入导出、Viewer、Phase 00 输入和生产数据。

## 执行步骤

- [x] 核对治理、Phase 01 归档证据、Search 实现、调用方、数据模型和测试；独立提交 Phase 01，不 push。
- [x] 实现 Search API/SQL/纯文本高亮与 `MaxResults` 边界；运行 Core Search 定向测试。
- [x] 修正旧搜索面板竞态、取消、错误、Redirect 点击和安全渲染；执行静态检查。
- [x] 建立完整定向回归与 10,000 行性能 fixture；Windows Release 预热验收。
- [x] 使用 `luna_critic` 对正确性、范围、安全、回归和测试缺口做一次只读对抗审查；修复后复验。
- [x] 更新查询契约、README、project 和 architecture 状态，记录验证结果并归档计划。
- [x] 最终执行一次 Windows Core Release build、全量 test、Core 平台依赖扫描、`git diff --check` 和状态/生成物检查；Phase 02 保持未提交、未 push。

## 验证与回归计划

### Windows Core-only

- 七级顺序、同级确定排序、页面/FTS 跨来源去重。
- 上限 `25/50/75/100`，正文超过 50；`0/101` 拒绝。
- 大量模糊标题/Redirect 下精确结果不被预截断。
- 多词 AND、Unicode 空白、空输入、纯符号、单双引号、字面 `%`/`_`/反斜线。
- 全短词只查标题/Redirect；混合长短词用长词 FTS 候选并校验短词。
- 坏 Redirect、陈旧 FTS、预取消/查询取消和数据源不可用。
- 恶意 HTML 样式正文保持纯文本；高亮范围合法、递增、非重叠且可安全切片。
- 10,000 页面/FTS 行临时 fixture；Release 预热后精确标题、精确 Redirect、正文各 `<2s`，返回数 `<=100`。
- 代码稳定后一次最终 `dotnet.exe build ... -c Release --no-restore` 与一次全量 `dotnet.exe test ... -c Release --no-restore`，要求 0 warnings / 0 errors 和全部测试通过。

### WSL / 静态

- `git diff --check`。
- `rg` 确认 Core 无 MAUI/Avalonia/Razor/WebView/平台依赖，搜索 UI 无 `MarkupString`。
- 检查所有 Search 用户值和最终 limit 均为 SQL 参数，查询无正文硬编码 `LIMIT 50`。
- 检查内层 Phase 02 diff 未提交、未 push；外层治理文件不进入内层 Git；无数据库/包/临时 fixture 被跟踪。

### 未执行的平台验证

- 未安装 MAUI workload，因此不 build/run 旧 MAUI 宿主；旧面板通过源码审查和 Core 查询测试验证，不宣称宿主运行通过。
- 本 Phase 不建立 Avalonia 生产客户端或 Android 验证。

## 风险与回退

- 动态多词 SQL 的参数顺序错误：集中构造 clause/argument，并用标点与多词 fixture 覆盖。
- FTS parser 注入或短词退化：所有长词作为转义引号短语绑定；全短词跳过 FTS；短词 LIKE 只与长词 MATCH 同一候选查询组合。
- 跨来源去重选错结果：窗口排序先按稳定 MatchKind，再按 rank/长度/标题/Redirect 目标确定；limit 最后执行。
- snippet 标记与正文冲突：每次查询使用唯一内部标记，解析后不向 API/UI暴露标记；UI仍验证范围再切片。
- 旧 UI 无 workload 自动测试面：保持改动局部，用请求版本和 CTS 双重保护，最终明确记录未运行 MAUI 宿主。
- 性能阈值受冷启动影响：fixture 建立后分别预热目标查询，只计后续调用；阈值仅代表当前 Windows 开发机。

## 进度记录

- 2026-08-24：重新读取治理、spec、ADR、Phase 01 归档计划和 Search 源码；两名 `luna_scout` 完成 Phase 01 边界及 Search 调用链只读盘点。
- 2026-08-24：Windows Core Release 测试 74/74 通过；Phase 01 在内层提交为 `0fbddeb`，未 push；Phase 02 计划进入 `in-progress`。
- 2026-08-24：完成 Core 七级 Search、短词/字面输入策略、纯文本 snippet/highlights 与旧搜索面板的请求版本、取消、错误、别名导航和安全渲染；Search/WikiQuery 定向测试 19/19 通过。
- 2026-08-24：对抗审查发现搜索面板 CTS 释放竞态与查询入口全量 FTS 正文聚合；改为同锁 CTS 所有权，以及最终 limit 后只为最多 100 个标题参数化读取摘要。复核确认两项 blocker 均关闭，定向测试仍为 19/19。
- 2026-08-24：修复后 10,000 页面/FTS 临时 fixture 的 Windows Release 预热实测：精确标题 4.3 ms、精确 Redirect 3.6 ms、正文返回 100 条 67.8 ms，均低于 2 秒。
- 2026-08-24：查询契约与 README/project/architecture 状态已同步；最终 Windows Core Release build 为 0 warnings / 0 errors，全量测试 89/89，通过最终静态、差异和状态检查。Phase 02 转为 `completed` 并归档，未进入 Phase 03。

## 发现、偏差与决策

- Highlights 使用 .NET UTF-16 索引，以便现有 Razor 对 `string` 安全分段；这是接口语义，完成后同步查询 spec。
- Query normalization 只折叠 Unicode 空白，不引入数据库 normalization/collation 迁移；精确和前缀大小写继续服从 SQLite LIKE。
- 旧 MAUI 宿主当前无法在不安装 workload 的约束下自动化组件竞态；不为此引入 UI 测试依赖或提前建设 Presentation/Application。
- 对抗审查确认不能用 10,000 行小正文 fixture 替代大库复杂度判断；标题/Redirect 候选不再在查询入口物化整张 `WikiSearchIndex` 正文，最终 limit 后的摘要读取最多包含 100 个参数化标题。`Title UNINDEXED` 仍意味着摘要读取可能扫描 FTS 标题列，这是保留现有 schema 的已知约束。

## 验证结果

- Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --filter 'FullyQualifiedName~SearchQueryTests|FullyQualifiedName~WikiQueryTests'`：19/19 通过。
- Windows 10,000 行性能定向测试：1/1 通过；修复后精确标题 4.3 ms、精确 Redirect 3.6 ms、正文 67.8 ms（100 条）。fixture 位于测试临时目录并由测试释放，未访问生产数据。
- UI 静态检查：单引号拦截和 `MarkupString` 已移除；Redirect 点击保留 `item.Title`；请求 CTS 与版本检查均在组件释放时清理。语言 JSON 可解析，`git diff --check` 通过。
- `luna_critic` 首轮发现 2 个高严重度与 1 个关联中严重度问题；修复后复核确认 CTS 竞态、空查询 CTS 泄漏和全量 FTS 正文聚合均关闭。
- 最终 Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，0 warnings / 0 errors。
- 最终 Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：89/89 通过，0 skipped。
- Core 静态依赖扫描未发现 MAUI、Avalonia、Razor、WebView、Preferences、平台条件、loopback、Vector 或 Embedding 引用；Search SQL 无 `LIMIT 50`、入口 `PageIndex` 全表正文聚合或 HTML `<mark>`。
- 最终 `git diff --check` 退出码 0；旧 MAUI 工作树仅显示既有 CRLF→LF 提示，无 whitespace error。内层未暂存的 Phase 02 diff 为 8 个修改文件和 1 个新增测试文件；cached diff 为空，`origin/main..HEAD` 只有 Phase 01 提交 `0fbddeb`，Phase 02 未 commit/push。
- 内层未跟踪 `.db`、`.pkg`、WAL 或 SHM；Windows build/test 更新的 `bin/obj` 均由既有 ignore 排除。普通/性能测试只使用自动释放的临时 fixture，未发现或打开生产数据库与 golden 包。
- 按约束未安装 MAUI/Android workload，未 build/run 旧 MAUI 宿主；因此不宣称旧宿主 UI 运行、Avalonia 生产客户端或 Android 已验证。

## 完成摘要

Phase 02 已完成：保留原 SQLite schema、FTS5 trigram tokenizer 和数据格式，在同一 `WikiReader.SearchAsync` 上固定七级匹配类型、AND/Unicode 空白/字面符号语义、`MaxResults 1..100`、短词候选策略、最终去重截断、坏数据排除、纯文本摘要与 UTF-16 高亮范围。旧搜索面板现在取消旧请求并以版本保证最后输入获胜，区分取消/错误/无结果，Redirect 点击沿用 Read 边界，数据库文本由 Razor 编码后分段高亮。

新增定向与 10,000 行性能回归，最终 Windows Core Release build 为 0 warnings / 0 errors、全量测试 89/89。Phase 02 源码保持未提交、未 push；未改变依赖、schema、tokenizer、`.pkg`、Read、Application、AI、生产 Presentation、Windows Runtime 或 Android，也未进入 Phase 03。
