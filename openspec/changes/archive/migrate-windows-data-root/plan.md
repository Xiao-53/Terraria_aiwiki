# 迁移 Windows Wiki 数据根并删除旧 BigBearKing 目录

- 状态：completed；用户确认迁移工作已完成，计划归档
- Phase：无
- 执行环境：Windows Codex desktop
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：本机 Windows Wiki 数据根、Host 保存地址与旧应用目录
- 关联架构/规范/ADR：`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0009-use-managed-default-windows-data-root.md`、`openspec/specs/wiki-data-compatibility/spec.md`

## 背景与当前证据

用户明确要求把当前旧数据根 `C:\Users\Xiao\AppData\Local\BigBearKing\com.bigbearking.terrariawiki\Data` 迁移到新标准根 `C:\Users\Xiao\AppData\Local\Terraria AI Wiki\WikiData`，验证后直接删除旧目录。

只读检查确认旧根 10.758 GiB，其中正式 `Terraria_Wiki_zh\data.db` 为 3,781,029,888 字节，含 4390 页面、29774 Redirect、26237 资源、20 条历史；另有两套已确认孤立的 anchor staging 家族 7,766,784,632 字节。旧根还存在刚生成的 `Terraria_Wiki\Terraria_Wiki.db` WAL 家族，仅含 1 个默认页、0 Redirect、0 资源、0 收藏、0 历史，是当前截图中数据布局冲突的旧侧。旧 Manager 统计被该冲突流程改成 1 页/4096 字节，与完整目标库不符。

新标准根当前只有 86,016 字节的空初始化数据：Manager 四个默认 WikiBook，活动内容库仅 1 个默认页，无 Redirect、资源、收藏或历史。`host-settings.json` 当前仍保存旧 BigBearKing `Data` 路径。当前没有 Terraria Wiki/BigBearKing 相关进程；`BigBearKing` 父目录只包含这一款旧应用目录。

## 目标与完成定义

- 完整旧数据移动到 `%LocalAppData%\Terraria AI Wiki\WikiData`，正式内容数据库字节身份和业务计数保持。
- 空初始化目标、孤立 anchor staging 和仅含默认页的冲突旧布局不进入最终数据根。
- Manager 活动 Wiki 统计修复为实际完整库计数与文件大小。
- `host-settings.json` 的 `dataRoot` 更新为新标准根。
- SQLite quick check、schema/计数、正式库 hash、目录状态和设置路径验证通过后，删除临时空目标备份、设置备份与整个旧 `BigBearKing` 目录。

## 前置条件

- 用户已明确授权迁移并删除旧目录。
- 源和目标均在 C: 同卷；可用目录 rename 完成主体迁移，不复制 3.5 GiB 正式库。
- 当前无相关应用进程；执行前与删除前再次核对。

## 范围与非目标

范围内：上述两个精确数据根、现代 `host-settings.json` 的 `dataRoot`、旧 `BigBearKing` 应用目录、现场验证与本计划。

非目标：修改产品源码、数据库 schema/内容页面、AI 配置、对话、WebView2 数据、其他 LocalAppData 目录或发布安装。

## P0 / 兼容性影响

- 完整 `Terraria_Wiki_zh\data.db` 是唯一保留的正式内容库；移动前后核对 SHA-256、长度、schema 和业务计数。
- 目标现有 86 KiB 空初始化根先改名保存，主体迁移和设置更新验证前不删除。
- 正式库不做页面/资源写入；仅在备份 Manager 后修复其派生统计。
- 只有完整目标和设置均验证成功后才删除旧根与临时备份。

## 方案与待决问题

- 生成唯一迁移 ID；同卷把现有 `WikiData` 改名为临时备份，再把旧 `Data` 原子移动为新的 `WikiData`。
- 从新根精确删除两套 orphan staging 家族和已证明为空的 `Terraria_Wiki` 冲突目录。
- 备份并事务更新 Manager 活动 Wiki 的 Page/Redirect/Resource/DataSize 统计；原子更新 Host 保存路径。
- 完成 hash、quick check、计数与路径检查后删除所有迁移备份及 `BigBearKing` 旧根。
- 待决问题：无。

## 文件变更

- MOVE `...\BigBearKing\com.bigbearking.terrariawiki\Data` → `...\Terraria AI Wiki\WikiData`。
- MODIFY `%LocalAppData%\Terraria AI Wiki\host-settings.json`：仅更新 `dataRoot`。
- MODIFY 新根 `Manager.db`：仅修复活动 Wiki 派生统计。
- REMOVE 新根中的四个严格 orphan staging 文件和空 `Terraria_Wiki` 冲突目录。
- REMOVE 验证后的空目标/设置临时备份及整个旧 `BigBearKing` 目录。
- KEEP 正式内容库、日志、现代客户端其他设置/对话/WebView2 数据和产品源码。

## 执行步骤

