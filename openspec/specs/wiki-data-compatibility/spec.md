# Wiki 数据库兼容与交换契约

- 状态：confirmed baseline
- 适用范围：Portable Wiki Core、Application、Windows 与未来 Android
- 来源：`rebuild-local-wiki-search`、ADR 0011 与 ADR 0012

## 目的

定义 v2 内容数据库、逐页更新和单文件数据库交换的可恢复边界，同时保留 Manager、Viewer、资源、收藏和多 Wiki 能力。

## 数据库格式

- 每个 Wiki 使用一个 `data.db`，Manager 继续保存 Wiki 配置和下载统计。
- v2 内容库必须声明 `format_version`、`extractor_version` 和 `wiki_id`。应用启动和导入都验证格式、加工版本及 Wiki 身份。
- 内容库保存完整 `WikiPage.Content` HTML、Redirect、资源、收藏、章节和搜索区域。搜索区域保存上下文与完整正文；Preview 不是持久化原文。
- 唯一全文索引是 contentless `WikiSearchRegionFts`。FTS 行与区域行一一对应，不存储第二份正文；旧 trigram 与 ShortTerm 索引不属于 v2。
- v1 及无元数据的开发期内容库明确拒绝，不自动转换、不在启动时遍历 HTML 补建索引。内容通过新版下载流程恢复。
- Manager、设置和 AI 会话不随内容格式退休而删除。

## 内容事务与生命周期

- 页面下载、增量更新和失败重试调用同一页面事务；页面 HTML、页面元数据、章节、区域和 FTS 全部提交或全部回滚。
- 替换区域时使用旧区域正文重新生成 contentless FTS 删除词元，再写入新区域和词元。空正文更新必须清除旧章节、区域和索引。
- 删除页面必须在同一事务中删除其章节、区域、FTS 与页面行。
- 页面事务提交后即可搜索；下载结束只记录状态并执行必要维护。单页变化不得重建全库。
- 解析或事务失败进入既有失败重试，不能计为下载成功。
- 完整版与轻量版使用同一 HTML 获取、处理和存储链路，仅媒体资源下载行为不同。

## 单文件数据库交换

- UI 导入/导出当前活动 Wiki 的完整 `.db`；旧 `.pkg` 容器、reader/writer、迁移和公共用例已经退休。
- 导出必须从活动 SQLite 连接创建一致快照，并收口为没有 `-journal/-wal/-shm` 的独立文件。
- 检查必须覆盖 integrity、v2 表/主键/索引、格式与加工版本、Wiki 身份、真实 FTS5 能力、区域/FTS 数量和孤立引用，并以内容 hash 绑定 inspection token。
- 导入只覆盖明确的当前活动 Wiki。生产替换前把来源复制到同卷 staging，重新核对 token 和 Wiki 身份并再次验证。
- 目录级 backup、journal、rollback 与中断恢复保护原目标；当前 Wiki 的下载辅助文件在 raw 导入中保留。
- 成功后 Manager 的页面、Redirect、资源、大小和下载状态来自新库。失败、中断或 token 变化不得留下部分生产数据。

## 收藏与单一事实源

- 收藏只读取 Manager 中注册 Wiki 的当前 `data.db`；不查找或迁移旧数据库名称。
- 手动搜索、离线阅读和 AI 复用同一内容库，不建立第二套 Wiki 数据库、向量库或预生成知识副本。

## 验收场景

1. 新库声明正确格式、加工版本和 Wiki 身份；旧格式与错误 Wiki 的导入均在替换前拒绝。
2. 合成页面验证成功提交、强制故障回滚、空内容更新和删除后的页面/章节/区域/FTS 一致。
3. 应用重启不从页面 HTML 生成缺失区域；已提交页面无需下载结束步骤即可搜索。
4. 完整与轻量下载的页面、章节、区域和 Redirect 一致，只有媒体资源不同。
5. raw 导出包含 WAL 最新提交且无 sidecar；导入 token、结构、身份或索引错误时原库继续可用。
6. raw 导入成功保留辅助状态，失败和模拟中断可由 backup/journal 恢复。
7. Wiki 切换、收藏和 Viewer 继续使用当前 `data.db`。

## 变更规则

格式、加工版本或交换语义变化时更新本 spec 和 ADR；具体 SQL、fixture 与平台命令进入对应 change。
