# 用 ZIP Skill 替换核心源码 RAR Skill

- 状态：archived
- Phase：无
- 执行环境：WSL Codex CLI；Windows PowerShell 提供内置 ZIP 压缩
- 创建日期：2026-09-12
- 最后更新：2026-09-12
- 范围：项目级核心源码打包 Skill 与 README 入口
- 关联架构/规范/ADR：`AGENTS.md`、`README.md`、`openspec/project.md`、`openspec/changes/archive/package-core-source-rar/plan.md`

## 背景与当前证据

- 用户提供的字面路径 `C:\Users\Xiao\mypython\terraria\_aiwiki\...` 不存在；当前仓库中的对应目录位于 `C:\Users\Xiao\mypython\terraria_aiwiki\.agents\skills\`。
- 新 ZIP Skill 当前是未跟踪内容，并被错误地多嵌套了一层 `package-core-source-zip/`，因此不能按其文档中的标准调用路径使用。
- ZIP Skill 的固定白名单、未跟踪源码防遗漏、临时 staging、禁止覆盖、归档清单和 SHA-256 规则与现有 RAR Skill 保持同一边界。
- WSL 中已完成 Skill 结构校验、Bash 语法检查、115 文件 dry-run，以及写入 `/tmp` 的实际 ZIP 创建、CRC 与 manifest 校验；测试产物随后清理。

## 目标与完成定义

1. ZIP Skill 位于 `.agents/skills/package-core-source-zip/` 的标准可发现结构，并可按文档路径运行。
2. README 将 ZIP Skill 作为当前核心源码打包入口。
3. RAR Skill 完整移入 `.agents/archive/package-core-source-rar/`，不再作为活动 Skill 被发现，且历史实现仍可恢复。
4. 完成后重新执行结构、语法、dry-run 和临时实际打包验证。

## 前置条件

- [x] 已读取项目入口、架构、项目基线、计划规范和历史 RAR 打包计划。
- [x] 已读取并检查 ZIP/RAR Skill 的 `SKILL.md`、`agents/openai.yaml` 和脚本。
- [x] 用户已明确授权以 ZIP Skill 替换并归档 RAR Skill。

## 范围与非目标

- 只调整两个项目级 Skill、README 入口和本计划。
- 不修改产品源码、测试、数据、发布配置或历史 RAR 计划中的既有证据。
- 不创建桌面备份；实际验证只在 `/tmp` 产生临时 ZIP。

## P0 / 兼容性影响

不影响产品 P0 能力。ZIP Skill 沿用原 RAR Skill 的 115 文件源码白名单与保护规则，只替换压缩容器和外部工具依赖。

## 方案与待决问题

- 将 ZIP Skill 的内层内容提升到标准 Skill 根目录，不改变已验证脚本逻辑。
- 将完整 RAR Skill 移入仓库已有 `.agents/archive/` 体系，保留可恢复性。
- 无待决问题。

## 文件变更

- NORMALIZE：`.agents/skills/package-core-source-zip/`
- ARCHIVE：`.agents/skills/package-core-source-rar/` → `.agents/archive/package-core-source-rar/`
- MODIFY：`README.md`
- ADD/ARCHIVE：本计划

## 执行步骤

- [x] 检查 ZIP Skill 结构、内容与调用边界。
- [x] 执行结构校验、脚本语法、dry-run 和临时实际打包前向测试。
- [x] 修正 ZIP Skill 目录层级，更新 README，并归档 RAR Skill。
- [x] 对最终路径重新验证并检查 Git 差异。
- [x] 完成本计划并移入 `openspec/changes/archive/`。

## 验证与回归计划

- WSL：`quick_validate.py`、`bash -n`、脚本 `--dry-run`。
- WSL + Windows PowerShell：在 `/tmp` 创建临时 ZIP，验证 115 个条目、CRC、manifest 和 SHA-256，随后清理。
- Git：检查旧活动 RAR 路径消失、归档路径完整、ZIP 标准路径存在、无产品源码改动。
- 不运行产品 build/test；本变更不修改产品代码。

## 风险与回退

- 目录移动遗漏：对移动前后文件清单和最终 Git diff 复核；RAR 完整保存在 `.agents/archive/`，可直接移回。
- ZIP 行为偏差：最终标准路径重复执行真实临时归档验证。
- 用户既有内容被覆盖：ZIP 未跟踪内容只做目录规范化，不改其已验证语义。

## 进度记录

- 2026-09-12：确认用户给出的字面路径不存在，定位到当前仓库内同名 Skill。
- 2026-09-12：ZIP Skill 结构校验与 115 文件实际打包验证通过；发现唯一阻断是多余嵌套目录。
- 2026-09-12：ZIP Skill 已提升到标准目录，README 入口已替换，RAR Skill 已完整移入 `.agents/archive/package-core-source-rar/`。

## 发现、偏差与决策

- Windows 默认 Python 缺少 PyYAML，`skill-creator` validator 改在项目主执行环境 WSL 中运行并通过。
- ZIP 使用 Windows 内置 PowerShell `Compress-Archive`，已证明无需 WinRAR 即可完成相同源码边界的归档与验证。

## 验证结果

- 前置验证：Skill valid；Bash 语法通过；dry-run 识别 115 个文件；临时 ZIP 包含 115 个文件，CRC 与 manifest 验证通过。
- 最终验证：活动 ZIP Skill 与归档 RAR Skill 均通过 `quick_validate.py`；ZIP 脚本通过 `bash -n` 和 115 文件 dry-run。
- 最终前向测试：从标准路径在 `/tmp` 创建 115 条目的 ZIP，CRC 与 manifest 验证通过，SHA-256 为 `4fef62ff686e265d5676cb3e89d7328c05e56705990b59e871ad4f919334451d`；测试产物已清理。
- 未执行产品 build/test；本变更未修改产品代码。

## 完成摘要

`package-core-source-zip` 已成为当前活动的核心源码备份 Skill，并在不依赖 WinRAR 的条件下保持原 115 文件源码边界和完整性验证。`package-core-source-rar` 已可恢复地归档，不再位于活动 Skill 目录；产品源码与历史 RAR 证据未修改。
