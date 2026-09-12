# 预规划：修复本地 Wiki 收藏链路并收敛 Wiki 三层抬头

- 状态：pre-plan / 待 Astra 基于完整仓库继续核验、优化并生成正式实施 plan
- 创建日期：2026-09-12
- 依据源码：`terraria-aiwiki-core-source-20260912-v2.zip`
- 视觉依据：`Snipaste_2026-09-12_17-31-17.png`
- 历史依据：已归档的“新增参考图风格的全宽本地 Wiki 搜索页面” plan，仅继承其中已经确认的产品边界、Windows 实机验收标准与唯一 Viewer 约束；不继续维护旧 plan
- 本轮性质：只做预规划，不修改源码、不提交、不合并、不 push
- 交接目标：Astra 应先复核完整仓库、测试与 Viewer Web 端协议，再把本预规划收敛成可直接实施的正式 plan

## 1. 本轮只解决的三个结果

### P0-A：修复 Viewer ↔ Reader 当前页面身份不同步

必须保证 Reader 收藏按钮永远对应 **Viewer 当前实际显示的页面**，而不是某次导航请求的旧标题。

覆盖：

- 从搜索结果打开页面。
- 从收藏打开页面。
- 从资料库打开页面。
- Wiki 正文内部点击内部链接。
- Viewer 返回上一页。
- 打开首页。
- Redirect / 别名页跳转到规范标题。
- 快速连续导航、失败导航和取消导航。
- 活动 Wiki 切换后不得保留旧 Wiki 的页面身份或收藏状态。

### P0-B：“收藏”页面聚合展示所有本地 Wiki 的收藏

收藏页不再只展示当前活动 Wiki 的 `Content.db`。

完成后：

- 所有已存在本地数据的 Wiki 收藏都在同一个“收藏”页面可见。
- 收藏项必须携带 Wiki 身份，至少包含 `WikiId + WikiDisplayTitle + PageTitle + FavoritedAt`。
- 不同 Wiki 中同名页面必须视为不同收藏。
- 可以直接打开任意 Wiki 的收藏项。
- 可以直接取消任意 Wiki 的收藏项，不允许为了取消一个非活动 Wiki 收藏而偷偷切换整个活动 Wiki。
- 现有各 `Content.db` 中已经保存的收藏不能丢失。

### P0-C：解决截图中的三个原生抬头叠加问题

当前实机截图中依次出现：

1. Shell 顶栏：`Terraria AI Wiki`
2. 独立 `WikiToolbar`：`Terraria Wiki / 本地搜索`
3. Reader 48 DIP 标题栏：返回 + `Terraria Wiki / Terraria Wiki` + 收藏

最终要求：

- 独立 Wiki 模式只保留 **一层应用原生顶栏**。
- 现有 Shell 顶栏继续保留产品标题 `Terraria AI Wiki`。
- Wiki 搜索、主页、返回搜索、Viewer 返回、收藏等上下文操作按状态合并到这一个 Shell 顶栏。
- 独立 Wiki 首页/正文不再显示 Reader 自带 48 DIP 标题栏。
- 网页正文内部的 Terraria Logo、横幅和页面内容不是“原生抬头”，继续保留。
- Reader 底部加载/成功/错误状态条继续保留。

---

## 2. 最新源码已经确认的直接证据

### 2.1 Reader 当前页面身份目前不是由 Viewer 实际页面驱动

`ReaderPaneViewModel` 当前保存：

```csharp
private string? _currentPageTitle;
```

`OpenAsync()` 的流程是：

```text
发出 _openPage(request.Target)
→ 成功后直接把 _currentPageTitle = request.Title
→ RefreshFavoriteAsync()
```

因此它保存的是“调用者请求标题”，不是 Viewer 最终规范页面身份。

当前 `BackAsync()` 更明显：

```text
执行 Viewer Back
→ _currentPageTitle = null
→ IsWikiFavorite = false
```

所以 Viewer 返回成功后，画面已经是上一页，但 Reader 会直接失去当前页面身份。

这两个行为都必须移除。

### 2.2 ViewerBridge 已经能拿到规范页标题

`ViewerBridge.PageRedirectAsync` 中：

