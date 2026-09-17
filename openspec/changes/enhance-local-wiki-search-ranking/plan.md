# 增强本地 Wiki 关键词搜索、排序与相关摘要

- 状态：rejected / rolled-back；用户否决本次实现路线，产品源码已回到 `master@2497f9b`；不得把下文历史结果解释为当前能力或再次实施授权
- 取代者：`openspec/changes/rebuild-local-wiki-search/plan.md`；新变更不复用本计划的 trigram、ShortTerm 或启动后台构建方案
- Phase：无；Phase 11 后续路线图中的独立“本地 Wiki 搜索能力优化”变更
- 执行环境：WSL2 Codex CLI 负责阅读、编辑、计划维护与平台无关检查；Windows 11 x64 负责 .NET/SQLite/Avalonia 构建、真实数据库、窗口响应与发布资产验证；不安装 MAUI/Android 工具链
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：现有 Core/Application 本地 Search、搜索派生 schema、旧库本地升级、页面增量索引、社区别名数据、相关 Snippet、必要的最小 Windows 结果适配及完整回归
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/project.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/application-use-cases/spec.md`；`openspec/specs/wiki-data-compatibility/spec.md`；`openspec/specs/wiki-tools/spec.md`；`openspec/specs/windows-presentation/spec.md`；`openspec/changes/post-phase-11-roadmap/plan.md`；`openspec/changes/archive/phase-02-search-fix/plan.md`；`docs/adr/0002-portable-core-and-data-root.md`；`docs/adr/0011-use-managed-data-root-and-raw-database-transfer.md`
- 需求来源：用户提供的 `C:\Users\Xiao\Desktop\PLAN-local-wiki-keyword-search-ranking.md`；该桌面文件只作为输入，本计划是仓库内唯一实施状态来源

## 回退决定（当前权威状态）

- 2026-09-13：用户在实际使用中发现搜索与打开 Wiki 页面明显变慢，随后明确要求回退到本次开发前的保存点，并否决本次路线。
- 当前产品源码、持续规范和测试均以 `master@2497f9b` 为准；拒绝方案只保留在分支 `enhance-local-wiki-search-ranking@d739d82`，不得合并或发布。
- 本次路线的关键问题是首次 v2 派生构建会进入 `Building`，而 Search 等待完整 `Ready`；实际客户端又对活动数据库启动了构建，造成搜索等待及大规模 SQLite I/O，并连带拖慢 Viewer 页面读取。
- 活动数据库未在 Git 回退中做破坏性清理。基线代码会忽略新增派生表并恢复 legacy Search；是否备份后删除派生表、回收磁盘空间必须另行确认，不能由本计划自动执行。
- 下文的方案、勾选项和验证结果仅是被否决分支的历史证据，用于说明为何不能原样重试；任何新方案必须建立新的设计与验收门，至少保证搜索不等待全量重建、Viewer 不受索引 I/O 连带影响，且未经明确确认不得写活动库。

## 背景与当前证据

用户要求只增强本地 Wiki 的空格分隔关键词搜索，使 `猪鲨 武器`、`光女 召唤物`、`克眼 掉落`、`剑`、`弓`、`武器`、`药水`、`饰品` 等查询稳定返回符合直觉的结果。多词查询必须先给出全部词均有效命中的规范页面，严格结果不足时才用部分匹配补足；能由页面标题、Wiki Redirect 或社区别名解析的实体词比普通主题词重要；Title/Redirect/社区别名、Heading、Body 的证据强度必须可区分；结果摘要必须解释本次命中。未来 AI 自动复用同一 Application Search 顺序和摘要，但本变更不得修改 Agent、Prompt、聊天、`read_wiki` 或 AI 协议。

已读取并确认的代码事实：

- `src/Terraria_Wiki.Core/Services/DatabaseService.cs` 的 Content 初始化建立 `WikiPage`、`WikiRedirect`、`WikiHistory`、`WikiFavorite`、`WikiAsset` 和 `WikiSearchIndex(Title UNINDEXED, PlainContent, tokenize='trigram')`。当前没有搜索 schema/version 元数据，也没有独立 Heading 或短词索引。
- 当前 `DatabaseService.SearchAsync` 以一个 SQL CTE 固定 `ExactTitle → ExactRedirect → TitlePrefix → RedirectPrefix → TitleContains → RedirectContains → BodyFts` 七级顺序，在规范页面去重前以显示 `Title` 聚合，最终才应用 `MaxResults`。它没有实体词概念、社区别名、AND 不足后的 OR 补足或 Heading/Body 区分。
- 当前查询把 Unicode 空白折叠为空格，逐词 Title/Redirect 包含使用 AND；仅 Unicode scalar 数不少于 3 的词进入 trigram FTS。全部词少于 3 scalar 时明确不搜索正文；混合长短词只在长词 FTS 候选的 `PlainContent` 上校验短词。这与本次中文 1～2 字正文/Heading 目标直接冲突。
- `src/Terraria_Wiki.Core/Data/WikiContentProcessor.cs` 已用 HtmlAgilityPack 清理 HTML，并从 `p/h1-h6/li` 合并生成单一 `SearchText`；它在生成搜索文本时排除 message-box、infobox 与 navigation，但没有保留章节层级、Heading/Body 边界或片段位置。
- `src/Terraria_Wiki.Core/Data/WikiDataLifecycle.cs` 已在远程 `LastModified` 未变化时跳过页面。变化页面目前先单独保存 `WikiPage`，再单独调用 `SaveSearchIndexAsync`，两步不是同一事务；Redirect 直接读取 `WikiRedirect` 事实表，不需要再复制一份 Redirect 搜索表。
- `src/Terraria_Wiki.Core/Query/WikiQuery.cs` 的 `WikiReader.SearchAsync` 是唯一 Core Search 入口；`src/Terraria_Wiki.Application/UseCases/WikiQueryUseCases.cs` 只把 Application 设置中的 `SearchLimit` 转给 Core，不重排结果。
- `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiViewModel.cs` 已有 180 ms debounce、旧 CTS 取消、请求身份检查和安全错误状态。结果点击仍进入唯一共享 Reader。Presentation 没有第二套评分逻辑，但当前匹配标签只认识七种 `WikiSearchMatchKind`。
- `src/Terraria_Wiki.WikiTools/WikiMcpTools.cs` 直接序列化同一个 Application Search 结果和既有七种 `match_type`。因此只要 Core/Application 返回的新顺序、摘要和兼容 `MatchKind` 保持稳定，AI 将来会自然复用，不需要改 WikiTools 或 Python Agent。
- `src/Terraria_Wiki.Core/Compatibility/WikiContentSchemaValidator.cs` 和 raw database/package 导入要求现有 trigram `WikiSearchIndex` 存在并能执行 MATCH；新增派生对象若保留该表，就可继续接受旧库、现有 `.pkg` 和单文件 `.db`，也可为旧程序回退保留原搜索能力。
- `tests/Terraria_Wiki.Core.Tests/SearchQueryTests.cs` 目前锁定七级顺序、短词只搜 Title/Redirect、混合词候选、UTF-16 highlights、坏 Redirect/陈旧 FTS、取消与 10,000 行性能；Application、WikiTools 和 Windows tests 又分别锁定透传、协议和 debounce/Presentation 边界。这些断言必须有选择地更新，不能整体删除。
- 仓库当前没有社区别名文件，也没有 `猪鲨`、`光女`、`克眼` 对应记录。
- 当前 `master` 为 `2497f9b`。计划创建前工作树已有用户改动 `.agents/skills/package-core-source-zip/scripts/package_source_zip.sh`；该文件与本变更无关，必须保留且不得进入本变更保存点或提交。
- 用户已纠正 `openspec/changes/post-phase-11-roadmap/plan.md` 的顺序：先完成本项，再完成 AI / Token 后续优化，最后完成 Quick Access + 托盘常驻；本项不依赖后两项，顺序已确认。

事实与推断边界：

- 已知旧真实数据记录约为 3.34 GB，但本轮没有获得一个明确、可写、可销毁的真实 `data.db` 副本，也没有执行新版索引体积或耗时实验。实施验收必须由显式输入提供真实副本，不能猜测或直接修改用户正在使用的数据库。
- 基于 trigram 对少于 3 scalar 不召回、当前 `WaitAsync` 只能取消等待而不能证明底层长扫描立即终止的事实，本计划选择 SQLite 内部中文短词倒排表，而不把全库 `LIKE '%短词%'` 作为正式方案。具体表体积和批大小仍要在 Windows provider + 真实副本 PoC 中验证。
- 当前 Search DTO 的七种 `MatchKind` 同时被 Windows 文案和 WikiTools schema 消费。为遵守“不改 AI”边界，本计划保留该兼容字段与七个协议值，新增仅供本地 UI解释的证据类型；新排序不再把七级枚举值当作唯一全局排序键。

## 目标与完成定义

1. 所有 Search 调用仍统一经过 `WikiReader.SearchAsync → WikiQueryUseCases.SearchAsync`；Windows UI 和 WikiTools 不自行召回、合并或评分。
2. 查询只支持空格/Unicode 空白分隔的简单关键词。去除首尾空白、折叠连续空白、忽略重复词；词序只作为最终稳定 tie-breaker，不把第一个词固定为主词。
3. 每个词先按 `精确页面标题 → 有效 Wiki Redirect → 有效社区别名` 解析实体；标题/Redirect 事实优先于人工别名。多个可解析词均为实体词。
4. 多词结果按规范页面聚合：全部不重复查询词均命中的结果形成严格组并始终排在所有部分结果之前；只有严格组少于 `MaxResults` 时才从部分组补足；不同召回路径只返回一个规范页面。
5. 排序明确体现实体词覆盖、直接 Title/Redirect/Alias 身份证据、其它 Title、Heading、Body 和部分覆盖层次；同一数据库、同一查询、同一上限的顺序完全可复现。
6. `猪鲨`、`光女`、`克眼` 分别能通过独立社区别名配置映射到 `猪龙鱼公爵`、`光之女皇`、`克苏鲁之眼`；目标页面不存在、别名记录坏、文件缺失或整个文件解析失败时忽略对应扩展，基础 Title/Redirect/FTS 搜索继续可用。
7. `剑`、`弓`、`武器`、`药水`、`饰品`、`掉落` 能在 Title、Heading 和 Body 中召回；不以完整页面名搜索代替正文能力，不执行未设上限且不可中断的 HTML/正文全表短词扫描。
8. Heading 从现有 `WikiPage.Content` 派生，保持章节顺序和路径，并能以高于普通 Body 的证据参与排名；不修改存储 HTML。
9. Snippet 为纯文本，优先选择覆盖最多查询词的同一章节；Heading 命中显示章节路径及附近正文，Body 命中显示词附近上下文，跨章节时最多组合两个有界片段，只有身份命中时回退到页面导语。Highlights 继续使用合法、递增、非重叠的 .NET UTF-16 范围。
10. 旧 Content 数据库无需联网、重新下载或完整重新导入，即可从 `WikiPage.Content` 构建新版派生索引；事实表行值不变；构建中断/失败后旧搜索仍可用并可续建或重建；Ready 的同版本数据库不在每次启动重复全量处理。
11. 未变化页面不重新派生；新增/变化页面保存后只更新本页的旧兼容索引和新版文档/章节/短词派生数据。新版派生更新失败不得破坏已提交的页面事实，索引退回非 Ready 并由维护流程修复。
12. Wiki Redirect 查询始终直接使用当前 `WikiRedirect` 事实表；同一 `FromName` 目标或 anchor 更新后下一次 Search 即使用新关系，不维护可能陈旧的第二份 Redirect 副本。
13. 仍只有现有 `data.db`；不创建 `search.db`，不增加网络或运行时 AI 依赖，不更换 SQLite/FTS5，不引入向量、Embedding 或第三方搜索引擎。
14. 现有精确标题、Redirect、标题前缀、标题包含、普通长词 FTS、纯文本安全、高亮、`MaxResults 1..100`、错误/取消、Reader 打开路径均无明显回归。
15. 自动化、真实 Terraria Wiki 数据库和 Windows 实机验证全部完成；真实库验证记录索引升级时间、空间增量、冷/热查询耗时、取消延迟、锁行为和逐查询前列结果，不用少量 mock 代替真实性能结论。

## 前置条件

- [x] 用户批准本计划的范围和方案；批准计划不自动批准付费请求、联网抓取、发布、Android 或其他路线图候选。
- [x] 用户已明确本项先于 AI / Token 与 Quick Access，路线图已同步；无需等待后两项完成或再次确认顺序。
- [x] 实施前检查工作树，建立只包含当前已确认基线的可恢复 Git 保存点；排除现有 `.agents/skills/package-core-source-zip/scripts/package_source_zip.sh` 用户改动及其它无关文件。实际使用 `2497f9b` 作为基线，建立独立分支 `enhance-local-wiki-search-ranking`；未收录现有工作树 diff。
- [x] 通过现有 `DatabaseService.BackupExistingAsync` 对活动中文库生成一致、可写、可销毁的临时副本；所有升级、版本故障、Redirect 临时写入和性能实验只作用于副本，生产活动库未初始化或修改。
- [x] 副本使用前记录路径、3,781,029,888 字节输入、SQLite integrity、事实表计数与有序事实摘要 hash；试验输出位于 `artifacts/search-real-acceptance-temp` 并在记录证据后清理。
- [x] Windows 目标机 C 盘执行前可用约 536 GB；实际派生增长 309,215,232 字节，观测 WAL 约 53 MB，没有触发空间拒绝或自动 VACUUM。
- [x] 未安装或恢复旧 MAUI checkout，未安装 Android SDK/JDK/Emulator；Windows 构建继续使用根 `Terraria_Wiki.Core.slnx`。

## 范围与非目标

范围内：

- Core 查询分词、实体解析、候选聚合、严格/放宽排序、规范页面去重和 Snippet/highlight。
- 版本化搜索派生 schema、旧库增量/全量重建、可恢复状态与一致性检查。
- 从既有清理后 HTML 提取页面导语、Heading 路径、章节正文及中文短词派生项。
- 现有页面下载/更新路径的每页派生同步和失败降级。
- 每 Wiki 的静态社区别名 JSON、容错加载、Windows build/publish 打包与 Application 组合。
- Windows 结果匹配标签或打开身份承载新证据所必需的最小 ViewModel 适配；不改变 XAML 页面结构、布局和交互。
- Core/Application/Windows/兼容与真实数据库 tests，以及受影响持续 spec/计划状态。

非目标：

- Agent、Prompt、Token、聊天、Sidecar、MCP schema、WikiTools Search/Read 映射或 `read_wiki` 行为修改。
- 自然语言句子理解、运行时 AI 识别、拼音、错别字修正、语义模糊搜索、同义词自动生成或联网更新别名。
- 搜索 UI 重做、导航/Reader/Viewer 结构改变、第二套结果排序、结果点击以外的新交互。
- 更换/增加 Elasticsearch、Lucene、Meilisearch、向量库、Embedding、第二个 SQLite 文件或第二份 Wiki 事实数据。
- 重写抓取、版本检测、资源下载、Reader、Viewer、导入导出或 package 格式。
- 清理旧 `WikiHistory`、改变收藏/资源/页面事实 schema、自动 VACUUM、依赖升级、Phase 12、Android 或公开发行。
- 本预规划明确排除错别字纠正；路线图已按本次搜索范围同步，该能力留待未来独立需求。

## P0 / 兼容性影响

### 事实与派生边界

- P0 事实保持为现有 `WikiPage(Title, Content, LastModified)`、`WikiRedirect`、`WikiAsset`、收藏/历史和 Manager 数据。新增搜索对象只保存可由页面事实和静态别名重新生成的派生数据。
- 保留现有 `WikiSearchIndex(Title, PlainContent, trigram)` 的名称、列和 tokenizer，并继续为变化页面更新它。它承担旧数据库/旧应用兼容、raw database validator、`.pkg` 兼容和新版索引不可用时的基础 Search 回退。
- 新版对象只能创建在同一 Content `data.db`。删除新版对象后，事实表和旧 FTS 仍形成可用回退；旧程序会忽略额外表。实现前以当前 validator、package import/export 和旧查询回归验证这一推断。
- 新版索引的坏数据、Building/Dirty 状态或版本不符不能使 Read、Viewer、资源、收藏、导入导出或旧 Search 失效。只有事实表/旧 P0 schema 本身不兼容时才沿用现有初始化失败语义。

### API 与消费者兼容

- `SearchResultItem.Title`、`RedirectTo`、`Snippet`、`MatchKind`、`Highlights` 和七个既有 `WikiSearchMatchKind` 保留。WikiTools 的七种 `match_type`、字段名和 JSON schema 不变。
- 新增的证据类型只用于解释社区别名、Heading 与短词正文来源；Windows `WikiSearchResultViewModel` 可用它替换匹配标签文本，但不得参与评分。社区别名结果用真实规范页面标题作为 `Title`，保证现有 Reader/`read_wiki` 无需学会社区别名。
- 既有 Wiki Redirect 结果继续保留可由 `ReadAsync` 解析的别名 `Title` 与 `RedirectTo`，避免丢失 Redirect anchor 行为；内部另以解析后的规范目标作为去重键。
- Application 继续只透传 Core Search；若需要传入别名 catalog 或管理索引维护，只允许增加平台无关、搜索专用的可选组合输入，不暴露 SQLite、表模型或路径给 Presentation/WikiTools。

### 数据生命周期与交换

- 页面事实与旧兼容 FTS 在一个事务中写入，确保基础搜索永远对应已提交事实；同一事务先把原 Ready 的新版 metadata 降为 Dirty，防止事实提交后仍查询旧 v2。新版每页派生在紧随其后的独立事务更新，全部成功后才恢复 Ready。新版事务失败时保留已提交事实与旧 FTS，并在后续维护中只重建缺失/变化页；即使后续错误继续发生，也不会把陈旧 v2 冒充 Ready。
- 全量升级按小批次提交并记录状态；任何批次失败都不删除 `WikiPage`、`WikiRedirect` 或旧 FTS，不把半成品标为 Ready。取消后可按页面内容 hash/修改时间续建。
- raw `.db` / `.pkg` 导入仍先通过现有事实和旧 FTS validator；导入成功后把缺失、过旧或损坏的新版索引视为派生维护任务，不因可重建表缺失而拒绝一个原本兼容的旧数据库。
- raw database 导出与 `.pkg` 会自然包含同一 `data.db` 中的新版派生表；现有格式字节协议、inspection token、staging/backup/journal/rollback 不改变。

## 方案与待决问题

### 1. 查询规范化和实体解析

1. 沿用现有 Unicode 空白折叠和字面 `%`、`_`、反斜线、单双引号安全处理；按折叠后的空格拆词。
2. 重复词按不区分 ASCII 大小写的稳定比较去重，保留首次出现序号只用于结果解释和最终 tie-breaker；第一词没有额外业务权重。
3. 对每个词执行精确身份解析，优先级固定为：
   - 现有 `WikiPage.Title`；
   - 目标页面真实存在的 `WikiRedirect.FromName`，目标的 `#anchor` 从规范页面身份中拆出但不丢失现有 Redirect 导航；
   - 当前 Wiki ID 对应的社区别名，且目标 `WikiPage` 必须存在。
