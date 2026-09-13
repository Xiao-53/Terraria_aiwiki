# 重建本地 Wiki 数据库与区域搜索

- 状态：in-progress；实现交付后等待用户下载真实数据验收
- Phase：无
- 执行环境：WSL2 Codex CLI 实施；Windows 11 x64 构建、测试和本地 Release 验证
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：SQLite 内容库 v2、HTML 区域提取、FTS5 搜索、社区 Alias、下载增量事务、Search DTO、必要的 Windows 结果适配、旧内容兼容与 `.pkg` 退休
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/wiki-data-compatibility/spec.md`、`openspec/specs/application-use-cases/spec.md`、`docs/adr/0011-use-managed-data-root-and-raw-database-transfer.md`

## 背景与当前证据

当前 Core 把完整 Viewer HTML 存入 `WikiPage`，搜索只把普通段落、标题和列表合成一条 trigram FTS 文本；Infobox 被排除，表格未覆盖。页面与搜索索引分两次提交，失败可造成不一致。现有七级 Search 只允许三个 Unicode scalar 以上的正文词，不能满足中文单字、区域结果和表格上下文。

本机旧中文库约 4.09 GB、4390 页、29774 条 Redirect，并残留已否决方案的派生表。它只用于设计抽样；用户明确授权废弃旧开发期内容库，并要求由新版程序重新下载。真实质量、体积和性能验收延后到用户完成下载后。

## 目标与完成定义

1. 新内容库保存格式与 Wiki 身份元数据、完整页面 HTML、结构化搜索区域和单套 FTS5 索引；旧内容格式明确拒绝。
2. 正文、标题、列表、定义列表、表格和 Infobox 的有意义文字均形成带页面、章节、Anchor 与上下文的区域；Viewer HTML 不因搜索加工改变。
3. 单字/双字中文正文可查，多个关键词按同一区域、同章节、同页面、部分覆盖分级，实体 Title/Redirect/Alias 高于普通正文。
4. Search 返回页面、区域、章节来源、Anchor、完整证据、Preview 和高亮；同一页面可返回多个去重区域。
5. 页面 HTML、区域和 FTS 在单个事务内更新；少量页面更新不全库重建，应用启动不扫描旧 HTML 建索引。
6. 删除旧 `.pkg` 公共 API/实现及旧内容库迁移；保留并验证新 `.db` 的检查、快照、staging、journal 与 rollback。
7. Windows Release 构建与自动化通过，交付可运行目录。计划保持等待真实下载验收状态，真实指标通过后再完成归档。

## 前置条件

- 当前分支从 `master@2497f9b` 创建；已有 `.codex/config.toml`、路线图和被否决旧计划改动不混入本变更保存点。
- Windows 已安装 .NET 10 与现有项目依赖；不安装 MAUI 或 Android 工具链。
- 真实下载验收由用户在本轮本地 Release 程序交付后执行。

## 范围与非目标

实施 Core/Application/Windows 搜索链路、新数据库校验与 raw `.db` 交换；同步相关规范、架构和 ADR。保留 Manager Wiki 配置、收藏、资源、Viewer、下载更新和 AI 会话。

不修改 AI Runtime、Prompt、Token、Chat、`read_wiki` 协议、整体搜索 UI、Viewer 架构、依赖版本、公开发布或 Android。

## P0 / 兼容性影响

- Viewer 继续读取 `WikiPage.Content`；完整与轻量下载只有媒体资源行为不同。
- 新程序拒绝旧内容库，不执行启动迁移或后台全库构建。用户数据通过重新下载恢复。
- raw `.db` 只接受 v2 格式与完整索引；旧 `.pkg` 能力及其迁移入口删除。
- Manager、设置、AI 会话不属于删除范围。本机只删除已核对的默认数据根中注册 Wiki 的旧内容目录和辅助下载文件。

## 方案与决策

- 使用普通区域表保存完整正文与上下文，使用 contentless FTS5 保存派生词元。中文 Han scalar 编码为逐字 token，拉丁/数字按连续词编码；查询连续中文生成 FTS 短语。
- 表格构造逻辑网格并展开 rowspan/colspan；行区域继承 caption、列头、分组头与行头。Infobox 的属性、掉落分组和列表项继承页面实体及结构标签。
- 查询对每个不重复概念分别召回区域，在内存按规范页面、章节和区域聚合。层级固定为同区域完整、同章节完整、同页面完整、部分；同层再比较实体覆盖、直接身份、标题/章节证据、BM25、页面标题和区域顺序。
- Alias 使用发布资产中的版本化 JSON，按 Wiki ID 加载；初始包含 `克眼`、`猪鲨`、`光女`。Alias 不写 Redirect、不联网、不调用 AI。
- 页面下载调用单个数据库事务，写 `WikiPage`、删除旧区域/FTS 并写新区域/FTS。解析失败使该页面下载任务失败并进入既有重试链路。
- 搜索结果点击使用区域 Anchor；无真实 Anchor 时只打开页面并显示区域来源。

## 文件变更

- ADD：搜索区域/词元/Alias Core 类型和服务、`assets/search/community-aliases.zh-cn.json`、新格式 ADR、区域提取与查询测试。
- MODIFY：`WikiContentProcessor`、数据库模型/服务、`WikiReader`、`WikiDataLifecycle`、Application 组合与 Search DTO 消费、Windows 结果标签/导航、raw `.db` 校验与相关 specs。
- REMOVE：Core `.pkg` reader/writer/contracts/path policy 与 transfer 分支、Application `.pkg` DTO/use cases、旧内容迁移实现及专属测试。
- KEEP：Viewer、WikiAsset、WikiFavorite、Manager WikiBook、Python Agent、AI Runtime、整体 Wiki XAML 布局。

## 执行步骤

- [ ] 建立新格式元数据、区域模型、确定性 HTML 提取与 token 编码，并完成结构测试。
- [ ] 建立 contentless FTS5 的逐页事务写入、格式校验、增量更新与删除测试。
- [ ] 实现 Alias、实体解析、多层召回/排序、Preview/完整证据/高亮与核心查询测试。
- [ ] 扩展 Search DTO，适配 Application、WikiTools 兼容映射和 Windows 区域来源/Anchor 导航。
- [ ] 收拢 raw `.db` 交换并删除 `.pkg`、旧内容迁移及过时契约。
- [ ] 更新架构、spec、ADR、路线图与旧计划 superseded 记录。
- [ ] 在 Windows 执行 Release build、相关及全量测试，构建本地 Release 目录并做隔离 smoke。
- [ ] 核对并删除默认数据根旧内容，重置 Manager 下载统计，记录不可恢复边界和新程序下载步骤。
- [ ] 用户下载真实数据后执行相关性、P50/P95、体积、全量构建和单页更新验收。

## 验证与回归计划

- Core：表格合并单元格、Infobox 掉落、嵌套列表、异常 HTML、单字/双字、多词顺序、Alias/Redirect、层级排序、去重、取消、字面符号和事务回滚。
- Application/Windows：Search 透传、结果来源、Anchor 点击、Viewer/收藏/Wiki 切换、raw 数据库导入导出和恢复。
- Windows：Release solution build、全量 .NET tests、本地 Release 运行目录、搜索到唯一 Reader 的隔离 smoke。
- 后续真实库：八个核心查询；冷暖延迟及 P50/P95；各表/索引体积；下载构建与单页更新成本。暖查询 P95 目标不超过 200 ms。

## 风险与回退

- contentless FTS 删除依赖旧 token：逐页事务先读取旧区域、发出 FTS delete，再删除区域；测试强制中途失败证明整体回滚。
- HTML 结构多样：专用 table/infobox/list 提取后，以通用块回退覆盖未消费可见文字，并通过真实 HTML 抽样和合成 fixture 防止漏失与重复。
- 新格式不兼容旧库：启动和导入用明确版本错误失败关闭；用户通过旧 Release 或外部备份恢复旧数据，代码可回退到保存点。
- 本机旧内容删除不可由 Git 恢复：删除前关闭连接、解析绝对目标并限制在默认数据根注册 Wiki 目录；下载可恢复内容。

## 进度记录

- 2026-09-13：用户批准完整实施，并确认同页多区域、废除旧 `.pkg`/旧内容迁移、本地 Release 交付和下载后真实验收顺序。
- 2026-09-13：从 `master@2497f9b` 建立 `codex/rebuild-local-wiki-search`；开始实现。

## 发现、偏差与决策

- 被否决旧方案保留 trigram 与 ShortTerm 派生表，并在启动后台构建；本轮不复用该路径。
- 官方页面在线访问返回 403；本地页面 HTML 足以确认 Boss 掉落位于 Infobox 分组列表，宝藏袋页面存在 colspan/rowspan 表格。本轮自动化使用合成 HTML，真实验收等待新下载库。

## 验证结果

待实施记录。

## 完成摘要

待用户下载真实数据并通过最终验收后填写。
