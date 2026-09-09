# Add Terraria Development Workflow Skill

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：项目级 Codex skill 与其 UI 元数据
- 关联架构/规范/ADR：`AGENTS.md`、`.agent/PLANS.md`；无能力规范或 ADR 变更

## 背景与当前证据

用户要求创建覆盖本项目全部工程任务的 `terraria-dev-workflow` skill，并要求代码实现固定由 `gpt-5.6-luna`、`max` 推理强度的子 agent 完成，主 agent 负责指导和审核。

当前 `.agents/` 下没有已有 skill；外层工作区不是 Git 仓库，`terraria-wiki-offline/` 是独立且当前无改动的源码仓库。`AGENTS.md` 已定义范围、安全和验证规则，`.agent/PLANS.md` 已定义持久化计划生命周期，因此新 skill 只编排实际工作流，不复制这些文档全文。

## 目标与完成定义

- 在 `.agents/skills/terraria-dev-workflow/` 创建有效的 `SKILL.md` 与 `agents/openai.yaml`。
- 支持自动匹配和 `$terraria-dev-workflow` 显式调用，覆盖规划、审查、诊断、实现和验证任务。
- 对代码、测试、可执行脚本及影响构建或运行时的配置修改，明确执行 Luna max 编写、主 agent 指导审核的分工。
- 通过 skill 快速验证，并确认没有修改业务源码或添加无用资源。

## 前置条件

- 用户已批准该实现计划与指定的 agent 分工。
- `skill-creator` 的 `SKILL.md` 和 `references/openai_yaml.md` 已完整读取。
- 不需要 Windows 或 Android 目标平台验证。

## 范围与非目标

范围仅包括 change plan、项目级 skill 指令及 UI 元数据。不修改 `terraria-wiki-offline/`、架构、能力契约、ADR、依赖或 Codex 个人 skills。

## P0 / 兼容性影响

不适用：不修改业务源码、数据、搜索、同步、Viewer 或平台 Runtime。通过检查内层 Git 状态保护只读源码基线。

## 方案与待决问题

使用 skill-creator 初始化器创建最小结构，不增加 scripts、references、assets 或 README。自动调用保持启用。skill 直接引用仓库权威文件，并在指定 Luna max 不可用时停止代码修改、报告阻塞，不静默更换实现者。

待决问题：无。

## 文件变更

- **ADD** `.agents/skills/terraria-dev-workflow/SKILL.md`：触发边界和工程工作流。
- **ADD** `.agents/skills/terraria-dev-workflow/agents/openai.yaml`：UI 元数据和自动调用策略。
- **ADD / ARCHIVE** 本计划：记录实施和验证证据。
- **KEEP** `terraria-wiki-offline/`：业务源码只读。

## 执行步骤

- [x] 建立已批准 change plan；预期结果：持久化范围、分工和验证方式；回退点：删除尚未产生其他改动的 change。
- [x] 委派 Luna max 初始化并编写 skill；预期结果：仅生成两个目标文件；验证方式：主 agent 检查实际内容和目录清单；回退点：删除新增 skill 目录。
- [x] 主 agent 审核触发边界、授权边界和委派协议；预期结果：与治理文件及用户要求一致；验证方式：逐条对照并将问题退回 Luna 修正。
- [x] 运行 quick validator 和情景静态检查；预期结果：结构有效、无占位符、四类请求行为明确。
- [x] 收尾并归档计划；预期结果：记录验证结果，确认业务源码无改动。

## 验证与回归计划

- 运行 `skill-creator/scripts/quick_validate.py .agents/skills/terraria-dev-workflow`。
- 检查 `SKILL.md` frontmatter、`openai.yaml` 字段和 `$terraria-dev-workflow` 默认提示。
- 静态核对规划、只读审查/诊断、代码实现委派、Luna max 不可用四类场景。
- 列出 skill 目录，确认无多余资源。
- 运行 `git -C terraria-wiki-offline status --short`，确认源码仓库保持干净。

## 风险与回退

- 自动触发过宽：用项目路径和工程任务类型限定 description，排除普通 Terraria 内容问答和其他项目。
- 委派边界含糊：明确代码类文件范围、主 agent 可做事项和不可用时停止条件。
- 与治理文件漂移：skill 引用权威文件并要求按需读取，不复制完整规则。
- 回退方式：删除新增 skill 目录；本任务不涉及业务数据或不可逆操作。

## 进度记录

- 2026-08-20：完成环境与治理勘察，用户确认名称、范围、自动调用和 Luna max 固定分工；开始实施。
- 2026-08-20：Luna max 仅新增两个目标 skill 文件；主 agent 完成内容审核、官方发现路径核对和独立验证，未发现需返工项。

## 发现、偏差与决策

- 外层工作区不是 Git 仓库，最终以目标文件清单和内容审核代替外层 `git diff`；内层源码仓库仍使用 Git 状态保护。
- skill 本身不创建第二套治理规范，只负责将现有规范落实成可执行流程。
- OpenAI 官方 Build skills 文档确认 `$CWD/.agents/skills` 是仓库级加载位置，Codex 支持 description 隐式调用和 `$skill` 显式调用，并会自动检测 skill 变更。

## 验证结果

- `python3 /home/xiao/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/terraria-dev-workflow`：通过，输出 `Skill is valid!`。
- 目录清单：仅包含 `SKILL.md` 与 `agents/openai.yaml`，无 scripts、references、assets、README、示例或占位符。
- 主 agent 逐条核对规划、只读审查/诊断、实现委派和 Luna max 不可用四类场景：行为与用户确认一致。
- `openai.yaml` 启用隐式调用，默认提示显式包含 `$terraria-dev-workflow`；frontmatter description 限定当前工作区并排除普通 Terraria 问答和其他项目。
- `git -C terraria-wiki-offline status --short`：无输出，业务源码仓库保持干净。
- 未执行 Windows、Android 或业务构建：本变更不修改平台代码或业务源码，这些验证不适用。

## 完成摘要

已创建项目级 `terraria-dev-workflow` skill。它把现有治理文档落实为覆盖规划、审查、诊断、实现和验证的工程流程，并固定代码类文件由 Luna max 编写、主 agent 指导审核的职责边界。skill 结构、元数据、发现位置和关键场景均已验证，业务源码未修改；如需回退，可删除新增 skill 目录。
