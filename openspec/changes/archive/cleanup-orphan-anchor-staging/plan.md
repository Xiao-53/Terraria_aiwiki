# 清理中断的锚点迁移暂存数据库

- 状态：archived；兼容清理实现与回归已完成
- Phase：无
- 执行环境：Windows Codex desktop；Windows .NET 构建与测试
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：Portable Core 旧锚点兼容迁移恢复与本机孤立暂存文件清理
- 关联架构/规范/ADR：`openspec/specs/wiki-data-compatibility/spec.md`、`docs/adr/0002-portable-core-and-data-root.md`

## 背景与当前证据

用户发现 Windows 默认数据根的 `Terraria_Wiki_zh` 目录达到 10.756 GiB。只读检查确认正式 `data.db` 为 3.521 GiB，另有两套 2026-09-04 产生的 `data.db.anchor-staging-<32 hex>` 及大型 SQLite rollback journal，共 7.233 GiB。正式库存在且比暂存副本更新，Manager 的 `DataSize` 与正式库长度一致；正式库可只读查询 4390 个页面、29774 个 Redirect、26237 个资源，默认页已经包含 `data-wiki`，当前不再需要锚点迁移。

源码事实：暂存库在迁移与严格校验完成后才写 `data.db.anchor-migration.json`；现存两套暂存家族没有该交换 journal 或 backup，但各自保留非空 `-journal`，说明事务在交换前中断。现有恢复只识别交换 journal；数据库家族辅助方法只处理 `.db/-wal/-shm`，且 best-effort 删除失败会被吞掉，因此交换前中断的 rollback journal 和暂存库不会在后续启动恢复。

## 目标与完成定义

- 正式目标库存在且没有交换 journal 时，恢复入口清理名称严格匹配的孤立 `anchor-staging-<32 hex>` 数据库家族。
- SQLite rollback `-journal` 与 `-wal/-shm` 一并纳入拒绝、存在判定和清理。
- 不匹配严格迁移 ID 或带未知后缀的相似文件保持不变；目标库缺失时不自动清理暂存候选。
- 清理无法完成时以稳定兼容错误失败关闭，不静默继续创建更多暂存副本。
- 定向 Core 测试、全量 .NET 测试与 Windows Release build 通过后，才按精确文件名清理本机已确认孤立的四个文件。

## 前置条件

- Phase 00–10 已完成；本变更不属于新 Roadmap Phase。
- 目标源码与测试在当前 `HEAD` `0e5c4c4` 上无未提交差异，可作为恢复点。
- 仓库存在与本变更无关的 README、计划及 Skill 调整；不得修改或提交这些既有改动。

## 范围与非目标

范围内：锚点迁移 orphan staging 识别与清理、rollback journal 数据家族语义、Core 回归测试、兼容 spec 和本计划；验证后精确清理本机已确认孤立文件。

非目标：改变 Wiki schema/FTS5、迁移谓词、数据包格式、Application 公共 API、Presentation、同步、AI、Viewer、Android、依赖或其他数据目录。

## P0 / 兼容性影响

- 正式 Manager/Content 数据库不作为删除目标；清理只发生在正式目标仍存在、没有交换 journal、文件名严格属于 `anchor-staging-<32 hex>` 家族时。
- 有交换 journal 时继续优先执行既有原子恢复；目标缺失或名称不严格匹配时保持文件不变。
- rollback journal 可能代表未完成 SQLite 事务，不能作为可安装的新数据库；它与对应暂存主文件作为一次性迁移产物共同清理。

## 方案与待决问题

- 在 `RecoverPendingAnchorMigration` 的无交换 journal 分支枚举同目录候选，严格解析 32 位十六进制 migration id 及已知 `.db/-journal/-wal/-shm` 家族成员。
- 复用数据库家族删除辅助方法，并在清理后检查是否仍有成员；残留则抛出 `anchor-migration-cleanup-failed`。
- 既有 journal 驱动的恢复算法、交换顺序和 schema/FTS 校验保持不变。
- 待决问题：无。

## 文件变更

- MODIFY `src/Terraria_Wiki.Core/Compatibility/LegacyWikiCompatibility.cs`：严格发现 orphan staging，纳入 rollback journal，并对恢复清理失败关闭。
- MODIFY `tests/Terraria_Wiki.Core.Tests/LegacyWikiCompatibilityTests.cs`：覆盖 orphan 清理、安全名称边界、目标缺失边界和 rollback journal 拒绝。
- MODIFY `openspec/specs/wiki-data-compatibility/spec.md`：补充中断迁移暂存家族恢复契约。
- ADD/MODIFY `openspec/changes/cleanup-orphan-anchor-staging/plan.md`：持续记录实施与验证。
- KEEP 其他源码、数据库 schema、数据包、Application、Windows UI 与平台配置。

## 执行步骤