```text
_application.Query.ReadAsync(data)
→ read.Title
→ ViewerPage(read.Title, read.Content, ...)
```

这说明 C# 侧已经存在一个很接近“权威当前页面”的规范标题来源。

但当前：

- `ViewerBridge` 没有向 Reader 发布“页面已经切换”的事件。
- `ViewerWebViewSession` 只暴露 Ready/Status/ProtocolError 等状态。
- Reader 只能靠外部 `OpenAsync(request)` 猜当前页。

Astra 必须先检查完整 Viewer HTML/JS 协议，确认 `PageRedirectAsync` 是否与“页面实际 commit”严格一一对应。

如果是，可以直接在 C# bridge/session 增加权威页面变更事件。

如果不是，应增加最小的 Viewer `PageChanged/PageCommitted` 协议消息，由实际页面提交完成后回传规范标题。

**不能继续让 Reader 根据请求标题推断当前页面。**

### 2.3 当前收藏查询只读取活动 Wiki

`WikiLibraryUseCases.GetFavoritesAsync()`：

```text
_state.RequireActiveBook()
→ _state.ContentDatabase
→ 当前活动 Content.db 的 WikiFavorite
```

`IsFavoriteAsync/AddFavoriteAsync/RemoveFavoriteAsync` 同样绑定当前 `_state.ContentDatabase`。

因此“收藏页只显示当前 Wiki”是现有 Application 边界本身决定的，不只是 UI 漏显示。

### 2.4 当前收藏 DTO 没有 Wiki 身份

目前：

```csharp
public sealed record WikiFavoriteItem(
    string WikiTitle,
    DateTime FavoritedAt);
```

`FavoritesViewModel` 的 Wiki 收藏项也只把 `string title` 传给：

```text
Open
Remove
```

因此它无法区分：

```text
Terraria Wiki / Guide
Calamity Wiki / Guide
```

也无法在点击非活动 Wiki 收藏后知道应该切到哪个数据源。

### 2.5 当前三个抬头来自真实的三层 Native UI

当前 `ShellView.axaml`：

```text
Shell 顶栏
└─ Terraria AI Wiki

WorkspaceGrid Row 0
└─ WikiToolbar
   ├─ 主页
   ├─ 返回搜索
   ├─ Terraria Wiki
   ├─ 本地搜索
   └─ 搜索框

ReaderView
└─ 固定 48 DIP Reader Header
   ├─ Viewer Back
   ├─ Reader.Title / Reader.Location
   ├─ Favorite
   └─ Close
```

这与用户实机截图完全一致。

---

## 3. 核心设计原则

1. **Viewer 是“当前实际页面”的唯一事实来源。**
2. Reader 负责展示页面状态和执行收藏，不再自己猜页面身份。
3. 页面身份至少应是：

```text
WikiId
CanonicalPageTitle
可选 Anchor
```

4. 收藏身份必须是 `(WikiId, PageTitle)`，不能继续只有标题。
5. “跨 Wiki 收藏展示”不应通过反复切换全局 ActiveWiki 来实现。
6. 优先保留现有各 Wiki `Content.db` 中的 `WikiFavorite`，避免为了 UI 聚合做高风险数据搬迁。
7. 不创建第二个 Viewer / NativeWebView / Viewer session。
8. 不重写现有搜索逻辑、FTS、Wiki 下载、数据包和 Agent。
9. 三抬头问题只收敛独立 Wiki 模式；Chat/收藏/资料库/数据管理来源打开普通 Reader 时，其 Reader header 行为保持不变。
10. 任何跨 Wiki 打开动作必须走现有“活动 Wiki 切换 + Runtime/Viewer 刷新”完整链路，不能只改 `_state.ActiveBook` 或只换数据库。

---

## 4. 推荐方案 A：建立统一的 Viewer 当前页面身份事件

这是收藏 Bug 的第一优先级，应先于全局收藏 UI 实施。

### 4.1 新增显式页面身份模型

建议在 Windows Presentation/Viewer 边界定义类似：

```csharp
internal sealed record ViewerPageIdentity(
    int WikiId,
    string CanonicalTitle,
    string? Anchor);
```

具体命名由 Astra 根据现有规范调整。

不要把“显示标题”“请求标题”“规范标题”继续混成一个 string。