- [x] 完成源/目标、业务计数、进程、设置和旧父目录只读预检。
- [x] 记录正式库 hash；再次确认无进程并执行同卷可回滚移动。
- [x] 备份并修复 Manager 统计，更新 Host 数据根；孤立/空冲突产物等待 UI 删除确认。
- [x] 验证 hash、SQLite quick check、schema/计数与设置路径。
- [ ] 删除临时备份和旧 BigBearKing 根；复核无旧路径残留并归档计划。

## 验证与回归计划

- 文件：正式 `data.db` 移动前后 SHA-256 与长度一致；旧根不存在；新根不含 anchor staging、旧冲突目录或数据库 sidecar。
- SQLite：Manager 与 Content `PRAGMA quick_check`；WikiPage/Redirect/Asset/Favorite/History 计数；Manager `DataFolder`、下载状态和派生统计。
- Host：`host-settings.json` 可解析且 `dataRoot` 精确等于新根。
- 运行边界：迁移期间不启动客户端；完成后不自动发网络或 AI 请求。

## 风险与回退

- 主体移动失败：目标空根备份仍在，旧 `Data` 若未移动则原位；若已移动可在设置更新前移回。
- 设置更新失败：恢复设置备份并把数据根移回旧位置，恢复空目标。
- Manager 修复失败：恢复 8 KiB Manager 备份；正式内容库不变。
- 验证失败：在删除任何迁移备份和旧父目录前回滚。
- 最终删除不可恢复：仅在 hash、quick check、业务计数和设置全部通过后执行；删除目标限制为已解析的三个精确路径。

## 进度记录

- 2026-09-13：用户根据客户端冲突页面明确授权直接迁移数据库目录并删除旧目录。
- 2026-09-13：完成现场预检，确认完整源、空目标、冲突旧布局、错误 Manager 派生统计和当前 Host 保存路径。
- 2026-09-13：正式库迁移前 SHA-256 为 `3ED6C94B15E27818C05A2C8CF84E2F362A42BD5CE66FD668F68731B135C8ED39`；同卷把原空 `WikiData` 改名为 `WikiData.pre-bigbearking-migration`，再把旧 `Data` 原子移动到标准 `WikiData`。旧 `Data` 路径已不存在。
- 2026-09-13：创建 8 KiB Manager 与 393 字节 Host settings 临时备份；修复活动 Wiki 为 4390 页面、26237 资源、29774 Redirect、3,781,029,888 字节，并把 Host `dataRoot` 更新为标准路径。
- 2026-09-13：迁移后 Manager/Content `PRAGMA quick_check` 均为 `ok`，正式库 hash 与迁移前一致，页面/Redirect/资源/收藏/历史计数分别为 4390/29774/26237/0/20。命令行删除继续被宿主策略拒绝，已按 `computer-use` Skill 转为回收站删除，等待 action-time 确认。
- 2026-09-13：用户再次确认删除；当前桌面环境未暴露任何原生应用，资源管理器绑定返回 `cua.getApp is not a function`，无法执行已确认的回收站动作。复核表明没有文件被删除：四个 orphan、空冲突目录、三份临时备份和旧 BigBearKing 设置根仍在；旧 `Data` 已成功移出且新正式库保持有效。

## 发现、偏差与决策

- 发现：截图冲突不是现代根与旧根的跨根比较，而是当前旧根内 `Terraria_Wiki` 与 `Terraria_Wiki_zh` 同时存在；前者仅为空默认库且带 WAL，后者是完整库。
- 决定：最终新根只保留 `Terraria_Wiki_zh` 完整库，不迁入空目标内容、orphan staging 或冲突旧布局。
- 偏差：宿主禁止通过命令行执行删除；主体移动、设置和数据库修复不受影响。最终删除改为使用 Windows 资源管理器移入回收站，按 Skill 规则必须在动作前再次取得用户确认。
- 阻塞：用户已确认 UI 删除，但本会话的原生 Windows 控制能力被禁用，且命令执行策略也拒绝删除。本会话不能安全完成最终删除，必须由用户在资源管理器手动删除精确候选，或在提供原生 Windows 控制的会话中继续。

## 验证结果

- 正式库迁移前后长度：3,781,029,888 字节；SHA-256 均为 `3ED6C94B15E27818C05A2C8CF84E2F362A42BD5CE66FD668F68731B135C8ED39`。
- Manager quick check：`ok`；Content quick check：`ok`。
- 业务计数：WikiPage 4390、WikiRedirect 29774、WikiAsset 26237、WikiFavorite 0、WikiHistory 20；Manager 派生统计一致。
- Host `dataRoot`：`C:\Users\Xiao\AppData\Local\Terraria AI Wiki\WikiData`；旧 `...\BigBearKing\...\Data` 已不存在。
- 待验证：删除候选、临时备份和旧父目录后的最终目录占用与客户端启动。

## 完成摘要

进行中。
