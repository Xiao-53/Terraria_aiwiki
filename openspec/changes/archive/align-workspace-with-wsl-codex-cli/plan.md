# 对齐 WSL Codex CLI 开发环境

- 状态：completed
- Phase：无
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：工作区 Agent 规则、入口、项目基线和计划规范
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.5.md`；无 ADR

## 背景与当前证据

用户明确说明，后续开发由安装在 WSL 中的 Codex CLI 执行，而不是 Codex 桌面会话。上一轮治理文件把执行环境写成“Agent 可能从 Windows 或 WSL 访问”，没有把 WSL Codex CLI 定为唯一主要执行者，也没有说明应从外层工作区根启动。

当前工作区根 `C:\Users\Xiao\mypython\terraria_aiwiki` 不是 Git 仓库，实际源码仓库是子目录 `terraria-wiki-offline/`。对应 WSL 路径是 `/mnt/c/Users/Xiao/mypython/terraria_aiwiki`。

官方 OpenAI 文档确认：

- Codex 在每次运行或 TUI 会话开始时建立 `AGENTS.md` 指令链；从项目根向当前目录查找，离当前目录更近的文件后加载并覆盖更高层规则。
- 找不到项目根时，Codex 只检查当前目录；因此本项目应从外层工作区根启动，避免把内层 Git 仓库当作发现起点而漏掉外层 `AGENTS.md`。
- WSL2 中 Codex 运行在 Linux 环境；Windows 挂载路径 `/mnt/c/...` 可用但 I/O 可能较慢。官方更推荐 Linux home 中的仓库，但本任务不授权移动项目。
- Codex CLI 应从项目目录运行；CLI 可以检查、编辑和运行本机已安装工具。

## 目标与完成定义

- 明确唯一主要开发执行环境是 WSL2 中的 Codex CLI。
- 明确固定启动目录、WSL 路径与 Windows 路径映射。
- 明确必须从外层工作区根启动，而不是内层源码 Git 仓库。
- 明确 WSL 负责读取、编辑、搜索和平台无关检查；Windows 负责 MAUI Windows 构建、运行、调试和打包验证。
- 禁止工作计划依赖桌面端会话、隐藏状态或桌面专属能力。
- 不自动把项目从 `/mnt/c` 移入 WSL home；性能迁移必须由用户单独决定。

## 前置条件

- 用户已在 WSL 中安装 Codex CLI。
- 当前项目继续存放在 Windows C 盘。

## 范围与非目标

范围：`AGENTS.md`、`INIT.md`、`README.md`、`openspec/project.md`、`.agent/PLANS.md`。

非目标：

- 不移动或复制项目到 WSL Linux 文件系统。
- 不运行、升级或重新配置用户的 Codex CLI。
- 不修改 `~/.codex/config.toml`、全局 `~/.codex/AGENTS.md` 或权限策略。
- 不修改 Master Architecture、业务源码、依赖、构建配置或 Phase 状态。

## P0 / 兼容性影响

不影响产品 P0 能力。本次仅修正文档中的执行环境和 Codex CLI 发现规则。

## 方案与待决问题

- 在 AGENTS 中加入 WSL Codex CLI 固定运行环境和启动规则，并说明 `AGENTS.md` 在会话开始时自动加载。
- INIT 提供唯一推荐启动命令和路径映射。
- README 面向用户说明 WSL/Windows 职责分工。
- project 把 WSL Codex CLI 写入长期开发基线。
- PLANS 要求每份计划记录 WSL 与 Windows 验证边界，并以文件计划作为持久状态。
- 保留 `/mnt/c` 当前布局；是否迁移至 `~/code` 为未来用户决策。

## 文件变更

- MODIFY `AGENTS.md`
- MODIFY `INIT.md`
- MODIFY `README.md`
- MODIFY `openspec/project.md`
- MODIFY `.agent/PLANS.md`
- KEEP `docs/architecture/ARCHITECTURE-v0.5.md`
- KEEP `terraria-wiki-offline/`

## 执行步骤

- [x] 阅读当前治理文件并核对官方 Codex CLI、AGENTS.md 与 WSL 文档。
- [x] 修改五份治理文件，写明 WSL CLI 启动与验证边界。
- [x] 搜索并消除“Windows 或 WSL 均可作为 Agent 环境”、桌面端隐含依赖和错误启动位置。
- [x] 核对路径、链接、文件哈希和源码仓库状态。
- [x] 完成并归档本 change。

## 验证与回归计划

- 检查五份文件均明确 WSL2 Codex CLI 是主要执行环境。
- 检查启动路径为 `/mnt/c/Users/Xiao/mypython/terraria_aiwiki`，并禁止从内层源码仓库启动主会话。
- 检查 Windows 仅承担 MAUI 目标平台构建、运行、调试和打包验证。
- 检查不存在“Agent 可能从 Windows 或 WSL 访问”等模糊表述。
- 检查架构路径和 README 相对链接仍有效。
- 检查 `terraria-wiki-offline/` Git 状态无变化。

## 风险与回退

- `/mnt/c` I/O 性能可能低于 WSL home；只记录限制，不擅自移动项目。
- 用户若从内层 Git 仓库启动 Codex，外层 AGENTS 可能未加载；通过醒目的启动规则降低风险。
- 可通过恢复上一归档 change 的对应文档版本回退。

## 进度记录

- 2026-08-20：确认用户实际开发面为 WSL Codex CLI；完成官方文档核对并建立修正计划。
- 2026-08-20：完成五份治理文件草稿修正；固定外层 WSL 工作区启动、文件化计划和 Windows MAUI 验证边界。
- 2026-08-20：将修正版写回项目并完成哈希、路径、旧表述和源码状态检查。

## 发现、偏差与决策

- 外层工作区不是 Git 根，因此 Codex CLI 的启动目录本身是指令发现能否正确工作的关键条件。

## 验证结果

- `AGENTS.md`、`INIT.md`、`README.md`、`openspec/project.md` 和 `.agent/PLANS.md` 均明确 WSL2 Codex CLI 为主要执行环境。
- 五份项目文件与交付草稿的 SHA-256 分别一致。
- 固定启动路径为 `/mnt/c/Users/Xiao/mypython/terraria_aiwiki`，并明确禁止从内层源码 Git 仓库启动主会话。
- 活跃治理规则不再使用“Agent 可能从 Windows 或 WSL 访问”的模糊表述；该文字仅保留在本计划的历史问题描述中。
- `docs/architecture/ARCHITECTURE-v0.5.md` 仍存在，README 架构链接未改变。
- `git -C terraria-wiki-offline status --short` 无输出，业务源码仓库未修改。
- 未启动用户的 WSL Codex CLI，也未修改其全局配置；需要用户在新 CLI 会话中确认指令加载。

## 完成摘要

工作区治理文件已专门对齐 WSL2 Codex CLI：固定外层工作区根启动、Linux 路径和文件化上下文，Windows 只负责 MAUI 目标平台验证，并禁止依赖桌面端能力或擅自迁移 `/mnt/c` 项目。Master Architecture、业务源码和 Phase 状态未改变。
