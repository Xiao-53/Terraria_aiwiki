# ADR 0002：单一 Portable Core 与旧数据根原位复用

- 状态：accepted
- 日期：2026-08-24
- 决策范围：Phase 01 的生产代码物理边界、宿主依赖方式与 Windows 旧数据根策略
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/changes/archive/phase-01-portable-wiki-core/plan.md`

## 背景

现有 Terraria Wiki Offline 是内层源码仓库中的 MAUI 单项目。持久化模型、SQLite/FTS5、抓取更新、内容处理、数据包和维护逻辑与 MAUI 路径、设置、弹窗、文件选择及全局 `App.*` 混合。Phase 00 已证明这些核心数据能力可在普通 .NET 10 Windows 进程中运行，并确认真实旧数据约 3.34 GB、使用 WAL，不能通过简单复制单个 `.db` 文件安全迁移。

Phase 01 必须决定正式 Core 放置位置、最小物理拆分和旧数据是否迁移。用户已选择完整 P0 Core、内层源码仓库和旧数据原位复用。

## 决策

1. 正式生产 Core 放在内层 `terraria-wiki-offline/` 源码仓库，建立一个 `net10.0` Core project 和独立 Core-only test/solution。
2. Phase 01 只建立一个 Core assembly。数据库、查询/阅读、抓取更新、内容处理、资源、数据包与旧数据迁移按文件夹组织，不提前拆分多个基础 project。
3. 旧 MAUI project 通过 `ProjectReference` 使用这份 Core；Presentation、Viewer transport 和平台行为继续留在旧宿主 façade，直到后续 Phase 接管。
4. Core 不发现平台数据目录，只接收宿主提供的绝对数据根。数据布局继续是 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db`。
5. Windows 迁移期间原位复用现有数据根，不自动复制约 3 GB 数据到新产品目录。旧 MAUI 当前传入 `FileSystem.AppDataDirectory`；未来 Windows Adapter 负责定位同一旧根，Core 不硬编码该平台路径。
6. Core 拒绝相对根、路径穿越和非法 `DataFolder`。涉及旧格式升级、数据包导入或 destructive maintenance 时使用同卷 staging/backup/rollback，并把 `.db/-wal/-shm` 作为一致数据集处理。
7. 移动的旧持久化类型在 Phase 01 暂时保留现有 namespace 和数据映射，避免把物理提取与大规模 API 重命名合并为一次风险变更。

## 后果

正面后果：

- 新 Core 与测试不需要 MAUI、Android、Avalonia 或 WebView 工具链。
- 旧、新宿主共享同一份后端实现和本地数据，不复制第二套 Wiki 数据。
- 避免首次启动复制数 GB 数据及其失败、空间和一致性风险。
- 平台路径、网络特例和 UI 反馈通过小型边界注入，不反向污染 Core。

负面后果与约束：

- 新 Windows 宿主在后续 Phase 必须可靠定位旧 MAUI 数据根；定位失败时应明确要求用户恢复/选择，而不能静默创建看似空白的第二根。
- 迁移期旧、新客户端不得并发写同一数据根；宿主需要单实例或数据根写锁，具体实现属于 Windows Client/Runtime Phase。
- 暂时保留旧 namespace，不代表长期 API 命名已经完成；只有出现实际收益时才通过独立兼容变更调整。
- Core-only 构建不能证明旧 MAUI 宿主已在当前无 workload 环境编译，相关限制必须如实记录。

## 被否决方案

- 把生产 Core 放在外层治理仓库：会形成跨嵌套 Git 仓库引用和不清晰的源码所有权。
- 立即拆分 Storage、Crawler、Package 等多个 project：当前真实依赖边界尚不足以证明该复杂度必要。
- 自动复制到新数据根：对现有多 GB WAL 数据增加无必要的空间、时间和恢复风险。
- 只定义路径接口而延后数据策略：会把 Phase 01 的关键迁移决定继续留给后续宿主实现。
