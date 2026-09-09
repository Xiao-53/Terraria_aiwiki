# 精简 Windows UI 常驻说明文案

- 状态：completed
- Phase：无；独立非 Phase 变更
- 执行环境：WSL2 Codex CLI 负责审计、实施和静态检查；Windows .NET 环境负责 Avalonia Release 构建
- 创建日期：2026-09-09
- 最后更新：2026-09-09
- 范围：Windows Avalonia Shell、六个现有主视图与 Windows UI 规范 Skill
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`docs/adr/0001-use-avalonia-client.md`、`docs/adr/0010-use-bounded-local-ai-conversations.md`

## 背景与当前证据

用户提供的 2026-09-09 Windows 截图显示，Shell 品牌名、Chat 页标题、标题下常驻说明和空状态说明在同一视区重复介绍产品与页面用途。源码审计确认 Chat、收藏、Wiki 资料库、数据管理、设置和内部 Wiki 搜索页均在自解释标题下固定放置 `page-subtitle`；Shell 品牌名下还有宣传式副文案。数据管理与设置中还存在少量只复述按钮、选项或内部实现的静态说明。

持续 Presentation 契约要求保留完整用户流程、状态、安全披露、确认影响、首次使用恢复和键盘提示。现有 UI Skill 虽规定说明“不重复标题”，但“页头：标题 + 一行说明”的默认结构仍会诱导每页补写副标题，规则需要收敛。

工作区开始时只有用户既有的未跟踪目录和文档，本次不修改或纳入这些内容。当前产品 `HEAD` 为 `e733b67`。

## 目标与完成定义

- 六个现有主视图不再在自解释页面标题下显示常驻概述文案，Shell 品牌区不再显示宣传式副标题。
- 删除或压缩数据管理、设置中只复述控件、按钮或内部架构的静态说明。
- 保留会影响用户决定或操作的空状态下一步、键盘提示、字段约束、安全/隐私说明、破坏性操作影响、数据恢复和异步状态。
- UI Skill 明确采用“默认不写页头副标题；只有新增且决策相关的信息才保留”的文案标准。
- Windows Release XAML 编译通过；未执行的测试或真实窗口检查明确记录。

## 前置条件

- Phase 00–10 已完成，当前变更不依赖 Phase 11 的公开发行输入。
- 不发起真实模型请求，不需要 API Key、网络付费或 Android 工具链。
- 用户已明确要求直接实施，因此本计划范围视为已批准并进入实施。

## 范围与非目标

范围内：现有 Windows XAML 的常驻说明文案、必要的局部容器简化、项目级 `terraria-ui` Skill。

非目标：改变导航、业务流程、ViewModel 状态语义、Application/Core、SQLite、Viewer 协议、AI 协议、依赖、主题 token、Android 或发布产物；不删除 ToolTip、Automation Name 等可访问性文本。

## P0 / 兼容性影响

不改变 P0 能力、数据格式、SQLite/FTS5、导入导出、搜索、会话、Viewer 或 Runtime。风险仅限静态 Presentation 文案和页头布局；保护措施是保留操作约束与状态文本，并执行 XAML 编译和最终差异审查。

## 方案与待决问题

采用语义审计而非删除所有次级文字：若文本只复述标题、可见控件、选中值或内部实现，则删除；若文本说明下一步、合法输入、安全/隐私、数据影响、恢复方式或实时状态，则保留。数据库文件夹说明压缩为默认行为和可选目录约束，不移除首次使用事实。

无待决产品选择。

## 文件变更

- MODIFY：`src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml`，移除品牌副文案。
- MODIFY：`Chat/ChatView.axaml`、`Favorites/FavoritesView.axaml`、`Library/WikiLibraryView.axaml`、`Management/WikiManagementView.axaml`、`Settings/SettingsView.axaml`、`Wiki/WikiView.axaml`，移除页头重复说明并精简确定为冗余的静态帮助文本。
- MODIFY：`.agents/skills/terraria-ui/SKILL.md` 与 `references/windows-ui-standard.md`，固化文案精简判据与检查项。
- KEEP：ViewModel 状态、空状态下一步、确认影响、输入约束、隐私安全说明、ToolTip 与 Automation 文本。

