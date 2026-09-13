# 增加 Viewer 鼠标侧键前进导航

- 状态：archived；实现、Windows 回归与真实鼠标侧键验收均已完成
- Phase：无
- 执行环境：WSL2 Codex CLI 负责源码阅读、编辑、静态检查与计划维护；Windows 11 x64 负责 Windows .NET/Avalonia 构建、相关测试、NativeWebView 和真实鼠标验收
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：Windows Local Viewer 的内存导航历史、网页端鼠标侧键映射、现有侧键设置的兼容表达
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/local-wiki-viewer/spec.md`、`openspec/specs/application-use-cases/spec.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`.agents/skills/terraria-ui/SKILL.md`

## 背景与当前证据

用户要求在已有“鼠标侧键后退”基础上增加“前进”，行为接近浏览器。本 change 已按批准范围实施，仍保持单 Viewer、宿主内存历史和现有设置字段兼容。

### 已核验事实

| 位置 | 当前事实 |
|---|---|
| `assets/windows/Viewer/Web/_common/wiki-app-common.js` | `bindNavigation` 在 `mouseup` 中把 `e.button === 3 || e.button === 4` 都发送为 `WikiBackAsync`；当前不存在前进分支。`gotoPage` 在成功渲染后以 `SaveToTabHistory` 保存离开前页面，`backToPage` 只恢复页面和滚动位置，不写普通历史。 |
| `src/Terraria_Wiki.Windows/Viewer/ViewerBridge.cs` | 只有 `_history` 一条宿主内存后退栈；`WikiBackAsync`、`TryTakeBackCommand` 与 `RestoreBackCommand` 只处理 `BackToPage`。`ViewerHistoryEntry` 已包含标题、滚动位置、read token 和导航版本。 |
| `src/Terraria_Wiki.Windows/Viewer/ViewerWebViewSession.cs` | Reader 顶部的后退命令直接调用 `TryTakeBackCommand` 后向 WebView 发送 `BackToPage`；网页侧请求则由 `HandleWebMessageAsync` 接收 Bridge 返回的 command 后再发送。当前 Session 没有前进方法，也没有读取当前页面滚动状态的通用结果 API。 |
| `src/Terraria_Wiki.Windows/MainWindow.cs` | `session.HistoryChanged` 只更新 Reader 的 `CanGoBack`；启动 smoke 也只覆盖一次打开和后退。Viewer 仍只有一个 `ViewerWebViewSession` 和一个 `NativeWebView`。 |
| `src/Terraria_Wiki.Application/Contracts/ApplicationPreferences.cs`、`src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs` | 现有 `IsSideButtonBackEnabled` 是持久设置，Runtime 创建和实时更新 Bridge 时都会使用它；没有独立的前进开关。 |
| `src/Terraria_Wiki.Windows/Presentation/Settings/SettingsView.axaml` | 用户可见文案仍为“鼠标侧键返回”。若同一开关控制前进和后退，该文案将不再完整表达实际行为，但设置字段名和存储格式可保持兼容。 |
| `openspec/specs/windows-presentation/spec.md`、`openspec/specs/local-wiki-viewer/spec.md` | 当前契约明确保留鼠标侧键返回和宿主内存返回栈，但尚未定义前进栈、前进清除规则或按钮 3/4 的方向映射。 |

当前分支为 `master`，HEAD 为 `643b568 refine windows theme search and viewer layout`。工作树已有用户既有修改：RAR Skill 删除、ZIP Skill 新增、README/后续路线图修改及归档计划；本 change 不覆盖、不整理、不纳入这些改动。

### 关键推断与边界

- Windows WebView2/DOM `MouseEvent.button` 的浏览器惯例是按钮 3 为 X1/后退、按钮 4 为 X2/前进；实现前仍须用目标 Windows 实机确认当前鼠标驱动没有重映射。
- 只把按钮 4 改成 `WikiForwardAsync` 不足以实现浏览器语义：后退后必须记录当前页到前进栈，普通新导航必须清空前进栈，且前进后还要能再次后退。
- `PageCommitted` 的可信页面身份、generation、navigationVersion 和 read token 约束必须继续复用；前进功能不能把历史移到 Application/Core、不能保存第二份 Wiki 数据或持久浏览历史。

## 目标与完成定义