4. 同一个词若同时命中多个来源，Title 胜过 Redirect，Redirect 胜过社区别名；同一社区别名有冲突目标时整条别名失效而不是依赖 JSON 顺序。多个不同词可分别解析为实体，不选唯一“主词”。
5. 实体词的召回变体包含原词和规范页面标题。例如 `猪鲨` 可直接给 `猪龙鱼公爵` 页面身份分，也可用 `猪龙鱼公爵` 在其它页面的 Title/Heading/Body 中查找关系；这使“实体相关的武器页面”有机会进入严格组，而不是只返回 Boss 自身页面。

### 2. 社区别名数据和加载

- 新增 UTF-8、版本化 JSON，源文件候选为 `assets/search/community-aliases.zh-cn.json`，发布后位于 `SearchAssets/community-aliases.zh-cn.json`。首版只为 Wiki ID 1 提供：`克眼 → 克苏鲁之眼`、`猪鲨 → 猪龙鱼公爵`、`光女 → 光之女皇`。
- JSON 根包含 `version` 和按稳定 Wiki ID 分组的 records；每条只有 `alias` 与 `target_title`。不允许 anchor、链式社区别名、正则、脚本或远程 URL。
- loader 限制文件大小和记录数，严格解析 version，规范化空白，忽略空字段、同值重复、未知 Wiki 与不存在目标。单条坏记录不影响其它条目；文件不存在、不可读、版本未知或 JSON 整体损坏返回空 catalog，不阻断 Application 初始化或 Search。
- Windows Host 用 `AppContext.BaseDirectory/SearchAssets/...` 传入 Application；Core/Application 不自行猜测安装目录或用户数据根。测试构造器默认空 catalog，从而不改变既有测试和非 Windows consumer。
- build、publish 与 release payload 检查必须证明 JSON 被复制、进入 manifest、无用户绝对路径/秘密；删除/损坏发布副本的 smoke 必须证明基础 Search 正常。

