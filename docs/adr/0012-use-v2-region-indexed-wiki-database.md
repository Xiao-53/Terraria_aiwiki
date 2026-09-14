# ADR 0012：使用 v2 区域索引 Wiki 数据库并退休旧内容兼容

- 状态：accepted
- 日期：2026-09-14
- 决策范围：内容数据库格式、HTML 搜索加工、FTS5、旧内容格式与 `.pkg` 兼容
- 关联：`docs/adr/0011-use-managed-data-root-and-raw-database-transfer.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/wiki-data-compatibility/spec.md`、`openspec/changes/archive/rebuild-local-wiki-search/plan.md`

## 背景

旧内容库只为每页保存一条 trigram FTS 文本，普通段落之外的表格和 Infobox 证据缺失，中文单字和双字正文无法稳定查询。页面 HTML 与搜索索引分别提交，下载中断可能留下新页面配旧索引。启动迁移和旧 `.pkg` 分支又让当前数据库交换与退休格式交织在同一实现中。

本轮用户明确选择重新下载内容，允许旧开发期内容库和 `.pkg` 兼容退休。Manager Wiki 配置、完整 Viewer HTML、Redirect、媒体、收藏、设置和 AI 会话仍须保留。

## 决策

1. 内容库格式升级为 v2，并在库内保存 `format_version`、`extractor_version` 和 `wiki_id`。无这些元数据、版本不符或 Wiki 身份不符的库明确拒绝，不自动转换。
2. 每页保存完整 HTML、章节与搜索区域。区域按 DOM 顺序保存类型、章节路径、真实 Anchor、结构上下文和完整正文；Preview 只在查询时生成。
3. 表格先建立展开 rowspan/colspan 的逻辑网格，再按数据行保存 caption、列头、分组头和行头。Infobox、定义列表和嵌套列表保存真实继承关系；折叠模式内容不因显示状态被丢弃。
4. 唯一正文索引是 contentless FTS5。汉字逐 Unicode scalar 编码，拉丁字母和数字按词编码，字面符号确定性编码；连续中文查询使用位置短语。不保留旧 trigram 或 ShortTerm 表。
5. 页面 HTML、章节、区域与 FTS 使用同一 SQLite 事务更新或删除。contentless FTS 删除词元由旧区域正文重新生成；解析或事务失败进入现有重试。
6. Search 先确定性解析 Title、官方 Redirect 和按 Wiki 身份分发的社区 Alias，再按同区域、同章节、同页面、部分匹配分层。上下文与正文 BM25 权重为 2:1，层级优先于词频。
7. `.pkg` reader、writer、容器、公共用例和旧内容/Anchor 迁移删除。raw `.db` 导入导出继续使用 ADR 0011 的快照、inspection token、staging、backup、journal 和 rollback，并增加 v2 格式、身份与搜索一致性验证。
8. 完整版和轻量版共用页面 HTML 与搜索加工，只改变媒体资源下载。应用启动只检查格式和恢复状态，不遍历 HTML 建索引。

## 后果

- 搜索结果保留同页多个真实区域的完整证据集合、Preview 和定位来源。2026-09-14 按用户对重复结果的反馈调整为每规范页一条结果，选择真实代表区域作为摘要/点击目标；单概念查询优先标题候选和介绍正文。该查询层调整见 `openspec/changes/improve-search-page-results/plan.md` 与持续 Search spec，不改变区域索引或数据库格式。
- 新程序不能读取旧开发期内容库；内容须由新版下载流程重建。代码回退也不能恢复已明确删除的本机内容。
- contentless FTS 避免重复保存正文，但逐页替换必须保留由旧正文生成删除词元的事务规则。
- raw `.db` 具有稳定 Wiki 身份，导入不再依靠当前目标之外的猜测。
- 真实相关性、性能和体积必须用用户下载的完整语料验证；2026-09-14 的验收结果记录在归档 change 的 `real-acceptance.md`。

## 被否决方案

- 同时保留 trigram、ShortTerm 和新索引：增加体积与多套更新一致性风险。
- 启动扫描旧 HTML 后台补建：已下载数据不会随页面事务立即可查，且把构建成本放入启动路径。
- 为短中文词执行正文全表扫描：无法提供稳定的完整语料性能边界。
- 把完整表格复制到每一行：放大数据库并稀释行级证据。
- 伪造 Anchor：会把 Reader 导航到不存在的位置。

## 验证

合成 HTML、SQLite 故障注入、raw 数据库交换、Application/WikiTools/Windows 回归和本地 Release 构建在 change 计划中记录。完整真实 Wiki 的八个查询、P50/P95、体积、全库构建、单页更新和候选索引对照于 2026-09-14 通过验收；最终选择保持单套字符位置 FTS5。
