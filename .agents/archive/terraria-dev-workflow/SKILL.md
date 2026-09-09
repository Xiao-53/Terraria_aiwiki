---
name: terraria-dev-workflow
description: "Implement non-trivial code changes in /mnt/c/users/xiao/mypython/terraria_aiwiki with Luna max owning investigation, implementation, documentation, validation, debugging, and self-review while Sol only scopes the goal, writes the brief, and handles true escalation. Do not use for planning-only, review, diagnosis, validation-only work, documentation or governance changes, or short low-risk edits."
---

# Terraria Dev Workflow

只在当前工作区需要非简单代码实现时，使用 Luna max 端到端执行、Sol 最小介入的工作流。普通 Terraria 内容问答和其他项目不使用本 skill。

## 判断是否委派

只有同时满足以下条件才拉起 Luna max：

- 用户明确要求修改或实现；
- 主要交付物是业务源码、测试、可执行脚本，或影响构建/运行时行为的配置；
- 工作不是局部、低不确定性、无需新公共接口、迁移或跨平台验证的短小低风险修改。

多文件或跨模块实现、公共行为或接口变化、数据迁移、依赖/构建/Runtime/平台变化，以及需要实质性测试建设的代码工作，视为非简单代码开发。边界不清时由 Sol 只收敛任务目标和授权边界，不代替 Luna 调查代码。

以下工作由 Sol 直接完成，不拉起 Luna：

- 复杂规划、方案设计和 change plan 编写；
- 审查、解释、诊断、调查和纯验证；
- 文档、治理文件和 skill 维护；
- 局部、短小、低风险且边界明确的修改。

即使用户显式调用 `$terraria-dev-workflow`，未满足委派条件时也由 Sol 直接处理，不得仅因 skill 已加载就创建子 agent。

## Sol 的职责

Sol 只负责：

1. 判断用户目标、成功条件、授权范围和是否满足委派条件；
2. 给 Luna max 编写一次完整 implementation brief；
3. 等待并最终读取 Luna 的压缩报告；
4. 只有 Luna 明确报告重大架构问题、持续测试失败或无法在授权范围内作出决定时才介入。

委派后，Sol 不并行搜索或阅读业务代码，不修改实现、plan 或 ADR，不运行测试或 debug，也不自行做 git diff 审查。用户在等待期间另行明确交给 Sol 的独立任务不受此限制。

Sol 直接创建 `model=gpt-5.6-luna`、`reasoning_effort=max` 的子 agent。若指定能力不可用，停止该非简单代码实现并报告阻塞，不得静默换模型或由 Sol 接管实现。

implementation brief 至少明确：

- 目标、成功条件和用户指定的交付物；
- 已授权的变更范围与禁止范围，包括业务源码、无关 Phase、依赖和结构性改动边界；
- 已知约束、待 Luna 取证的未知项，以及接口、数据、平台和兼容性要求；
- 验证环境、必须覆盖的检查和可接受的未验证项；
- 数据安全、回退点和需要触发升级的条件。

Sol 不需要先替 Luna 生成精确文件清单、读取全部项目上下文或确定实现细节；这些属于 Luna 的端到端职责。

## Luna 的端到端职责

Luna 不得再次委派。收到 brief 后，自主完成当前授权范围内的全部工作：

1. 按 `AGENTS.md`、`INIT.md` 和 `.agent/PLANS.md` 读取必要上下文；
2. 阅读相关源码、配置、入口、调用方、数据模型、测试、spec、进行中 change、plan 和 ADR；
3. 依据真实代码收敛允许修改的精确文件和最小方案；
4. 在仓库规则要求时创建或持续维护 plan，并同步记录进度、发现、偏差、决定和验证；
5. 实现代码、测试、脚本和任务范围内确有必要的 plan / ADR / 状态文档更新；
6. 运行相称的静态检查、测试、构建、目标平台验证和 smoke test，失败时自主 debug 和重试；
7. 检查实际文件清单、git status、git diff、生成物、临时文件、范围边界、回归影响和回退路径；
8. 修正自审发现的问题，直到成功条件满足或触发升级条件。

不得因负责 plan / ADR 而扩大用户授权：只更新当前任务和仓库规则实际要求的文档，不提前实施相邻 Phase，不创建未经证据支持的长期决策。

## 只在三类情况升级给 Sol

Luna 只有遇到以下情况才暂停并给 Sol 发送简短升级报告：

- 新证据要求改变 P0、Master Architecture、Phase 边界、平台方向或作出其他重大长期架构选择；
- 关键测试在合理 debug 和安全替代方案后仍失败，已经影响成功条件或无法判断结果可信度；
- 所需选择会实质扩大权限、触碰禁止范围、产生不可逆影响，或多个方案的产品取舍无法从现有事实决定。

普通代码选择、局部方案调整、可恢复测试失败和计划内 debug 由 Luna 自主处理，不升级给 Sol。

## Luna 的压缩报告

完成或真实阻塞后，Luna 只向 Sol 返回一份可直接转述的压缩报告，包含：

- 实际修改的文件组与用户可观察行为；
- 实际执行的验证命令、环境和结果；
- 仍失败或未验证的平台、场景及其影响；
- 重大风险、已作决定和回退方式；
- 业务源码、plan、ADR、生成物和仓库状态是否符合范围。

报告不复述调查过程，不要求 Sol 重新阅读代码、diff 或测试日志。Sol 的最终答复以该压缩报告为依据，只宣称 Luna 提供证据支持的结果。
