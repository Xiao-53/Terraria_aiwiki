# Phase 10 — Legacy MAUI Retirement

- 状态：completed
- Phase：10 — Legacy MAUI Retirement
- 执行环境：WSL Codex CLI；Windows 11 x64 构建、测试、发布与 smoke 验证
- 创建日期：2026-09-03
- 最后更新：2026-09-03
- 范围：旧 MAUI 参考 Submodule 退休、来源与回退记录、当前治理/架构/基线同步
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0007-separate-product-source-from-reference.md`

## 背景与当前证据

Phase 09 已完成并归档，Windows Avalonia Presentation 已通过五入口 P0 接管、全量 .NET tests 和目标 Windows 窗口验收。外层根级 `src/`、`tests/`、`agent/`、`assets/` 与 `Terraria_Wiki.Core.slnx` 是唯一产品基线；产品 project、tests、build、publish 和 runtime 已有边界测试禁止引用 `terraria-wiki-offline/`。

用户选择移除而非保留只读参考 Submodule。实施前已把既有 Android 暂缓变更单独提交为 `9d21906`；该提交是 Phase 10 父提交和外层恢复点。待删除 gitlink 固定为上游 `a1dd89981d7604006ec91955d4d19e93cb933ce1`，Submodule 工作树干净；本地迁移恢复分支 `archive/new-app-before-extraction` 指向产品快照 `d67d904c32b853edb664b68121897a2f2a8f2655`。

当前工作区还有既有未跟踪治理归档、`.codex/`、Phase 00 实验和历史文件；它们不属于本 Phase，不得删除、修改或暂存。

## 目标与完成定义

- 从当前 checkout 移除旧 MAUI/Razor 宿主 gitlink 与唯一 `.gitmodules`，不清理上游仓库或用户数据。
- 当前治理文件不再要求本地参考 Submodule；旧行为通过归档记录、ADR、固定上游 URL/提交和 Git 父提交追溯。
- 保留全部 Windows Viewer/bridge/icon 产品快照、来源、hash 和许可提示。
- 公共 API、SQLite schema/FTS5、数据根、`.pkg`、NDJSON、AI 配置与 Viewer 协议不变。
- 根 solution 在没有参考 Submodule 的状态下通过 Windows Release build/tests、两种 `win-x64` publish 与合成数据 smoke。
- 完成后 Phase 11 Windows Productization 成为下一阶段；Phase 12 仍按需，Phase 13 仍暂缓。

## 前置条件

- Phase 00–09 已完成；Android 暂缓变更已独立保存为 `9d21906`。
- 参考 Submodule 干净且固定到 `a1dd899`；外层恢复点、上游 URL 和本地恢复分支可解析。
- Windows .NET 10、Avalonia/WebView2 和既有合成 smoke fixture 可用。

## 范围与非目标

范围内：参考 gitlink/`.gitmodules` 删除；`AGENTS.md`、`INIT.md`、README、Master Architecture、项目基线、Local Viewer spec、ADR 0007 和本计划的当前事实同步；Windows 构建、测试、发布与 smoke 验证。

非目标：修改 `src/`、`agent/` 或 Avalonia 页面；删除 Core、Application、Viewer/icon 快照或历史归档；改变 P0 行为、数据、协议或依赖；实施 Phase 11/12/13；清理任何既有未跟踪文件。

## P0 / 兼容性影响

参考 Submodule 已不参与产品 build/runtime，删除不改变 P0 数据生命周期。删除前复核受控资产清单/hash；删除后通过现有边界 tests、全量 tests 和两种发布 smoke 证明产品继续只使用外层 Core、Agent 与 Viewer 快照。恢复旧参考的路径是恢复 Phase 10 父提交并按上游 `a1dd899` 初始化；不复制第二套源码。

## 方案与待决问题

先记录 gitlink、上游 URL、恢复分支和资产 hash，再 deinitialize 参考 Submodule，删除 tracked gitlink 与空的 `.gitmodules`。不主动删除 `.git/modules/terraria-wiki-offline` 本地对象库，使本机恢复信息继续可用；持久回退不依赖该本地缓存。

ADR 0007 增加 Phase 10 决议，不新建 ADR。当前事实源改为“上游固定参考”而非“本地 Submodule”；历史 ADR/归档计划保持原文。无待决问题。

## 文件变更

- REMOVE `.gitmodules`、`terraria-wiki-offline` gitlink：退休旧宿主 checkout。
- MODIFY `AGENTS.md`、`INIT.md`、`README.md`：删除本地 Submodule 工作流，更新当前结构与阶段状态。
- MODIFY Master Architecture、`openspec/project.md`：记录 Phase 10 完成和 Phase 11 下一阶段。
- MODIFY ADR 0007、Local Viewer spec：记录退休决议，并把构建禁令泛化为任何上游参考 checkout。
- KEEP `assets/windows/**`、`src/**`、`tests/**`、`agent/**`、历史 ADR/归档计划和既有未跟踪文件。

## 执行步骤

- [x] 独立提交既有 Android 暂缓变更，建立 Phase 10 父提交 `9d21906`。
- [x] 记录 gitlink、Submodule 状态、恢复分支和 94/96 文件资产 hash。
- [x] Deinitialize 并移除参考 gitlink 与 `.gitmodules`；保留本地 module 对象库。
- [x] 更新当前治理、架构、项目基线、spec、ADR 和阶段入口。
- [x] 执行 WSL 静态/差异/资产/工作树检查。
- [x] 在 Windows 执行 Release build、全量 tests、两种 publish 与合成数据 smoke。
- [x] 记录验证，完成并归档计划，创建独立 Phase 10 提交且不 push。

## 验证与回归计划

- WSL：确认 `.gitmodules` 和 mode `160000` gitlink 消失；product project/source/test/publish target 不引用参考目录或 MAUI；复核资产数量/hash；执行 `git diff --check`、secret/数据库/package/WAL/SHM/build-output 和未跟踪范围检查。
- Windows：根 solution Release restore/build 0 warning/0 error；全量 .NET tests 无失败并记录实际数量；framework-dependent/self-contained `win-x64` publish 含 ViewerAssets、AgentRuntime、图标和 manifest；两种产物用合成数据根通过 Search/Viewer/资源/退出 smoke，不调用付费 Provider。
- UI：无 Avalonia 页面或视觉变化，不重复 Phase 09 人工视觉验收。
- Python：Agent 源码与依赖不变，不单独运行 Python lint/tests；发布资产复制与客户端 smoke 覆盖本阶段相关边界。

## 风险与回退

- 错删未跟踪内容：删除前后保存完整状态清单，只操作明确 gitlink/`.gitmodules`，最终逐项核对。
- 资产来源或文件遗漏：保留 `assets/windows/README.md` 的 URL、提交、hash 和许可说明，并在删除前后复核快照。
- 旧实现无法核对：Phase 10 父提交可恢复 gitlink，上游 `a1dd899` 可重新 checkout；本机 module 对象库不主动清理。
- 构建或 smoke 失败：保持计划 in-progress，回退到 `9d21906`，不得进入 Phase 11。

## 进度记录

- 2026-09-03：用户批准移除 Submodule，并选择先独立保存现有 Android 暂缓变更。
- 2026-09-03：完成 Android 暂缓提交 `9d21906`；Phase 10 进入实施。
- 2026-09-03：确认 gitlink/HEAD 为上游 `a1dd899`、Submodule 工作树干净、本地恢复分支为 `d67d904`；94/96 文件资产 aggregate hash 分别与来源记录的 `5ac2429…` / `59a57f6…` 一致。
- 2026-09-03：已 deinitialize 并删除 `terraria-wiki-offline` gitlink 与 `.gitmodules`；`.git/modules/terraria-wiki-offline` 和本地恢复分支保留。当前治理、架构、项目基线、Viewer spec 与 ADR 0007 已同步为无本地参考 checkout 的事实。
- 2026-09-03：WSL 静态/资产检查通过；Windows root restore、Release build、275/275 tests、两种 `win-x64` publish 与修正隔离 fixture 元数据后的两次客户端 smoke 均通过。临时发布、数据库、日志和 WebView 数据已清理，计划完成并归档。

## 发现、偏差与决策

- 决定：删除本地参考 Submodule，不建立源码副本、Git bundle 或第二套归档目录。
- 决定：历史 ADR 和已归档计划保留当时事实，不做全库措辞清洗。
- 决定：本 Phase 不触发 Terraria UI Skill，因为不设计或实质改变 Windows Avalonia 页面/组件。
- 偏差：删除命令在 `git rm` 已成功后，对同一路径执行冗余 `git add -u` 返回 pathspec 不存在；检查确认 gitlink 已正确 staged 删除，随后单独 staged `.gitmodules` 删除，未影响其他文件。
- 发现：README/项目基线记录的 274 tests 已过期；实际全量为 Core 92、Application 54、WikiTools 28、Windows 101，共 275/275，已按真实结果更新。
- 偏差：现有 `WindowsClientSmokeFixtureTests` 创建页面和资源但未把 Manager 的 `IsPageDownloaded` 置为真；Phase 09 后客户端按契约停留在无页面数据状态，因此首轮两种 smoke 未进入 Viewer，人工关闭后均报告 `exitCleanupCompleted=true`。本 Phase 未修改测试或产品源码，只在隔离临时 Manager 数据中把活动 Wiki 标为已下载后重跑；两种 smoke 均通过。该 fixture 自包含性问题留待后续测试维护，不影响产品结果。

## 验证结果

- Git/工作区：`.gitmodules` 不再 tracked，索引无 mode `160000` gitlink，仓库无 `submodule.*` 配置；本地 `.git/modules/terraria-wiki-offline` 与 `d67d904` 恢复分支保留。既有未跟踪文件未删除、修改或暂存。
- 资产：94 文件快照与 96 文件支持集的 aggregate hash 分别为 `5ac242971b3781303b8ee52c6105da31ee6e8d4eb42382658c431c3ed32e443a` 和 `59a57f6e08a6ad0a9cb37d48cc7abf8d7eba1fbee01a8ebad2493e642fe35231`，删除前后一致。
- Windows：根 solution restore 成功；Release build 0 warning/0 error。全量 tests 为 Core 92、Application 54、WikiTools 28、Windows 101，共 275/275 通过。
- Publish：framework-dependent 约 162 MB、self-contained 约 267 MB；两者均包含 93 个 ViewerAssets、9 个 AgentRuntime 文件和 Windows 可执行文件。
- Smoke：两种发布均为 Search 1、JS 1、CSS 1、image 1、audio 4、history 0、`ai: null`、`exitCleanupCompleted=true`；关闭总耗时分别为 63 ms / 73 ms，未调用 Provider。
- WSL：产品 build/source/test 中的 MAUI/旧目录命中仅存在于边界禁令断言；`git diff --check` 通过。无 Phase 10 临时发布、数据库、日志或进程残留。

## 完成摘要

Phase 10 已完成。旧 MAUI/Razor 参考 gitlink 与 `.gitmodules` 已从产品仓库移除；固定上游来源、资产 provenance/hash、历史记录与 Git 父提交继续提供追溯和回退。外层产品在无参考 checkout 状态下通过 Windows Release 0-warning build、275/275 tests 和两种发布 smoke；产品源码、公共 API、数据、协议、依赖与 Avalonia UI 均未改变。下一阶段为 Phase 11 Windows Productization。