1. 鼠标侧键映射明确为：按钮 3 后退、按钮 4 前进；两者不再触发同一个动作。
2. 形成浏览器式双向内存历史：
   - `A → B → C → 后退` 显示 `B`；再次后退显示 `A`；前进显示 `B`，再次前进显示 `C`。
   - 后退到 `B` 后打开新页面 `D`，前进到 `C` 的路径被清空；前进边界操作不改变当前页面。
   - 后退和前进都尽量恢复对应历史项的滚动位置，规范标题和当前 Wiki 不被别名或旧页面身份污染。
3. 现有 Reader 顶部/独立 Wiki 的后退命令与鼠标侧键共用同一历史状态；不会出现“界面后退后鼠标前进失效”或两套栈分叉。
4. 现有设置字段和配置格式保持兼容；默认由同一个开关控制两个鼠标侧键。关闭时两个侧键都不执行页面导航，Reader 可见的后退命令不受此设置错误地禁用。
5. 空后退、空前进、无效当前状态、目标读取/渲染失败、取消、快速重复点击和 session generation 变化均不破坏当前页面及剩余历史。
6. 不增加第二个 Viewer、NativeWebView、数据库表、持久 WikiHistory、Application port、NuGet 依赖或其他平台实现。
7. 定向 Windows tests、全量 .NET tests、Windows Release build 和真实 Windows 侧键验收完成后，才能将计划标为 completed；本计划本身不等于实施授权。

## 前置条件

- Phase 00–11 的 Windows Viewer、唯一共享 Reader、Host/Bridge 协议和现有侧键返回实现继续作为基线；本 change 不属于 Roadmap Phase 12/13。
- 实施前需要用户明确批准本 draft 的范围。仅生成本计划不授权修改产品源码。
- Windows 验证机需要可识别浏览器后退/前进键的物理鼠标或可复现的 Windows 鼠标输入；仅用网页脚本静态检查不能证明硬件映射。
- 自动化测试使用临时合成数据和现有 Viewer fixture，不对用户正式 Wiki 数据库写入或模拟历史迁移。

## 范围与非目标

### 允许修改

- Windows Viewer Bridge/Session 的内存前进栈、导航事务、当前页面状态读取和错误恢复。
- 受控产品资产 `wiki-app-common.js` 的侧键方向映射、前进页面恢复 handler 和普通导航清除前进栈接线。
- 现有设置页的准确文案，以及 Windows Presentation / Local Viewer / Application settings 持续契约中与侧键导航有关的描述。
- Viewer Bridge、Viewer asset/协议静态边界、Windows smoke 和必要的 Presentation 回归测试。

### 非目标

- 不新增可见的 Reader 前进按钮、全局浏览器工具栏或窗口级导航历史；如后续需要可见前进按钮，另建 change。
- 不把历史持久化到 SQLite、Manager.db、文件或 `WikiHistory`；不恢复旧持久历史读写。
- 不改搜索排序、Redirect 规则、Wiki 数据抓取、导入导出、AI、Sidecar、WikiTools、安装器、发布物或 Android。
- 不重构 Viewer transport、NativeWebView、loopback server、页面 HTML/CSS 或其他站点资产。
- 不将 `IsSideButtonBackEnabled` 改名为破坏现有设置 JSON/API 的新字段；只在用户可见文案和契约中准确表达其覆盖后退/前进的语义。

## P0 / 兼容性影响

- P0 受影响能力只有 Local Viewer 的页面导航、内部链接和 Reader 内存返回；SQLite/FTS5、页面内容、资源、数据包、抓取更新和持久数据格式不变。
- 双栈只保存标题、滚动位置、可信 read token 和导航版本等小型宿主内存元数据；不保存 HTML、图片、音频或新的 Wiki 数据副本。
- `BeginSession`、强制重载、切换 Wiki、数据根重组和 Viewer 关闭时必须同时清空后退栈、前进栈和未完成导航事务，旧 generation 不得导航进新 Wiki。
- 普通新导航只有在目标页面成功 DOM commit 后才清空前进栈；读取失败、取消、过期导航不能提前清空可恢复的前进路径。
- 事务移动必须成对发生：后退从后退栈取目标并将当前页放入前进栈；前进从前进栈取目标并将当前页放入后退栈；目标命令失败时恢复原两栈状态。
- 继续通过可信 `PageCommitted` / read token 约束当前页；JS 提供的标题只能作为请求数据，不能取代 Bridge 已确认的规范标题和 Wiki 身份。
- 侧键设置只约束 JS 触发的鼠标导航；现有 Reader 顶部后退命令不应因为设置关闭而变成不可用。设置保存和 Runtime 热应用仍沿用现有 Application/Windows 链路。

