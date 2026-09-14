# Wiki Search / Read 边界契约

- 状态：confirmed baseline
- 适用范围：Core、Application、Presentation、Wiki Tool Adapter、AI Backend
- 来源：`rebuild-local-wiki-search` 与本地 Wiki 单一事实源决策

## 目的

为手动搜索、Reader 和 Wiki Tool 提供同一套区域级本地 Wiki 查询，使调用方不依赖 SQLite 表、FTS 细节或平台状态。

## Search 契约

- Search 查询当前活动 Wiki 的 v2 内容库。每个规范页面只返回一个结果，选取最佳真实区域作为摘要和点击目标；同页其他命中区域合并保留在 `EvidenceRegions` 中。
- 查询先执行 Unicode NFKC 与空白规范化，再解析精确 Title、官方 Redirect 和按 Wiki 身份加载的社区 Alias。多个输入名称解析到同一实体时合并为一个概念，输入顺序不得改变关键词权重。
- 社区 Alias 是随程序发布的独立版本化 JSON，只允许确定性实体映射；它不写入 Redirect、不联网、不调用 AI。
- 唯一正文索引是 contentless FTS5 位置索引。汉字按 Unicode scalar 编码为逐字 token，连续拉丁字母和数字按词编码，字面符号编码为确定性 token；连续中文查询使用短语匹配。不得同时保留旧 trigram 或 ShortTerm 明细索引。
- 上下文和正文分列索引，BM25 权重固定为 2:1。完整上下文只能来自页面标题、当前章节路径或真实 DOM 结构关系。
- 排序层级固定为 `SameRegion`、`SameSection`、`SamePage`、`Partial`。只有上一层结果不足调用方上限时才会返回下一层；低层结果不能依靠词频越级。
- 同层先比较实体关系、概念覆盖和明确结构证据，再使用 BM25；最后按规范页面标题和区域顺序稳定排序。
- 单概念查询在同层内优先精确标题/Redirect/Alias，其次标题前缀、标题包含，最后正文提及；标题查询中的 `%`、`_` 和反斜杠按字面匹配。优先正文段落摘要，标题命中优先最早的介绍段落，避免零碎属性或历史片段抢占摘要。多概念查询继续遵守上述实体/覆盖/结构排序。
- 章节或页面放宽结果返回共同支持匹配的完整 `EvidenceRegions` 集合，并选择其中一个真实区域作为点击目标。证据块保持原区域边界，不拼接成原文没有表达的关系。
- 结果包含显示标题、规范页面标题、区域 ID/顺序、章节路径、已有 Anchor、完整证据、证据类型、匹配层级、Preview 和 Preview 高亮。`Snippet` 继续作为 Preview 的兼容字段。
- Preview 在查询时从证据生成；完整正文只保存在区域表。高亮使用 Preview 的零基 .NET UTF-16 起始位置与长度。
- 无真实区域或章节 ID 时 Anchor 为空，调用方打开页面并显示可读区域来源，不生成定位 ID。
- 先按规范页面合并，再应用 `MaxResults`；上限固定为 `1..100` 个页面。代表区域的 Scope 只描述代表证据的匹配关系，不宣称合并的所有证据属于同一区域。
- 取消不得变成空结果；数据源不可用、格式不支持和 Wiki 身份不匹配使用稳定错误语义。

## Read 契约

- Read 通过稳定页面身份读取同一 v2 内容库中的完整 Viewer HTML。
- Redirect、Anchor 和未找到页面的语义明确，调用方不直接拼接数据库查询。
- 搜索加工不修改 Viewer HTML。Viewer、手动阅读和 AI Adapter 读取同一份 Core 数据。

## 调用边界

- Presentation 和 Wiki Tool Adapter 都通过 Application Search/Read 用例访问 Wiki。
- Wiki Tool 保持现有协议，通过薄映射消费 Search；本变更不扩展 AI Tool、Runtime、Prompt、Token 或 Chat。
- AI transport、Python 进程和 Tool Calling 协议不得进入 Core。
- 不建设 Vector Search、Embedding Retrieval、Vector Database 或第二套 AI Wiki 数据。

## 验收场景

1. 汉字单字、双字、连续中文、拉丁词、数字和字面符号可在区域正文中查询。
2. Title、Redirect、Alias、多概念去重和输入顺序交换有确定性回归。
3. 同区域、同章节、同页面和部分匹配严格分层，低层不越级。
4. 表格、Infobox、列表与普通正文结果保留完整证据、来源、真实 Anchor 和合法 Preview 高亮。
5. 同页多区域合并为一个结果，证据区域按 ID 去重且保留真实 Anchor，再应用 `1..100` 个页面上限；“雪人”在精确页之后包含“雪人炮”等标题候选。
6. 手动 UI 与 Wiki Tool 获得顺序一致的候选；Read 正确处理 Redirect、Anchor 与不存在页面。
7. 取消、格式错误和数据源不可用无需调用方直接操作数据库即可得到明确结果。

## 变更规则

能力语义变化时更新本 spec；SQL、提取规则、权重和测试语料写入对应 change 与 ADR。
