# 采用 Sol + Luna 分层子代理

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI；不需要 Windows/Android 验证
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：项目级 Codex skill、多代理配置、Agent 工作规则和旧 skill 归档
- 关联架构/规范/ADR：`AGENTS.md`、`.agent/PLANS.md`；不改变产品架构、能力规范或 ADR

## 背景与当前证据

用户要求采用 `https://github.com/Yuri-NagaSaki/subagent-skills` 替代项目自建的 `terraria-dev-workflow`，并归档旧 skill。

已确认事实：

- 上游仓库当前提供 `skills/sol-luna-setup`，包含 Sol 主会话和 Luna scout/worker/critic/tester 的项目模板与验证脚本。
- 现有项目 skill 位于 `.agents/skills/terraria-dev-workflow/`，其日常开发委派职责将由新的分层子代理政策替代。
- 当前 `.codex/config.toml` 只定义 sandbox、approval 和 web search，没有 Agent 角色配置。
- `codex login status` 显示 `Logged in using ChatGPT`；环境中没有 `OPENAI_API_KEY` 或 `CODEX_API_KEY`。
- 本机 `codex debug models` 已列出 `gpt-5.6-sol`、`gpt-5.6-terra` 和 `gpt-5.6-luna`，无需为发现 Luna 预先生成自定义模型目录。
- 上游 bootstrap 会附带创建 Claude Code 文件并使用自定义 Gateway 占位配置；这与本项目 Codex-only 边界和用户选择的现有 Codex 登录不一致，不能整包盲运行。

用户已明确选择保留现有 Codex 登录。该选择覆盖上游 setup skill 对 Gateway API Key 的默认前置要求；不得写入 provider、base URL 或密钥。

## 目标与完成定义

- 将上游 `sol-luna-setup` 安装为项目级 skill，并保留可追溯的上游来源。
- 增加四个 Luna 角色配置，并让 Sol 作为主会话负责拆解、协调、整合与最终验收。
- 保留项目现有 sandbox、approval、network 和 web search 配置，使用当前 ChatGPT 登录。
- 将 `terraria-dev-workflow` 完整移动到非 skill 发现目录 `.agents/archive/terraria-dev-workflow/`。
- 合并多代理政策到现有 `AGENTS.md`，不覆盖 Terraria 项目治理和 P0 护栏。
- 通过严格配置解析、skill 校验、Sol/Luna 单模型和多代理 smoke test；无法通过时记录实际失败和影响。

## 前置条件

- 用户已授权替换并归档旧 skill。
- 当前 WSL Codex CLI 版本为 `0.148.0`，使用 ChatGPT 登录。
- 不要求安装或配置 Claude Code、Pi、自定义 Gateway 或 API Key。

## 范围与非目标

范围内：

- `.agents/skills/sol-luna-setup/`
- `.agents/archive/terraria-dev-workflow/`
- `.codex/config.toml`
- `.codex/agents/luna_{scout,worker,critic,tester}.toml`
- `AGENTS.md`
- 必要的本地模型目录脚本与 `.gitignore` 保护（仅在验证证明需要时）
- 本 change plan

非目标：

- 不修改 `terraria-wiki-offline/` 业务源码。
- 不安装或配置 Claude Code、Pi、npm 全局包、自定义 Gateway。
- 不写入、迁移或显示认证凭据。
- 不改变 Master Architecture、Roadmap Phase、spec 或 ADR。
- 不提交、推送或创建 PR。

## P0 / 兼容性影响

不触碰 Wiki 数据、SQLite/FTS5、同步、导入导出、Viewer 或平台实现。主要兼容风险是错误配置导致 Codex 无法启动或 Luna 无法被发现；通过保留现有配置、严格解析和 smoke test 控制。回退时可恢复归档的旧 skill，并移除新增 Agent 配置和 AGENTS 追加段落。

## 方案与待决问题

采用上游 Codex 模板的角色划分，但做项目适配：

- 安装上游 skill，不改写其来源内容。
- 不运行会生成 `.claude/`、`CLAUDE.md` 和 Gateway 占位配置的整包 bootstrap；只合并与本项目有关的 Codex 文件。
- `.codex/config.toml` 不设置 `model_provider`，沿用现有 ChatGPT 登录；设置 Sol 主模型、Luna 默认子模型、推理强度、并发和 multi-agent 特性。
- AGENTS 政策受现有范围、安全、计划和验证规则约束；Luna 不获得 commit/PR/deploy 权限，也不得越过业务源码只读边界。
- 只有实际 spawn 失败且证据指向 catalog 兼容问题时，才引入 `prepare-luna-catalog.sh` 与被忽略的 `.codex/models-v1.json`。

## 文件变更

- **ADD** `.agents/skills/sol-luna-setup/**`：用上游项目级 setup skill 替代自建 skill。
- **MOVE** `.agents/skills/terraria-dev-workflow/**` → `.agents/archive/terraria-dev-workflow/**`：保留可恢复副本并移出 skill 发现目录。
- **MODIFY** `.codex/config.toml`：保留现有安全配置，增加无自定义 provider 的 Sol/Luna 配置。
- **ADD** `.codex/agents/luna_*.toml`：新增四个边界明确的 Luna 角色。
- **MODIFY** `AGENTS.md`：合并分层子代理政策，并明确其服从现有项目治理。
- **ADD/MODIFY only if needed** `.gitignore`、`scripts/prepare-luna-catalog.sh`：只在 catalog 修复实际需要时落地。
- **MOVE** 本计划到 `openspec/changes/archive/adopt-sol-luna-subagents/plan.md`：完成验证后归档。