## 方案与待决问题

### A. 双栈和导航事务

在 `ViewerBridge` 内把现有单一 `_history` 扩展为后退栈与前进栈，优先复用 `ViewerHistoryEntry`，不引入新的持久模型。

拟定语义：

```text
普通导航成功：      current ──→ back.push(previous), forward.clear()
后退成功：          back.pop(target), forward.push(current)
前进成功：          forward.pop(target), back.push(current)
导航失败/取消：      两个栈恢复到操作前
新 session/rebind：  back.clear(), forward.clear()
```

计划在实现时把“取栈项”和“发给 JS 的 `BackToPage`/`ForwardToPage` command”绑定成可回滚的内部 transition，而不是只靠 command method 推断回滚方向。历史状态的公开事件至少要继续准确驱动现有 `CanGoBack`；是否内部增加 `CanGoForward` 只服务测试/诊断，不能借机创建新的 UI 状态体系。

### B. 获取当前页状态并保持滚动位置

现有后退栈项记录的是“发起下一次导航前”的页面，前进也必须知道离开当前页面时的标题、read token、navigation version 和 `pageYOffset`。拟采用最小协议扩展：

1. 公共 Viewer JS 提供一个宿主可请求的当前导航快照，或在 `WikiBackAsync` / `WikiForwardAsync` 请求中直接携带同一快照。
2. 快照至少含 `readToken`、当前导航版本和非负有限滚动位置；页面标题由 Bridge 依据当前可信 commit 补足/校验，不接受 JS 任意改写规范身份。
3. `ViewerWebViewSession.BackAsync` 在调用 Bridge 取后退项前读取当前快照，使顶栏后退和鼠标后退走同一双栈路径；鼠标前进沿用同一 transition 机制。
4. `BackToPage` 和新增的 `ForwardToPage` 共用同一个页面恢复实现，均等待图片布局稳定后恢复位置，并继续由现有 `navigationVersion` 丢弃过期 DOM 结果。

### C. 普通导航与历史导航的区分

- `gotoPage` / `SaveToTabHistory` 继续表示普通新导航；成功 commit 后追加后退项并清空前进项。
- `backToPage` 不写普通后退历史；前进恢复函数也不写普通后退历史，而由 Bridge 的 transition 在宿主侧移动两栈。
- 若普通导航请求在页面 commit 后又被 `config.refresh()` 或后续增强脚本打断，已提交页面仍是当前页面，栈更新只发生一次，不能因重复 commit 清空/追加多次。
- 快速连续侧键通过已有导航版本配合单一在途 history transition 处理；在前一项尚未完成时，后续同向/反向请求要么排队成明确顺序，要么安全拒绝并保持两栈一致，不能允许两个 JS 恢复任务并行覆盖 DOM。

### D. 设置和用户表达

- 保留 `ApplicationPreferences.IsSideButtonBackEnabled`、设置序列化字段和 Runtime 方法名，避免无意义迁移；将其产品语义定义为“鼠标侧键导航开关”。
- Settings 页可见文本、Automation Name 和持续契约改为表达“鼠标侧键导航（后退/前进）”；`side-button-back-disabled` 旧后退错误码是否继续保留、或统一为兼容的导航禁用错误，需要以现有测试/诊断依赖为依据选择最小兼容方案。
- 不新增独立前进开关，除非实施前用户明确要求分别控制两个方向。

### 需要实施时确认的决策点

1. 实机确认按钮 3/4 是否正好对应当前鼠标的后退/前进；若驱动层已重映射，只记录实际输入行为并调整验收，不扩展为全局鼠标映射配置。
2. 确认是否保留现有“鼠标侧键返回”字段名/错误码作为兼容别名；默认方案是保留存储/API 字段，新增前进错误分支，更新用户可见文案。
3. 确认无前进工具栏按钮的范围；默认不新增按钮，前进入口仅为鼠标侧键，现有 Reader 后退按钮保持原位置和语义。

## 文件变更

### MODIFY candidates

