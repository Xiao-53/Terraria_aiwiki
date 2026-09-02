# Terraria AI Wiki — Codex CLI 工作规则

## 适用范围与默认边界

本文件专供 **WSL2 中运行的 Codex CLI** 使用，适用于工作区根目录及其所有子目录。

- 外层仓库是唯一产品源码仓库；产品代码与构建入口固定在根级 `src/`、`tests/`、`agent/`、`assets/` 和 `Terraria_Wiki.Core.slnx`。
- 工作区治理、架构、规范、计划和 ADR 必须放在外层仓库的对应目录中，不得写入参考 Submodule。
- `terraria-wiki-offline/` 是固定到上游提交的只读 Git Submodule，只用于核对旧实现、历史行为和来源；不得成为产品 build/runtime 依赖，也不得在其中继续开发新产品。
- 用户要求检查、解释或诊断时，只做读取与报告；只有用户明确要求修改或实现时，才写入相应范围。
- 不得把当前任务扩展成相邻重构、依赖升级、目录整理或后续 Phase 实施。

## Codex CLI 启动与执行环境

唯一主要开发执行环境是 WSL2 中安装的 Codex CLI。固定路径映射：

```text
Windows: C:\Users\Xiao\mypython\terraria_aiwiki
WSL:     /mnt/c/Users/Xiao/mypython/terraria_aiwiki
```

每个主开发会话都应从外层工作区根启动：

```bash
cd /mnt/c/Users/Xiao/mypython/terraria_aiwiki
codex
```

- 不要先进入 `terraria-wiki-offline/` 再启动主 Codex 会话。该目录是参考 Submodule；从那里启动会把参考仓库误当作产品根，并漏掉外层源码、`AGENTS.md` 和治理文件。
- Codex CLI 在会话开始时自动发现并加载 `AGENTS.md`。修改本文件后应结束并重新启动 Codex 会话，使新指令链生效。
- 日常读取、搜索、编辑、脚本和平台无关检查使用 WSL/Linux 工具与 `/mnt/c/...` 路径。
- Windows 负责新 .NET/Avalonia 客户端的构建、运行、调试、发布及其他必须在 Windows 执行的验证。不得把 WSL 中的 `dotnet` 结果等同于 Windows 客户端验证。
- Windows 新版本不得使用 .NET MAUI，也不得为了当前 Windows 开发安装 MAUI workload、Android SDK、JDK 或 Emulator。Android 工具链只允许在用户批准进入 Android Phase 后安装和使用。
- 产品平台范围是 Windows（当前）与 Android（未来）；其余产品平台全部为非目标。WSL/Linux 只是开发执行环境，不得据此规划或宣称 Linux 产品支持。未经用户重新授权并更新架构决策，不得为 iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 或其他平台增加实现、依赖、验证或发布工作。
- 不依赖 Codex 桌面端的面板、连接器、线程状态、隐藏计划或其他桌面专属能力。所有持久上下文必须存在于工作区文件、Git 状态或可复现命令中。
- `/mnt/c` 的 I/O 可能慢于 WSL Linux 文件系统，但当前项目以 C 盘目录为唯一事实源。未经用户明确要求，不得复制、移动或另建 `~/code` 工作副本。

## 文档职责与冲突处理

| 位置 | 唯一职责 |
|---|---|
| `README.md` | 面向人和新会话的项目入口、当前状态与导航 |
| `INIT.md` | 最短读取顺序和任务开始方式 |
| `AGENTS.md` | Agent 必须遵守的工作、范围、安全与验证规则 |
| `docs/architecture/ARCHITECTURE-v0.6.md` | 项目目标、P0、目标分层、Phase 路线和全局决策 |
| `openspec/project.md` | 当前技术与产品基线、文档治理关系 |
| `openspec/specs/` | 已确认且持续有效的能力契约 |
| `openspec/changes/` | 正在讨论或实施的具体变更与执行计划 |
| `docs/adr/` | 已确认的长期技术决策、替代方案和后果 |
| `.agent/PLANS.md` | 何时建计划、计划格式和生命周期 |

这些文件分工不同，不应相互复制全文。发生冲突时不得静默选择：先按内容所属职责判断权威来源，并向用户报告冲突。当前用户的明确要求可以授权变更，但不得被解释为对无关范围的授权。

低层计划不得自行推翻 Master Architecture 的 P0、Phase 边界或平台方向；ADR 或能力规范发生重大变化后，应同步更新架构总纲或项目基线，不能长期保留相互矛盾的文件。

## 开始任务前

Codex CLI 已自动加载本文件；随后按 `INIT.md` 的顺序读取必要上下文。最低要求是：

1. 阅读根 `README.md`、Master Architecture 与 `openspec/project.md`。
2. 查找当前任务相关的 specs、进行中 changes 和 ADR；不得只看摘要或文件名。
3. 涉及源码时，先阅读根 solution 与 `src/`、`tests/`、`agent/`、`assets/` 中的相关配置、入口、直接调用方、核心实现、数据模型和已有测试；只有需要核对旧行为或来源时才读取参考 Submodule。
4. 满足 `.agent/PLANS.md` 的触发条件时，先创建并持续维护 `openspec/changes/<change-id>/plan.md`。
5. 如果任务属于 Roadmap Phase，确认前置 Phase 和阶段边界；只计划、实施用户指定的 Phase。

