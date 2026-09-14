# 重建本地 Wiki 数据库与区域搜索

- 状态：archived；真实数据库验收通过并已归档
- 当前进度：实施与真实数据库验收 9/9 已完成
- Phase：无
- 执行环境：WSL2 Codex CLI 实施；Windows 11 x64 构建、测试和本地 Release 验证
- 创建日期：2026-09-13
- 最后更新：2026-09-14
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
7. Windows Release 构建与自动化通过，交付可运行目录；真实语料的查询、性能、体积、构建和更新成本通过验收后完成归档。

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

- [x] 建立新格式元数据、区域模型、确定性 HTML 提取与 token 编码，并完成结构测试。
- [x] 建立 contentless FTS5 的逐页事务写入、格式校验、增量更新与删除测试。
- [x] 实现 Alias、实体解析、多层召回/排序、Preview/完整证据/高亮与核心查询测试。
- [x] 扩展 Search DTO，适配 Application、WikiTools 兼容映射和 Windows 区域来源/Anchor 导航。
- [x] 收拢 raw `.db` 交换并删除 `.pkg`、旧内容迁移及过时契约。
- [x] 更新架构、spec、ADR、路线图与旧计划 superseded 记录。
- [x] 在 Windows 执行 Release build、相关及全量测试，构建本地 Release 目录并做隔离 smoke。
- [x] 核对并删除默认数据根旧内容，重置 Manager 下载统计，记录不可恢复边界和新程序下载步骤。
- [x] 用户下载真实数据后执行相关性、P50/P95、体积、全量构建、单页更新、索引对照和受保护数据库交换验收。

当前实施与真实验收进度为 9/9。验收明细见 `real-acceptance.md`；本计划已按治理规则归档。

## 验证与回归计划

- Core：表格合并单元格、Infobox 掉落、嵌套列表、异常 HTML、单字/双字、多词顺序、Alias/Redirect、层级排序、去重、取消、字面符号和事务回滚。
- Application/Windows：Search 透传、结果来源、Anchor 点击、Viewer/收藏/Wiki 切换、raw 数据库导入导出和恢复。
- Windows：Release solution build、全量 .NET tests、本地 Release 运行目录、搜索到唯一 Reader 的隔离 smoke。
- 真实库：八个核心查询；冷暖延迟及 P50/P95；各表/索引体积；下载构建与单页更新成本。暖查询 P95 目标不超过 200 ms。

## 风险与回退

- contentless FTS 删除依赖旧 token：逐页事务先读取旧区域、发出 FTS delete，再删除区域；测试强制中途失败证明整体回滚。
- HTML 结构多样：专用 table/infobox/list 提取后，以通用块回退覆盖未消费可见文字，并通过真实 HTML 抽样和合成 fixture 防止漏失与重复。
- 新格式不兼容旧库：启动和导入用明确版本错误失败关闭；用户通过旧 Release 或外部备份恢复旧数据，代码可回退到保存点。
- 本机旧内容删除不可由 Git 恢复：删除前关闭连接、解析绝对目标并限制在默认数据根注册 Wiki 目录；下载可恢复内容。

## 进度记录

- 2026-09-13：用户批准完整实施，并确认同页多区域、废除旧 `.pkg`/旧内容迁移、本地 Release 交付和下载后真实验收顺序。
- 2026-09-13：从 `master@2497f9b` 建立 `codex/rebuild-local-wiki-search`；开始实现。
- 2026-09-14：完成 v2 数据库、区域提取、contentless FTS5、Alias、多层搜索、raw `.db` 交换收拢及调用方适配。
- 2026-09-14：Windows Release solution build 以 0 warning/0 error 通过；全量测试 307/307 通过；self-contained 本地 Release 与绑定 Python Runtime 构建完成，隔离 v2 数据库的搜索到 Reader smoke 通过。
- 2026-09-14：关闭测试程序后核对默认数据根，只删除注册的 `Terraria_Wiki_zh/data.db` 所在内容目录并清空空的 `Temp`；Manager 对应下载统计重置为 0。配置、Manager 备份、日志、设置及 AI 会话未删除。
- 2026-09-14：用户确认完整下载结束。下载库包含 4359 页、28700 条 Redirect、25730 个资源、25022 个章节和 332386 个搜索区域。
- 2026-09-14：真实查询发现全局候选上限会遮蔽实体页面区域，并发现区域 Anchor 会误取后代引用 ID；增加实体页内召回、收紧 Redirect/Alias 变体和真实 Anchor 选择，加工版本升至 2。
- 2026-09-14：真实库八个查询全部通过；200 个暖查询样本 P50 21.8570 ms、P95 81.2922 ms。全库重建 124.6768 秒，代表页事务更新 P95 78.5299 ms。
- 2026-09-14：同语料索引对照后保留字符位置索引；验收副本经受保护 raw `.db` 交换安装为默认活动库。最终 Release build、310/310 测试和隔离 GUI smoke 通过。