### 4.2 建立 Viewer → Reader 单向同步

目标链路：

```text
Viewer 实际完成页面导航
        ↓
得到 canonical title
        ↓
ViewerPageChanged / PageCommitted
        ↓
MainWindow / Shell 接线
        ↓
Reader.ApplyCurrentPageAsync(identity)
        ↓
更新 Title / Location / _currentPageIdentity
        ↓
按 (WikiId, CanonicalTitle) 刷新收藏状态
```

### 4.3 Reader.OpenAsync 只负责“发起导航”

`Reader.OpenAsync()` 应保留：

```text
busy
loading status
_openPage(...)
异常/取消处理
```

但不应在 `_openPage()` 返回后直接：

```csharp
_currentPageTitle = request.Title;
```

最终身份必须等 Viewer 的权威页面事件。

### 4.4 Reader.BackAsync 不再清空身份

当前：

```csharp
_currentPageTitle = null;
IsWikiFavorite = false;
```

必须删除。

Back 的目标语义应变成：

```text
请求 Viewer Back
→ Viewer 真正回到上一页
→ ViewerPageChanged 回传上一页 identity
→ Reader 自动刷新上一页收藏状态
```

如果 Viewer history 已空或返回失败，原当前页面身份必须保持。

### 4.5 Redirect 统一到规范标题

例如：

```text
用户请求别名 A
→ Query.ReadAsync()
→ 实际 read.Title = B
→ Viewer 显示 B
→ Reader 收藏 B
```

收藏列表中不应因为从不同入口进入而出现 A/B 两个重复身份。

Astra 需要验证现有 Redirect 行为和 history 保存的 title 是请求名还是规范名，并统一契约。

### 4.6 防止异步旧事件覆盖新页面

Astra 应检查以下竞态：

```text
打开 A
快速打开 B
A 的异步完成晚于 B
```

页面事件必须只代表 Viewer 最终已经 commit 的页面，或者带导航序号/请求 ID 来丢弃过期事件。

不能仅在 `PageRedirectAsync` 一返回就无条件改 Reader，除非已证明该方法不会被预取且返回顺序就是实际页面提交顺序。

---

## 5. 推荐方案 B：收藏页跨所有 Wiki 聚合，但不搬迁现有收藏数据

### 5.1 默认不迁移 `WikiFavorite` 到 Manager.db

预规划首选：

```text
每个 Wiki Content.db
└─ WikiFavorite 保持原样

新增 Application 聚合读取边界
└─ 从所有“已有本地 Content.db”的 Wiki 读取收藏
   → 合并
   → 按 FavoritedAt DESC 排序
   → 返回带 Wiki 身份的 DTO
```

理由：

- 不丢现有收藏。
- 不改变 Content schema。
- 不改变旧数据包兼容性。
- 不需要一次性迁移。
- 回退简单。
- 当前本地 Wiki 数量很少，收藏数据量也很小，聚合读取足够轻。

### 5.2 新增跨 Wiki 收藏 DTO，不强行破坏旧接口

建议保留当前活动 Wiki 用的接口，以减少回归。

新增类似：

```csharp
public sealed record GlobalWikiFavoriteItem(
    int WikiId,
    string WikiDisplayTitle,
    string PageTitle,
    DateTime FavoritedAt);
```

正式命名由 Astra 决定。

优先新增：

```text
GetAllFavoritesAsync(...)
IsFavoriteAsync(wikiId, pageTitle)
SetFavoriteAsync(wikiId, pageTitle, favorite)
```

而不是直接把现有所有 `WikiFavoriteItem` API 改签名，除非完整仓库证明改动面很小。

### 5.3 聚合读取不能切换 ActiveWiki

禁止这种实现：

```text
for 每个 Wiki:
  ActiveWiki.SwitchAsync()
  GetFavoritesAsync()
```

因为它会：

- 重建活动数据源。
- 暂停 Viewer/consumer。
- 改设置中的 active wiki。
- 污染当前 UI。
- 让收藏页刷新变成肉眼可见的数据源切换。
- 与正在阅读/AI/搜索状态冲突。

应增加针对指定 `WikiBook/DataFolder` 的离线读取路径。

