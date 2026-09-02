# 新增 Terraria Windows UI 规范 Skill

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI；不需要 Windows/Android 产品运行验证
- 创建日期：2026-09-02
- 最后更新：2026-09-02
- 范围：项目级 Codex Skill、Windows Avalonia UI 设计规范、发现与触发规则
- 关联架构/规范/ADR：`AGENTS.md`、`openspec/specs/windows-presentation/spec.md`、`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户要求建立可在后续新增功能和新页面时复用的 UI 规范或 Skill。当前 Windows Presentation 已有 `ThemeResources.axaml`、`Controls.axaml` 和持续 Presentation 能力契约，但没有活动的项目级 UI Skill，也没有集中说明页面层级、组件选择、卡片克制、响应式、交互状态和验收清单的视觉规范。

官方 Fluent 2 指南以语义 design tokens、排版层级和空间关系保证跨主题一致性；Avalonia 官方指南支持用 style classes 与 `:pointerover`、`:pressed`、`:focus-visible`、`:disabled` 等伪类共享控件状态。当前源码已采用同一方向，本变更把已经验证的做法固化为项目级执行规范，不引入新 UI 框架。

## 目标与完成定义

- 创建可自动发现的 `.agents/skills/terraria-ui/`，匹配 Windows Avalonia 新页面、新组件和显著视觉重做任务。
- Skill 强制读取一份集中 UI 规范，优先复用现有语义 token、控件 class 和相邻页面模式。
- 规范覆盖视觉方向、页面结构、排版、间距、颜色、按钮层级、列表/卡片、响应式、可访问性、状态与验证清单。
- `AGENTS.md` 明确后续新建或显著重做 Windows 页面必须使用该 Skill；README 提供人类入口。
- Skill validator、Markdown/路径检查和 Git 差异检查通过；不修改业务源码。

## 前置条件

- 当前产品目标为 Windows Avalonia，Android 尚未进入实现 Phase。
- Windows Presentation 持续契约和现有样式资源是行为与实现事实来源。
- 用户已明确授权创建 UI 规范或 Skill，并要求后续页面使用。

## 范围与非目标

范围内：项目级 Skill、Skill UI 元数据、详细 Windows UI 参考规范、根工作规则和 README 导航。

非目标：修改当前应用页面、重做现有视觉、添加依赖、改变 Presentation 行为契约、Legacy MAUI UI、Android UI、业务逻辑或数据。

## P0 / 兼容性影响

不适用：本变更只约束未来 Windows UI 工作流，不读写 Wiki 数据，不改变 SQLite/FTS5、Viewer、AI、Application/Core 或产品 Runtime。Skill 不构成未来业务源码修改授权。

## 方案与待决问题

- 使用一个精简 `SKILL.md` 负责触发、权威来源、工作流和边界；详细视觉规范放入 `references/windows-ui-standard.md`，避免入口文件过长。
- 添加 `agents/openai.yaml` 供 Skill UI 展示，保留隐式调用，使“新增页面/组件/视觉重做”可自动匹配，也允许 `$terraria-ui` 显式调用。
- 规范采用 Fluent 2 作为交互与层级语法、Terraria 绿色作为克制的品牌层；具体颜色继续以源码 `ThemeResources.axaml` 为唯一事实源，不在文档复制易漂移的 hex 值。
- 待决问题：无；若未来进入 Android Phase，另建目标平台 UI 规范，不把 Windows 尺寸和宿主模式直接外推。

## 文件变更

- ADD `.agents/skills/terraria-ui/SKILL.md`：触发范围、读取顺序、实施与验证流程。
- ADD `.agents/skills/terraria-ui/references/windows-ui-standard.md`：Windows UI 详细规范与检查表。
- ADD `.agents/skills/terraria-ui/agents/openai.yaml`：Skill 显示名称、默认提示和隐式调用政策。
- MODIFY `AGENTS.md`：规定新建/显著重做 Windows Avalonia 页面必须使用该 Skill，并说明权威关系和平台边界。
- MODIFY `README.md`：增加 UI 规范/Skill 导航。
- KEEP `terraria-wiki-offline/`、Presentation spec、架构、ADR 和所有产品源码。

## 执行步骤

- [x] 使用 `skill-creator` 初始化最小项目级 Skill 结构。
- [x] 编写 Skill 与 Windows UI 规范，更新根触发规则和 README 导航。
- [x] 运行 Skill validator、内容/路径/差异检查，确认业务源码仓库保持干净。
- [x] 记录验证结果，归档本 change 并保存独立治理提交。

## 验证与回归计划

- 运行 `skill-creator/scripts/quick_validate.py .agents/skills/terraria-ui`。
- 检查 YAML 可解析、默认提示显式包含 `$terraria-ui`、隐式调用保持开启。
- 检查规范引用的本地文件均存在，且没有复制页面 hex、引入新依赖或扩大到 Android/Legacy MAUI。
- `git diff --check`；列出本次文件；`git -C terraria-wiki-offline status --short` 必须无输出。
- Windows/Android 运行验证不适用：本变更没有修改产品代码或运行时资源。

## 风险与回退

- 规则过宽可能误触发普通后端任务：description 限定 Windows Avalonia 页面、组件和视觉任务，并明确排除后端、Legacy MAUI 和 Android。
- 规范与 Presentation 契约漂移：行为以持续 spec/当前 Phase plan 为权威，颜色和控件实现以现有 XAML 资源为事实源；Skill 只负责执行方法和视觉一致性。
- 回退：删除新增 Skill 目录并恢复 `AGENTS.md`、README；不涉及数据或业务源码回退。

## 进度记录

- 2026-09-02：读取项目治理、现有 Presentation 契约和样式资源；确认当前活动 `.agents/skills/` 为空，归档 Skill 不在发现范围。
- 2026-09-02：读取 `skill-creator` 与 `openai.yaml` 规范，核对 Fluent 2 design tokens/layout/typography 和 Avalonia styles/style classes/pseudo-classes 官方指南，确定 Skill + 独立 reference 的最小结构。
- 2026-09-02：创建 `terraria-ui` Skill、详细 Windows UI reference 和 UI 元数据；根规则要求未来新增/显著重做 Windows Avalonia 页面使用该 Skill，README 增加人类入口。旧 `terraria-dev-workflow` 仍只位于 `.agents/archive/`，没有进入活动发现目录。

## 发现、偏差与决策

- 决定：规范不复制 `ThemeResources.axaml` 的颜色数值，防止主题调整后文档与代码双重维护。
- 决定：Skill 自动匹配未来 Windows UI 任务；根 `AGENTS.md` 再提供显式触发护栏，确保新会话不会只依赖用户记住 `$terraria-ui`。

## 验证结果

- `python3 /home/xiao/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/terraria-ui`：通过，输出 `Skill is valid!`。
- 活动 Skill 文件清单只有 `terraria-ui/SKILL.md`、`agents/openai.yaml` 和 `references/windows-ui-standard.md`；元数据显式启用隐式调用，默认提示包含 `$terraria-ui`。
- 未发现 TODO/占位内容；Skill 引用的规范、主题资源、共享样式、Shell 和治理文件均存在。
- `git diff --check` 通过；`git -C terraria-wiki-offline status --short` 无输出，业务源码未修改。
- Windows/Android 运行验证未执行且不适用：本变更只影响 Codex 项目级 Skill 与治理文档。

## 完成摘要

已建立可自动匹配、也可通过 `$terraria-ui` 显式调用的项目级 Windows UI Skill。详细规范固定 Fluent Terraria 视觉方向、语义 token、页面结构、排版间距、按钮层级、连续列表/卡片边界、响应式、状态、可访问性和交付检查表；根规则确保后续新增或显著重做 Windows Avalonia 页面使用它。没有修改业务源码、依赖、产品行为或其他平台范围。