### 3. 新版派生 schema

在保留旧 `WikiSearchIndex` 的前提下，首版候选对象如下；命名可在 PoC 后为 SQLite 限制做局部调整，但职责不得合并回事实表：

- `WikiSearchMetadata`：单例记录 `SchemaVersion`、ExtractorVersion、`State`（Building/Ready/Dirty）、已处理页数、最后完成时间和失败后可安全重试所需的最小游标。它不是事实来源。
- `WikiSearchDocument`：每个规范页面一行，记录页面标题、`LastModified`、Content SHA-256、导语和章节数，用于跳过未变化页面、识别缺失/陈旧派生数据和身份 fallback。
- `WikiSearchSection`：每个页面至少一行导语/无标题段，随后按 HTML 文档顺序记录 Heading level、稳定 ordinal、Heading path、Heading text 与该 Heading 后直到下一 Heading 的 Body text；以页面标题 + ordinal 唯一并建立页面索引。
- `WikiSearchSectionFts`：以 `WikiSearchSection` 为 external-content 的 FTS5 trigram 索引，Heading 与 Body 为独立列并共享 section rowid；通过同事务触发器或显式 FTS command 保持一致。PoC 必须验证当前 sqlite-net/Windows provider 上的 delete/update、`snippet()`、`bm25()` 和 `integrity-check`，失败则改用同职责的普通 content FTS 表，不更换引擎。
- `WikiSearchShortTerm`：SQLite `WITHOUT ROWID` 倒排表，主键以 `Term, LocationKind, SectionId` 开头，并保存有界 occurrence/首个 UTF-16 offset。只派生连续中文 Han scalar 的唯一 unigram/bigram，Heading 与 Body 分列；查询 `剑`/`武器` 使用索引等值查找，不扫描整库正文。其它少于 3 scalar 的非中文词保持现有 Title/Redirect 行为。

