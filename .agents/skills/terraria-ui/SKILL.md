---
name: terraria-ui
description: "Design, implement, or review new and materially changed Avalonia Windows pages and shared visual components in Terraria AI Wiki. Use for page creation, navigation views, layouts, forms, lists, cards, buttons, empty states, responsive behavior, and visual refreshes under Terraria_Wiki.Windows. Do not use for backend-only work, the legacy MAUI UI, or Android UI."
---

# Terraria Windows UI

为 Terraria AI Wiki 的 Windows Avalonia Presentation 建立一致、克制且可验证的 Fluent Terraria 界面。此 Skill 约束视觉与交互实现，不扩大用户对业务功能、依赖或相邻 Phase 的授权。

## 开始前必须读取

1. 按根 `AGENTS.md` 与 `INIT.md` 读取当前任务所需的架构、spec、进行中 change/plan 和 ADR。
2. 完整读取 [Windows UI 规范](references/windows-ui-standard.md)。
3. 涉及源码时读取：
   - `terraria-wiki-offline/src/Terraria_Wiki.Windows/Presentation/Styles/ThemeResources.axaml`
   - `terraria-wiki-offline/src/Terraria_Wiki.Windows/Presentation/Styles/Controls.axaml`
   - `terraria-wiki-offline/src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml`
   - 与新页面最接近的现有 view、view model、直接调用方和测试。
4. 用户提供截图或参考图时先实际查看，区分内容、布局、样式和功能问题，不凭缩略图猜测。

持续能力契约和当前 change/plan 决定行为；本 Skill 的 reference 决定默认视觉方向；现有 XAML token 与 style 是实现事实来源。发生冲突时不得静默选择，先按职责判断并向用户报告。

## 工作方式

1. 先明确页面任务、主操作、信息层级、空/加载/错误/禁用状态和宽窄布局，再决定控件。
2. 优先复用现有布局模式、语义 token 和共享 style class。只有现有语义确实不匹配且新样式可复用时，才在共享样式中增加命名明确的 class。
3. 保持 Presentation 薄：页面通过现有 view model/Application 用例工作，不直接访问 Core、SQLite、Viewer backend 或平台外部状态。
4. 实现最小范围的 XAML、状态和测试；不借视觉任务重构业务逻辑、添加第二套 UI 框架或升级依赖。
5. 按改动风险验证 XAML 编译、状态行为、明暗主题、键盘/焦点、默认与最小窗口、宽窄布局和真实 Windows 外观。

## 强制视觉护栏

- 页面 XAML 不写 hex 颜色；使用 `DynamicResource` 语义 token。
- 常驻说明必须提供会改变操作或决定的新信息；默认不在自解释的页面/分区标题下添加副标题，不重复可见按钮、选项、状态或内部实现。保留必要的输入约束、安全/隐私、数据影响、恢复方式和异步反馈。
- 不让显著或重复操作无意继承突兀的默认灰色按钮；从现有语义按钮 class 中选择，并实际检查结果。
- 不用大量独立灰卡、圆角药丸或嵌套卡片分割本可连续阅读的内容；列表优先留白、分组和轻量分隔线。
- 可交互控件必须覆盖适用的 `:pointerover`、`:pressed`、`:focus-visible`、`:disabled`，选中控件还要有非纯颜色提示。
- 图标按钮命中区不小于 44×44 DIP，并提供准确的 `AutomationProperties.Name`；颜色不能是唯一状态信号。
- 新页面必须在 Light/Dark 和 900×600 最小窗口下可用；涉及 Reader 时遵守现有 1100 DIP 宽窄分界和唯一 Viewer 契约。
- 使用现有 FluentTheme、字体、图标路径和 Terraria 品牌 token；不新增图标包、字体、主题框架或装饰性图片，除非用户单独批准。
- Windows 规范不得外推为 Android 设计或产品支持。

## 完成门槛

- 检查新增页面是否覆盖规范中的页面、组件、状态、响应式和可访问性清单。
- 为关键视觉边界增加有意义的静态或行为回归，不用只匹配无关文案的脆弱测试充数。
- Windows 专属 XAML 必须用 Windows .NET 环境实际编译；显著页面或布局变化应运行真实窗口检查。无法执行的主题、缩放或目标环境检查必须明确说明。
- 完成前检查 diff、临时截图/脚本、生成物、两个 Git 状态和当前 plan；只提交当前授权范围。
- 最终报告只陈述实际变更、实际验证和仍未验证事项。