### 5.4 推荐的跨库读取方式

Astra 需在完整仓库中选最小安全方案，首选方向：

```text
ManagerDatabase 列出 WikiBook
→ WikiDataPaths 解析每个 Content.db
→ 先检查文件真实存在
→ 对存在的数据库创建短生命周期 ContentDbService / 专用 FavoriteStore
→ 读取 WikiFavorite
→ Close
```

关键要求：

- **不能因为刷新收藏页而创建一个本来不存在的 Wiki 数据目录或空 Content.db。**
- 使用现有安全路径校验规则，不能自己 `Path.Combine` 后信任任意 DataFolder。
- 不访问已删除/未下载的数据源时，不把它当成致命错误导致所有收藏不可用。
- 单个 Wiki 收藏数据库损坏时，应有安全降级策略；至少不能把其他 Wiki 已成功读取的收藏全清掉而不给原因。

如果现有 `ContentDbService.Init()` 会在只读场景创建 schema，Astra 应评估是否增加一个更窄的只读 Favorites store，而不是滥用完整 Init。

### 5.5 非活动 Wiki 的取消收藏不能切换 ActiveWiki

收藏列表中的“取消收藏”必须针对：

```text
WikiId + PageTitle
```

直接修改对应 Content.db。

应该复用：

```text
CoordinationGate
OperationGate
路径安全规则
```

或新增等价的专用安全边界。

不要：

```text
切 Wiki → 删除收藏 → 再切回来
```

### 5.6 打开非活动 Wiki 收藏时允许切换活动 Wiki

点击收藏项打开正文时：

```text
if favorite.WikiId != activeWiki.Id:
    走现有正式 ActiveWiki 切换流程
    等 Runtime / Viewer / Search / Home 等消费者完成刷新

再：
    打开 favorite.PageTitle
```

重点是“复用已有完整切换流程”。

Astra 必须检查 `WikiManagementViewModel`、`MainWindow`、`WindowsClientRuntime` 中现在是谁负责：

```text
ActiveWiki.SwitchAsync
Viewer base/home refresh
WikiViewModel data-source reset
Favorites refresh
Library refresh
AI tool reconnect/consumer pause
```

如果目前切换流程只能由数据管理 UI 调用，建议抽出一个最小的 Runtime/Shell 级协调方法供收藏复用，而不是在 Favorites 中复制一套。

### 5.7 收藏页显示建议

每条 Wiki 收藏至少展示：

```text
页面标题
所属 Wiki 名称
收藏时间
```

例如：

```text
泰拉刃
官方中文 Terraria Wiki · 2026-09-12 17:40
```

不要仅显示标题，否则跨 Wiki 聚合后仍然无法辨别来源。

---

## 6. Reader 收藏接口建议同步改为带 Wiki 身份

当前 Reader 的 Favorites delegate：

```text
Func<string, Task<bool>> isFavorite
Func<string, bool, Task<bool>> setFavorite
```

这会把 Reader 永远绑定“当前活动数据库 + 标题”。

建议收敛为显式 identity：

```text
IsFavoriteAsync(ViewerPageIdentity)
SetFavoriteAsync(ViewerPageIdentity, bool)
```

或：

```text
IsFavoriteAsync(int wikiId, string title)
SetFavoriteAsync(int wikiId, string title, bool favorite)
```

这样：

- Viewer 内链后不会收藏旧标题。
- ActiveWiki 切换竞态不会误写别的 Wiki。
- 同名页面可安全区分。
- 收藏页与 Reader 使用同一个身份规则。

---

## 7. 推荐方案 C：把三个原生抬头收敛到 Shell 单顶栏

### 7.1 删除独立 WikiToolbar 工作区行

当前：

```text
WorkspaceGrid RowDefinitions="Auto,*"
Row 0 = WikiToolbar
Row 1 = 内容
```

独立 Wiki 模式应改成：

```text
现有 Shell TopBar
└─ 产品标题 + Wiki 上下文操作 + 搜索

Workspace
└─ 首页 / 搜索结果 / Reader
```

不再存在第二层独立 WikiToolbar。

### 7.2 把 Wiki 控件放进现有 Shell TopBar

在 `IsWikiSelected` 时，顶栏右侧根据状态显示。