- `assets/windows/Viewer/Web/_common/wiki-app-common.js`：按钮 3/4 方向分流；增加 `ForwardToPage` 恢复 handler；提供当前导航快照；保持普通导航和历史导航不混写，并防止侧键重复请求造成并行恢复。
- `src/Terraria_Wiki.Windows/Viewer/ViewerBridge.cs`：增加前进栈、方向操作、普通导航清除前进栈、current-state 校验、transition 回滚和 session 清理；不改变 Application/Core 边界。
- `src/Terraria_Wiki.Windows/Viewer/ViewerWebViewSession.cs`：让 Reader 后退也携带当前页状态并复用双栈事务；在 Bridge command 完成/失败时提交或回滚；按需要暴露前进状态事件但不创建新 Viewer。
- `src/Terraria_Wiki.Windows/Presentation/Settings/SettingsView.axaml`：把“鼠标侧键返回”改成准确的“鼠标侧键导航（后退/前进）”表达；保留现有绑定、布局和设置格式，是否显示括号以内的说明以实际最小窗口检查为准。
- `src/Terraria_Wiki.Application/Contracts/ApplicationPreferences.cs`、`src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs`、`src/Terraria_Wiki.Windows/Presentation/Settings/SettingsViewModel.cs`：默认 KEEP；只有测试/契约证明需要更准确的内部注释或错误语义时才做最小修改，不改字段序列化名和保存行为。
- `openspec/specs/windows-presentation/spec.md`：补充按钮 3/4 映射、双栈行为、同一设置控制两个侧键和 Reader 后退独立于设置的持续契约。
- `openspec/specs/local-wiki-viewer/spec.md`：补充宿主内存后退/前进栈、普通新导航清空前进栈、失败回滚和 session 清理规则。
- `openspec/specs/application-use-cases/spec.md`：将设置契约中的“侧键返回”改为不改变字段兼容性的“侧键导航”语义；不新增 Application port。
- `assets/windows/README.md`：若公共 JS 改动，重新记录该产品覆盖文件的 SHA-256 与 94/96 文件支持集 hash，并保留来源、固定提交和许可说明。
- `tests/Terraria_Wiki.Windows.Tests/ViewerBridgeTests.cs`：增加双栈顺序、位置、边界、普通导航清空前进、失败回滚、session 清理、设置关闭和无效 current state 回归。
- `tests/Terraria_Wiki.Windows.Tests/ViewerServerTests.cs` 或同一 Windows 测试项目中的最小资产契约测试：断言 JS 明确区分按钮 3/4、注册 `ForwardToPage`、保留 commit/refresh 顺序和必要的当前状态协议。
- `src/Terraria_Wiki.Windows/MainWindow.cs`、`src/Terraria_Wiki.Windows/Presentation/Reader/ReaderPaneViewModel.cs`：默认 KEEP；若 Session 的事务签名需要传递当前快照或状态事件，才做局部接线，不新增可见前进按钮。

### KEEP

`Terraria_Wiki.Core`、Application 查询/阅读用例、SQLite schema/FTS5、持久 `WikiHistory` 兼容表、`ViewerServer`、`viewer-host.html`、`iframe-bridge.js`、四站点正文 HTML/CSS、AI/Sidecar/WikiTools、打包流程和 Android 范围均保持不变，除非实施中出现直接编译约束并在计划中记录理由。

### ADD / REMOVE

- ADD：不预设新 project、依赖或公共 port；只有在真实代码表明独立 `ViewerNavigationTransition` 类型能显著降低回滚风险时，才在 Windows Viewer 内部新增一个窄内部类型。
- REMOVE：无。不得删除现有 `BackToPage`、`WikiBackAsync`、后退设置、持久兼容表或旧 Viewer backend。

## 执行步骤