构建规则：

- `WikiContentProcessor` 在既有 junk 清理后、存储 HTML 不变的前提下，从克隆/只读遍历中一次生成 legacy `SearchText` 与结构化章节。继续排除当前 message-box、infobox、navigation，避免本阶段无授权改变事实清理语义。
- Heading path 使用 h1–h6 栈；每遇到新 Heading 开启新 section，Heading 前内容进入 ordinal 0 导语。正文只含既有 `p/li` 可搜索文本。HTML entity 解码和空白折叠沿用当前规则。
- 中文短词只从 Heading/Body 纯文本生成；每 section 对同一 term 去重并保留有界频次/首位置，避免重复词线性放大行数。
- Ready 前必须验证页面集合、每页 fingerprint、无 orphan section/short-term、FTS external-content 行对应关系和 FTS integrity。只有验证事务成功后才把 metadata 标为 Ready。

### 4. 旧库升级、启动和恢复

- Content 初始化只执行轻量 schema/version/state 探测并确保新版空表可创建，不在每次启动同步扫描整库。
- Application 为活动 ContentDB 持有一个最小搜索索引维护协调器，并复用现有 `WikiDataOperationGate`。初始化后可在后台按小批次构建；每批获得 gate、提交后释放并让出执行，下载/更新/导入/切换可在批次边界优先进行。
- `WikiQueryUseCases.SearchAsync` 在首次需要增强搜索时等待当前活动库达到 Ready；等待接受调用方 cancellation，因此既有 debounce 的旧请求不会覆盖新请求。若维护发生非取消失败，则本次安全回退现有 legacy Search，事实读取与 Reader 保持可用，metadata 保持 Dirty/Building 以供下次重试。
- 活动 Wiki 切换、raw/package import、整库删除和 Application dispose 必须先取消并排空该库维护任务，再切换连接/目录；成功绑定新库后重新探测并按需启动。维护任务不得持有已被切换的共享路径或连接。
- 构建使用页面 fingerprint 跳过已完成且一致的行。进程中断后不删除整库或事实，下一次从缺失/不一致页面继续；若 metadata/schema/FTS 自身损坏，则只 drop/recreate 新版派生对象，旧 FTS 继续服务。
- 构建批次限制事务规模和 WAL 峰值；不为索引升级强制全库 checkpoint、复制第二个数据库或自动 VACUUM。空间预检失败时不启动构建并保持 legacy Search。
- Ready 的同版本/同 extractor 数据库只做轻量一致性抽查；明确 Dirty、版本提升、FTS integrity 失败或事实 fingerprint 不一致才进入修复。

### 5. 候选召回、聚合与排序

每个不重复查询词生成证据行，证据统一带 `TermOrdinal`、规范页面、显示/导航身份、是否实体词、实体解析来源、命中位置、section、位置 rank 和兼容 `MatchKind`：

- Title/Redirect 的精确、前缀、包含仍使用参数化 LIKE 与现有字面 escape；Redirect 必须 join 到现有目标页面。
- 长词及实体规范标题变体通过 `WikiSearchSectionFts` 分别查询 Heading/Body；1～2 个 Han scalar 通过 `WikiSearchShortTerm` 等值查询。
- 社区别名直接身份只给真实目标规范页；不会插入 `WikiRedirect`，也不会改变 `ReadAsync`。
- 同一词/页面/位置的重复证据先折叠为最强一条；再按规范页面聚合所有词，计算完整覆盖、实体覆盖、总覆盖和位置证据。SQL/内存候选均不得在规范页面聚合与严格优先排序前按某个单一路径预截断。

最终排序采用可解释的字典序，而不是依赖输入顺序或不可审计的单个魔法分数：

1. `FullCoverage` 降序；完整组无条件在部分组之前。
2. 已命中的实体词重要度总和降序，再按实体词命中数、全部词命中数降序；部分组中命中实体的页面高于只命中泛词的页面。
3. 直接实体身份数降序，并在同数时按 Title、Redirect、CommunityAlias 的来源质量顺序。
4. Title 命中词数、Heading 命中词数降序，Body-only 命中词数升序。
5. Heading/Body 的 FTS BM25 与短词 occurrence 形成位置内 tie-breaker；不得让 BM25 跨越完整覆盖或实体重要度层级。
6. 覆盖最多词的 Snippet section、标题长度、规范标题、显示标题、section ordinal 形成完全确定的最终 tie-breaker。

统一排序后应用 `MaxResults` 即可保证：严格组达到上限时无部分结果；严格组不足时只在尾部补足部分结果。查询计划必须用 `EXPLAIN QUERY PLAN`/FTS 证据证明短词走 `WikiSearchShortTerm` 主键，不接受正文全表扫描作为正式路径。

兼容输出：

- 七种现有 `WikiSearchMatchKind` 继续描述最强的兼容召回大类，供 WikiTools 原协议使用，但不再决定全局排序。Heading、短词 Body 归入 `BodyFts` 兼容类；社区别名目标以规范 Title 返回并使用标题身份兼容类。
- 新增 `WikiSearchEvidenceKind`（或等价只读字段）精确描述 ExactCommunityAlias、Heading、BodyShort 等本地证据；Windows 只用它显示“社区别名 / 章节标题 / 正文匹配”等文本，不重新评分。
- 若实施发现无法在不向 WikiTools 输出错误语义的情况下保留兼容字段，必须暂停并更新本计划/`wiki-tools` spec，不能静默修改 MCP schema；本计划不预授权该扩展。

### 6. Snippet 与高亮

- 每个候选先选覆盖查询词最多的 section；同覆盖下 Heading 命中优先、实体词证据优先、ordinal 更小优先。
- Heading 命中输出 Heading path，加该 section 中最接近其它词的正文窗口；Body 命中以最小覆盖窗口为中心截取上下文。多个词只能分布于不同章节时，最多组合两个片段并以纯文本省略号分隔。
- 输出设定明确的 Unicode scalar/UTF-16 双重安全上限（实现阶段依据现有 60-token体验在测试中固定，候选不超过约 180 个可见字符）；不得切断 surrogate pair，不返回 HTML、属性、脚本或内部 FTS marker。
- Highlights 同时覆盖原查询词和确实用于召回的实体规范标题变体；按 .NET UTF-16 计算，合并重叠区间，保证可由 Avalonia/.NET `Substring` 安全切片。
- 只有 Title/Redirect/Alias 身份命中且没有正文证据时，用 `WikiSearchDocument` 导语；导语空时再用 legacy `PlainContent` 首段，最终才使用现有“暂无描述...”文本。

### 7. 增量维护

- 把当前“保存 `WikiPage` → 保存旧 FTS”的两步收敛为数据库内部 `SavePageAndLegacySearchAsync`（名称可调整）事务。此事务写页面事实、现有 `WikiSearchIndex`，并在 v2 曾为 Ready 时先把 metadata 降为 Dirty；失败时保留旧页面/旧索引/旧状态。
- 事实事务成功后，以新页面的已处理 HTML/结构化章节更新本页 `WikiSearchDocument/Section/SectionFts/ShortTerm`。该事务完成本页一致性检查后才恢复 Ready；失败不回滚已提交事实，后台维护按 fingerprint 修复。若数据库故障严重到无法继续写 metadata，前一事务已经留下 Dirty，查询仍回退 legacy。
- `BuildUpdateListAsync` 的 `LastModified` 比较保持不变；未变化页既不下载也不派生。重试成功页走同一保存入口。
- Redirect 不生成派生副本；`FetchRedirectsAsync` 更新已有 `FromName` 后，新 Search 直接看到新目标/anchor。测试覆盖同一 Redirect 改目标、失效目标、Redirect 与社区别名冲突；不在本变更顺带重写远端 Redirect 全量同步算法。
- 删除整库沿用现有目录级可回滚流程，新空库自然创建空新版 schema。单页孤儿派生通过 Ready 一致性检查发现并清理；陈旧 FTS/派生行仍必须 join 真实 `WikiPage`，不能泄漏为结果。