#### Home

```text
Terraria AI Wiki                         [搜索框] [搜索]
```

#### Search

```text
Terraria AI Wiki              [主页]     [搜索框] [搜索]
```

#### Reading

```text
Terraria AI Wiki   [主页] [返回搜索] [页面返回] [收藏] [搜索框] [搜索]
```

实际顺序、图标和响应式由 Astra 按现有控件规范优化，但语义要区分：

- `返回搜索结果`：Shell 的 Wiki 三态导航。
- `页面返回`：Viewer history back。
- 两者不是同一个返回按钮。

### 7.3 独立 Wiki 模式隐藏 Reader Header，但普通 Reader 保持

当前 `ReaderView`：

```xml
<Grid RowDefinitions="48,*,Auto">
    <Border Grid.Row="0">...</Border>
```

这里有一个实现陷阱：

**仅把 Header Border 设为 `IsVisible=false` 不够。**

因为 Row 0 是固定 `48`，仍可能保留 48 DIP 空白。

正式实现必须让布局一起折叠，例如：

```text
RowDefinitions="Auto,*,Auto"
+ Header IsVisible
```

或其他等价方案。

要求：

- `ShellSection.Wiki` 的 Home/Reading 使用独立 Wiki 模式时 Header 真正不占空间。
- 从 Chat、收藏、资料库、数据管理等页面打开共享 Reader 时，Header 继续显示。
- Reader 底部状态栏继续显示。

### 7.4 Reader Header 被隐藏后，必要行为必须迁移

不能因为视觉收敛丢掉：

```text
Viewer Back
Toggle Favorite
```

在 Wiki Reading 状态下，这两个命令迁移到 Shell 顶栏，并继续绑定同一个 Reader command/state。

Reader Close：

- 独立 Wiki 模式本来就是整页阅读，不显示关闭按钮。
- 非 Wiki 共享 Reader 继续保留原 Close 行为。

### 7.5 搜索行为不重写

继续绑定当前：

```text
Wiki.Query
Wiki.SearchCommand
180 ms debounce
CancellationToken
request identity
Application.Query.SearchAsync
```

不要新增第二套搜索状态机。

### 7.6 900×600 必须保持单顶栏可用

Astra 正式 plan 必须明确：

- 搜索输入框可收缩。
- Wiki 上下文按钮只在需要时出现。
- 必要时使用图标 + Tooltip，不重复长文本。
- 不因为窄窗重新恢复第二层 WikiToolbar。
- 不产生水平滚动。
- Light/Dark 都实机验收。

---

## 8. 建议的候选文件范围

Astra 应在完整仓库再次核验后确定最终文件清单。

### 高概率 MODIFY

```text
src/Terraria_Wiki.Windows/Viewer/ViewerBridge.cs
src/Terraria_Wiki.Windows/Viewer/ViewerWebViewSession.cs
src/Terraria_Wiki.Windows/Presentation/Reader/ReaderPaneViewModel.cs
src/Terraria_Wiki.Windows/Presentation/Reader/ReaderView.axaml
src/Terraria_Wiki.Windows/Presentation/Favorites/FavoritesViewModel.cs
src/Terraria_Wiki.Windows/Presentation/Favorites/FavoritesView.axaml
src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs
src/Terraria_Wiki.Windows/Presentation/Shell/ShellView.axaml
src/Terraria_Wiki.Application/Contracts/WikiDtos.cs
src/Terraria_Wiki.Application/UseCases/WikiLibraryUseCases.cs
```

### 可能 MODIFY / EXTRACT

```text
src/Terraria_Wiki.Windows/MainWindow.cs
src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs
src/Terraria_Wiki.Application/UseCases/ActiveWikiUseCases.cs
src/Terraria_Wiki.Application/Internal/ApplicationState.cs
src/Terraria_Wiki.Core/Services/DatabaseService.cs
```

仅当需要：

- 复用正式 ActiveWiki 切换协调链路。
- 增加指定 Wiki 的 Favorites 只读/写入边界。
- 增加 Viewer page-commit 接线。

### 可能需要 Viewer Web asset / protocol 修改

只有当完整仓库证明：

```text
PageRedirectAsync != 页面实际 commit
```

