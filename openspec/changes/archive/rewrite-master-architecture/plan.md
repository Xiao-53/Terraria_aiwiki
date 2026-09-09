# 重写项目 Master Architecture / Roadmap

- 状态：completed
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：项目级架构总纲与根 README
- 关联规范/ADR：`AGENTS.md`、`INIT.md`、`openspec/project.md`；无 ADR

## 背景与现状

用户要求把 `ARCHITECTURE-v0.4.md` 重写为用于派生各阶段详细计划的项目级母文件，并允许同步更新已过时的根 `README.md`。项目根目录目前没有任何版本的 `ARCHITECTURE` 文件，v0.4 原稿位于 `C:\Users\Xiao\Downloads\ARCHITECTURE-v0.4.md`。

已阅读根 `AGENTS.md`、`INIT.md`、`README.md`、`openspec/project.md`、`.agent/PLANS.md` 和原项目 README；并核对了 MAUI 项目文件、应用入口、数据与搜索服务、数据库模型、旧 Presentation、Local Viewer 及资源链路。可确认原项目是单项目 .NET MAUI Blazor Hybrid 应用，已有 Wiki 下载/更新、SQLite/FTS5、Redirect、搜索索引、导入导出、资源管理与 Local Viewer 等能力，同时 UI 中存在对全局 `App.*` 和数据库服务的直接访问。

v0.4 包含正确的总体方向和护栏，但混入了具体 SQL 策略、类/文件矩阵、目录草图、运行实现候选、逐条测试样例和实现级验收，不适合作为稳定的项目母文件。根 README 仍把 Viewer/媒体能力描述为可裁剪项，并把 AI 接入放在路线末端，与后续已确认决策不一致。

## 目标

- 新建 `ARCHITECTURE-v0.5.md`，只定义项目目标、P0 不可破坏能力、目标分层、总体复用/重构策略、Phase 路线、全局已决策/未决策和 Phase 计划文件规范。
- 把 SQL、具体类/文件级实现、详细测试步骤、具体传输与打包实现下沉到各 Phase 的 `plan.md`。
- 更新根 `README.md`，使项目定位、保留原则、当前路线和文档入口与 v0.5 一致。
- 不修改 `terraria-wiki-offline/` 源码，不实施任何 Phase。

## 非目标

- 不修复搜索代码，不新增 Application/AI/UI 实现。
- 不创建各 Phase 的详细计划正文。
- 不修改数据库、依赖、构建配置、OpenSpec 能力规范或 ADR。
- 不宣称任何运行、构建或平台能力已经验证通过。

## 约束与风险

- v0.5 必须是长期稳定的方向与边界文件，不能继续承载易变实现细节。
- P0 能力只允许在后续阶段经显式决策演进，不能把“隐藏旧入口”写成“删除底层能力”。
- Windows 是当前目标，Android 是后续目标；iOS/MacCatalyst 暂不支持。
- Vector Search、Embedding Retrieval、Vector Database 和第二套 LLM Wiki 数据库为明确非目标。
- 项目已有 OpenSpec 计划规则，Phase 计划必须复用 `openspec/changes/<change-id>/plan.md`，不得另建重复治理体系。
- `openspec/project.md` 仍含较早的可裁剪表述，本次范围不修改它；README 将把 v0.5 标为当前架构方向来源，后续可单独同步项目基线。

## 方案与决策

- 以 v0.4 的已确认方向为内容来源，但重新组织为“定位与护栏 → 目标架构 → 复用策略 → 路线 → 决策 → Phase 计划契约”六个主体区域。
- Phase 仅描述目标、边界、前置关系、阶段产物和高层完成条件；所有实现方案与验证步骤进入对应 Phase 计划。
- README 作为入口文件，只给出项目简介、核心原则、路线概览、目录入口和当前状态，不复制完整架构正文。

## 执行步骤

- [x] 阅读治理文件、v0.4、根 README 与相关源码证据。
- [x] 起草 `ARCHITECTURE-v0.5.md`，检查是否仍含实现级细节。
- [x] 重写根 `README.md`，与 v0.5 的定位和路线一致。
- [x] 检查链接、Phase 映射、决策状态、P0 列表及两份文档的一致性。
- [x] 写入项目目录，核对实际差异并完成变更记录。

## 验证计划

- 静态检查两份 Markdown 的标题层级、内部相对链接和关键章节是否齐全。
- 搜索不应在总纲展开的内容：具体 SQL、具体类/文件变更表、详细测试查询、具体 transport/打包命令。
- 搜索 README 中的过时表述，确认不再建议删除 Viewer/媒体底层或把 AI 放在最后才接入。
- 核对项目源码未被修改。

## 进度记录

- 2026-08-20：完成项目治理、v0.4、README、项目基线与相关源码核对；开始文档重写。
- 2026-08-20：完成 v0.5 与 README 草稿；确认总纲只有六个主体区域，Phase 计划路径沿用 OpenSpec 规则。
- 2026-08-20：将 v0.5 与 README 写入项目根目录，补做内容哈希和源码仓库状态检查。

## 发现与偏差

- 项目目录没有 v0.4；原稿来自用户提供的 Downloads 路径。
- 项目现有计划规范与早期对话建议的顶层 `plans/` 目录冲突，决定沿用 OpenSpec 目录规则。

## 验证结果

- 已检查 Markdown 标题层级和文件长度。
- 已确认六个必需主体区域、11 个 Phase 映射、README 架构入口和当前状态均存在。
- 已搜索 SQL 语句、具体业务类/文件、详细查询样例和具体运行/打包选型；总纲未展开这些实现内容。
- 已搜索 README 中的旧裁剪与“最后接入 AI”表述；未发现残留。
- 项目文件与交付副本的 SHA-256 完全一致；README 指向的 `ARCHITECTURE-v0.5.md` 已存在。
- `terraria-wiki-offline/` 执行 `git status --short` 无输出，业务源码没有被本次任务修改。
- 本次仅为 Markdown 文档变更，未执行 MAUI 构建或运行测试。

## 完成摘要

新增项目级 `ARCHITECTURE-v0.5.md`，把 v0.4 中的方向和护栏整理为六个稳定区域，并将实现细节下沉到 00–11 Phase 的 OpenSpec 计划。根 `README.md` 已同步新的产品定位、P0 能力、路线、当前状态和文档入口。没有修改业务源码、数据库、依赖或构建配置。
