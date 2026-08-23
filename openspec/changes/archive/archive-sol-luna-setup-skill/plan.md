# 归档 Sol-Luna Setup Skill

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI；不需要 Windows/Android 验证
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：项目级 skill 发现目录、Agent 分工规则与变更记录
- 关联架构/规范/ADR：`AGENTS.md`、`.agent/PLANS.md`；不改变产品架构、能力规范或 ADR

## 背景与当前证据

用户要求检查分工 skill、归档该 skill，并同步修改相关内容。

已确认事实：

- `.agents/skills/sol-luna-setup/` 是一份用于新机器或新项目的安装、脚手架、catalog 修复和 smoke test 快照；其 bootstrap 还包含本项目未采用的 Gateway、Claude Code 与 Pi 路径。
- 当前项目真正生效的日常分工由根 `AGENTS.md` 的“Sol + Luna 分层子代理政策”、`.codex/config.toml` 和 `.codex/agents/luna_*.toml` 独立定义，不依赖运行该 skill。
- `.agents/archive/` 已用于可恢复地保存退出活动发现范围的 skill，现有先例是 `terraria-dev-workflow`。
- 历史计划 `openspec/changes/archive/adopt-sol-luna-subagents/plan.md` 记录了当时采用该 setup skill 的事实，不应改写历史结果。
- 内层 `terraria-wiki-offline/` 源码仓库存在任务开始前已有的未提交改动；本变更不读取、修改、清理或验证这些业务改动。

## 目标与完成定义

- 将 `sol-luna-setup` 的完整快照移出 `.agents/skills/` 并保存到 `.agents/archive/sol-luna-setup/`。
- 更新 `AGENTS.md`，明确归档的是一次性 setup skill，当前分工规则以 `AGENTS.md` 和 `.codex` 项目配置为准。
- 保留现有 Sol/Luna 角色配置和多代理能力；归档 setup skill 不解释为禁用多代理。
- 确认活动 skill 目录不再发现 `sol-luna-setup`，归档文件完整，所有非历史活动引用与新状态一致。
- 完成验证后将本 change plan 移入 `openspec/changes/archive/`。

## 前置条件

- 用户已明确授权归档该 skill 并修改相关内容。
- 不需要安装、联网、认证、模型 smoke test 或目标平台验证；运行时 Agent 配置不发生变化。

## 范围与非目标

范围内：

- `.agents/skills/sol-luna-setup/**`
- `.agents/archive/sol-luna-setup/**`
- `AGENTS.md` 中与该 skill 活动状态直接相关的表述
- 本 change plan

非目标：

- 不禁用或改写 `.codex/config.toml` 的多代理配置。
- 不删除或修改 `.codex/agents/luna_*.toml`。
- 不改写既有归档计划中的历史事实。
- 不修改 `README.md`、`INIT.md`、Master Architecture、spec 或 ADR；这些文件没有该 skill 的当前引用或职责变化。
- 不触碰 `terraria-wiki-offline/` 业务源码及其既存改动。

## P0 / 兼容性影响

不影响 Wiki 数据、SQLite/FTS5、同步、导入导出、Viewer、业务源码或产品平台。归档后新会话不再把 setup skill 作为活动项目 skill 发现；Sol/Luna 角色和委派规则继续由项目配置提供。回退时可将归档目录移回 `.agents/skills/` 并恢复 `AGENTS.md` 原表述。

## 方案与待决问题

采用“归档安装脚手架、保留已落地配置”的最小方案：setup skill 已完成其安装职责，继续处于活动发现目录会把一次性环境配置说明暴露为日常触发项；但删除已落地的角色配置会扩大成禁用多代理，用户没有提出该要求。

当前无待决问题。若后续要停用 Sol/Luna 多代理，应作为独立变更处理 `.codex/config.toml`、角色文件和完整政策段落。

## 文件变更

- **MOVE** `.agents/skills/sol-luna-setup/**` → `.agents/archive/sol-luna-setup/**`：保留可恢复快照并退出活动 skill 发现范围。
- **MODIFY** `AGENTS.md`：将采用声明改为当前配置来源与归档状态说明；保留角色和委派约束。
- **ADD then MOVE** `openspec/changes/archive-sol-luna-setup-skill/plan.md` → `openspec/changes/archive/archive-sol-luna-setup-skill/plan.md`：记录本次检查、实施和验证。
- **KEEP** `.codex/config.toml`、`.codex/agents/luna_*.toml`：已落地的运行时分工配置不依赖活动 setup skill。

## 执行步骤