## 执行步骤

- [x] 精简 Shell 与六个现有主视图；检查保留文本仍覆盖操作与风险信息。
- [x] 更新并校验 `terraria-ui` Skill，使规则能区分冗余说明和必要帮助。
- [x] 运行 diff 检查、Skill 校验和 Windows Release build，并记录真实结果；按用户要求不额外运行测试。
- [x] 完成差异、临时文件与 Git 状态审计；更新计划状态和摘要。

## 验证与回归计划

- WSL：`git diff --check`；搜索六个页面标题父级，确认无默认 `page-subtitle`；运行 Skill `quick_validate.py`。
- Windows：使用 Windows `dotnet.exe` 执行 `Terraria_Wiki.Core.slnx -c Release --no-restore` 构建，验证 Avalonia XAML 编译。
- 测试与人工窗口：用户要求不要为本次文案精简额外测试，因此不运行测试套件、不启动额外窗口验收；不得把未执行项宣称通过。
- Android：不适用，未授权且本变更不外推 Android。

## 风险与回退

- 过度删减可能隐藏数据影响或安全约束：按判据逐条保留确认、恢复、隐私、合法范围和状态文本，并审查最终 diff。
- 删除副标题后页头可能留下无意义间距：通过 Windows XAML 编译和最终差异审查控制。
- 回退只需恢复本变更涉及的 XAML 和 Skill 文档；无数据迁移或不可逆副作用。

## 进度记录

- 2026-09-09：完成截图、架构、Presentation spec、相关 ADR、全部现有 Windows XAML、共享样式与测试审计；计划进入实施。
- 2026-09-09：完成 XAML 文案精简与 Skill 规则更新；`quick_validate.py` 已通过。
- 2026-09-09：Windows Release build 最终为 0 warning / 0 error；按用户要求撤销新增文案测试，不再运行测试套件或窗口验收。
- 2026-09-09：最终 diff/status 审计通过；本次仅保留 9 个已跟踪 UI/Skill 文件和本计划的改动，用户原有未跟踪内容未触碰。

## 发现、偏差与决策

- 决策：不把所有 `muted`/`page-subtitle` 一律删除。Reader 位置、空状态下一步、确认影响、首次使用、安全/隐私、输入约束和异步状态属于必要信息。
- 发现：UI Skill 的排版条目已说说明不能重复标题，但标准页面结构仍默认要求“一行说明”，两处表述需要统一。
- 发现：现有测试直接固定数据库首次运行说明；本变更保留该事实并压缩句子，因此不需要削弱该行为边界。

## 验证结果

- `python3 /home/xiao/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/terraria-ui`：通过，输出 `Skill is valid!`。
- Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，9 个项目，0 warning / 0 error；包含 Avalonia XAML 编译。
- `git diff --check`：通过；已删除的重复文案残留搜索无结果，六个主视图页头均不再带默认说明。
- 测试套件：未运行，遵循用户“不要没事就测试”的要求。
- Light/Dark、默认/最小窗口和宽/窄 Reader 真实窗口：本轮未额外运行，不能宣称通过。

## 完成摘要

已审计 Shell、Chat、收藏、Wiki 资料库、数据管理、设置、内部 Wiki 搜索、Reader 和 Markdown 视图。六个主页面与 Shell 的重复副文案已删除；数据管理和设置中的控件复述、内部架构说明已删除或压缩，操作约束、安全、隐私、确认后果、空状态下一步和实时状态保留。`terraria-ui` Skill 已把文案精简设为默认护栏并通过校验。产品行为、后端、数据和依赖未改变。
