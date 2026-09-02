# 避免无迁移时复制完整 Wiki 数据库

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI；Windows 11 x64 构建与测试
- 创建日期：2026-09-02
- 最后更新：2026-09-02
- 范围：Portable Core 旧锚点兼容迁移启动路径
- 关联架构/规范/ADR：`openspec/specs/wiki-data-compatibility/spec.md`、`openspec/specs/application-use-cases/spec.md`、`docs/adr/0002-portable-core-and-data-root.md`

## 背景与当前证据

用户报告 Windows 客户端启动偏慢。代码确认 `ActiveWikiUseCases.PrepareTargetAsync` 在每次发现现有内容数据库时都会调用安全锚点迁移；`MigrateAnchorDataSafelyAsync` 当前先把完整 `data.db` 复制到同目录 staging，随后才读取默认页判断迁移是否必要。对于已迁移的约 3.4 GB 真实数据库，这会在每次启动产生无必要的整库复制、完整校验和临时文件删除。

现有迁移判定只需只读默认页面：页面存在、包含旧 `<a ` 链接且尚不包含 `data-wiki` 时才需要迁移。真正迁移会遍历并改写 Wiki 页面，因此 ADR 0002 要求旧格式升级继续使用同卷 staging/backup/rollback；本变更不改成原库就地写入。

## 目标与完成定义

- 已迁移或无需锚点迁移的数据库经过轻量只读判定后直接进入既有初始化，不创建整库 staging 副本。
- 确实需要锚点迁移时，仍复制到唯一 staging、迁移、完整校验、以 journal/backup 原子替换并在失败时恢复。
- 数据 schema、迁移判定、Application 公共 API、数据目录和包格式不变。
- 新增回归测试明确证明无需迁移路径不调用复制操作，既有安全迁移与 sidecar 拒绝测试继续通过。
- Windows Release build 0 warning/0 error，相关测试及全量 .NET 测试通过。

## 前置条件

- Phase 00–08 已完成；Phase 09 进行中但本变更不扩展其 UI 范围。
- 内层源码恢复点为干净提交 `379ec17`；外层治理恢复点为 `826410b`。
- 用户已明确授权优化启动迁移路径。

## 范围与非目标

范围内：Core 锚点迁移的只读预检、复制分支和针对性测试；本计划与验证记录。

非目标：直接修改正式数据库、改变 SQLite schema/FTS5、移除迁移校验或恢复日志、优化 AI/WebView/关闭流程、修改 Presentation、Android 或旧 MAUI UI。

## P0 / 兼容性影响

- 正常启动只读检查现有默认页，不修改数据库；随后仍由既有 ContentDb 初始化使用同一份数据。
- 只有迁移判定为真时才进入破坏性路径；该路径保留 checkpoint 前置条件、sidecar 拒绝、staging、严格 schema/FTS 校验、backup、journal 和恢复。
- 无需迁移时不再对临时副本运行迁移后完整校验；该校验属于交换前的迁移产物保护，真正迁移时保持不变。正常 ContentDb 初始化行为不变。

## 方案与待决问题

- 抽取共享的锚点迁移判定，生产安全迁移先以只读 SQLite 连接检查正式库。
- 判定为假时返回既有 `LegacyAnchorMigrationResult(false, 0, 0)`；判定为真时才生成 staging/backup 标识并执行既有交换算法。
- 使用内部 Core 方法注入单次文件复制动作，测试可断言无需迁移分支不会复制；不增加公共 API 或运行时可变全局测试钩子。
- 待决问题：无。

## 文件变更

- MODIFY `terraria-wiki-offline/src/Terraria_Wiki.Core/Compatibility/LegacyWikiCompatibility.cs`：迁移判定前置，保留真正迁移的安全交换。
- MODIFY `terraria-wiki-offline/tests/Terraria_Wiki.Core.Tests/LegacyWikiCompatibilityTests.cs`：新增无需迁移不复制回归。
- MODIFY `README.md`、`openspec/project.md`：更新当前启动行为和自动化测试基线。
- ADD/MODIFY `openspec/changes/optimize-anchor-migration-startup/plan.md`：记录范围、进度和验证。
- KEEP Application、数据库 schema、package、Viewer、AI、Presentation 和平台配置。