才允许对 Viewer JS/HTML 增加最小 `PageChanged/PageCommitted` 消息。

不要为了“方便”顺手改 Viewer 内容样式或网页结构。

### Tests / Specs

按仓库真实位置补齐：

```text
Windows Presentation flow/boundary tests
Viewer bridge/session tests
Application WikiLibrary tests
Active Wiki switching tests
Core persistence tests（仅当新增跨库 store）
windows-presentation spec
local-wiki-viewer spec
wiki-query / library boundary spec（若公共契约变化）
```

---

## 9. 明确非目标

本 change 不做：

- 不泛化多游戏平台。
- 不重命名 `Terraria_Wiki.*` namespace。
- 不改 Wiki 搜索排序、FTS5、下载和更新。
- 不增加在线 Wiki 搜索。
- 不增加第二个 Viewer / NativeWebView / Viewer session。
- 不重做收藏页面整体视觉。
- 不把对话收藏和 Wiki 收藏拆成新页面。
- 不修改 Agent / MCP / Sidecar。
- 不改安装器和发布流程。
- 不改网页 Terraria Logo、横幅、正文样式。
- 不顺带恢复旧浏览历史功能。
- 不因为聚合收藏就删除现有各 Content.db 的 `WikiFavorite`。
- 不默认迁移到 Manager.db。

---

## 10. 数据兼容与迁移策略

### 首选：零迁移

继续保留：

```text
<WikiA>/Content.db -> WikiFavorite
<WikiB>/Content.db -> WikiFavorite
...
```

只新增跨库 Application 读取/写入能力。

验收：

- 升级前已经收藏的页面升级后全部可见。
- 不要求用户重新收藏。
- 回退旧版本后原收藏仍在各 Content.db 中可用。
- 不改变 Content schema version / package contract。

### 如果 Astra 最终判断必须中心化到 Manager.db

这不是首选。

正式 plan 必须额外写清：

- 新表 schema。
- `(WikiId, PageTitle)` 唯一键。
- 一次性扫描所有 Content.db 的迁移。
- 幂等性。
- 部分失败恢复。
- rollback。
- 旧版本回退后的数据可见性。
- Wiki package 导入导出是否携带收藏。
- Legacy compatibility validator。
- 删除/重装 Wiki 后收藏保留语义。

在这些问题没有完整答案前，不允许直接把收藏搬到 Manager.db。

---

## 11. 建议实施顺序

### Phase 0：Astra 核验

- [ ] 读取完整 repo，而不是只依据 core-source snapshot。
- [ ] 找到 Viewer HTML/JS 中 `GotoPage`、`PageRedirectAsync`、`BackToPage`、history 的真实调用顺序。
- [ ] 找到当前 ActiveWiki 切换后 Windows Runtime 刷新的完整协调链路。
- [ ] 找到所有 `WikiFavoriteItem` / `GetFavoritesAsync` 调用点。
- [ ] 确认测试项目、OpenSpec 和 ADR 当前版本。
- [ ] 建 checkpoint / 分支；只规划，不实施直到正式 plan 获批。

### Phase 1：先锁定收藏错页回归测试

先写失败测试，至少覆盖：

- [ ] 搜索打开 A → 收藏 A。
- [ ] A 内部链接到 B → 星标状态刷新为 B → 收藏实际写 B。
- [ ] B Viewer Back → 回 A → 星标恢复 A。
- [ ] alias A → canonical B → 只收藏 B。
- [ ] Back history 为空 → 当前身份不丢。
- [ ] 导航失败 → 当前已显示页面身份不被错误覆盖。
- [ ] 快速 A→B → 旧 A 完成不能覆盖 B。

### Phase 2：建立 Viewer 权威页面身份同步

- [ ] 引入明确页面 identity。
- [ ] Viewer 页面 commit 后发布 identity。
- [ ] Reader 只消费事件，不再根据 request 猜 title。
- [ ] Back 不再清空 title/favorite。
- [ ] Wiki 切换时清理/重建 identity。
- [ ] 收藏状态刷新使用 `(WikiId, CanonicalTitle)`。

### Phase 3：跨所有本地 Wiki 收藏聚合