- [x] 1. 获得用户对 draft 范围的批准；按用户“按照 plan 完成”授权，采用同一设置控制后退/前进且不增加可见前进按钮。
- [x] 2. 完成协议设计核对：Session 通过 `GetCurrentNavigationState` 获取当前快照；历史请求在单一 gate 内执行，Bridge command 完成后再回传原请求，并用带方向的 transition 回滚失败操作。
- [x] 3. 建立可恢复保存点并检查工作树：`3fb91ee chore(plan): approve viewer forward navigation` 只保存本 change 计划；既有 RAR/ZIP/README/路线图改动未被纳入或整理。
- [x] 4. 完成 `ViewerBridge` 双栈/事务与 `ViewerWebViewSession` 统一调用路径；Bridge 回归覆盖双栈移动、边界、无效状态、设置和 session reset。
- [x] 5. 完成公共 `wiki-app-common.js`：按钮 3 后退、按钮 4 前进；新增前进恢复与当前快照 handler；保留既有 commit、图片等待、滚动恢复、Redirect/anchor 和错误处理。
- [x] 6. 完成设置文案与持续契约更新；未改变配置字段名、默认值、序列化格式或 Application/Core API。
- [x] 7. 完成资产静态契约、Viewer 回归和 Windows smoke 接线；smoke 现在覆盖打开、后退、前进，普通新导航仍由 Bridge 清空前进栈。
- [x] 8. 完成静态检查、`git diff --check` 和产品资产 hash 复核；未引入第二 Viewer、依赖、数据库或临时源码。
- [x] 9. Windows Release build、全量 tests 与 NativeWebView smoke 已完成；默认 Release 输出构建为 0 warning / 0 error，隔离客户端 smoke 确认前进后的历史状态和退出清理。
- [x] 10. 用户已完成真实鼠标验收并确认通过；覆盖默认/最小窗口、Light/Dark、普通 Reader 与独立 Wiki Reading、按钮 3/4、滚动位置、空边界、快速点击、设置开关和页面失败恢复的验收项由用户确认。

## 验证与回归计划

### WSL / 静态

- `git diff --check`、工作树和实际文件清单检查；确保只新增/修改本 change 计划或经批准的实现文件。
- `rg` 检查按钮 3/4 只分别绑定 `WikiBackAsync` / `WikiForwardAsync`，`BackToPage` / `ForwardToPage` handler 存在，普通 `SaveToTabHistory` 仍在成功页面 commit 后执行。
- 检查 Windows Presentation 仍只有一个 `<NativeWebView>` 和一个 `new ViewerWebViewSession(`；不出现 SQLite/Core 直接访问、新依赖、第二 Viewer 或 Android/Web/Linux 实现。
- 检查所有新错误、命中区/Automation Name（如文案变化涉及设置控件）和 Light/Dark token 仍符合 UI Skill；无页面 raw hex。
- 重新计算 `assets/windows/README.md` 所记录的 JS SHA-256、94 文件运行资产 hash 和 96 文件支持集 hash。

### Windows .NET / NativeWebView

- 先执行 Windows Release build，要求 0 warning / 0 error。
- 运行 `ViewerBridgeTests`、`ViewerServerTests`、必要的 `PresentationFlowTests` / `PresentationBoundaryTests`，再运行 `dotnet test Terraria_Wiki.Core.slnx -c Release --no-build` 全量回归；计划中记录实际测试数量，不沿用历史数字。
- 通过现有合成数据根和唯一 NativeWebView 运行 A→B→C→后退→前进、滚动位置恢复、后退后打开 D 清空 C、空前进、读取失败、取消、快速请求和退出清理。
- 真实窗口检查至少覆盖 1280×820 与 900×600、Light/Dark、普通 Reader 和独立 Wiki Reading；不增加可见前进按钮，不改变 1100 DIP Reader 分界、单顶栏和唯一 Viewer 布局。
- 使用真实支持侧键的 Windows 鼠标确认按钮 3 后退、按钮 4 前进；记录鼠标型号/驱动若存在映射差异。设置关闭后两个侧键均不导航，设置重新打开后无需重建 Bridge 即恢复。
- Android、其他平台、发布/安装器、联网 Wiki、付费 AI 和用户正式数据库写入均不适用。

## 风险与回退

- **只改 JS 映射但没有前进状态**：会出现前进无目标或前进后无法再后退；以双栈不变量和端到端 A/B/C 场景阻断。
- **当前滚动状态缺失/不可信**：前进能换页但位置回到 0，或 JS 标题污染 Reader 身份；用 read token + 当前 generation 校验，标题从可信 commit 派生，位置只接受有限非负值。
- **Reader 后退与鼠标后退分叉**：顶栏后退可能没有把当前页送入前进栈；让 Session 与网页侧都调用同一个 Bridge transition，并覆盖混合操作场景。
- **并发侧键导致 DOM 竞态**：已有 navigationVersion 只能丢弃过期 commit，不能自动修复两栈移动；增加单一在途 transition 或可观察安全拒绝，并覆盖快速点击测试。
- **普通导航错误地清空前进栈**：只有成功 commit 后才执行 clear；失败、取消和过期结果保留原栈。
- **设置兼容性回归**：不改 `IsSideButtonBackEnabled` 字段名/默认值/JSON 结构；保留后退禁用的兼容错误语义，新增前进分支只在必要处区分。
- **资产 hash/发布漏更新**：任何公共 JS 修改都同步 `assets/windows/README.md` hash，并执行 build/publish 资产复制和真实 Viewer smoke；不修改站点内容资产。
- **回退**：实现与 JS 协议作为同一兼容单元按 change 提交逆序 revert；无数据库迁移，旧配置和持久数据可由旧版本继续读取。工作树有用户既有改动时只回退本 change 的已确认文件，不使用宽范围 reset/checkout。

