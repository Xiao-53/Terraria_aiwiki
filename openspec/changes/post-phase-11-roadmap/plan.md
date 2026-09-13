# Phase 11 后续产品路线图

- 状态：approved；仅批准作为后续工作登记，不代表批准实施任一候选项、Phase 12、Android 或外部发布
- Phase：无；跨 change 路线图
- 执行环境：WSL2 Codex CLI 维护计划与平台无关检查；Windows 专属工作仍须在对应 Windows 目标环境验证
- 创建日期：2026-09-12
- 最后更新：2026-09-13
- 范围：Phase 11 关闭后的 Windows UI、AI/Token、公开发行遗留与未来 Phase 候选排序
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/windows-productization/spec.md`、`docs/adr/0008-use-github-releases-windows-distribution.md`

## 背景与当前证据

- 用户于 2026-09-12 要求完整关闭旧 AI 会话 Token change、归档 Phase 11，并用新路线图承接后续优化和 Phase 11 未完成内容。
- Phase 11 已形成可运行的本地 Windows 产品化基线和本机 Windows 11 验证，但未达到公开发行门槛；归档不改变这些证据，也不把缺失矩阵标记为通过。
- 全宽本地 Wiki 首版已经实现并通过自动化，但真实窗口反馈否决三层重复抬头；对应旧 change 已归档，后续视觉收敛必须另建新 change。
- AI evidence 与会话 Token 的两轮 change 已归档。后续优化不得改写旧基准或默认获得付费 provider 调用授权。
- 路线图第一项“Wiki 收藏同步 + 页面身份修复 + 单顶栏”已由独立 change 完成、通过 Windows 验证和用户实机验收，并已合并到 `master`；不再是待实施候选。
- 数据根切换已按用户后续决定改为当前 Wiki 单文件 `data.db` 导入/导出，独立 change 已归档；这属于已完成的数据流程修订，不改变后续产品候选顺序。
- Phase 12 仅按真实 Toolset 需求决定是否实施；Phase 13 Android 继续暂缓。

## 目标与完成定义

1. 提供单一、可发现的后续候选队列，并保持 Master Architecture 为 Phase 边界权威来源。
2. 后续工作按用户批准的顺序推进：Wiki 收藏、页面身份与单顶栏修复已完成；下一项是重新定义并单独批准 AI / Token 后续优化；全局 Quick Access 与本地搜索继续后置。
3. 每个候选实施前建立独立 change/plan，重新确认范围、证据、验证环境和外部授权；路线图本身不构成实施授权。
4. Phase 11 遗留项完整保留在本计划末尾，既不遗失，也不继续占用已归档 Phase 的进行中状态。
5. README、项目基线、架构路线和 ADR 链接不再指向已移动的活动计划路径。

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

当前队列如下；已完成项保留证据，未开始项仍必须先单独建立并批准自己的 change/plan：

1. **已完成：Wiki 收藏同步 + 页面身份修复 + 单顶栏**：独立 change 已通过自动化、Windows smoke、窗口检查和用户实机验收，计划已归档。后续只在发现回归时建立新的局部 change。
2. **下一项：AI / Token 后续优化**：基于已归档 evidence 与会话实现重新定义问题、基准和验收集；当前尚未建立实施计划，旧 change 已关闭，不沿用未确认目标或自动发起付费测试。
3. **全局 Quick Access AI/Wiki 悬浮窗口 + 托盘常驻**：使用 Windows 全局快捷键唤起 Topmost 悬浮窗，在 AI 问答与 Wiki 搜索之间切换；Wiki 搜索复用主程序搜索并回到唯一 Viewer，AI 复用现有 AI Runtime；增加关闭到托盘、托盘恢复和真正退出。该项必须在前两项完成且有独立 change/plan 后进入实施。
4. **本地 Wiki 搜索能力优化**：暂时剔除 AI 层，先完善本地 HTML/数据库搜索索引，解决中文短词，加入可人工维护的社区别名/同义词库；按空格分词，多词查询优先 AND，命中不足时再降级 OR；补充章节定位、标题/别名的轻度错字容错、排序、UI 与回归测试。
5. **Windows 公开发行准备**：仅在仓库、版本、许可证/权利和外部写入授权齐备后，从本计划末尾遗留清单选择独立 change。
6. **Phase 12 — Extensions**：只有出现明确 Toolset 需求并获用户授权后，才创建 `openspec/changes/phase-12-extensions/plan.md`。
7. **Phase 13 — Android**：继续暂缓；恢复前必须重新获得用户明确授权，且不得继承 Windows Runtime/打包假设。

当前待决仅限于各独立 change 的具体范围、验收门槛和实施批准；不得在未更新本路线图并获得用户批准的情况下改序或合并实施。

## 文件变更

- **ADD** `openspec/changes/post-phase-11-roadmap/plan.md`。
- **MODIFY** `README.md`、`openspec/project.md` 与 Master Architecture：只更新 Phase 11 归档状态和本路线图入口。
- **KEEP** 所有产品源码、测试、依赖、数据和发布制品。

## 执行步骤

- [x] 关闭旧 Token change，把未执行证据保留为历史边界。
- [x] 归档 Phase 11，并将未完成内容转入本路线图。
- [x] 建立后续候选顺序和授权边界，明确后续工作按 Wiki → AI / Token → Quick Access 推进。
- [x] 为第 1 项建立并批准独立 change/plan。
- [x] 第 1 项完成、验证、用户验收并归档；其结果已合并到 `master`。
- [ ] 为第 2 项建立或批准独立 change/plan；完成前不得实施第 3 项。
- [ ] 第 2 项完成并验证后，为第 3 项建立或批准独立 change/plan。
- [ ] 完成一个后续 change 后，只更新对应候选状态和实际证据，不批量宣称其他候选完成。

## 验证与回归计划

- 检查所有被移动计划的内部链接和 README/架构/ADR 导航。
- `git diff --check`；确认不修改产品源码、测试、依赖、数据或发布物。
- 检查活动 change 目录只保留真实进行中的计划，本路线图不伪装成已授权实现。
- Windows/Android/build/test 不适用：本次只做治理归档和路线登记。

## 风险与回退

- **归档被误读为发布完成**：持续保留 `publicationReady: false` 与缺失证据清单，README/项目基线不得写成公开发行就绪。
- **路线图变成实施授权**：每个候选必须另建 change 并获得明确批准。
- **顺序被绕过或候选被合并**：未完成前置项时不得推进后项，除非用户重新批准并更新路线图。
- **重复治理**：Master Architecture 继续拥有 Phase 目标、依赖和平台边界；本计划只记录可执行候选与移交项。
- **回退**：恢复归档/导航提交即可；没有源码、数据或制品迁移。

## 进度记录

- 2026-09-12：用户要求关闭旧 Token change、归档 Phase 11、创建后续路线图并在末尾保留 Phase 11 未完成内容。
- 2026-09-12：建立本路线图；未授权或实施任何候选项。
- 2026-09-12：用户确认 Wiki 收藏/页面身份/单顶栏为第一项；AI / Token 后续优化顺延，其余候选继续后置。
- 2026-09-13：第一项独立 change 完成、通过 Windows 与用户实机验收并归档；相关分支已合并到 `master`，下一项切换为 AI / Token 后续优化的重新定义阶段。
- 2026-09-13：数据库地址切换改为单文件 `data.db` 导入/导出，相关 change 已归档；不改变 AI / Token → Quick Access → 搜索优化的后续顺序。

## 发现、偏差与决策

- 决策：Phase 11 以用户主动关闭的方式归档；本地产品化成果保留，公开发行门槛不伪称通过。
- 决策：旧 Token change 完整关闭；任何后续 Token/质量工作必须重新定义范围和验收。
- 决策：分叉的旧 Wiki WIP 分支不自动合并；当前主线实现的视觉修订由新 change 决定。
- 决策：Quick Access 复用现有 AI Runtime 与搜索/Viewer 能力；本地 Wiki 搜索优化暂时不包含 AI 层。
- 决策：第一项完成后不自动开始 AI / Token；必须先建立新的独立 change/plan 并重新确认基准、授权和验收门槛。

## 验证结果

- Phase 11、Token 与 RAR 计划状态和归档路径已更新；README、项目基线、Master Architecture、ADR 和持续 spec 的最终链接检查在提交前执行。
- `package-core-source-rar` Skill 通过结构校验、Bash 语法、115 文件 dry-run 和 `/tmp` 实际 WinRAR 创建/完整性测试；临时产物已删除。
- 未运行产品构建、测试、发布或付费请求，因为本次没有产品源码改动。
- 本次路线图更新已检查：路线顺序在目标、方案、执行步骤、风险和进度记录中一致；Phase 11 未完成内容未改动。
- 第一项实际证据保留在已归档计划 `openspec/changes/archive/fix-wiki-favorites-viewer-header/plan.md`；本路线图只登记其完成状态，不复制测试矩阵。

## 完成摘要

本路线图继续作为 Phase 11 关闭后的候选登记入口。第一项 Wiki 收藏/页面身份/单顶栏已完成并归档；下一步须为 AI / Token 后续优化重新建立并批准独立 change/plan。本路线图不替代 Master Architecture，也不自动授权后续实施。

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