### 8. 待决问题与早期验证门

以下不是产品行为待决，而是必须在开始大规模代码修改前用当前 Windows SQLite provider 关闭的物理实现门；若失败，先更新计划再继续：

1. external-content FTS5 在 sqlite-net-pcl 1.11.285 / SQLitePCLRaw 3.0.3 上的 trigger/delete/update、`snippet`、`bm25` 和 integrity command 是否稳定；失败时保留同列职责，改为显式维护的普通 content FTS5。
2. `WikiSearchShortTerm WITHOUT ROWID` 在真实中文库的总行数、文件增长、构建时间和查询计划是否可接受；若体积不可接受，只能优化 term 去重、字段/批次或 SQLite 表布局，不能退回不可取消全表扫描或更换引擎。
3. 后台批次与现有 WAL/Search/Reader/下载 gate 的锁行为；必须证明取消/切换/导入不会留下悬挂连接或长期锁库。若后台方案无法满足，改为有进度、可取消的一次性维护步骤并更新计划，不允许无提示阻塞启动。
4. 社区别名对真实中文 Wiki 的目标标题是否精确存在。若目标名称与预规划不同，只记录真实 Wiki 标题并向用户报告，不通过模糊匹配猜目标。

本阶段不需要新 ADR：SQLite/FTS5、单数据库、Application/Core 边界均已决定；具体派生表与排序属于本 change。若 PoC 迫使改变 P0 schema 兼容策略、公共 Search/WikiTools 协议或长期平台组合，再先提出 ADR/spec 变更。

## 文件变更

以下清单来自当前真实代码；新文件名是职责候选，实施时若只做局部合并可减少文件，但不得建立新 project/Registry/框架。

### ADD

- `assets/search/community-aliases.zh-cn.json`：版本化、按 Wiki ID 分组的社区别名事实补充配置。
- `src/Terraria_Wiki.Core/Query/WikiSearchIndexService.cs`：新版 schema/version、重建/续建、一致性、每页派生维护和 legacy fallback 协调。
- `src/Terraria_Wiki.Core/Query/WikiSearchAliases.cs`：有界 JSON catalog 模型/加载/规范化/冲突处理；若职责足够小可与前文件合并。
- `tests/Terraria_Wiki.Core.Tests/WikiSearchIndexTests.cs`：升级、中断、Dirty 修复、Heading/短词派生和增量一致性测试。
- `tests/Terraria_Wiki.Core.Tests/RealSearchAcceptanceTests.cs`：显式环境变量驱动的真实可写副本升级、事实 hash、质量和性能验收；无输入时普通测试不碰用户数据。

### MODIFY

