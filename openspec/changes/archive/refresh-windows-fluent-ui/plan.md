# 刷新 Windows Fluent UI

- 状态：archived
- Phase：无；Phase 09 期间的独立 Windows Presentation 视觉变更
- 执行环境：WSL Codex CLI；Windows 11 x64 Release 构建、测试与人工视觉验收
- 创建日期：2026-09-02
- 最后更新：2026-09-02
- 范围：Windows Avalonia Shell、Chat、Favorites、Library、Reader 与共享样式
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户确认现有界面不好看，选择 Fluent 2 + Terraria 品牌方向与平衡紧凑密度。现有 UI 已使用 Avalonia FluentTheme，但 `App.axaml` 集中大量临时样式，各页面散落颜色、间距、圆角和卡片层级；Shell 还有未绑定的折叠导航状态。

## 目标与完成定义

- 形成一致的 Fluent 2 + Terraria 视觉层级，支持明暗主题和高 DPI。
- 保持 Chat、收藏、资料库、Reader、IME、分页和唯一 WebView 行为不变。
- 不增加 UI/图标依赖；Windows Release build/tests 通过并完成目标截图人工验收。

## 前置条件

- `optimize-windows-lifecycle-latency` 完成并独立保存，避免性能与视觉回归混杂。

## 范围与非目标

范围内：Presentation XAML、共享资源、仅服务 UI 的 view-model 死代码和测试。非目标：Core/Application/SQLite、Viewer Wiki HTML、功能新增、Android、MAUI 退休。

## P0 / 兼容性影响

不改变用例调用、数据和命令语义；保持 1100 DIP Reader 切换点、最小宽度、单 WebView 和现有自动化名称。

## 方案与待决问题

- 保留 Avalonia FluentTheme 12.1.1，使用 Compact 密度并建立语义化颜色、字号、间距、圆角和 elevation tokens。
- Shell 使用 72 DIP 导航栏、64 DIP 顶栏、44×44 命中区和 Terraria 绿选中标记。
- Chat 改为连续内容流；Favorites/Library 共用页面头、工具栏、列表和状态；Reader 内联扁平、浮层保留 elevation。
- 所有自定义按钮补齐 hover/pressed/focus/disabled；颜色不是唯一选中信号。
- 移除无绑定的 `IsPaneOpen`、`TogglePaneCommand`、`NavigationPaneWidth`。

## 文件变更

- ADD Presentation 共享 theme/style resources。
- MODIFY `App.axaml` 和 Shell/Chat/Favorites/Library/Reader XAML。
- MODIFY Shell view model 死状态与 Presentation tests；KEEP 业务边界及 Viewer 内容。

## 执行步骤

- [x] 建立 Fluent/Terraria design tokens 与可复用控件样式。
- [x] 重排 Shell、Chat、Favorites、Library 和 Reader 视觉层级。
- [x] 清理只与旧布局相关的死状态并更新边界测试。
- [x] 执行 Windows Release build/tests、尺寸/DPI/明暗主题截图验收。
- [x] 更新持续规范、计划结果并独立提交源码和治理变更。

## 验证与回归计划

- 静态验证 raw hex 只存在于 token dictionary、所有资源可解析、唯一 NativeWebView 和自动化名称保留。
- Windows 覆盖 1280×820、1100 临界点、900×600，100%/150%/225% DPI，以及明暗主题和各主要页面状态。

## 风险与回退

资源键或控件模板可能造成运行时 XAML 错误；先建立 tokens，再逐页替换并每步构建。视觉切片独立提交，可回退而不影响性能变更。

## 进度记录

- 2026-09-02：用户批准 Fluent 2 + Terraria、平衡紧凑密度，并要求性能与 UI 分成两个变更；等待前置性能变更完成。
- 2026-09-02：性能切片独立提交后，完成 semantic tokens、共享控件样式和主要页面视觉重排；在 Windows 225% DPI 下完成宽屏、900×600 最小窗口和深色主题窗口级截图验收。

## 发现、偏差与决策

- 参考 Microsoft Fluent 2、Windows 11 导航与 Microsoft 365 Copilot 对话式 UI；Material 3 仅作比较，不采用。
- 具体采用 Fluent 2 的 design-token 思路、Segoe UI type ramp、克制 elevation 和完整交互状态，以及 Windows NavigationView 的固定顶层导航、24 DIP 常规内容边距和自适应内容模式。官方参考为 `https://fluent2.microsoft.design/design-tokens`、`https://fluent2.microsoft.design/typography` 与 `https://learn.microsoft.com/windows/apps/design/controls/navigationview`。
- 复用现有 Avalonia FluentTheme，并只通过 XAML 资源扩展 Terraria 绿色品牌层；没有增加依赖，也没有改动唯一 NativeWebView 或业务命令。

## 验证结果

- Windows Release XAML/build：成功，0 warning / 0 error。
- Presentation 定向 tests：24/24 通过；覆盖唯一 NativeWebView、自动化名称、响应式 1100 DIP 边界、IME、主题 token、raw hex 边界和 pointer/pressed/focus/disabled 状态。
- 全量 Release tests：Core 91、Application 54、WikiTools 28、Windows 85，共 258/258 通过。
- Windows 225% DPI 应用窗口截图：默认 1280×820 宽屏双栏、900×600 最小窗口单栏和 Dark 宽屏均完整显示；Reader、示例问题、composer、导航选择和明暗语义资源正常。截图仅用于临时验收，不进入仓库。

## 完成摘要

UI 已收敛为 Fluent 2 + Terraria：共享 Light/Dark tokens、Compact 密度、72/64 DIP Shell、连续式 Chat、扁平收藏/资料库列表和仅在覆盖层使用 elevation。旧布局死状态已移除，现有业务、IME、自动化边界和唯一 Viewer 保持不变。
