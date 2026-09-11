# 修复已接管 Wiki 数据被旧布局阻断

- 状态：archived
- Phase：无
- 执行环境：WSL Codex CLI；Windows .NET/Avalonia 构建与 smoke 验证
- 创建日期：2026-09-10
- 最后更新：2026-09-11
- 范围：Core 数据目录兼容、Application 启动、Windows Reader 可用状态
- 关联架构/规范/ADR：`openspec/specs/wiki-data-compatibility/spec.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/windows-presentation/spec.md`、ADR 0007、ADR 0009

## 背景与当前证据

用户在新默认数据目录中只能看到空 Reader，切换到旧产品数据根后又稳定收到 `data-directory-conflict`。只读检查确认旧根同时存在 `Terraria_Wiki/Terraria_Wiki.db` 与 `Terraria_Wiki_zh/data.db`：目标库有 4390 页和 29774 条重定向，覆盖旧库全部 4320 页和 28497 个重定向键；全部旧页均存在且更新时间未倒退；目标库也覆盖旧库全部 25914 个资源键并额外包含 323 个资源。当前实现只按目录同时存在判冲突，没有识别目标库已完整接管的事实。

空数据根中的 Viewer 服务能够启动，但未下载 Wiki 不会导航。`OpenWikiReaderCommand` 无可用性条件，仍能打开未绑定 Reader，形成“Viewer is ready / 等待本地 Viewer”的误导界面。

## 目标与完成定义

- 两套目录同时存在时，仅在目标内容库可证明覆盖旧库且页面版本不倒退时，完成旧布局退役并使用目标库。
- 不能证明覆盖关系时继续以 `data-directory-conflict` 失败关闭，且两套数据不变。
- 退役操作可恢复；目标库完成兼容准备与内容初始化后才清理旧布局。
- Reader 未绑定时，“打开 Wiki”命令不可执行，也不能打开空白面板。
- Windows Release 构建、相关测试、完整测试和隔离 smoke 通过；不直接修改或删除用户生产数据库。

## 前置条件

- Phase 10 Windows 接管能力与已归档 `optimize-windows-startup` 变更已完成。
- 当前 Git 工作树干净，HEAD `1396e3c` 是可恢复保存点。
- 用户已明确要求修复；未授权发布、安装或上传。

## 范围与非目标

修改 Core 兼容判断、Application 初始化事务边界、Windows Reader 命令状态及对应规范/测试。不得重建数据库格式、合并分叉内容、修改 MAUI/Android、发布安装包或直接操作生产数据。不可证明的冲突不自动选库。

## P0 / 兼容性影响

Wiki 数据目录属于 P0。解析仅接受严格覆盖关系；任何缺页、旧页版本较新、结构异常或 I/O 错误都保持 fail-closed。退役先原子改名为同根恢复目录，初始化失败时改回，成功后再清理。

## 方案与待决问题

在 `LegacyWikiCompatibility` 增加只读覆盖检查与可回滚退役结果。页面按主键及 `LastModified` 验证，重定向与资源按主键验证；目标计数不得更少且必须存在更新证据。Application 在目标内容库成功初始化后提交清理，异常时回滚旧目录。更新兼容 spec，把“任意双目录必失败”收窄为“不可证明覆盖时必失败”。

## 文件变更

- MODIFY `src/Terraria_Wiki.Core/Compatibility/LegacyWikiCompatibility.cs`
- MODIFY `src/Terraria_Wiki.Application/UseCases/ActiveWikiUseCases.cs`
- MODIFY `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs`
- MODIFY Core/Application/Windows 对应测试
- MODIFY `openspec/specs/wiki-data-compatibility/spec.md` 与相关 Presentation 契约

## 执行步骤

- [x] 实现目标库覆盖证明与旧目录可回滚退役；隔离 fixture 验证成功、拒绝及回滚。
- [x] 将提交/回滚接入初始化与 Wiki 切换事务；验证失败不丢数据。
- [x] 禁止未绑定 Reader 被打开；补 Presentation 回归。
- [x] 更新能力规范和计划记录。
- [x] 在 Windows 执行 Release 相关测试、完整构建/测试和隔离 smoke。
- [x] 检查差异、临时文件与 Git 状态，提交并归档 change。

## 验证与回归计划

WSL 执行 `git diff --check` 和只读生产数据证据核对。Windows `dotnet.exe` 执行 Core/Application/Windows 定向测试、solution Release build、全量 tests。用临时小型数据库构造严格覆盖及非覆盖冲突；不复制 3.8 GB 生产库，不把 Linux 结果声明为 Windows 验证。

## 风险与回退

最大风险是错误删除较新旧库。覆盖检查任一失败即保留原冲突；退役目录删除前可原名恢复；Application 初始化失败必须回滚。代码回退到当前 HEAD 即恢复原 fail-closed 行为。生产旧目录只在用户运行已验证版本后才会处理，本次开发验证不触碰它。

## 进度记录

- 2026-09-10：完成日志、配置与两份生产库只读诊断，确认目标库严格覆盖旧库的关键身份集合；开始实施。
- 2026-09-11：完成 Core/Application/Presentation 实现与 28/28、19/19、28/28 定向回归；Windows 全量 .NET tests 336/336 通过。
- 2026-09-11：隔离 Windows GUI smoke 通过，最终 Release solution build 通过；临时 fixture 已删除，生产数据库保持只读未修改。

## 发现、偏差与决策

- 已确认：切换数据根失败不是路径选择器问题，而是根内双目录冲突。
- 已确认：旧布局并非唯一有效数据；目标布局更完整，不能切回旧库覆盖目标。
- 决策：仅对可证明目标覆盖旧布局的案例自动完成接管；其他冲突继续失败关闭。
- 发现：SQLite 对不完整 WAL/SHM 组合的只读打开仍可能生成 `-shm`；覆盖检查在打开前拒绝 sidecar 不成对或非空 WAL，原冲突 hash 回归保持不变。

## 验证结果

- Windows Release 定向测试通过：Core 28、Application 19、Windows Presentation 28。
- Windows 全量 .NET tests 336/336：Core 97、Application 56、WikiTools 37、Windows 146。
- Windows Release solution build 通过 9 项目、0 warning / 0 error（实现后首次编译；最终差异后将复跑）。
- 最终 Windows Release solution build 通过 9 项目、0 warning / 0 error。
- 隔离 Windows GUI smoke 成功：Search 1，CSS/JS/图片各 1，音频 4，DOM Ready 2516 ms，退出清理完成。
- `git diff --check` 通过；无本轮临时 fixture、无运行中的 Windows 客户端。生产旧/目标数据库仅做只读检查，未重命名、删除、checkpoint 或初始化。

## 完成摘要

已修复真实双布局被永久判冲突的问题：只有目标库完整覆盖旧库且页面版本不倒退时，旧目录才进入可回滚退役；目标 Content 初始化成功后清理，失败则恢复。不能证明覆盖或 sidecar 不安全时继续原样失败关闭。未绑定 Reader 的顶部 Wiki 命令保持禁用，不再产生空白等待面板。Release build 输出已更新；未执行 publish、安装、打包或生产数据迁移。