找不到事实依据时明确标为待确认，不得用经验或架构图补全不存在的实现。

## Windows UI 规范与 Skill

- 新建 Windows Avalonia 页面或共享视觉组件、增加导航/表单/列表/卡片/按钮，或显著重做现有页面布局与视觉时，必须使用项目级 `.agents/skills/terraria-ui/` Skill，并完整读取其 `SKILL.md` 与 `references/windows-ui-standard.md`。
- 当前用户需求、持续 Presentation spec、当前 change/plan 与 ADR 决定产品行为和架构边界；UI Skill 负责默认视觉、组件复用和验收流程。发现冲突时按职责报告，不得用视觉规范自行改变行为契约。
- 小型文案修正、纯后端任务、Legacy MAUI UI 和未来 Android UI 不触发该 Skill。Windows 规范不得被解释为其他平台实现授权。
- Skill 或本文件变更后应重新启动 Codex 会话，使新的项目级发现和指令链完整生效。

## 实施纪律

- 只修改完成当前任务所需的文件，保持改动小、可审查、可回退。
- 大规模改动文件或触碰核心文件前，先检查外层产品仓库状态并建立可恢复的 Git 保存点；涉及参考 Submodule 时同时核对 gitlink 与 Submodule 状态。每个 Roadmap Phase 完成且验证通过后，必须用 Git 提交保存该阶段结果。保存点和提交只包含已确认的当前范围，不得夹带用户既有改动；仓库未初始化或改动无法安全分离时，先向用户报告并取得处理决定。
- 优先顺序固定为：`Reuse → Fix → Wrap → Extract when necessary → Rewrite last`。
- 不得把架构概念自动翻译成一组新接口、新 project、新 Registry 或新框架。
- 不复制现有数据库、搜索、同步、导入导出、资源或 Viewer backend 实现。
- 删除、替换或改变现有行为前，先确认调用关系、数据影响、平台差异、替代路径和回退方式。
- 不随意添加、升级或替换依赖。必要变更必须在计划中说明必要性、兼容性、体积、平台与回滚影响。
- 尊重现有数据格式、SQLite 表、FTS5 索引、MediaWiki 抓取流程、数据包和跨平台条件编译。
- 不创建与现有职责重复的 `memory.md`、`context.md`、`roadmap.md`、`todo.md` 或第二套计划系统。

## 架构与 P0 护栏

Master Architecture 中的 Existing Core Capabilities 是所有 Phase 的强制回归边界。未经独立决策、迁移方案、验证证据和用户确认，不得删除、绕开或重复实现。

特别注意：

- 隐藏或替换旧 UI 入口不等于删除底层能力。
- 搜索问题默认在现有 SQLite FTS5 能力上局部修正，不得自行升级为搜索系统重建。
- AI 通过稳定的 Wiki 查询边界使用事实，不直接建立第二套 Wiki 数据或复制 Core。
- Windows Runtime 属于平台实现；不得把其进程、路径、localhost 或打包假设写进 Android 共用逻辑。
- Vector Search、Embedding Retrieval、Vector Database 和第二套 LLM Wiki 数据库属于明确非目标。

如果实现开始要求大量搬动 C# 文件、拆分多个基础 project、复制 Core 或提前建设后续扩展框架，应暂停并重新评估更小方案。

## 计划、决策与文档更新

- Roadmap Phase 的计划目录使用 Master Architecture 规定的 `phase-XX-<name>`。
- 非 Phase 的复杂工作使用简短、稳定的 kebab-case change id。
- 计划必须是执行期间的活文档，进度、发现、偏差、决定和验证随工作同步更新。
- 未经用户明确要求，不把“生成计划”理解为“开始实施”。
- 发现跨阶段或长期技术取舍时，先记录候选与证据；确认后写 ADR。
- 已确认能力契约写入 specs；实现细节只写入对应 change/plan；不得把这些内容堆回 Master Architecture。
- 改变 P0、分层、Phase 顺序或平台方向时更新 Master Architecture；只改变当前状态或入口时更新 README。

## 验证与完成标准

- 验证强度与风险相称：执行相关静态检查、测试、构建、目标平台验证或人工验收。
- Windows 或 Android 专属行为，只有在对应目标环境实际验证后才能宣称通过；不为范围外平台建立验证承诺。
- Codex CLI 运行在 WSL2，但工作区位于 Windows C 盘挂载路径。必须同时考虑 Linux 工具语义与 Windows 文件系统的路径、大小写、换行、文件锁和性能差异。
- 无法执行的验证必须写明原因和影响，不能用“理论上可行”替代结果。
- 完成前检查实际文件差异、生成物、临时文件、计划状态、外层产品仓库状态和参考 Submodule 指针/状态。
- 不得未经证据宣称“已完成”“已修复”“构建通过”或“平台可用”。最终汇报列出实际变更、实际验证和仍未验证事项。

## 单模型工作规则

本项目只使用当前主会话模型完成任务，不使用分层 Agent，不创建或委派子代理。探索、实施、审查和验证均由主会话直接完成。