- [x] 实现严格 orphan staging 发现与数据库家族清理；静态检查目标差异。
- [x] 增加 Core 回归测试并运行定向测试。
- [x] 更新兼容 spec；运行 Windows Release build 与全量 .NET tests。
- [x] 核对本机正式库、进程和精确候选；宿主执行策略拦截直接删除，保留为带修复版本首次准备 Wiki 时的自动清理或用户手动清理事项。
- [x] 更新验证记录、检查 diff/临时产物并归档计划。

## 验证与回归计划

- Windows：`Terraria_Wiki.Core.Tests` 定向筛选；`Terraria_Wiki.Core.slnx` Release build；全量 .NET tests。
- 静态：`git diff --check`、目标文件 diff、工作区状态和本机数据目录精确清单。
- 本机数据：删除前再次确认无 Terraria Wiki 进程、正式 `data.db` 与 Manager 匹配、无交换 journal；删除后只读复核正式库和目录大小。
- 不启动 UI、不发网络请求、不修改正式数据库内容；不建立 Android 验证。

## 风险与回退

- 误删相似用户文件：只接受目标文件名后紧跟 32 位十六进制 ID，后缀仅允许空、`-journal`、`-wal`、`-shm`；测试证明其他名称不变。
- 并发迁移：Application/Windows 既有协调门闩、数据根写锁和单实例边界保持；Core 清理失败关闭，不静默重试。
- 目标缺失：不清理任何无 journal 暂存候选，避免破坏人工恢复线索。
- 回退：源码恢复到 `0e5c4c4` 对应版本；本机孤立文件一旦删除不可由应用恢复，因此只删除已确认不属于当前正式库的精确路径。

## 进度记录

- 2026-09-13：完成只读现场检查、调用链、迁移算法、测试、兼容 spec 与 ADR 核对；用户授权优化。
- 2026-09-13：确认目标源码/测试无既有差异，建立本计划并进入实施。
- 2026-09-13：实现无交换 journal 的严格 orphan staging 清理，把 rollback `-journal` 纳入数据库家族；新增名称边界、目标缺失和 sidecar 回归。Windows Core 定向测试 31/31 通过，`git diff --check` 通过。
- 2026-09-13：补充正式目标 rollback journal 在快速只读预检前失败关闭的边界；最终定向测试 32/32、Windows Release build 0 warning/0 error、全量 .NET tests 370/370 通过。
- 2026-09-13：删除前再次确认无 Terraria Wiki 进程、正式库 3,781,029,888 字节与 Manager `DataSize` 完全一致、无交换 journal、无 backup、四个严格候选合计 7,766,784,632 字节且无未知 anchor 文件。直接删除命令被宿主执行策略拒绝，未绕过保护，正式库和四个候选均未修改。

## 发现、偏差与决策

- 发现：现存 `-journal` 是 SQLite DELETE 模式的 rollback journal，不属于现有 `.db/-wal/-shm` 家族辅助方法。
- 决定：不改变迁移算法，只补全一次性迁移产物的恢复边界；有交换 journal 时仍由原算法决定提交或回滚。
- 发现：`PrepareAndMigrateAnchorDataSafelyCoreAsync` 的正常 WAL 快速路径必须允许 `-wal/-shm` 由 SQLite 一致读取，但正式目标的 rollback `-journal` 应在读取前单独拒绝；已增加该局部检查和回归。
- 偏差：原计划包含由本会话立即删除本机四个孤立文件；宿主执行策略拒绝删除命令，因此实现不以绕过策略完成一次性现场删除。带修复版本下次进入同一恢复入口时会自动清理，当前也可由用户在关闭应用后手动删除这四个精确候选。

## 验证结果

- Windows Core 定向测试：`LegacyWikiCompatibilityTests` 32/32 通过。
- Windows Release build：9 项目通过，0 warning / 0 error。
- Windows 全量 .NET tests：370/370 通过（Core 104、Application 59、Windows 162、WikiTools 45）。
- 静态：`git diff --check` 通过；目标源码和测试的实施前恢复点为 `0e5c4c4`，无计划外目标文件改动。
- 本机只读检查：正式库可读且与 Manager 大小一致；无应用进程、交换 journal、backup 或未知 anchor 文件；四个孤立候选仍存在，因为宿主策略阻止直接删除。
- 未执行：安装/发布新客户端和真实 UI 启动；不宣称旧安装已获得自动清理逻辑。

## 完成摘要

Portable Core 现在会在正式目标仍存在且没有交换 journal 时，严格发现并清理交换前中断的 `anchor-staging-<32 hex>` 数据库家族；rollback `-journal` 已进入 sidecar 拒绝、存在与清理语义，异常清理失败关闭。相似名称、未知后缀和目标缺失边界保持不删除。源码、契约和测试已完成 Windows 自动化验证；本机 7.233 GiB 既有孤立文件因宿主删除策略未直接移除，将由带修复版本首次准备该 Wiki 时自动清理或由用户手动处理。
