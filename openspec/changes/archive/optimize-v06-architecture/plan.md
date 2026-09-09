# 收敛 Master Architecture v0.6 与迁移契约

- 状态：completed
- Phase：无；项目级架构治理优化
- 执行环境：Codex 桌面会话读取与编辑；不执行 .NET/Avalonia 构建
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：Master Architecture、Phase 00 draft、capability specs、入口与计划规则
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户要求优化当前工作区架构，并明确 `ARCHITECTURE-v0.6.md` 只保留类似 v0.5 的项目级大纲，不承担完整实施文件职责。

只读评审确认：当前方向和 P0 护栏基本合理，但 v0.6 混入了源码盘点、Phase 00 技术门槛和 Viewer 实现候选；Viewer 依赖图与文字规则不一致；Windows AI Runtime 验证晚于 Presentation 重建；`.agent/PLANS.md` 仍指向已被替代的 v0.5。真实源码还表明数据库初始化会写入、搜索读取 MAUI Preferences、数据包导入缺少路径包含检查、Viewer 固定使用 loopback 端口，因此这些事实应进入 Phase 计划和持续能力契约，而不是 Master Architecture。

## 目标与完成定义

- 将 v0.6 收敛为项目目标、P0、分层、迁移原则、Phase 路线和全局决策大纲。
- 修正 Viewer/AI/平台 Adapter 的向内依赖方向。
- 把 Windows Runtime smoke test 提前到 Presentation Rebuild 之前。
- 在 Phase 00 中定义数据库副本、数据包兼容 oracle、安全输入和 Viewer 生命周期验证。
- 为跨 Phase 稳定的 Wiki 数据兼容、Viewer、Search/Read 建立 capability specs。
- 修正当前治理文件的 v0.5 指向与 `.pak`/`.pkg` 待确认事实。
- 不修改 `terraria-wiki-offline/` 业务源码。

## 前置条件

- 用户已明确授权优化工作区架构文档。
- `phase-00-migration-baseline` 仍为 draft，尚未实施，允许在批准前收敛验收条件。
- 源码仓库当前状态干净。

## 范围与非目标

### 范围

- 重写 `docs/architecture/ARCHITECTURE-v0.6.md` 的信息层级。
- 更新 Phase 00 draft 和相关持久规范。
- 同步 README、`.agent/PLANS.md` 与项目基线中的必要入口/顺序。

### 非目标

- 不实施 Phase 00，不安装 SDK/依赖，不创建 PoC。
- 不修改 ADR 0001 的技术选择结论。
- 不修改、移动或格式化业务源码。
- 不为未来 Phase 预建接口、project 或实现方案。

## P0 / 兼容性影响

本变更只调整治理文档。P0 范围不缩减；数据格式、数据库、Viewer 和 Search/Read 的长期保护从 Master Architecture 摘要下沉为可持续 capability specs。

## 方案与待决问题

- Master Architecture 采用 v0.5 的母文件职责：只定义跨 Phase 稳定内容。
- Phase 00 保留具体宿主硬门槛、fixture、兼容 oracle、安全探针和 Windows 验证。
- capability specs 只记录已确认的外部行为和保护边界，不写具体类、SQL 或包版本。
- `.pak` 是旧 README 表述，`.pkg` 是当前代码事实；在拿到历史样本前记录为待确认兼容项，不静默改写历史事实。

## 文件变更

- **MODIFY** `docs/architecture/ARCHITECTURE-v0.6.md`
- **MODIFY** `openspec/changes/phase-00-migration-baseline/plan.md`
- **ADD** `openspec/specs/wiki-data-compatibility/spec.md`
- **ADD** `openspec/specs/local-wiki-viewer/spec.md`
- **ADD** `openspec/specs/wiki-query-boundary/spec.md`
- **MODIFY** `README.md`
- **MODIFY** `.agent/PLANS.md`
- **MODIFY if needed** `openspec/project.md`
- **KEEP** `terraria-wiki-offline/**`

## 执行步骤

- [x] 对照 v0.5、v0.6、ADR、Phase 00、治理规则和真实源码证据。
- [x] 重写 v0.6，只保留架构母文件内容并修正依赖/Phase 顺序。
- [x] 建立三项 capability specs。
- [x] 收紧 Phase 00 的兼容、安全、Viewer 和验证 oracle。
- [x] 同步入口、计划规则和当前基线。
- [x] 检查 Markdown 链接、Phase 映射、旧表述与源码 Git 状态。

## 验证与回归计划

- 检查 v0.6 不再包含具体源码统计、SQL、依赖版本、PoC 命令或完整 Phase 00 实施细节。
- 检查当前非归档文档只把 v0.6 作为 Master Architecture。
- 检查 Phase 00–13 的名称、前置与路径在当前文档中一致。
- 检查 capability specs 与 Master Architecture/Phase 00 不冲突。
- 检查所有新增/修改 Markdown 本地链接存在。
- 检查业务源码 Git 状态和 diff 均为空。

## 风险与回退

- 过度压缩 v0.6 可能丢失 P0 或阶段边界：用 specs、ADR 和 Phase 00 承接细节，并逐项核对原文。
- 调整 Phase 07/08 可能留下旧路径：全局搜索当前文档；归档计划保留历史事实不修改。
- 把现有缺陷写成契约：spec 只要求安全、兼容的外部结果，不固化具体不安全实现。

## 进度记录

- 2026-08-20：完成 v0.5/v0.6、Phase 00、ADR、治理关系和真实源码证据复核，开始文档收敛。
- 2026-08-20：完成 v0.6 大纲化、三项 capability specs、Phase 00 收紧和入口同步。
- 2026-08-20：完成行数、链接、Phase 映射、过时指向和业务源码状态检查。

## 发现、偏差与决策

- Master Architecture 不再承担 Phase 00 的完整验收清单；只保留“必须先通过代表性验证”的项目级门槛。
- Local Viewer Adapter 和 Wiki Tool Adapter 都是向内依赖 Application/Core 稳定能力的 Adapter；Core 不依赖 WebView 或 loopback transport。
- Windows AI Runtime 是高风险集成，应在完整 Presentation Rebuild 前完成 smoke test。

## 验证结果

- `ARCHITECTURE-v0.6.md` 从修改前 342 行收敛为 248 行；v0.5 为 325 行。v0.6 不再包含源码统计、具体 SQL、依赖版本、PoC 命令或完整 Phase 00 操作清单。
- 当前路线已调整为 Phase 07 Windows Runtime Smoke Test、Phase 08 Presentation Rebuild；README 顺序一致，旧路径未在当前非归档文档残留。
- `.agent/PLANS.md` 已改为指向 v0.6。
- 三项 capability specs 已存在，Phase 00 已关联并落实数据库原件/副本分离、独立包 oracle、安全路径/长度验证和 Viewer 端点/生命周期要求。
- 检查范围内本地 Markdown 链接 0 个失效。
- `terraria-wiki-offline/` 的 `git status --short` 与 `git diff --stat` 均无输出；未修改业务源码。
- 本变更只涉及文档，未执行 .NET/Avalonia/SQLite/Viewer/publish 验证，也未改变 Phase 00 的 draft 状态。

## 完成摘要

已把 v0.6 恢复为类似 v0.5 职责的项目级大纲：只保留方向、P0、分层、迁移原则、Phase 路线和全局决策。详细兼容、安全和 Viewer 验收已下沉到 Phase 00 与 capability specs；Viewer 依赖方向、Core 迁移措辞、Windows Runtime 验证顺序和治理文档漂移已修正。业务源码保持不变。