- [x] 完整读取 setup skill、脚本和模板，核对当前 `.codex` 配置、根规则、归档先例与历史采用计划。
- [x] 移动完整 skill 到 `.agents/archive/sol-luna-setup/`，确认活动目录不再发现它。
- [x] 更新 `AGENTS.md` 的来源与归档状态表述，不改变现有角色权限和委派边界。
- [x] 检查文件完整性、活动引用、TOML 解析、工作区状态和内层源码既存改动；记录实际结果。
- [x] 标记完成并归档本计划。

## 验证与回归计划

- 对比移动前后的文件相对路径、文件数与校验和，确认归档快照完整。
- 搜索非归档活动文件中的 `sol-luna-setup` 引用，确认不存在错误的活动采用声明。
- 用 Python 标准库 `tomllib` 解析 `.codex/config.toml` 与四个角色 TOML，确认保留配置仍合法。
- 检查 `.agents/skills/`、`.agents/archive/`、外层工作区状态和 `git -C terraria-wiki-offline status --short`。
- Windows/Android 验证不适用；不改变产品或目标平台行为。

## 风险与回退

- 风险：把归档误解为禁用多代理。通过保留 `.codex` 配置和完整角色政策，并在 `AGENTS.md` 明示当前来源来控制。
- 风险：归档移动遗漏隐藏模板文件。移动整个目录，并以移动前后清单和校验和验证。
- 风险：新会话仍缓存旧 skill。`AGENTS.md` 已规定修改后重启；最终报告明确需要结束并重新启动 Codex 会话。
- 回退：将 `.agents/archive/sol-luna-setup/` 移回 `.agents/skills/sol-luna-setup/`，恢复 `AGENTS.md` 原采用声明。

## 进度记录

- 2026-08-24：完成治理上下文、活动 skill 全部 14 个文件、当前 Agent 配置、归档先例和历史计划检查；确定保留运行时分工配置、仅归档一次性 setup skill。
- 2026-08-24：创建本执行计划，准备实施目录移动与根规则更新。
- 2026-08-24：将完整 skill 移入 `.agents/archive/sol-luna-setup/`，并更新根 `AGENTS.md` 的当前配置来源与归档状态说明。
- 2026-08-24：完成文件完整性、活动发现范围、引用、TOML 和仓库状态检查；本变更满足完成定义。

## 发现、偏差与决策

- 决定：历史采用计划保持原样，作为当时状态的审计记录。
- 决定：`.codex` 中的四个 Luna 角色和默认子模型配置继续保留；它们是当前运行时配置，不是活动 setup skill 的一部分。
- 发现：setup skill 的通用 bootstrap 含 Gateway、Claude Code、Pi 和全局安装路径，而项目当时只采用了 Codex 部分；该差异进一步说明它适合作为历史安装快照归档。
- 发现：内层源码仓库已有未提交 Search 相关文件，本变更将其视为用户既存工作并保持不动。

## 验证结果

- 归档完整性：移动前后均为 14 个文件；逐文件 SHA-256 完全一致，包括隐藏的 `.claude` 与 `.codex` 模板。
- 活动发现范围：`find .agents/skills -type f` 无输出，`.agents/skills/sol-luna-setup` 不存在；归档 `SKILL.md` 存在。
- 当前引用：对 `AGENTS.md`、入口/基线文档、`.codex`、活动 skills 与本 change 的搜索只发现 `AGENTS.md` 的“已归档”说明和本计划记录；没有仍宣称采用活动 setup skill 的配置或入口。
- 配置解析：Python 3 标准库 `tomllib` 成功解析 `.codex/config.toml` 与四个 `.codex/agents/luna_*.toml`，共 5 个文件。
- 根规则检查：角色权限和六条委派规则保持不变，仅替换 setup skill 的当前来源说明。
- 工作区检查：外层工作区当前内容整体为未跟踪状态，无法用外层 Git diff 单独证明本次移动；已通过明确路径、文件清单、校验和与内容检查验证实际变更。
- 业务源码边界：`git -C terraria-wiki-offline status --short` 仍显示任务开始前发现的 8 个已修改文件和 1 个未跟踪测试文件；本次未触碰、清理或验证这些既存改动。
- Windows/Android 验证未执行且不适用；本变更只影响 WSL Codex CLI 的项目级 skill 发现和治理说明。

## 完成摘要

`sol-luna-setup` 已作为完整、可恢复的历史快照退出活动 skill 目录；根规则已改为引用实际生效的 `AGENTS.md` 与 `.codex` 配置。现有 Sol/Luna 多代理角色未被禁用或改写，业务源码和产品能力未修改。修改 `AGENTS.md` 与项目 skill 目录后，需要结束并重新启动 Codex 会话，下一会话才会按新发现结果加载。
