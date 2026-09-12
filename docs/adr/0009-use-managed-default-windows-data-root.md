# ADR 0009：Windows 使用宿主管理的默认数据根

- 状态：accepted
- 日期：2026-09-04
- 决策范围：Windows 首次运行的数据根创建、恢复与用户更改行为
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`docs/adr/0002-portable-core-and-data-root.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/changes/archive/phase-11-windows-productization/plan.md`

## 背景

ADR 0002 在旧 MAUI 迁移阶段要求原位复用已有大型数据根，并在定位失败时让用户恢复或选择，避免静默创建第二份空数据。Phase 10 已退休旧 MAUI 参考，Phase 11 的首次试用表明强制选择文件夹会打断启动，误选后的恢复也增加了不必要的门槛。用户明确要求数据库文件夹地址自动创建，同时保留更改地址功能。

现有 `DatabaseService` 已能在给定绝对数据根下创建目录、`Manager.db`、默认 WikiBook 记录和活动 Wiki 的 `data.db`。因此无需在 Windows Presentation 或 Host 中复制 SQLite schema 或数据库初始化逻辑。

## 决策

1. Core 继续不发现平台目录，只接收宿主提供的绝对数据根；既有 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db` 布局不变。
2. Windows 宿主按以下顺序解析数据根：有效的显式 `--data-root`、有效的已保存地址、没有已保存地址时的宿主管理默认根。
3. 默认根固定为 `%LocalAppData%\Terraria AI Wiki\WikiData`。宿主只创建目录并保存绝对地址，数据库与默认内容仍由现有 Application/Core 初始化。
4. 已保存地址存在但已失效时，不用默认根覆盖它，也不删除或移动其中的数据；应用进入数据管理页，让用户显式恢复或更改。
5. 数据管理页始终显示当前地址并保留“更改文件夹”。用户可以选择已有有效 `Manager.db` 的数据根，也可以选择空文件夹作为新数据根；非空且没有有效 `Manager.db` 的目录拒绝使用，避免污染误选目录。
6. 更改地址只在路径验证通过后保存并重新组合 Runtime。取消、验证失败或初始化失败都保留可再次操作的页内入口，不弹阻塞对话框。

## 后果

正面后果：

- 新用户首次运行无需理解数据库布局或操作系统文件夹选择器。
- 默认数据位于安装根之外，普通安装、更新、repair 和卸载仍不会替换它。
- 已有用户仍可原位使用旧数据根，也可以把新空目录作为后续存储位置。
- 数据库创建继续复用同一 Application/Core，不产生第二套 schema 或迁移实现。

负面后果与约束：

- 新装会在 LocalAppData 中创建一份空的 Wiki 数据根，实际页面和资源仍需用户下载或导入。
- 已保存的外置根临时离线时不会自动切换到默认根；用户需要在数据管理页重新连接或更改，以避免无提示的数据源变化。
- 选择已有目录时必须区分空目录、有效数据根和非空错误目录；任何错误都不得删除目录内容。

## 被否决方案

- 首次启动自动弹出文件夹选择器：打断启动且误选恢复成本高。
- 在 Core 中硬编码 LocalAppData：会把 Windows 平台路径反向写入 portable Core。
- 对任意非空目录直接创建 `Manager.db`：可能污染误选的文档、下载或其他应用目录。
- 已保存根失效时静默改用默认根：会让用户误以为原有 Wiki 数据丢失，并覆盖其地址选择。
