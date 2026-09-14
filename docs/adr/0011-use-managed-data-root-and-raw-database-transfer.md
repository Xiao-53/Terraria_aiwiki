# ADR 0011：Windows 固定管理数据根并使用单文件数据库导入导出

- 状态：accepted
- 日期：2026-09-13
- 决策范围：Windows 数据根用户流程、活动 Wiki 数据导入导出
- 关联：`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0009-use-managed-default-windows-data-root.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/wiki-data-compatibility/spec.md`

> ADR 0012 已取代本文关于保留 `.pkg`、缺少数据库 Wiki 身份元数据和旧内容兼容的决定；本文继续约束固定管理数据根及 raw `.db` 的一致快照与安全交换。

## 背景

ADR 0009 为首次运行引入 `%LocalAppData%\Terraria AI Wiki\WikiData` 默认根，同时在数据管理页保留“更改文件夹”。实际迁移表明，把整个数据根作为用户可选择和搬迁的对象会暴露 `Manager.db`、多个 Wiki 子目录、旧布局与 SQLite sidecar，容易让残留目录触发安全冲突。用户明确决定不再提供数据库地址切换，并把原区域改为导入/导出当前 Wiki 的完整 `data.db`。

活动内容库使用 WAL。直接复制正在使用的主文件可能漏掉尚在 WAL 中的提交，因此 raw database 导出仍必须经 SQLite 一致快照；导入必须在完整校验和确认后才能替换生产库。

## 决策

1. Windows 普通 UI 不显示或更改数据根。默认根继续为 `%LocalAppData%\Terraria AI Wiki\WikiData`，安装、更新、repair 和卸载不替换该用户数据。
2. 有效的旧保存根和显式 `--data-root` 暂时保留为升级兼容与诊断入口，但不构成普通用户的数据地址切换功能；无保存根时继续创建默认根。
3. 数据管理页原“数据库文件夹”区域改为“数据库文件”，只提供当前活动 Wiki 的“导入数据库”和“导出数据库”；`.pkg` 能力按 ADR 0012 退休。
4. 导出结果是一个可独立打开、没有 `-wal/-shm/-journal` 的 SQLite `.db` 文件。实现必须通过活动连接的 SQLite backup 取得一致快照，并在完整成功后才替换用户已存在的目标文件。
5. 导入固定覆盖当前活动 Wiki。选择的 `.db` 必须是无 sidecar 的独立文件；先执行完整 integrity/schema 检查并绑定 inspection token，用户确认目标与影响后，再暂停消费者并在同卷 staging/backup/journal 下替换。
6. raw 导入只替换数据库家族，保留当前 Wiki 的资源列表、失败列表等非数据库辅助文件。成功后刷新 Manager 统计并重绑同一个 Viewer；失败或中断恢复原数据库。

## 后果

正面后果：

- 用户只处理一个与实际页面/资源数据一致的 `data.db`，不需要理解数据根内部布局。
- 导出不会遗漏 WAL 提交；导入不会直接覆盖正在使用的数据库。
- 多 Wiki 继续共享一个 Manager 和数据根，切换 Wiki 不搬迁文件。
- raw `.db` 的现有一致快照与可恢复交换边界可以独立保留。

负面后果与约束：

- raw `.db` 仍只覆盖调用方明确选择的当前活动 Wiki，并校验库内 v2 Wiki 身份一致。
- 检查、哈希、暂存和完整性校验需要多次顺序读取大型数据库，并在导入期间需要至少一份数据库大小的额外可用空间。
- 已保存外置根失效时不再提供 UI 选择器；用户须恢复该路径或通过受控升级/诊断方式回到默认根。

## 被否决方案

- 继续让用户选择整个数据根：会再次暴露多库布局、Manager 与 sidecar 冲突。
- 直接 `File.Copy` 活动 `data.db`：无法保证包含 WAL 最新提交。
- 删除 `.pkg` Core/API：原决定已由用户对 v2 数据库重构的明确授权和 ADR 0012 取代。
- 根据 raw database 内容猜测目标 Wiki：数据库没有稳定、完整的 Wiki 身份 metadata，容易覆盖错误数据源。