- `src/Terraria_Wiki.Core/Data/WikiContentProcessor.cs`：在不改变存储 HTML 的前提下返回 legacy 搜索文本、导语和结构化 sections。
- `src/Terraria_Wiki.Core/Models/DatabaseQueryModels.cs`：保留旧 DTO/enum，增加规范身份/证据类型或实现所需的非持久 Search 结果字段。
- `src/Terraria_Wiki.Core/Services/DatabaseService.cs`：旧 FTS 事务保存、参数化候选/排名/摘要查询、新派生表低层操作；避免继续把全部逻辑堆进一个巨型方法，只有证据要求时做最小提取。
- `src/Terraria_Wiki.Core/Query/WikiQuery.cs`：组合 alias、索引 readiness、增强 Search 与取消；Read 保持零语义修改。
- `src/Terraria_Wiki.Core/Data/WikiDataLifecycle.cs`：变化页调用统一事实+legacy 保存和本页新派生更新，保持远端版本检测/下载流程。
- `src/Terraria_Wiki.Application/Internal/ApplicationState.cs`、`src/Terraria_Wiki.Application/WikiApplication.cs`：持有搜索 catalog/维护协调器，提供安全生命周期；只增加搜索专用可选组合输入。
- `src/Terraria_Wiki.Application/UseCases/ActiveWikiUseCases.cs`、`src/Terraria_Wiki.Application/UseCases/WikiDataUseCases.cs`：初始化、切换、导入、删除与 dispose 时启动/取消/排空对应活动库维护，不改变其它用例。
- `src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs`：把固定发布资产路径传给 Application；不加载或解释别名内容。
- `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：build/publish 复制 SearchAssets。
- `src/Terraria_Wiki.Windows/Packaging/Build-WindowsRelease.ps1`：把别名 JSON加入 required payload/manifest 安全检查。
- `src/Terraria_Wiki.Windows/Presentation/Wiki/WikiViewModel.cs`：仅按 Core 证据字段修正匹配标签；保留 debounce、取消、状态和结果打开流程。
- `tests/Terraria_Wiki.Core.Tests/SearchQueryTests.cs`、`WikiQueryTests.cs`、`DatabaseServiceTests.cs`、`WikiDataLifecycleTests.cs`：替换被新需求推翻的短词/七级全局排序断言，扩充候选、排序、摘要和增量回归；保留现有安全与错误测试。
- `tests/Terraria_Wiki.Application.Tests/WikiQueryUseCaseTests.cs`、`ActiveWikiTests.cs`、`WikiDataUseCaseTests.cs`、`TestSupport.cs`：Application 透传、维护生命周期、导入后重建、取消与公开边界。
- `tests/Terraria_Wiki.Windows.Tests/PresentationFlowTests.cs`、`PresentationBoundaryTests.cs`、`ProductizationBoundaryTests.cs`：最新查询胜出、匹配标签、单 Reader/无 UI 评分和发布别名资产。
- `tests/Terraria_Wiki.Core.Tests/WikiPackageTransferTests.cs`：旧库和含额外派生表的新库均可检查/导入，坏新版派生可重建且不破坏现有 package/raw rollback。
- `openspec/specs/wiki-query-boundary/spec.md`：用新覆盖/实体/位置排序、中文短词、规范去重、摘要和 legacy fallback 取代旧“七级即全局排序”和“全短词不搜正文”契约。
- `openspec/specs/application-use-cases/spec.md`：Search 仍透传 Core，但增加别名 catalog/索引 readiness 与维护生命周期语义。
- `openspec/specs/wiki-data-compatibility/spec.md`：事实/派生、额外同库 schema、旧库升级/恢复和新旧 `.db/.pkg` 兼容。
- `openspec/specs/windows-presentation/spec.md`：仅在匹配标签可见语义实际变化时记录最小适配；不复制排序规则。
- `openspec/changes/post-phase-11-roadmap/plan.md`：用户已确认搜索 → AI / Token → Quick Access 的顺序并完成同步；实施期间只更新本项实际状态。
- `README.md`、`openspec/project.md`、必要时 `docs/architecture/ARCHITECTURE-v0.6.md`：只在完成后更新当前能力/入口，不复制 SQL 和实施细节。
- 本计划：实施中实时更新进度、发现、偏差、决定与实际验证；完成后移入 archive。

### KEEP / EXPECT NO CHANGE

- `src/Terraria_Wiki.WikiTools/**`、`tests/Terraria_Wiki.WikiTools.Tests/**` 的生产协议、`agent/**`、AI Runtime、Prompt、聊天与会话存储；只运行现有回归证明自动复用 Search，没有 AI 源码修改。
- `WikiReader.ReadAsync`、Application `ReadAsync`、Viewer/Reader backend、Viewer assets、Wiki 页面存储 HTML与资源链路。
- `src/Terraria_Wiki.Core/Compatibility/WikiContentSchemaValidator.cs` 和 package/raw import 生产算法原则上保持；先用“保留旧 FTS、允许额外表”验证兼容。若真实测试证明必须识别新版 Ready 健康，只增加可重建派生检查，不把它变成旧库拒绝条件。
- `Terraria_Wiki.Core.slnx`、现有 project/PackageReference/lock files、SQLite/HtmlAgilityPack 版本和 `.pkg` 字节格式。
- Windows XAML 页面/布局、导航、唯一 Reader、搜索输入/结果交互；未来 Android 与非目标平台全部不触碰。

## 执行步骤（已否决分支的历史记录）

- [x] **0. 实施授权与恢复点**：用户已明确要求完成本计划；从 `master`/`2497f9b` 建立 `enhance-local-wiki-search-ranking` 分支。建分支前的无关改动为 `.agents/skills/package-core-source-zip/scripts/package_source_zip.sh` 和 `openspec/changes/post-phase-11-roadmap/plan.md`，另有本计划目录未跟踪；均未进入基线保存点。
- [x] **1. Windows SQLite/真实副本 PoC**：Windows provider 的普通 content FTS5、显式 delete/insert、`bm25`、integrity、中文 unigram/bigram、32 页批次、取消/续建、WAL、空间和真实副本均完成验证。external-content 物理候选按计划回退为职责相同的显式维护普通 content FTS，以避免 trigger 一致性风险。
- [x] **2. Characterization 与 schema v2**：保留旧 validator/package/Search 契约；实现 metadata/document/section/FTS/short-term、Building/Ready/Dirty、版本重建和 only-derived 恢复。未知版本、丢失 FTS、额外表交换、legacy fallback 与事实 hash 已验证。
- [x] **3. 结构化内容派生**：`WikiContentProcessor` 在保存 HTML 后从 clone 派生导语、Heading 栈/路径、章节正文和 legacy 文本；现有排除语义及纯文本/Unicode 回归通过。
- [x] **4. 可续建升级与生命周期**：实现单飞、小批次、游标、空间预检、取消排空、切换/导入/删除/dispose 协调、事实+legacy 原子保存和每页 v2 更新。真实副本取消排空 2.1 ms，续建 4:09.342；重建前后结果一致。
- [x] **5. 社区别名**：加入按 Wiki ID 的三条版本化数据、有界容错 loader、Application/Windows 组合及 build/publish/release payload；真实库三个目标标题均存在。
- [x] **6. 增强候选与排序**：实现去重词、Title→Redirect→Alias 实体解析、实体变体、规范页证据聚合、完整覆盖优先、部分补足、最终 limit 和稳定 tie-breaker；词序交换与真实查询矩阵通过。
- [x] **7. 相关 Snippet/highlights**：实现最佳同章节、最多双片段、Heading 路径、导语 fallback、180 UTF-16 安全截断及合并高亮；现有恶意 HTML/marker/highlight 回归保留。
- [x] **8. 最小消费者适配**：Application 只传活动 Wiki context，Windows 只显示 evidence label；无 XAML/布局/评分改动，WikiTools 生产源码和 Agent 零改动。现有 debounce、最后输入胜出、唯一 Reader 与 MCP 逐项输出测试通过。
- [x] **9. 持续契约和自动化回归**：四份受影响持续 spec 已同步；Windows Release build 0 warning/error，全量 387 项 .NET tests、WikiTools 45 项、边界扫描、`git diff --check` 和 build/publish 资产验证通过。
- [x] **10. 真实数据库质量/性能/升级验收**：3.78 GB 可写副本完成 v1→v2、Ready 二次启动、未知版本取消/续建、Redirect 事实即时读取、事实 hash/integrity、结果稳定与九组查询矩阵。首次构建 3:57.655、增长约 309 MB；优化后查询约 15–120 ms。
- [x] **11. Windows 实机 P0 回归**：Windows 11 x64 实际启动 Avalonia/WebView2 自动 smoke 窗口，Search、Redirect Reader、前进/后退、主题切换、CSS/JS/图片/音频和退出清理通过；真实短词/多词/别名/摘要由同一 Windows Core/Application 路径的 3.78 GB 矩阵验证。没有 XAML 视觉改动，因此未另做视觉重设计验收。
- [x] **12. 收口与提交**：已核对实际 diff、计划状态、spec、README/project、临时 DB/WAL/PoC、发布资产和无关用户改动；4.09 GB 副本、Windows smoke 根、临时 publish 与辅助工具均已删除。本计划移入 archive，并为本变更建立独立 Git 提交，不 push。

## 验证与回归计划

### Core 查询与排序

- `猪鲨`：社区别名解析到存在的 `猪龙鱼公爵`，规范页明显优先；缺别名文件时仍有基础结果/空结果的正常语义，不抛初始化错误。
- `猪鲨 武器`、`光女 召唤物`、`克眼 掉落`：严格全部词页面先于任一部分页面；实体相关页不被大量泛主题页淹没；反转词序不改变业务排序。
- 多实体词、实体+多个泛词、重复词、连续 Unicode 空白、空输入、1/100 上限、0/101 拒绝。
- 同一规范页经 Title、Redirect、社区别名、Heading、Body、实体标题展开同时召回时只保留一项；Redirect 失效目标和 stale 派生行不返回。
- 字面 `%`、`_`、反斜线、单双引号、FTS 操作符、emoji 和纯符号保持参数化安全；不允许 FTS/LIKE/动态 SQL 注入。
- 同级完全确定：重复运行、不同插入顺序、索引全量重建与增量维护后，结果身份和顺序一致。

### Alias / Redirect

- 正常三条社区别名；按 Wiki ID 隔离；目标精确存在检查。
- Wiki Redirect 正常、带 anchor、改目标/anchor、坏目标；Title/Redirect/Alias 同名冲突按固定优先级。
- JSON 不存在、不可读、空、语法坏、未知版本、未知字段、单条空值、同值重复、冲突目标、超限大小/记录数均安全降级。
- 社区别名不写入 `WikiRedirect`、Prompt、Agent 或数据库事实；发布副本缺失只损失扩展。

### 中文短词与 Heading

- 单字：`剑`、`弓`；双字：`武器`、`药水`、`饰品`、`掉落`；多个短词组合及短+长/实体混合。
- 每个词分别覆盖 Title-only、Heading-only、Body-only、Heading+Body 噪声；Heading 高于 Body，严格覆盖仍高于位置权重。
- `EXPLAIN QUERY PLAN`/表访问证明确实走短词倒排主键；正式路径不扫描 `WikiPage.Content` 或所有 `WikiSearchSection.BodyText`。
- 非中文短 scalar 保持当前仅 Title/Redirect 边界，除非用户另开变更。

### Snippet / highlights

- Heading 命中包含章节路径和附近相关正文；Body 命中围绕词；多词同章节优于跨章节，必要时两个片段解释覆盖。
- 只命中 Title/Redirect/Alias 时使用合理导语；导语空时 fallback 确定。
- Snippet 不含 HTML/脚本/FTS marker；所有 highlight 可按 UTF-16 安全切片，递增、非重叠，不切 surrogate pair。
- Alias 展开的规范标题若作为真正证据可被高亮，但不得伪造原查询词出现在正文。

### schema、旧库升级与故障注入

- v1 旧库：无新版表、空库、部分 Building、Dirty、未知 version、丢表、坏 FTS、orphan section/short-term。
- 首次升级只新增/改派生对象；页面、Redirect、Asset、Favorite、History 的有序字段摘要和计数不变；不联网、不重新导入。
- 在建表、页面批次、FTS 写入、metadata Ready、取消和进程终止等边界注入失败；重启可续建/重建，事实和旧 FTS仍可查询。
- Ready 同版本重启不扫描/重写全部页面；extractor/schema version 提升才按明确策略重建。
- 全量重建结果与逐页增量结果逐查询相同；单页变化只改变该页派生记录；未变化页 fingerprint/行不变。
- raw `.db`、`.pkg` 旧/新库检查、导入、导出、token、rollback；坏新派生表不使兼容事实库永久不可导入，导入后可修复。

### Cancellation、并发与锁

- 预取消、FTS 查询取消、短词查询取消、等待索引 Ready 取消、后台批次取消均传播 `OperationCanceledException`，不转换为空结果。
- 快速输入序列只发布最新结果；旧索引维护/旧查询不能在新请求后覆盖 UI。
- Search 与 Reader 并发；Search 与下载/更新、切换、import、delete、dispose；维护在 gate 批次边界让路且不访问已切换连接。
- Windows WAL 中无明显长时间写锁；取消/异常/退出后连接、gate、CTS 和任务释放，数据库通过 integrity check。

### Windows/边界/打包

- Release solution build 和全量 .NET tests；Core/Application/WikiTools/Windows 项目计数逐项记录，不沿用旧 349/349 作为本变更证据。
- 静态确认 Core 无 Avalonia/Windows/MAUI，Application 无 SQLite/平台 API，Presentation/WikiTools/Agent 无评分 SQL或派生表访问。
- build 与 publish 都含 `SearchAssets/community-aliases.zh-cn.json`；release payload manifest/hash 覆盖它，未包含 `.db`、临时数据或本机路径。
- Windows 真实窗口保持原 Wiki 首页/搜索结果/正文三态、现有布局、唯一 Reader 和结果点击；只核对匹配文案/摘要承载，不进行视觉重做。

### 真实数据库矩阵

由新的 opt-in test（环境变量候选 `TERRARIA_SEARCH_REAL_DATABASE_COPY`）或等价固定参数接收显式可写副本；未设置时普通测试跳过真实项并明确不算验收通过。至少记录：

- 输入大小、页面/Redirect/资源数、SQLite/FTS版本、事实摘要 hash、旧/新派生表大小和 WAL 峰值。
- v1→v2 总时间、中断后恢复时间、第二次 Ready 启动维护时间、单页增量时间。
- 精确标题、Wiki Redirect、普通长词、三个多关键词示例、六个单/双字短词、多个短词组合、连续快速搜索的冷/热耗时和结果数。
- 每个示例前列规范页、覆盖组、证据来源与 Snippet；结果需要人工确认 Terraria 语义，自动断言负责层级/去重/稳定性。
- 搜索期间 Viewer/Reader 打开和数据库维护响应；没有明显 UI 卡顿、旧结果覆盖或长时间锁库。预规划不设任意固定毫秒门，本计划记录实测分布并由用户完成最终窗口体验验收。
- 完成后重新计算事实摘要与 integrity；只删除明确的临时副本和测试产物，不删除用户原件。生产活动库升级只能在上述副本矩阵通过、用户另行确认后进行。

## 风险与回退

- **新版索引体积过大**：中文 unigram/bigram 可能显著增大同一数据库。先在真实副本统计 unique term/section 和空间；通过每 section 去重、只索引 Han 1/2-gram、`WITHOUT ROWID` 和批次控制。不可接受时停止并修订物理布局，不创建第二数据库、不自动删除事实。
- **后台构建与数据操作争锁**：复用同一 `WikiDataOperationGate`、小批提交、批间让路；切换/import/dispose 先取消排空。若仍造成 UI/数据库长锁，退回显式可取消维护流程并更新计划。
- **FTS external-content 不一致**：Ready 前执行映射/FTS integrity；每页更新同事务；失败将新版状态 Dirty 并使用旧 FTS。可 drop/rebuild新派生对象，事实和旧 `WikiSearchIndex` 不动。
- **部分召回过宽导致性能/噪声**：只在最终规范页面聚合后按完整优先排序并 limit；用索引召回而非 HTML扫描；真实库检查通用短词。不得通过预截断牺牲严格结果正确性。
- **实体展开误召回**：只接受精确 Title/Redirect/Alias，Alias 目标存在，人工配置按 Wiki隔离；不做模糊/链式推断。实体身份分与正文关系分分开，回归记录解释。
- **Snippet 跨章节误导**：优先单 section 覆盖；跨章节明确用两个片段，不拼成仿佛同一句；仅高亮真实出现的原词/规范展开词。
- **取消只是取消等待**：正式短词使用倒排，构建按批次检查 token；真实 Windows 记录底层任务/锁释放，不以 UI task 已返回代替数据库已停止。
- **旧库/旧应用兼容退化**：保留并持续更新旧 FTS，新增表为可忽略附加对象；当前 validator/package/旧查询全回归。代码回退后旧二进制仍可用 legacy Search；新版表可留存或由明确维护命令删除。
- **派生失败污染事实更新**：事实+legacy 与“v2 降为 Dirty”先原子提交，新版成功后才恢复 Ready。重建只读 `WikiPage.Content` 并写派生表，不改事实字段。
- **升级中断与磁盘不足**：构建前空间检查，批次元数据，Ready 原子发布；不足/取消保留旧 Search。drop 派生表后的 freelist 不自动 VACUUM，以免制造额外长操作。
- **别名文件损坏/缺失**：空 catalog fallback；不缓存无法验证的旧文件到事实库，不阻断启动、Search、Read 或 Viewer。回退可只移除 SearchAssets 和 Host 可选参数。
- **WikiTools 语义被兼容字段扭曲**：现有协议回归必须通过；如不能诚实映射，停止并请求是否允许独立 WikiTools contract 变更，不在本计划内偷改 AI。
- **路线图顺序回退**：后续以已确认的搜索 → AI / Token → Quick Access 为准，不再把 AI / Token 或 Quick Access 当作本项前置条件；本轮文档更正不启动产品实施。
- **Git/用户改动混入**：保存点和提交使用精确文件清单；现有 package-core skill 修改及后续无关改动始终保留在工作树外侧。

可执行回退顺序：

1. 查询发生新索引错误时立即使用保留的 legacy `WikiSearchIndex` 路径，Read/Viewer 不受影响。
2. 把 metadata 标 Dirty，停止维护；必要时只 drop `WikiSearchMetadata/Document/Section/SectionFts/ShortTerm` 并从事实重建。
3. 别名问题删除/回退独立 JSON 或让 loader 返回空，不修改数据库事实/Redirect。
4. 代码级回退到实施前 Git checkpoint；因旧 FTS持续存在，旧二进制无需下载或导入即可搜索。
5. 真实用户库如需回退，只使用实施前通过现有 SQLite backup/export 获得的一致单文件快照；不得手工复制 WAL 主文件或删除 sidecar。

## 进度记录

- 2026-09-13：用户实机发现搜索与打开 Wiki 页面明显变慢，否决本次路线并要求回退；源码回到 `master@2497f9b`，拒绝实现保留在独立分支，仅供取证和比较。

- 2026-09-13：用户要求“完成这份 plan”，将状态推进为 in-progress；从 `2497f9b` 建立独立实施分支 `enhance-local-wiki-search-ranking`，保留并隔离已有用户 diff。
- 2026-09-13：实现同库 search projection、中文短词、结构化章节、实体/别名、规范页聚合、摘要、高亮、增量维护和 Application/Windows 生命周期；同步持续 spec 与测试。
- 2026-09-13：首次真实副本矩阵发现所有查询固定耗时 9～17 秒；定位为每次 Ready 快检扫描 346 万行短词表，改为 metadata/page count 与首尾 FTS rowid 常数级抽查，完整 integrity 只在构建完成执行。优化后真实查询降为约 15～120 ms。
- 2026-09-13：3.78 GB 中文库副本完成初建、取消续建、结果稳定、事实 hash/integrity、真实标题和 Redirect 验证；Windows 自动窗口 smoke 通过。
- 2026-09-13：全量 387 项回归通过，临时真实库/smoke/publish/辅助工具已清理，README、项目基线和路线图状态收口；计划归档并建立独立提交。
- 2026-09-13：用户提供桌面预规划并要求创建完整 plan；明确本轮只规划，不自动实施。
- 2026-09-13：按 `INIT.md` 读取 README、Master Architecture、项目基线、计划规范、Search/Application/Data/Presentation/WikiTools specs、Phase 02/03 与本地 Wiki 页面归档计划、Phase 11 后续路线图及相关 ADR。
- 2026-09-13：核对根 solution、Core schema/Search/内容处理/下载更新、Application 组合、Windows ViewModel/Host/打包、WikiTools映射、raw database validator 和现有测试；确认短词与七级契约冲突、无社区别名文件以及工作树已有无关用户改动。
- 2026-09-13：选择“保留 legacy FTS + 同库版本化章节/短词派生 + Core 规范页聚合 + Application 统一消费”的可回退方案；计划保持 draft，等待用户批准实施。
- 2026-09-13：用户纠正路线图，确认先完成本地 Wiki 搜索，再优化 AI / Token，最后完成 Quick Access + 托盘常驻；已移除本计划中相反的前置条件和待改序说明。

## 发现、偏差与决策

- 决策：不替换现有 `WikiSearchIndex`；新增 v2 派生索引并保留 v1，优先满足旧库、raw database、`.pkg`、旧应用和失败回退。
- 决策：中文 1～2 字正式召回使用 SQLite 普通倒排表，不使用正文全表 LIKE；只覆盖 Han unigram/bigram，非中文短词不扩展。
- 决策：Heading 从当前清理后 HTML 派生为有序 section；不修改存储 HTML，也不恢复当前搜索明确排除的 infobox/navigation。
- 决策：排序先比较完整覆盖，再比较实体重要度与 Title/Heading/Body 证据；第一输入词不加权，BM25 只作低层 tie-breaker。
- 决策：社区别名是随程序发布的独立 JSON，按 Wiki ID 精确映射；不写 Redirect、不进 Prompt、不影响 Read，坏文件整体安全降级。
- 决策：WikiTools/Agent 保持零源码修改；保留现有 Search 字段和七种协议 match type，通过同一 Application 顺序/摘要自然获益。
- 决策：新版每页派生失败不回滚已经安全提交的页面事实与 legacy 索引，而是标 Dirty 并修复；这是“事实高于可重建派生数据”的直接实现。
- 偏差：没有采用 external-content FTS + trigger；使用相同字段职责的普通 content FTS，并在每页派生事务中显式 delete/insert。当前 provider 的 update/delete、BM25、integrity 和真实恢复矩阵通过，避免把触发器变成第二套一致性机制。
- 发现：真实库共有 4,390 页、29,774 Redirect、26,237 资源、25,982 sections 和 3,464,552 条短词记录。首次实现的 Ready 健康检查逐次扫描短词 orphan，造成 9～17 秒查询；该检查现已移到完成验证，热路径只做有索引的轻量抽查。
- 决策：FTS rowid 显式等于 `WikiSearchSection.Id`，使 Ready 首尾抽查无需扫描 UNINDEXED `SectionId`；完整 count/orphan/FTS integrity 仍在发布 Ready 前运行。
- 决策：用户已确认本项是当前下一项；AI / Token 和 Quick Access 按顺序在本项完成并验证后推进，顺序无需再次确认。
- 待证据：真实中文库上的短词倒排行数/体积、v2 全量构建耗时、background gate 锁行为和三个社区别名目标标题；这些由执行步骤 1 关闭，不用推断写成事实。

## 验证结果（已否决分支的历史记录）

- Windows `.NET 10.0.11` / SQLite `3.53.3`：`dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo` 成功，0 warning / 0 error。
- Windows Release 全量测试成功：Core 115、Application 61、WikiTools 45、Windows 166，共 387/387。WikiTools 首轮暴露旧“七枚举全局顺序”测试假设；仅更新测试为逐项比较 Application 输出，生产 MCP schema/映射未改。
- 10,000 页合成性能：Exact Title 6.9 ms、Exact Redirect 4.9 ms、Body 100 results 97.0 ms，均低于既有 2 秒门槛。
- build 后 `SearchAssets/community-aliases.zh-cn.json` 存在；Windows `dotnet publish` 到 `/tmp` 后文件存在并与源 JSON `cmp` 一致，临时 publish 目录已删除；release required payload 已覆盖该文件。
- 真实输入由活动库通过现有 SQLite backup API 一致导出到可销毁副本；原库 3,781,029,888 字节且未被新版代码打开写入。副本事实摘要在全部试验前后均为 `5D9F74C726174B9AB9F1F9D920FB2E9971B3FF0BBDD90CEEEF059FB60267EEE9`，最终 `PRAGMA integrity_check = ok`。
- 真实库计数：WikiPage 4,390、WikiRedirect 29,774、WikiAsset 26,237、WikiFavorite 0、WikiHistory 20；v2 Document 4,390、Section/FTS 各 25,982、ShortTerm 3,464,552，metadata `2/1/Ready/4390`。
- 首次 v1→v2 构建 3:57.655，数据库增长 309,215,232 字节（约 8.18%），观测 WAL 约 53 MB；未知版本后台构建取消排空 2.1 ms，续建 4:09.342，Ready 二次检查 0.7 ms且不改 CompletedAt。
- 优化后真实查询冷/热范围约 15～120 ms：`猪鲨 武器` 114.6/64.3、`光女 召唤物` 38.0/42.4、`克眼 掉落` 76.3/73.1、`剑` 28.9/28.8、`弓` 17.0/18.8、`武器` 44.2/40.2、`药水` 30.8/34.2、`饰品` 37.2/35.6、`掉落` 56.3/52.5 ms；重复、取消续建和重建后结果身份完全一致。
- Windows 11 x64 实际 Avalonia/WebView2 smoke：`success=true`、Search results 2、CSS/JS/Image/Audio requests 1/1/1/5、Viewer history 1、runtime ready 4,742 ms、Viewer ready 5,012 ms、退出清理 33 ms。进程 stderr 有一次 Chromium `Failed to unregister class ... Error = 1412`，但 smoke、资源和清理证据均成功。
- `git diff --check` 与静态边界扫描通过：Core 无 UI/Windows 依赖，Application/Presentation/WikiTools/Agent 无新 SQLite/派生表评分访问；无 PackageReference、target framework、MAUI/Android/AI 生产变更。
- 创建计划前已有用户改动 `.agents/skills/package-core-source-zip/scripts/package_source_zip.sh` 和 `openspec/changes/post-phase-11-roadmap/plan.md`；前者从未修改，二者均从本变更提交排除。

## 回退摘要

本次实现路线已由用户否决并回退。当前产品使用 `master@2497f9b` 的原有 SQLite FTS5/legacy Search；新增 v2 派生、别名、排序、摘要和维护代码均不在当前源码或构建输出中。拒绝分支 `d739d82` 只作为可恢复证据保留，不代表完成状态，也不得直接继续实施。

## 否决原因

- 新增派生索引导致数据库大小增量过大，不符合用户对本地数据体积与维护成本的预期。
- 实际搜索能力和结果质量未达到用户预期，不能以现有实现路线继续推进。