## 执行步骤

- [x] 重构锚点迁移判定并在整库复制前执行；真正迁移路径保持 staging/validation/swap。
- [x] 新增无需迁移不复制测试，并运行 Core 定向测试。
- [x] 执行 Windows Release build、全量 .NET 测试与静态差异检查。
- [x] 更新计划验证记录，检查两个 Git 仓库状态并分别提交源码与治理变更。

## 验证与回归计划

- WSL：`git diff --check`、相关源码静态检查、双 Git 状态和临时产物检查。
- Windows：Core 定向测试；`Terraria_Wiki.Core.slnx` Release build；全量 .NET tests。
- 自动化覆盖：无需迁移时复制回调为零；需要迁移时页面内容正确、交换产物清理；WAL/SHM 仍被拒绝；Application 初始化迁移仍在首次 Read 前完成。
- 本变更不启动 UI、不发起网络或 AI Provider 请求。真实 3.4 GB 数据启动耗时属于可选目标机性能复测，未执行时不宣称具体提速数值。

## 风险与回退

- 预检与实际迁移判定漂移：两条路径复用同一判定函数，并保留 staging 上的二次判定。
- 正常库不再触发迁移产物完整校验：该校验只用于即将安装的迁移副本；数据库正常初始化、package 导入校验和真实兼容测试不变。
- 并发写入导致预检后状态变化：Application 原有数据操作门闩、消费者暂停、关闭连接及 Windows 单实例边界不变；本变更不放宽 sidecar 拒绝。
- 回退：恢复内层 `379ec17` 和本变更对应外层治理提交；不需要用户数据迁移。

## 进度记录

- 2026-09-02：完成启动链路、迁移实现、严格 validator、Application 调用方、相关 specs/ADR/Phase 记录和测试盘点；确认无迁移时的整库复制发生在必要性判断之前。
- 2026-09-02：用户批准优化；建立本独立变更，源码从干净提交 `379ec17` 开始。
- 2026-09-02：共享旧锚点判定已抽取；安全迁移先只读检查正式库，只有判定为真才调用文件复制并沿用既有迁移、严格校验、journal/backup 交换。新增测试以内部复制动作断言无需迁移时调用次数为零。
- 2026-09-02：Windows Release build 0 warning/0 error，全量 .NET tests 254/254 通过；源码以 `0e6c923` 保存。

## 发现、偏差与决策

- 发现：旧实现还会在无迁移时对 staging 执行 `PRAGMA integrity_check` 与完整 P0 schema/FTS 校验；这是复制之后、是否迁移判断之前的附带行为。
- 决定：正常启动只做共享的轻量只读迁移判定；严格完整校验继续用于真正发生迁移、即将替换正式库的 staging，不扩大为每次启动健康扫描。
- 偏差：未对真实 3.4 GB 用户数据库执行启动计时或写入式验证，以避免为了性能证明修改用户数据；自动化通过复制动作计数直接锁定本次优化分支。

## 验证结果

- Windows Core 定向测试：`LegacyWikiCompatibilityTests` 23/23 通过。
- Windows Release build：通过，0 warning / 0 error。
- Windows 全量 .NET tests：254/254 通过（Core 91、Application 54、Windows 81、WikiTools 28）。
- WSL：源码与治理 `git diff --check` 通过；源码提交前仅有计划内两个文件变化，无临时迁移产物。
- 未执行：真实 3.4 GB 用户数据库启动计时、真实 UI 启动；不宣称具体缩短秒数。

## 完成摘要

已迁移数据库的启动兼容路径现在先只读检查默认页；无需旧锚点升级时直接返回，不再复制完整 `data.db` 或运行迁移产物校验。确实需要升级时仍使用原有同卷 staging、严格 schema/FTS 校验、journal/backup 原子交换和失败恢复。公共 API、数据库 schema、package 与 Application 调用行为未改变。
