# Application 用例边界契约

- 状态：confirmed baseline
- 适用范围：Application、Windows/未来 Android Adapters、Avalonia Presentation、Wiki Tool Adapter
- 来源：Master Architecture v0.6、ADR 0002、Phase 03 Application Boundary

## 目的

在 Portable Wiki Core 之上提供稳定、薄且平台无关的用例边界，使 Presentation、Viewer Adapter 与 AI 不访问全局 `App.*`、SQLite、持久化表模型或平台设置 API。

## 契约

### 依赖与公共表面

- Application 是单一 `net10.0` assembly，只引用 Portable Wiki Core，不引用 MAUI、Avalonia、Razor、WebView、SQLite API 或目标平台 API。
- Application 只编排 Core；不得复制数据库、FTS5 Search、同步、资源、package、迁移或 Viewer backend 实现。
- 公共签名不得暴露 `DatabaseService`、SQLite connection 或 `WikiBook`、`WikiPage`、`WikiHistory`、`WikiFavorite`、`WikiAsset` 持久化模型。
- Search、progress、operation summary 与 package inspection 可直接复用无持久化泄漏的 Core 契约；Read、library、asset、WikiBook 和 import 结果映射为 Application DTO。

### 设置

- 强类型设置包含语言、应用主题、内容主题、侧键返回、Search 上限、页面/资源并发和重试次数；活动 Wiki ID 独立存取。
- 默认值固定为 `zh-cn`、`auto`、`auto`、侧键返回开启、Search 50、页面并发 2、资源并发 10、重试 5、活动 Wiki 1。
- Search 为 `1..100`，页面并发为 `1..3`，资源并发为 `1..20`，重试为 `0..20`；语言和主题只接受已支持值。
- 存储中的非法旧值按字段修复为默认并回写；调用方主动保存非法值必须拒绝且不得部分写入。
- 宿主通过 `IApplicationSettingsStore` 分别加载/保存设置与活动 Wiki ID；Application 不读取平台 Preferences。

### 初始化与活动 Wiki

- 初始化先打开 Manager、恢复全部已注册 Wiki 的未完成 import，再选择保存的 Wiki。
- 保存 ID 缺失或未知时优先选择 ID 1，否则选择最小注册 ID，并把实际选择写回；无注册 Wiki 或未初始化使用稳定 Application 错误 code。
- 打开 ContentDb 前完成已有旧目录、数据库家族和 anchor 迁移；没有现有数据库时允许 Core 初始化活动 Wiki 的默认内容。
- 热切换必须在共享数据操作门闩和 `IWikiDataConsumerCoordinator` pause lease 内执行。同 ID 幂等且不 pause；未知 ID 不改变任何状态。
- 目标初始化或设置保存失败时恢复旧 ContentDb、活动快照和旧设置；回滚失败使用独立稳定 code，不要求宿主重启。

### Query / Read / Asset

- Search 默认使用当前设置上限，也允许调用方显式指定 `1..100`；七级排序、短词、摘要与高亮语义完全沿用 Core。
- Read 是纯查询，保留 Redirect、anchor、not found、取消和数据源不可用语义；AI 或其他调用方 Read 不写历史。
- Application 不再公开或调用持久 Wiki 浏览历史的查询、记录或清空用例。
- WikiAsset 通过稳定 DTO 返回文件名、字节、MIME type 和修改时间；Viewer transport 仍属于后续 Adapter。

### Library

- `PageRequest` 使用零基 offset、`1..200` limit、可选 title filter；`PageSlice<T>` 同时返回当前项与过滤后的总数。
- 页面按标题升序，收藏按收藏时间降序；日期与本地化属于 Presentation。
- favorite 查询、添加与移除均通过用例；重复添加或移除保持幂等。
- Core `WikiHistory` 模型、旧表建表和 schema 容忍只为旧数据库/数据包兼容保留；Application 与 Viewer 不读写旧行，也不主动删除旧表或数据。

### 数据生命周期与 package

- 下载、资源下载、更新、失败重试/清理、统计、VACUUM、删除资源和删除整库仅作用于活动 Wiki。
- export 可指定任一已注册 Wiki；文件选择、输出命名、覆盖策略、确认、日志和本地化由调用方负责。
- import 固定为 Inspect → inspection token → 调用方显式 legacy confirmation → Import 两阶段流程；Application 不静默确认旧包。
- 活动库 import、整库删除、热切换和旧数据迁移在消费者 pause lease 下执行；操作结束或失败都必须恢复消费者。
- Application 保留 Core 取消和稳定错误语义；`OperationCanceledException` 不得转换为空结果或普通失败 DTO。

### Ports

- `IApplicationSettingsStore` 是设置与活动 Wiki ID 的唯一宿主持久化边缘。
- `IWikiDataConsumerCoordinator` 暂停活动数据消费者并返回异步 lease；释放 lease 时恢复。
- 除现有 Core HTTP transport 外，本阶段不为 picker、日志、主题效果、Viewer、窗口或平台文件系统增加 port。

## 验收场景

1. Phase 04 宿主只实现两个 Application ports 并提供 Core HTTP transport，即可组合五组用例。
2. 设置默认、修复、合法/非法保存和活动 ID 独立更新有确定性测试。
3. 初始化、切换、占用、取消、初始化/保存失败与回滚均保持一致活动数据源；pause lease 始终释放。
4. Search/Read/asset 和页面/收藏无需访问数据库 API；排序、分页、过滤、总数及写操作有回归，Application 公共表面不再暴露 WikiHistory 用例。
5. lifecycle 和 package 使用 fake HTTP、临时 SQLite 和合成 package 覆盖成功、错误、取消与 rollback。
6. 静态与反射检查确认 Application 的依赖和公共 API 不泄漏平台或持久化实现。

## 变更规则

用例语义、ports 或公共数据契约变化时更新本 spec；具体类签名、文件布局、fixture 和平台命令进入对应 Phase 计划。平台 Adapter、Viewer transport、AI transport 和 Presentation 行为分别由后续 Phase 决定。