## 进度记录

- 2026-09-13：读取 INIT、README、Master Architecture、项目基线、计划规范、Windows Presentation/Local Viewer/Application specs、相关 active plans、Windows UI Skill 及其规范。
- 2026-09-13：核对 `wiki-app-common.js`、`ViewerBridge`、`ViewerWebViewSession`、MainWindow、设置映射、Viewer/Presentation 测试和资产 provenance；确认当前按钮 3/4 均后退，宿主只有后退栈。
- 2026-09-13：创建本 draft；未修改产品源码、spec、设置、测试或资产，未运行产品构建/测试，未创建保存点/提交。
- 2026-09-13：用户授权按本计划实施；计划状态转为 `in-progress`。当前为 `master`，既有工作树改动继续排除在本 change 外。
- 2026-09-13：完成 Bridge/Session 双栈事务、JS 按钮 3/4 分流、设置文案、持续契约、Bridge/资产测试和 smoke 接线；全量测试通过 366/366，隔离 Release build 0 warning/0 error。
- 2026-09-13：`node --check`、`git diff --check` 通过；当前 JS SHA-256 为 `bbac8f177255d2bbfc3153d4040d3e886703fa670d6a3992b74363cc6fa50997`，94/96 资产聚合 hash 为 `89abf0d0fdcc9fece423642bd63b6b4dd5d46ec2ada25769af3b075d2d36f4c8` / `f764fa1fd505a8db279a168f5cf639bdae892480e99ec05e0e9412915b412f03`。
- 2026-09-13：用户关闭现有客户端后，以默认 Release 输出完成构建（0 warning / 0 error）、合成 fixture 和 NativeWebView smoke；smoke 成功，包含前进后的剩余历史状态 `1`、资源加载和退出清理。

## 发现、偏差与决策

- 当前 active change `fix-wiki-favorites-viewer-header` 已完成实现但保留用户实机验收状态；本 change 只复用其现有 Viewer 身份与 Reader 接线，不改写其 plan 或已存在的工作树改动。
- 计划默认采用同一现有侧键开关控制后退和前进，保留内部字段名以避免设置迁移；用户可见文案改为准确表达导航语义。
- 计划默认不新增可见前进按钮；用户当前要求的是鼠标侧键前进，后续若需要工具栏前进入口须另行授权。
- 计划默认由 Windows Viewer 宿主继续拥有内存历史；不采用 WebView 原生历史或新的持久历史系统，以保持当前页面渲染和可信 commit 边界。

## 验证结果

- Windows `dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore`：成功，0 warning / 0 error，默认 Release 输出已更新。
- 定向 `ViewerBridgeTests` + `ViewerServerTests`：17/17 通过。
- 全量 `dotnet test Terraria_Wiki.Core.slnx -c Release --no-restore`（隔离 `BaseOutputPath`）：Core 100、Application 59、WikiTools 45、Windows 162，共 366/366 通过。
- 静态：JS `node --check`、`git diff --check`、94/96 文件数量与哈希复核通过；单 NativeWebView/Session 边界未改变。
- NativeWebView smoke：通过（`success: true`）；页面和资源加载正常，前进后的 `remainingViewerHistory: 1`，退出清理完成。
- 未执行：用户正式数据、联网 Wiki 和付费 AI（不属于本 change 的验收范围）。

## 完成摘要

本 change 已实现“鼠标侧键前进”：按钮 3 后退、按钮 4 前进；后退/前进共用宿主双栈历史，普通新导航成功提交后清空前进路径，页面恢复保留滚动位置，现有设置字段保持兼容。自动化、默认 Release 构建、NativeWebView smoke 和用户真实鼠标验收均已通过，计划完成。
