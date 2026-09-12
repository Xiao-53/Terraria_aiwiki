# Phase 11 后续产品路线图

- 状态：approved；仅批准作为后续工作登记，不代表批准实施任一候选项、Phase 12、Android 或外部发布
- Phase：无；跨 change 路线图
- 执行环境：WSL2 Codex CLI 维护计划与平台无关检查；Windows 专属工作仍须在对应 Windows 目标环境验证
- 创建日期：2026-09-12
- 最后更新：2026-09-12
- 范围：Phase 11 关闭后的 Windows UI、AI/Token、公开发行遗留与未来 Phase 候选排序
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/windows-productization/spec.md`、`docs/adr/0008-use-github-releases-windows-distribution.md`

## 背景与当前证据

- 用户于 2026-09-12 要求完整关闭旧 AI 会话 Token change、归档 Phase 11，并用新路线图承接后续优化和 Phase 11 未完成内容。
- Phase 11 已形成可运行的本地 Windows 产品化基线和本机 Windows 11 验证，但未达到公开发行门槛；归档不改变这些证据，也不把缺失矩阵标记为通过。
- 全宽本地 Wiki 首版已经实现并通过自动化，但真实窗口反馈否决三层重复抬头；对应旧 change 已归档，后续视觉收敛必须另建新 change。
- AI evidence 与会话 Token 的两轮 change 已归档。后续优化不得改写旧基准或默认获得付费 provider 调用授权。
- Phase 12 仅按真实 Toolset 需求决定是否实施；Phase 13 Android 继续暂缓。

## 目标与完成定义

1. 提供单一、可发现的后续候选队列，并保持 Master Architecture 为 Phase 边界权威来源。
2. 每个候选实施前建立独立 change/plan，重新确认范围、证据、验证环境和外部授权。
3. Phase 11 遗留项完整保留在本计划末尾，既不遗失，也不继续占用已归档 Phase 的进行中状态。
4. README、项目基线、架构路线和 ADR 链接不再指向已移动的活动计划路径。

## 前置条件

- 本路线图本身不授权源码修改、付费模型请求、GitHub 写入、发布、Android 工具链或 Phase 12 实施。
- 开始任一候选前先确认当前 `master`、工作树和恢复点，并读取对应持续 spec、ADR、归档计划、真实源码与测试。
- 外部仓库、许可证、版本、凭据、付费请求和目标平台环境仍需在对应 change 中单独关闭。

## 范围与非目标

范围内：记录候选顺序、承接关系、阻塞输入和 Phase 11 遗留项；随用户决定更新状态。

非目标：不在本计划直接实施 Wiki UI、AI、发布、Phase 12 Toolset 或 Android；不复制持续 spec、release runbook 或归档计划全文；不建立第二套 Master Architecture。

## P0 / 兼容性影响

本计划只改变治理状态和导航，不改变任何产品行为、SQLite/FTS5、数据包、Viewer、AI Runtime、会话格式、安装包或用户数据。未来 change 仍必须遵守现有 P0 回归边界。

## 方案与待决问题

建议按用户价值和前置依赖选择下一项，而不是把所有候选合并为一个大 change：

1. **Windows Wiki 顶栏与信息层级**：承接首次真实窗口反馈，目标是消除重复原生抬头并保留唯一 Viewer、搜索、返回与收藏行为。
2. **AI / Token 后续优化**：基于已归档 evidence 与会话实现定义新的问题、基准和验收集；旧 change 已关闭，不沿用未确认目标或自动发起付费测试。
3. **Windows 公开发行准备**：仅在仓库、版本、许可证/权利和外部写入授权齐备后，从本计划末尾遗留清单选择独立 change。
4. **Phase 12 — Extensions**：只有出现明确 Toolset 需求并获用户授权后，才创建 `openspec/changes/phase-12-extensions/plan.md`。
5. **Phase 13 — Android**：继续暂缓；恢复前必须重新获得用户明确授权，且不得继承 Windows Runtime/打包假设。

待决：用户尚未指定上述候选的实施顺序、下一项范围或验收门槛。

## 文件变更

- **ADD** `openspec/changes/post-phase-11-roadmap/plan.md`。
- **MODIFY** `README.md`、`openspec/project.md` 与 Master Architecture：只更新 Phase 11 归档状态和本路线图入口。
- **KEEP** 所有产品源码、测试、依赖、数据和发布制品。

## 执行步骤

- [x] 关闭旧 Token change，把未执行证据保留为历史边界。
- [x] 归档 Phase 11，并将未完成内容转入本路线图。
- [x] 建立后续候选顺序和授权边界。
- [ ] 用户选择下一项后，为该项建立或批准独立 change/plan；不得直接从本路线图实施。
- [ ] 完成一个后续 change 后，只更新对应候选状态和实际证据，不批量宣称其他候选完成。

## 验证与回归计划

- 检查所有被移动计划的内部链接和 README/架构/ADR 导航。
- `git diff --check`；确认不修改产品源码、测试、依赖、数据或发布物。
- 检查活动 change 目录只保留真实进行中的计划，本路线图不伪装成已授权实现。
- Windows/Android/build/test 不适用：本次只做治理归档和路线登记。

## 风险与回退

- **归档被误读为发布完成**：持续保留 `publicationReady: false` 与缺失证据清单，README/项目基线不得写成公开发行就绪。
- **路线图变成实施授权**：每个候选必须另建 change 并获得明确批准。
- **重复治理**：Master Architecture 继续拥有 Phase 目标、依赖和平台边界；本计划只记录可执行候选与移交项。
- **回退**：恢复归档/导航提交即可；没有源码、数据或制品迁移。

## 进度记录

- 2026-09-12：用户要求关闭旧 Token change、归档 Phase 11、创建后续路线图并在末尾保留 Phase 11 未完成内容。
- 2026-09-12：建立本路线图；未授权或实施任何候选项。

## 发现、偏差与决策

- 决策：Phase 11 以用户主动关闭的方式归档；本地产品化成果保留，公开发行门槛不伪称通过。
- 决策：旧 Token change 完整关闭；任何后续 Token/质量工作必须重新定义范围和验收。
- 决策：分叉的旧 Wiki WIP 分支不自动合并；当前主线实现的视觉修订由新 change 决定。

## 验证结果

- Phase 11、Token 与 RAR 计划状态和归档路径已更新；README、项目基线、Master Architecture、ADR 和持续 spec 的最终链接检查在提交前执行。
- `package-core-source-rar` Skill 通过结构校验、Bash 语法、115 文件 dry-run 和 `/tmp` 实际 WinRAR 创建/完整性测试；临时产物已删除。
- 未运行产品构建、测试、发布或付费请求，因为本次没有产品源码改动。

## 完成摘要

本路线图已建立为 Phase 11 关闭后的候选登记入口。它不替代 Master Architecture，也不授权执行候选项；下一步由用户选择独立 change。

## Phase 11 未完成内容

以下事项在 Phase 11 归档时仍未完成，未来必须按真实需要拆入独立 change，不得解释为已有证据：

1. 提供并确认公开 GitHub 仓库 `owner/repo` 与 remote，以及任何 GitHub 外部写入授权。
2. 确认首个正式版本、SemVer/tag 规则和不可覆写的发行资产策略。
3. 确认项目软件许可证精确文本和适用范围；完成 Viewer/Wiki/游戏素材、Calamity 内容、项目图标及生产依赖的逐项许可与署名审计。
4. 在没有 .NET、Python、uv、pip 或开发工具的干净 Windows 11 x64 环境完成安装、默认数据根、Viewer/Search、内置 AI fake、WebView2 已有/缺失、credential failure、Runtime 损坏、repair、卸载与重装矩阵。
5. 获得授权后，在目标 GitHub 仓库独立 RC/prerelease channel 完成真实 N→N+1、index/full/hash、断网、限流、取消、损坏下载、重启及 portable update 矩阵。
6. 仅在不增加专属代码、依赖或维护成本时，用同一制品完成 Windows 10 22H2 x64 可选矩阵；否则继续保持 Windows 11 only。
7. 完成公开候选发布审计：unsigned/SmartScreen 告知、SHA-256、许可/notices、隐私/support、安装/更新/修复/卸载说明；真实 provider 验收仍需单独付费授权。
8. 在以上门槛关闭前保持本地候选 `publicationReady: false`；不得上传或发布 GitHub Release，也不得宣称 Windows 成品已达到公开发行门槛。
