# 用数据库文件导入导出替代数据根切换

- 状态：archived；实现与自动化回归已完成，正式 3 GiB 数据库实机导入未执行
- Phase：无
- 执行环境：Windows Codex desktop；Windows .NET/Avalonia 验证
- 创建日期：2026-09-13
- 最后更新：2026-09-13
- 范围：Windows 数据管理页、Application 数据用例、Core SQLite 数据库快照导入导出与恢复、相关持续规范/ADR
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/wiki-data-compatibility/spec.md`、`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0009-use-managed-default-windows-data-root.md`

## 背景与当前证据

用户明确取消 Windows 数据管理页的数据库地址切换，并要求把原“数据库文件夹”区域及其功能改为直接导入/导出当前 Wiki 的完整 `data.db`。本机有效中文 Wiki 内容库约 3.5 GiB，页面与资源均存于同一 SQLite 数据库；直接复制正在使用的 WAL 数据库主文件不能保证包含最新提交。

现有 Core 已有 `DatabaseService.BackupExistingAsync`，可从活动 SQLite 连接生成一致的单文件快照；`.pkg` 导入已有 staging、backup、journal、rollback 和启动恢复能力。Windows 页面当前同时暴露数据根选择与 `.pkg` 导入导出，后者通过 Application/Core 且不会让 Presentation 访问 SQLite。

## 目标与完成定义

- 数据管理页不再显示数据库根路径或“更改文件夹”，也不再暴露 `.pkg` 按钮。
- 原区域改为当前 Wiki 的“导入数据库”“导出数据库”；文件选择器只选择/保存 `.db`。
- 导出通过 SQLite 在线备份生成包含 WAL 最新提交的一致单文件快照；已有目标只有在完整成功后才替换。
- 导入固定作用于当前活动 Wiki：先检查单文件、显示 Wiki/大小/覆盖影响并确认，再在消费者暂停、同卷 staging/backup/journal 下替换；失败和中断可恢复原数据库。
- 导入成功刷新 Manager 统计并重绑唯一 Viewer；切换 Wiki、在线更新和原 `.pkg` Core 兼容能力不被删除。
- Windows Release 编译、相关行为测试和数据边界检查通过。

## 前置条件

- 用户已明确授权改变数据管理流程并移除地址切换入口。
- 当前 Git 恢复点为 `0e5c4c44150c6f96fdde6df98a4e4a4a13cf9755`；工作树已有用户/先前任务改动，实施只编辑本计划列出的相关文件，不回退其他改动。
- 当前数据根继续由 Windows Host 内部解析；显式 `--data-root` 与既有保存值保留为兼容/诊断能力，但普通 UI 不再允许切换。

## 范围与非目标

范围内：raw SQLite `data.db` 检查、导入、导出；Windows picker、确认、状态文案和测试；持续能力契约与数据根 ADR 的用户流程修订。

非目标：删除 `.pkg` Core 格式或公共兼容 API、改变 SQLite schema、复制资源到第二套数据库、自动搬迁任意数据根、改变 Wiki 热切换、发布或安装新版本、Android。

## P0 / 兼容性影响

- SQLite、FTS5、页面、Redirect、资源、收藏与兼容历史必须作为一个数据库快照整体保留。
- 导出不得裸拷贝活动主文件；必须使用现有 SQLite backup API。
- 导入来源不得携带 `-wal/-shm/-journal`；选中的单文件先复制到受控 staging 并执行完整 integrity/schema 校验，确认 token 不匹配时不触碰生产目录。
- 生产目录替换沿用现有 import journal/目录交换与回滚；保留 `res.txt` 等非数据库辅助文件。
- `.pkg` 后端继续保留，避免删除已确认 P0 数据包兼容能力。

## 方案与待决问题

- 在 Core 现有 transfer service 上增加 raw database inspection/export/import 契约，复用同一操作 gate、schema validator、目录交换 journal 和恢复入口。
- raw inspection token 使用文件内容 SHA-256；导入复制到 staging 时同步重新计算并验证，防止确认后文件被替换。
- raw import staging 先复制目标目录的非数据库辅助文件，再安装经验证的 `data.db`，因此只替换数据库家族。
- Windows VM 将 package 类型替换为 database DTO，但 Core/Application 的 `.pkg` API 保留且不再由 Windows 页面调用。
- 启动数据根错误改为可重试恢复文案；不再触发不存在的文件夹选择入口。
- 待决问题：无。用户已指定导入/导出的就是约 3 GiB 的 `data.db` 单文件。

## 文件变更

- MODIFY `src/Terraria_Wiki.Core/Packages/WikiPackageContracts.cs`：增加 raw database transfer contracts。
- MODIFY `src/Terraria_Wiki.Core/Packages/WikiPackageTransferService.cs`：增加一致快照导出、检查、原子导入并复用恢复 journal。
- MODIFY `src/Terraria_Wiki.Application/Contracts/WikiDtos.cs`、`Internal/ApplicationMapper.cs`、`UseCases/WikiDataUseCases.cs`：增加薄 Application DTO/用例。
- MODIFY `src/Terraria_Wiki.Windows/MainWindow.cs`、`Presentation/Shell/ShellViewModel.cs`、`Presentation/Management/WikiManagementViewModel.cs`、`WikiManagementView.axaml`：替换 picker、命令、确认和 UI；删除数据根选择交互。
- REMOVE `src/Terraria_Wiki.Windows/Hosting/AvaloniaDataRootPicker.cs`：普通 UI 不再选择数据根。
- MODIFY Core/Application/Windows 相关测试。
- MODIFY `openspec/specs/windows-presentation/spec.md`、`application-use-cases/spec.md`、`wiki-data-compatibility/spec.md`、ADR/架构/项目入口中相关长期事实。

## 执行步骤

- [x] 读取现有数据根、`.pkg`、SQLite backup、Application、Windows 管理页、启动恢复和测试实现。
- [x] 实现 Core raw database 检查、快照导出、原子导入及恢复测试。
- [x] 接入 Application 用例并验证消费者 pause、统计刷新与失败边界。
- [x] 按 `terraria-ui` 规范替换 Windows UI/文件选择器/确认，删除数据根选择入口并更新回归。
- [x] 更新持续 specs、ADR、架构/项目状态，消除旧“可更改文件夹”契约。
- [x] 执行 Windows Release build、相关及全量测试、diff/status/生成物检查；记录实际 UI 未验证项。

## 验证与回归计划

- Core：正常 WAL 源导出为独立可读 `data.db`；非法/不完整 schema、inspection token 变化、导入失败和 journal 恢复不破坏原目标；辅助文件保留。
- Application：raw 导入只作用活动 Wiki、暂停/恢复消费者、刷新统计；raw 导出复用指定 Wiki 数据源；原 `.pkg` 回归继续通过。
- Windows：`.db` picker、导入确认、覆盖文案、取消/失败/成功状态、Viewer rebind；XAML 不出现路径选择和 `.pkg` 操作，Presentation 不访问 SQLite/Core storage。
- Windows Release：solution build 0 warning/0 error，相关测试与全量测试。
- UI：Light/Dark、1280×820、900×600、宽/窄 Reader 下操作区可用；若未启动真实窗口则明确记录。

## 风险与回退

- 大文件空间：导入需要 staging 与原目录 backup 同时存在，开始前检查可用空间；不足时在生产替换前失败。
- 活动 WAL 一致性：导出只用在线 backup；不复制 `data.db-wal`。
- 导入覆盖：确认明确指出当前 Wiki；目录交换发生前完整验证，失败恢复 backup。
- 崩溃恢复：继续使用现有 per-Wiki import journal，启动先恢复未完成交换。
- UI 回退：可恢复到本 change 前文件版本；`.pkg` 后端未删除，避免数据兼容回退困难。

## 进度记录

- 2026-09-13：用户取消数据库地址切换，要求原区域改为导入/导出约 3 GiB 的 `data.db`。
- 2026-09-13：完整读取 UI Skill 与 Windows UI 规范，并核对现有 Presentation、Application、Core package、SQLite backup、恢复 journal 和测试。确认不能直接复制活动 WAL 主文件。
- 2026-09-13：Core/Application/Windows 接入完成；Windows UI 删除数据根选择和 `.pkg` 操作，改为活动 Wiki 的 `.db` 导入/导出。
- 2026-09-13：修正导出快照继承 WAL 模式的问题，导出完成前切换为 DELETE journal mode，保证交付单文件且无 sidecar。
- 2026-09-13：Release solution build 通过，四个测试项目共 374 项全部通过，`git diff --check` 通过。

## 发现、偏差与决策

- 决定：Windows 普通 UI 只交换活动 Wiki 的 raw `data.db`；数据根成为宿主管理的内部位置。
- 决定：保留 `.pkg` Core/API 兼容能力但从 Windows 数据管理页移除，避免违背 P0 护栏。
- 决定：raw 导入沿用 package 的目录级原子交换和启动恢复，而不是在 Presentation 中直接覆盖文件。
- 发现：SQLite 在线备份会继承源库 WAL journal mode；导出后必须显式转换为 DELETE mode，才能形成真正独立的单文件数据库。

## 验证结果

- `dotnet build Terraria_Wiki.Core.slnx -c Release`：通过，0 warning / 0 error。
- `dotnet test Terraria_Wiki.Core.slnx -c Release --no-build`：通过，Core 107、Application 60、WikiTools 45、Windows 162，共 374 项。
- `git diff --check`：通过。
- 未用用户约 3 GiB 的正式数据库执行覆盖式导入，也未启动真实窗口做 Light/Dark 和多尺寸人工视觉验收；这是为避免碰触正式数据和继续扩大测试时间。XAML 已经 Release 编译，相关 Presentation/UI 静态与流程测试通过。

## 完成摘要

Windows 数据管理页已不再允许切换数据库目录，原位置改为当前活动 Wiki 的 `data.db` 导入/导出。导出通过 SQLite 在线备份生成无 sidecar 的一致单文件；导入在完整校验、内容 token 与用户确认后，以 staging/backup/journal 原子替换并保留辅助列表，成功后刷新统计和 Viewer，失败则回滚。`.pkg` 后端兼容能力保留但不再暴露于 Windows UI。
