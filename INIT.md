# Terraria AI Wiki — WSL Codex CLI 入口

本文件只规定 WSL2 中 Codex CLI 的启动位置和新任务读取顺序。项目方向见 Master Architecture，强制工作规则由 Codex 自动加载的 `AGENTS.md` 提供。

## 启动 Codex

固定从外层工作区根启动：

```bash
cd /mnt/c/Users/Xiao/mypython/terraria_aiwiki
codex
```

## 固定位置

- 工作区：当前目录
- Windows 路径：`C:\Users\Xiao\mypython\terraria_aiwiki`
- WSL 路径：`/mnt/c/Users/Xiao/mypython/terraria_aiwiki`
- 产品 solution：`Terraria_Wiki.Core.slnx`
- 产品源码与测试：`src/`、`tests/`、`agent/`、`assets/`
- 旧实现参考：ADR 0007、归档 Phase 计划与 `assets/windows/README.md` 记录的上游固定提交
- Master Architecture：`docs/architecture/ARCHITECTURE-v0.6.md`
- 项目基线：`openspec/project.md`
- 计划规范：`.agent/PLANS.md`

产品源码和工作区治理文件都属于当前仓库；不得重新引入旧 MAUI checkout 作为产品 build/runtime 依赖。修改 `AGENTS.md` 后重新启动 Codex 会话。

## 新任务读取顺序

1. 确认 Codex 已从外层工作区根启动并自动加载 `AGENTS.md`。
2. `INIT.md`
3. `README.md`
4. `docs/architecture/ARCHITECTURE-v0.6.md`
5. `openspec/project.md`
6. 与任务相关的 `openspec/specs/`、`openspec/changes/` 和 `docs/adr/`
7. 需要建立计划时完整读取 `.agent/PLANS.md`
8. 涉及源码时，再读取根 solution 与 `src/`、`tests/`、`agent/`、`assets/` 中的相关配置、入口、调用方、实现、数据模型和测试；只有核对旧行为或资产来源时，才读取归档记录或 ADR 0007 固定的上游提交

只读取与当前任务有关的 specs、changes、ADR 和源码，不要无目的加载整个仓库。

## 开始工作

- 回答或审查任务：读取证据后直接报告，不修改文件。
- 诊断任务：确定原因和影响；用户未要求修复时不实施修复。
- 简单局部修改：确认范围和验证方式后实施。
- 多文件、跨模块、跨平台、高风险或 Phase 工作：先按 `.agent/PLANS.md` 建立可持久化的 change 和计划，不依赖会话内临时状态。
- 只要求生成 Phase 计划时：生成计划，不自动进入实现。

若文档互相冲突、前置 Phase 未完成或任务需要超出当前授权的结构性决策，停止扩展并向用户说明。
