# Application 用例边界契约

- 状态：confirmed baseline
- 适用范围：Application、Windows/未来 Android Adapters、Avalonia Presentation、Wiki Tool Adapter
- 来源：Master Architecture v0.6、ADR 0002、Phase 03 与 `rebuild-local-wiki-search`

## 目的

在 Portable Wiki Core 之上提供薄且平台无关的用例边界，使 Presentation、Viewer Adapter 与 AI 不访问 SQLite、持久化表或平台设置 API。

## 依赖与公共表面

- Application 是单一 `net10.0` assembly，只引用 Portable Wiki Core，不引用 UI、SQLite API 或目标平台 API。
- Application 只编排 Core；不得复制数据库、区域提取、FTS Search、同步、资源、数据库交换或 Viewer backend。
- 公共签名不暴露数据库连接和持久化实体。Search 可复用无持久化泄漏的 Core 查询契约；Read、library、asset、WikiBook 和数据库 import 结果映射为 Application DTO。

## 设置与活动 Wiki

- 强类型设置包含语言、主题、鼠标侧键、Search 上限、页面/资源并发和重试次数；活动 Wiki ID 独立存取。
- WikiFullScanIntervalDays 默认为 14，接受正整数天数，null 关闭定期核对；非法存储值修复为默认，显式非法设置拒绝。保存周期只更新应用设置，不清除同步状态、不联网、不启动后台任务；首次同步、失败恢复和锚点失效仍强制核对。设置控件另由后续 UI change 实施。
- 默认 Search 上限为 50，允许范围为 `1..100`；页面并发为 `1..3`，资源并发为 `1..20`，重试为 `0..20`。
- 初始化打开 Manager、恢复全部已注册 Wiki 的未完成 raw import，再选择保存的 Wiki。
- Content 初始化只检查 v2 格式、加工版本、Wiki 身份和状态，不迁移旧目录、不扫描 HTML、不全库建索引。没有数据库时创建当前 Wiki 的空 v2 库。
- 格式或身份错误以稳定 Application code 失败；不得自动转换旧内容库。
- 正常初始化使用 Manager 保存的统计快照；显式统计和数据操作刷新实际值。
- 热切换在共享数据操作门闩和消费者 pause lease 内执行；目标初始化或设置保存失败时恢复旧活动数据源。

## Query / Read / Asset

- Search 默认使用设置上限，也允许显式指定 `1..100`。区域身份、章节、Anchor、完整证据集合、Preview、高亮、Alias 和四级匹配语义沿用 Core。
- Read 是纯查询，保留 Redirect、Anchor、not found、取消和数据源不可用语义；完整 Viewer HTML 不经过搜索证据替换。
- Wiki Tool Adapter 通过同一 Search/Read 用例和薄映射保持既有协议。
- WikiAsset 通过稳定 DTO 返回；Viewer transport 属于平台 Adapter。

## Library

- 页面使用零基 offset、`1..200` limit 和可选标题过滤；页面按标题升序。
- 收藏查询、添加与移除均通过用例；重复操作幂等。
- 全库收藏枚举 Manager 注册 Wiki，只打开存在的当前 `data.db`，不创建、迁移、checkpoint 或查找旧数据库名称。
- Application 与 Viewer 不读写旧持久 WikiHistory。

## 数据生命周期与数据库交换

- 下载、资源下载、更新、失败重试/清理、统计、VACUUM、删除资源和删除整库只作用于活动 Wiki。
- 页面下载与更新复用 Core 页面事务；解析或事务失败保留在失败重试状态。
- `UpdateAsync(bool includeResources, ...)` 保持签名，透传 Core 的增量/完整核对及恢复语义；summary 的 Pages 只计实际成功替换，Checked/Skipped/Deleted/UsedFullScan 区分检查、跳过、删除与完整核对。无内容变化时 Changed=false，仍可推进成功覆盖时间。
- raw export 作用于活动 Wiki，通过 Core SQLite backup 返回独立 v2 `.db` 快照。
- raw import 固定为 Inspect → inspection token → 调用方确认 → 当前活动 Wiki 覆盖；不从路径或文件名猜测目标。
- import 使用消费者 pause、共享 gate、staging/backup/journal/rollback；成功后刷新 Wiki 统计，失败恢复原数据源。
- Application 不提供 `.pkg` inspect/import/export 或 legacy confirmation 用例。
- `OperationCanceledException` 不得转换为空结果或普通失败 DTO。

## Ports

- `IApplicationSettingsStore` 是设置与活动 Wiki ID 的宿主持久化边缘。
- `IWikiDataConsumerCoordinator` 暂停活动数据消费者并返回异步 lease。
- Core HTTP transport 负责下载；picker、日志、Viewer 和窗口仍属于宿主。

## 验收场景

1. Application 依赖和公共 API 不泄漏 UI、SQLite connection 或持久化模型。
2. 设置默认、修复、合法/非法保存和活动 ID 更新有确定性测试。
3. 初始化、切换、占用、取消与回滚保持一致活动数据源，pause lease 始终释放。
4. Search 透传完整区域证据且与 Wiki Tool 顺序一致；Read、asset、页面和收藏无需数据库 API。
5. 下载与 raw database 交换使用 fake HTTP、临时 SQLite 和 journal fixture 覆盖成功、错误、取消与恢复。
6. 反射和静态检查确认 `.pkg` 与旧迁移入口不再属于 Application 公共表面。

## 变更规则

用例、ports 或公共契约变化时更新本 spec；具体类、文件和平台命令进入对应 change。