- [ ] 增加带 Wiki 身份的聚合 DTO。
- [ ] 枚举 ManagerDatabase WikiBook。
- [ ] 只读取真实存在的 Content.db。
- [ ] 合并、按收藏时间倒序。
- [ ] FavoritesViewModel 使用全局集合。
- [ ] 收藏卡片显示所属 Wiki。
- [ ] 非活动 Wiki 取消收藏不切 ActiveWiki。
- [ ] 打开非活动 Wiki 收藏复用正式 ActiveWiki 切换链路。
- [ ] 切换完成后再打开指定页面。
- [ ] 相同 PageTitle + 不同 WikiId 同时存在时行为正确。

### Phase 4：单顶栏收敛

- [ ] 删除 `WikiToolbar` 工作区行。
- [ ] 把搜索与 Wiki 上下文操作合并到 Shell TopBar。
- [ ] Reader header 在独立 Wiki 模式真正折叠为 0 高度。
- [ ] Wiki Reading 顶栏提供 Viewer Back + Favorite。
- [ ] 非 Wiki Reader header 完全保持。
- [ ] Reader status bar 保持。
- [ ] 900×600、1280×820、Light/Dark 实机验收。

### Phase 5：全量回归与文档

- [ ] Windows Release build 0 warning / 0 error。
- [ ] 定向 Viewer/Favorites/Application/Presentation tests。
- [ ] 全量 .NET tests。
- [ ] `git diff --check`。
- [ ] 确认仍只有一个 NativeWebView / ViewerWebViewSession。
- [ ] 确认未改搜索实现、Agent、下载、包格式。
- [ ] 更新持续 spec。
- [ ] 用户实机验收后才合并。

---

## 12. 必须加入的验收场景

### 收藏正确性

1. Terraria Wiki 收藏“泰拉刃”。
2. 正文内部进入“武器”。
3. 顶栏星标必须立即反映“武器”的状态。
4. 点击收藏，只能新增“武器”，不能再次写“泰拉刃”。
5. Viewer Back 回“泰拉刃”后，星标恢复“泰拉刃”的真实状态。

### Redirect

1. 从一个 Redirect 名称打开正文。
2. Viewer 最终显示规范页面。
3. 收藏列表只出现规范页面身份。
4. 再从其他 Redirect 进入同一目标，不产生重复收藏。

### 全局收藏

准备：

```text
Wiki A：收藏 Page X、Page Y
Wiki B：收藏 Page X
```

收藏页必须出现 3 条，并明确来源。

`A/Page X` 和 `B/Page X` 不得互相覆盖。

### 非活动 Wiki 取消收藏

当前 ActiveWiki = A。

从收藏页取消 `B/Page X`：

- ActiveWiki 仍然是 A。
- 当前 Viewer 不跳转。
- B 的收藏被删除。
- A 的状态不受影响。

### 打开非活动 Wiki 收藏

当前 ActiveWiki = A。

点击 `B/Page X`：

- 使用正式切换链路切到 B。
- Viewer / Search / Home / AI consumer 等按现有契约完成切换。
- 最终打开 B/Page X。
- Reader identity = `(B, canonical Page X)`。
- 收藏星标正确。

### 三抬头视觉

独立 Wiki 首页/正文：

```text
只出现一层原生应用顶栏
↓
直接进入 Viewer/搜索结果
```

禁止再次出现：

```text
Terraria Wiki / 本地搜索 独立工具栏
Reader Terraria Wiki / Terraria Wiki 标题栏
```

网页 Terraria Logo 正常存在。

### 普通 Reader 回归

从 Chat 来源、收藏页、资料库等非 Wiki 主页面打开 Reader：

- 原 Reader header 仍然存在。
- Back / title / location / favorite / close 行为维持。
- 1100 DIP 宽窄 Reader 行为不变。

---

## 13. 重点风险

### 风险 1：把 `PageRedirectAsync` 误当 commit

如果 JS 会预读取页面或并发请求，直接在 `PageRedirectAsync` 返回时改 Reader 可能再次产生错页。

Astra 必须先核验 Viewer JS。

### 风险 2：聚合收藏时意外创建空数据库

`DatabaseService` 构造和 `Init()` 有创建目录/schema 的能力。

