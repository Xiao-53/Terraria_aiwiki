# Narrow Terraria Development Workflow Trigger

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：项目级 `terraria-dev-workflow` skill 的触发与委派边界
- 关联架构/规范/ADR：`AGENTS.md`、`.agent/PLANS.md`；无能力规范或 ADR 变更

## 背景与当前证据

当前 skill 的 description 覆盖规划、审查、诊断、实现和验证，并要求所有代码类文件都由 Luna max 子 agent 编写。用户现要求降低触发范围：只在非简单代码开发任务中使用 Luna max；复杂规划与短小任务由主 agent 直接完成，不拉起子 agent。

OpenAI 官方 Build skills 文档确认隐式匹配主要由 frontmatter `description` 控制，`allow_implicit_invocation` 只决定是否允许隐式调用。因此本次保留自动调用，但收窄 description、正文适用条件和 UI 提示。

## 目标与完成定义

- skill 只对当前项目中非简单代码实现任务隐式匹配。
- Luna max 只承担非简单代码实现；规划、审查、诊断、纯验证、文档治理和短小低风险修改由主 agent 完成。
- 保留 `$terraria-dev-workflow` 显式调用和自动调用能力。
- skill validator 通过，业务源码保持不变。

## 前置条件

- 用户已明确授权本次 skill 更新。
- 已读取 `openai-docs`、`skill-creator`、现有 skill 及项目治理上下文。

## 范围与非目标

只修改 `.agents/skills/terraria-dev-workflow/SKILL.md` 和 `agents/openai.yaml`。不修改业务源码、架构、spec、ADR、依赖或 Roadmap Phase。

## P0 / 兼容性影响

不适用：仅调整 Codex skill 的触发与委派行为，不改变产品能力或数据。

## 方案与待决问题

将隐式触发限定为“主要交付是非简单代码、测试、可执行脚本或构建/运行时配置变更”。短小低风险修改定义为局部、低不确定性、无需新公共接口/迁移/跨平台验证的变更，由主 agent 直接完成。待决问题：无。

## 文件变更

- **MODIFY** `SKILL.md`：收窄 description，简化正文并定义直接完成与委派条件。
- **MODIFY** `agents/openai.yaml`：让短描述和默认提示与新范围一致；保留隐式调用。
- **ADD / ARCHIVE** 本计划：记录调整及验证。
- **KEEP** `terraria-wiki-offline/`：业务源码只读。

## 执行步骤

- [x] 读取当前实现、项目治理和官方 skill 触发说明。
- [x] 更新 skill 触发、直接执行和 Luna max 委派边界。
- [x] 运行 validator，静态核对代表性触发/不触发场景及业务源码状态。
- [x] 记录结果并归档本计划。

## 验证与回归计划

- 运行 skill-creator `quick_validate.py`。
- 核对非简单代码实现会触发；规划、审查/诊断、纯验证、文档治理、短小低风险修改不会隐式触发。
- 检查 `openai.yaml` 保留 `allow_implicit_invocation: true` 且默认提示包含 `$terraria-dev-workflow`。
- 检查 `terraria-wiki-offline` Git 状态无变化。

## 风险与回退

“短小”可能产生判断差异，因此用局部性、风险、公共接口、迁移和平台验证要求作判据；边界不清时由主 agent 先判断，不因不确定而自动委派。回退可恢复本次修改前的两个 skill 文件。

## 进度记录

- 2026-08-20：完成证据核对并开始最小调整。
- 2026-08-20：主 agent 直接完成两个 skill 文件更新，验证通过并准备归档。

## 发现、偏差与决策

- 保留隐式调用；通过收窄 description 降低误触发，而不是改为仅显式调用。
- 本任务本身属于短小治理调整，按用户新要求由主 agent 直接完成，不创建子 agent。

## 验证结果

- `python3 /home/xiao/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/terraria-dev-workflow`：通过，输出 `Skill is valid!`。
- frontmatter description 明确只匹配非简单代码实现，并排除 planning、review、diagnosis、validation-only、文档治理和短小低风险修改。
- 正文明确复杂规划、审查/诊断、纯验证、skill 维护和简单代码修改均由主 agent 直接完成；非简单代码实现才创建 Luna max。
- `openai.yaml` 保留 `allow_implicit_invocation: true`，UI 描述与默认提示已收窄且默认提示包含 `$terraria-dev-workflow`。
- skill 目录仍只有 `SKILL.md` 和 `agents/openai.yaml`；`git -C terraria-wiki-offline status --short` 无输出，业务源码保持干净。
- 未执行 Windows、Android 或业务构建：本次未修改产品或平台代码，这些验证不适用。

## 完成摘要

已将 `terraria-dev-workflow` 从覆盖全部工程任务的通用编排 skill 收窄为非简单代码实现委派 skill。复杂计划与非编码工程工作、skill/治理维护及短小低风险修改由主 agent 直接完成；只有满足明确条件的代码开发交给 Luna max，并继续由主 agent 审核验收。
