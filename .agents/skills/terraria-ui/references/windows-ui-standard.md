# Terraria AI Wiki — Windows UI 规范

本规范用于 Windows Avalonia Presentation 的新页面、共享组件和显著视觉调整。它把当前已经验证的 Fluent Terraria 方向固化为默认设计语言，不替代产品能力契约，也不适用于 Legacy MAUI 或未来 Android UI。

## 权威关系

出现冲突时按内容职责判断：

1. 用户当前明确要求决定本次产品结果与授权范围。
2. `openspec/specs/windows-presentation/spec.md`、当前 change/plan 和 ADR 决定行为、平台与架构边界。
3. 本规范决定没有被更高层文件指定时的默认视觉与交互表达。
4. `ThemeResources.axaml`、`Controls.axaml` 和现有相邻页面是当前实现事实来源。

颜色数值、圆角和阴影不得复制到本文维护；应直接读取主题资源。规范与源码不一致时，先判断是实现缺陷还是已确认的新方向，再更新唯一负责该事实的文件。

## 设计方向

界面是一款安静、可信、以离线知识和 AI 阅读为核心的 Windows 工具：

- 用 Fluent 2 的层级、排版、空间和状态语言建立熟悉感。
- 用 Terraria 橄榄绿色做克制的品牌强调，不把整个界面染绿，也不模拟游戏 HUD、木纹或像素边框。
- 让内容成为主角。信息关系优先通过留白、对齐和排版表达，边框、底色和阴影只在确有边界时出现。
- 每个页面应有一个清楚的主任务；主操作数量有限，次级与危险操作不与主操作争夺注意力。

参考依据：