## 发现、偏差与决策

- 被否决旧方案保留 trigram 与 ShortTerm 派生表，并在启动后台构建；本轮不复用该路径。
- 官方页面在线访问返回 403；下载后的本地 HTML 和数据库成为真实验收事实源。
- 初次隔离 smoke 的显式 `--data-root` 被既有 Host 设置逻辑持久化，导致用户下载落入 smoke 目录。下载完成后核对库身份、计数与合成标记，将完整内容目录移回默认数据根，同步 Manager 统计并恢复 Host 设置；后续 smoke 固定使用独立 `--host-settings`。
- 全局 FTS 候选上限不能保证实体页面的相关区域进入排序。搜索现在对已解析实体页补充受页面约束的其他概念召回，且 Redirect 只在名称包含关系成立时同时检索原名；社区 Alias 只检索规范实体名，避免“猪鲨链球”等偶然正文抢占实体结果。
- 后代引用 ID 不是区域位置。加工版本 2 只接受区域自身/祖先 ID，标题只接受自身/直接子节点 ID；旧加工版本由启动与导入校验明确拒绝。
- 真实全库逐页替换会留下可合并的 FTS 段。完整下载增加 FTS5/SQLite 优化和空间回收，增量与失败重试只优化实际变更后的索引。

## 验证结果

- `dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore`：最终源码在 Windows 通过，9 projects，0 warning、0 error。
- `dotnet test Terraria_Wiki.Core.slnx -c Release --no-build --no-restore`：最终源码通过，Core 51、Application 51、WikiTools 45、Windows 163，共 310/310。
- 本地 Release：`artifacts/local-release/rebuild-local-wiki-search/Terraria_Wiki.Windows.exe`；self-contained win-x64，含 Viewer、Search Alias 与由锁定依赖生成的 Python Runtime。
- 最终隔离 smoke：使用独立 Host 设置和合成数据根，`success=true`，搜索返回 3 个区域结果，Redirect 页面打开、Reader/Viewer 导航、脚本、样式、图片、音频与退出清理均通过；证据为 `artifacts/acceptance/rebuild-local-wiki-search/final-windows-smoke.json`。
- 真实库：格式 2、加工版本 2、Wiki ID 1，`integrity_check=ok`；数据库 4,132,286,464 字节，其中媒体 Blob 3,512,314,689 字节、页面 HTML 470,447,097 字节、区域行 55,326,370 字节、FTS 载荷 38,965,260 字节。
- 八个查询均返回有效区域和完整证据；三个组合查询首项分别为光之女皇、猪龙鱼公爵和克苏鲁之眼。暖查询 P50 21.8570 ms、P95 81.2922 ms，满足 200 ms 目标。
- 全库加工版本 2 重建：4359 页、124.6768 秒、平均 28.6022 ms/页；“光之女皇”20 次页面事务更新 P50 44.7355 ms、P95 78.5299 ms。
- 索引对照：当前字符位置独立索引 42,627,072 字节，trigram+短词扫描 69,296,128 字节，unigram/bigram 63,873,024 字节；当前方案在完整排序下八个预期页面均为首项，故保持不变。
- raw `.db` 真实交换使用 inspection token、staging、backup、journal 与 rollback 路径，通过后无交换残留，默认数据根和 Manager 统计正确。

## 完成摘要

实现与真实数据库验收均已完成。最终程序继续使用单一 v2 内容库和区域级字符位置 FTS5；未制作安装包或公开发布。详细证据见 `real-acceptance.md`，本变更进入归档。