全局收藏扫描必须先判断数据文件是否真实存在，并使用不会“刷新一下收藏就创建新 Wiki 数据”的读取路径。

### 风险 3：跨 Wiki 打开只切数据库、不刷新 Runtime

如果收藏点击自己调用 `ActiveWiki.SwitchAsync`，但没执行当前 Windows 宿主的完整 refresh，可能造成：

```text
Viewer 仍指向旧 Wiki
Search 已是新 Wiki
Reader/AI/首页状态不一致
```

必须复用当前正式切换入口。

### 风险 4：隐藏 Header 后仍留下 48 DIP 空行

固定：

```xml
RowDefinitions="48,*,Auto"
```

意味着只 `IsVisible=false` 可能视觉上仍有空隙。

正式 plan 必须明确布局折叠。

### 风险 5：收藏刷新竞争

当前 `Favorites.RefreshAsync()` 有 `IsBusy` 直接 return。

如果短时间内：

```text
收藏 → 切 Wiki → Refresh
```

可能丢掉最后一次刷新请求。

Astra 应检查是否需要：

- refresh pending 标志，
- 串行队列，
- 或在 mutation 完成后直接局部更新集合。

不要在没有复现证据的情况下扩大重构，但正式 plan 应至少验证此竞态。

---

## 14. Astra 生成正式 plan 时必须回答的待决问题

1. Viewer 的真正“页面已提交”信号在哪里？
2. `PageRedirectAsync` 是否 1:1 对应页面 commit？
3. BackToPage 是否会重新经过 PageRedirectAsync？
4. history 中保存的是请求 title 还是 canonical title？
5. 当前完整仓库中 Activity Wiki 切换的唯一正式 Windows 协调入口是什么？
6. 指定 Wiki 的收藏跨库读取，使用短生命周期 `ContentDbService` 是否会造成创建/迁移副作用？
7. 是否需要单独的只读 `WikiFavoriteStore`？
8. 非活动 Wiki 的收藏写入如何正确进入现有 OperationGate/CoordinationGate？
9. Wiki 数据被删除、导入或替换时，全局收藏列表怎样刷新？
10. 正式实现后哪些 OpenSpec/ADR 需要更新？
11. 900×600 时单顶栏最小搜索宽度与按钮折叠规则是什么？
12. `Favorites.RefreshAsync()` 的 Busy 丢刷新问题是否能被测试复现，需要不要顺带修？

---

## 15. 正式 plan 的完成定义

Astra 优化后的正式 plan 只有同时满足以下条件才可以交实施：

- Viewer 当前页身份有单一权威来源。
- Reader 不再通过 request title 或 Back 后清空来猜当前页。
- 收藏 identity 明确包含 WikiId。
- 收藏页能看到所有本地 Wiki 已有收藏。
- 非活动 Wiki 取消收藏不切数据源。
- 非活动 Wiki 收藏可正确切换并打开。
- 现有收藏零丢失，默认无 schema/data migration。
- 独立 Wiki 模式只剩一层原生顶栏。
- Wiki Reader header 真正折叠，不留 48 DIP 空白。
- Viewer Back / 收藏 / 搜索等必要行为没有因视觉收敛丢失。
- 非 Wiki Reader、AI、资料库、数据管理、搜索、Viewer 唯一实例等原行为有明确回归测试。
- Windows 真实窗口验收包含 1280×820 与 900×600、Light/Dark。
- 不包含与本 change 无关的泛化、重构或清理。

---

## 16. 给 Astra 的交接说明

这是一份 **预规划，不是最终实施步骤**。

请先以完整仓库现状为准验证所有事实；对于本文件中标记为“建议”“可能”“需核验”的部分，不要直接照抄成最终方案。

正式 plan 应：

1. 保留本文件已经确认的 Bug 原因和产品目标。
2. 把 Viewer 页面 commit 信号、跨 Wiki 收藏存储访问方式、ActiveWiki 切换协调入口三个技术不确定项查实。
3. 给出准确文件级改动清单。
4. 给出先失败测试、再实现、最后 Windows 实机验收的顺序。
5. 尽量采用最小改动，不引入中心化迁移，除非完整仓库证据证明这是唯一合理方案。
6. 不实施源码，直到新的正式 plan 再次获得批准。