## 执行步骤

- [x] 使用官方 `skill-installer` helper 将上游 `sol-luna-setup` 安装到 `.agents/skills/`；核对文件清单和来源内容。
- [x] 用 `apply_patch` 合并 Codex 配置、四个 Luna 角色和 AGENTS 多代理政策；确认未写入 provider、URL 或密钥。
- [x] 将 `terraria-dev-workflow` 移到 `.agents/archive/`；确认 `.agents/skills/` 不再发现旧 skill。
- [x] 执行 skill 校验、严格配置解析、单模型和多代理 smoke test；按实际结果修正或记录。
- [x] 检查状态、秘密模式和范围；完成并归档本计划。

## 验证与回归计划

- WSL：运行 skill quick validator 验证新 skill。
- WSL：运行 `codex --strict-config features list` 验证配置键可解析。
- WSL：运行 Sol 与 Luna 的只读 `codex exec` smoke test，沿用 ChatGPT 登录。
- WSL：运行多代理 scout smoke test，验证 Luna 角色可 spawn。
- WSL：搜索 API Key、Gateway 占位 URL、`.env` 和 `models-v1.json`，确认未写入工作区。
- WSL：检查 `git status --short`、实际文件清单和 `terraria-wiki-offline/` 未被修改。
- Windows/Android：不适用，本变更只影响 WSL Codex CLI 工作流。

## 风险与回退

- 配置键与当前 Codex 版本不兼容：严格解析失败时撤回对应新增键，不破坏原配置。
- Luna 目录兼容问题：仅在实际失败后生成被忽略的本地 catalog；不提交生成物。
- 新政策覆盖项目治理：采用追加且显式从属的方式，现有 P0、Phase、源码授权和验证规则优先。
- 上游更新不可控：本次安装形成项目内快照；后续升级必须单独审阅。
- 回退：移除新 skill/Agent 配置和 AGENTS 新段落，把 `.agents/archive/terraria-dev-workflow` 移回 `.agents/skills/`。

## 进度记录

- 2026-08-20：完成上游 skill、模板和脚本审阅；确认上游 bootstrap 不适合直接整包运行。
- 2026-08-20：确认用户选择现有 Codex/ChatGPT 登录；确认本机可见 Sol、Terra、Luna。
- 2026-08-20：创建并进入本执行计划。
- 2026-08-20：安装项目级 `sol-luna-setup`，合并 Codex-only 配置和角色政策，并把旧 skill 移入 `.agents/archive/`。
- 2026-08-20：完成静态、认证、单模型和多代理验证；检查范围后准备归档。

## 发现、偏差与决策

- 决定：只采用 Codex 部分，不引入 Claude Code/Pi 文件。
- 决定：不配置自定义 Gateway，不要求 API Key；沿用 `codex login` 的 ChatGPT 凭据。
- 决定：catalog 脚本从“上游必做”降为证据触发的兼容回退，因为当前模型目录已直接包含 Luna。
- 偏差：`codex --strict-config features list` 不支持 strict 模式，因此使用真实的 `codex --strict-config exec` 同时验证配置解析和模型调用。
- 偏差：读取项目 `README.md` 的多代理 smoke 被安全审查拒绝且未执行；改用不读取项目文件、不调用工具的固定标记握手测试。

## 验证结果

- `python3 /home/xiao/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/sol-luna-setup`：通过，输出 `Skill is valid!`。
- Python `tomllib` 解析 `.codex/config.toml` 和四个 `.codex/agents/luna_*.toml`：通过，共 5 个 TOML 文件。
- `codex --strict-config exec --sandbox read-only -c 'model="gpt-5.6-sol"' ...`：通过，provider 为 `openai`，输出 `SOL_SMOKE_OK`。
- `codex --strict-config exec --sandbox read-only -c 'model="gpt-5.6-luna"' ...`：通过，provider 为 `openai`，一次 WebSocket TLS 握手错误后自动回退成功，输出 `LUNA_SMOKE_OK`。
- 无项目内容的 Sol → Luna 握手：通过，Sol 完成一次 `luna_scout` 委派并输出 `SCOUT_DONE`。
- `.agents/skills/` 中不再出现 `terraria-dev-workflow`；归档目录保留原 `SKILL.md` 和 `agents/openai.yaml`。
- `rg` 检查活动 `.codex` 配置和根 `AGENTS.md`：没有 `base_url`、`env_key`、`model_provider`、API Key 变量或疑似 `sk-` 密钥。
- `.codex/models-v1.json` 不存在；当前模型目录无需 catalog 修复。
- `git -C terraria-wiki-offline status --short` 无输出，业务源码仓库保持干净。
- 未执行 Windows、Android 或业务构建：本次仅修改 WSL Codex 工作流，这些验证不适用。

## 完成摘要

已用上游 `sol-luna-setup` 的 Codex 角色模型替代项目自建的单一 `terraria-dev-workflow`：项目现在由 Sol 负责范围、协调和验收，四个 Luna 角色分别承担只读探索、边界实现、对抗审查和验证。配置沿用现有 ChatGPT 登录，不含 Gateway 或密钥；旧 skill 已可恢复地归档，业务源码未修改。