- [Fluent 2 Design Tokens](https://fluent2.microsoft.design/design-tokens)：使用语义 token 统一颜色、排版、间距、圆角和主题。
- [Fluent 2 Layout](https://fluent2.microsoft.design/layout)：用间距和邻近关系表达分组与层级，并支持窗口变化时重排。
- [Fluent 2 Typography](https://fluent2.microsoft.design/typography)：使用 Segoe/系统字体和清楚、可扫描的排版层级。
- [Avalonia Styles](https://docs.avaloniaui.net/docs/styling/styles) 与 [Style Classes](https://docs.avaloniaui.net/docs/styling/style-classes)：通过共享 class 表达控件用途。
- [Avalonia Styling Best Practices](https://docs.avaloniaui.net/docs/styling/style-best-practices)：避免局部值覆盖共享样式，保持 class 命名和优先级可维护。

## 设计基础

### 颜色与主题

- 页面只使用 `ThemeResources.axaml` 中的语义资源；Light/Dark 必须同时成立。
- `TerrariaBrandBrush`：选中指示、图标、关键强调和有限的主操作。
- `TerrariaBrandSoftBrush` / `Hover` / `Pressed`：品牌相关的浅层交互状态，不作为大面积页面背景。
- `TerrariaAppBackgroundBrush`：Shell 外层背景。
- `TerrariaNavigationBrush`：导航区域。
- `TerrariaSurfaceBrush`：主要阅读/工作表面。
- `TerrariaSurfaceAltBrush`：需要弱分组的次级表面。
- `TerrariaStrokeBrush`：克制的分隔与边界。
- `TerrariaTextSecondaryBrush`：说明、时间、元信息和非主文本。
- `TerrariaDangerBrush` / `DangerSoft`：删除、清空和错误状态。

品牌色不等于可点击：链接、选中、成功或装饰必须通过文字、图标、位置、边框或形状提供额外语义。不要在页面 XAML 中直接设置 hex，也不要通过任意 `Opacity` 组合出新的非语义颜色。

### 排版

默认字体栈沿用 `Controls.axaml` 的 Segoe UI Variable / Segoe UI / Arial。优先使用现有 class：

| 用途 | 当前基线 | 规则 |
|---|---:|---|
| 页面标题 `page-title` | 28 / 36，SemiBold | 每页一个，短而明确 |
| 页面说明 `page-subtitle` | 14 / 20 | 解释范围或结果，不重复标题 |
| 分区标题 `section-title` | 20 / 28，SemiBold | 只用于主要内容分组 |
| 正文 | 14 / 20 | 长内容保持易读，不随意加粗整段 |
| 标签 `eyebrow` | 12 / 16，SemiBold | 表示内容类别，不承担正文信息 |
| 元信息 `tag` / `muted` | 11–12 为主 | 时间、计数、状态和补充说明 |

中文页面使用自然句式，不用全大写、连续感叹号或营销式长标题制造层级。标题与正文左对齐；居中只用于简短空状态、独立确认或明确的 Hero。

### 间距与尺寸

- 基础间距阶梯：4、8、12、16、20、24、32 DIP；相邻元素优先从该阶梯选择。
- 标准页面边距 24 DIP。通用资料/设置页面内容宽度通常不超过 820 DIP；Chat 主内容通常不超过 800 DIP。
- Shell 当前使用 72 DIP 导航栏与 64 DIP 顶栏；页面不得自行复制或覆盖 Shell 尺寸。
- 默认窗口 1280×820 DIP，最小窗口 900×600 DIP。新页面必须在两者之间自然伸缩，不依赖固定像素位置。
- 组内间距小于组间间距。能用空间表达分组时，不额外添加边框或底色。
- 文本和图标按视觉基线对齐；图标自身包含内边距时允许使用 2/6/10 DIP 等补偿，但不要制造随机间距。

### 圆角、描边和阴影

- 使用 `RadiusSmall`、`RadiusControl`、`RadiusSurface`，不得在页面散落相近但不同的圆角数值。
- 常规边界使用 1 DIP `TerrariaStrokeBrush`；选中/聚焦可以使用品牌描边，但不能导致布局跳动。
- 阴影只用于 composer、窄屏 Reader 覆盖层和真正悬浮的确认/弹层。宽屏 Reader、普通列表和静态内容卡不加 elevation。

## 页面结构

### 标准页面

优先采用以下层级，而不是把每一层包成卡片：

```text
24 DIP 页面边距
├─ 页头：标题 + 一行说明 + 可选页级操作
├─ 可选：分段选择 / 筛选 / 页内提示
├─ 主内容：连续列表、表单或阅读内容
└─ 可选：轻量状态 / 分页操作
```

- 页头通常使用 `Grid ColumnDefinitions="*,Auto"`：左侧标题/说明，右侧最多一组页级操作。
- 页级“刷新”使用现有 `toolbar-action` 和刷新图标，不回退到默认灰色块。
- 页面主操作应靠近它影响的内容；不要为了填满顶部空间添加无功能按钮。
- 空状态只说明发生了什么和用户可做什么。除非产品需求明确要求，不添加预设问题、装饰性 pill 或无效快捷入口。

### 响应式与 Reader

- 宽度不小于当前 1100 DIP 分界时沿用主内容 + 唯一共享 Reader 分栏；两侧遵守现有最小宽度。
- 小于分界时主页面保持完整，Reader 作为右侧覆盖层；必须能由 Esc、关闭按钮或遮罩关闭。
- 页面本身不创建第二个 `NativeWebView`、`ViewerWebViewSession` 或 Reader。
- 窄窗口允许重排、换行、隐藏次要装饰或把横向操作变成紧凑布局，但不能隐藏主功能、依赖水平滚动或让操作重叠内容。

## 组件选择

### 按钮与操作层级

先按语义选择已有 class：

| class | 用途 |
|---|---|
| `primary-action` | 当前范围内唯一、明确的主提交/确认操作 |
| `toolbar-action` | 页头刷新或同等级次级工具操作 |
| `segment-button` | 同一页面的互斥内页/模式选择 |
| `icon-button` | 收藏、关闭、移除等图标操作；命中区至少 44×44 |
| `list-row` | 可打开的连续列表行 |
| `danger-action` | 删除、清空等危险操作；需要时增加二次确认 |
| `wiki-preview-button` | Shell 顶部打开共享 Wiki Reader；不挪作普通按钮 |
| `send-button` / `stop-button` | Chat composer 专用 |

- 一个区域通常只有一个主操作。取消/返回是中性次级，危险操作不使用品牌主按钮伪装。
- 默认 Fluent Button 只用于经实际检查仍与页面协调的偶发中性操作；显著或重复按钮必须选用语义 class，避免重新出现突兀的大灰块。
- 图标使用已有 `Path` / `PathIcon` 语言和当前字体，不用 emoji 充当产品图标，不为一个图标引入依赖。
- 图标 + 文本按钮保持 6–8 DIP 间距，文字描述动作而不是对象状态。

### 列表、卡片与表面

- 页面级列表优先 `list-row`、轻量分隔线、清楚标题和次级元信息；hover 提供浅品牌反馈。
- 不把每条普通记录做成厚重的独立圆角灰卡。只有来源、状态、确认、输入 composer 或真正需要边界的聚合内容使用卡片表面。
- 禁止“页面卡片 → 分区卡片 → 条目卡片”的嵌套 card soup。
- 计数和时间用 `tag`/`muted`，不做成抢眼徽章；状态需要同时有文字。
- 删除入口使用明确图标、Tooltip/Automation Name 和足够命中区；批量或不可逆删除必须确认。

### 表单、搜索与输入

- 标签、输入、帮助和错误形成一个紧密分组；相关字段间距小于不同分区间距。
- Placeholder 只作示例或提示，不替代永久标签。
- 错误留在相关字段或页面状态区，不用阻塞弹窗掩盖可恢复问题。
- 输入焦点、验证错误、禁用和只读必须可区分；不要只靠文本颜色。

### 状态与反馈

每个异步页面至少考虑：初始、加载、内容、空、失败、禁用/不可用和取消/过期结果。需要选择状态的控件还要考虑 selected。

- Loading 保留页面结构，使用轻量进度和动词状态，避免整个页面闪烁。
- Empty 解释为什么为空，并只提供真实可执行的下一步。
- Error 显示安全、可恢复的信息；重试不清除仍可用内容。
- Success 通常通过内容更新和短状态文字反馈，不弹阻塞成功框。
- 旧异步结果不得覆盖新筛选、新页签或新请求。

## 交互与可访问性

- 所有自定义可交互 style 定义适用的 `:pointerover`、`:pressed`、`:focus-visible`、`:disabled`；Toggle/选择控件再定义 checked/selected。
- 键盘可到达主操作，Tab 顺序与视觉顺序一致；焦点框在 Light/Dark 都可见。
- 图标按钮、发送/停止和其他精细操作保持至少 44×44 DIP 点击区域。
- 无可见文字的控件必须设置准确的 `AutomationProperties.Name`；关键导航/自动化入口按现有测试需要设置稳定 ID。
- 不能用颜色单独表达选中、错误、成功或运行状态；同时使用文字、图标、指示条、边框或位置。
- 文本应允许系统缩放和换行。不得为适配截图而裁掉系统字体、固定行高到无法显示中文或依赖精确像素。
- 动效不是必要层级来源；优先短、可中断、无布局跳变的反馈，并尊重系统设置。

## 新页面实施检查表

### 设计前

- [ ] 已明确页面主任务、主操作和非目标。
- [ ] 已找到最接近的现有页面和可复用 view model/Application 用例。
- [ ] 已列出初始、加载、内容、空、错误、禁用和选中状态。
- [ ] 已定义 1280×820、900×600，以及 Reader 宽/窄情况下的布局。

### XAML 与样式

- [ ] 页面没有 raw hex，没有重复主题资源或第二套样式体系。
- [ ] 页头、排版、间距和内容宽度沿用现有基线。
- [ ] 按钮使用正确语义 class；没有未经检查的突兀默认灰块。
- [ ] 连续内容没有被过量卡片、药丸和边框切碎。
- [ ] Light/Dark、hover、pressed、focus-visible、disabled/selected 都成立。
- [ ] 图标、Tooltip、Automation Name 和点击区域符合要求。

### 架构与行为

- [ ] Presentation 只通过现有内部状态/Application 边界工作，不直接访问 Core/SQLite。
- [ ] 没有复制 Viewer、Wiki 数据、搜索或 Runtime 实现。
- [ ] 没有新增依赖、平台或相邻 Phase 功能。
- [ ] 过期异步请求、失败和取消不会破坏当前可用内容。

### 验证与交付

- [ ] Windows Release XAML 编译 0 warning / 0 error。
- [ ] 相关行为/边界测试通过，且断言验证真实不变量。
- [ ] 显著 UI 变化已检查 Light/Dark、默认/最小窗口、宽/窄布局；未检查项已明确记录。
- [ ] 临时截图、脚本、日志和进程已清理；Git diff/status 只包含当前范围。

## 明确避免

- 页面内散落 hex、固定像素坐标或多个近似颜色。
- 大面积纯灰按钮、无层级的同权操作、只有 hover 才看得出可点击。
- 每项一个圆角阴影卡、嵌套卡、过量 badge/pill。
- 仅为“更好看”加入新的 UI 库、字体、图标包或图片资产。
- 用装饰覆盖加载慢、点击无响应、错误恢复或异步竞态。
- 为 Windows 页面预建 Android、Web、Linux 或第二套 Viewer 抽象。
