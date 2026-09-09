# 重新规划 Windows 退出 MAUI 的客户端架构

- 状态：completed
- Phase：无；项目级架构重规划
- 执行环境：Codex 桌面会话读取/编辑；Windows/WSL 构建不适用（本变更仅文档）
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：Master Architecture、客户端 ADR、项目入口/基线/Agent 规则、下一 Phase draft
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`docs/adr/0001-use-avalonia-client.md`

## 背景与当前证据

用户确定 Windows 新版本不继续使用 .NET MAUI，原因是当前 Windows 开发不应再安装 MAUI/Android SDK/JDK/Emulator/大型 workload。任务要求在最大限度保留 C# Wiki 后端的前提下，选择更轻的 Windows 路线，并为未来 Android 保留低代码迁移成本。

已按 `INIT.md` 顺序读取根 README、v0.5 Master Architecture、`openspec/project.md`、`.agent/PLANS.md`、相关归档计划和原项目 README；并核对源码 project、启动/生命周期、数据库模型和服务、下载/数据包、Local Viewer、应用状态、Razor 页面和平台代码。

已确认：现有代码是单 MAUI Blazor Hybrid project；数据库模型和 `DatabaseService` 可移植性高；`DataService` 核心完整但与 `App.*`、MAUI 路径/设置/文件对话/通知混合；Viewer 资产可保留；Razor 页面直接访问全局数据库/服务；窗口/键盘/安全区等属于旧宿主。源码仓库基线提交为 `a1dd89981d7604006ec91955d4d19e93cb933ce1`，变更前后状态干净。

官方资料核对了 WPF Blazor Hybrid、Avalonia Windows/Android 支持与 NativeWebView、.NET for Android 的 workload/SDK/JDK 依赖。结论区分了框架支持表和本项目实测，不把文档能力当作验证结果。

## 目标与完成定义

- 用 v0.6 替代 v0.5 的 Windows MAUI 路线。
- 选择一个明确首选方案，并保留以硬门槛触发的单一回退。
- 保护 Existing Wiki Core、SQLite/FTS5、数据包和 Local Viewer。
- 把 Windows 当前工具链与 Android 后续工具链隔离。
- 给出可执行的 Phase 00 PoC/决策计划，不自动实施或安装工具链。
- 同步当前入口、基线和 Agent 规则，消除持久文档冲突。

## 前置条件

- 用户已明确授权重新评估和规划，并明确 Windows 不使用 MAUI。
- 原业务源码只读；本任务不授权实施迁移。
- 未发现进行中的其他 Phase 或相关 ADR。

## 范围与非目标

### 范围

- 新增 v0.6 Master Architecture。
- 新增客户端技术 ADR。
- 新增 Phase 00 migration baseline draft。
- 更新 README、INIT、AGENTS 和 `openspec/project.md` 当前方向。

### 非目标

- 不修改 `terraria-wiki-offline/`。
- 不安装 .NET、Avalonia、MAUI、Android SDK、JDK 或 Emulator。
- 不创建 PoC、不运行构建、不修复搜索、不提取 Core、不建设 AI/UI。
- 不把 Avalonia 标成已验证；ADR 接受带硬门槛的首选方向，Phase 00 失败时必须由新 ADR supersede。

## P0 / 兼容性影响

本变更没有修改 P0 实现或数据。v0.6 继续把抓取/更新、SQLite/FTS5、Redirect、资源、数据包、旧数据迁移、多 WikiBook 和 Local Viewer 列为强制保护边界，并把数据/Viewer 兼容加入框架接受门槛。

## 方案与待决问题

首选方案：Portable .NET 10 Wiki Core + thin Application + Avalonia Presentation + NativeWebView + per-platform adapters。

WPF + Blazor Hybrid 作为 Avalonia 的 PoC 硬门槛失败回退。它没有被选为主方案，因为 WPF 没有 Android Presentation 路线，会造成第二套宿主或未来重新引入 MAUI。Uno、WinUI、Electron/Tauri、Flutter/React Native 等方案的取舍记录在 ADR。

Avalonia、Viewer transport、SQLite provider、Windows 数据目录和发布形态仍需 Phase 00/01 实测收敛。

## 文件变更

- **ADD** `docs/architecture/ARCHITECTURE-v0.6.md`
- **ADD** `docs/adr/0001-use-avalonia-client.md`
- **ADD** `openspec/changes/phase-00-migration-baseline/plan.md`
- **MODIFY** `README.md`
- **MODIFY** `INIT.md`
- **MODIFY** `AGENTS.md`
- **MODIFY** `openspec/project.md`
- **KEEP** `docs/architecture/ARCHITECTURE-v0.5.md` 作为被替代历史版本
- **KEEP** `terraria-wiki-offline/**`，零业务源码改动

## 执行步骤

- [x] 读取治理、v0.5、项目基线、相关归档计划和真实源码。
- [x] 盘点 Core 可移植区域、MAUI/静态全局耦合和 Viewer/Razor 复用边界。
- [x] 使用官方资料比较 Avalonia、WPF、WinUI、Uno 和非 .NET 客户端路线。
- [x] 新增 v0.6，定义目标分层、迁移策略、Phase 00–13 路线和硬门槛。
- [x] 新增 accepted-with-gate ADR，记录决策、回退、后果和接受条件。
- [x] 新增 Phase 00 draft，只计划轻量工具链和代表性垂直 PoC。
- [x] 同步 README、INIT、AGENTS 和项目基线。
- [x] 检查链接、过时表述、Phase 映射和业务源码状态。

## 验证与回归计划

- 检查所有当前入口指向 v0.6。
- 检查 Phase 00–13 的名称、前置、出口和计划路径。
- 检查本地 Markdown 链接存在。
- 搜索当前文档中的旧 New MAUI/Windows MAUI/Phase 00–11 表述。
- 检查 `terraria-wiki-offline/` Git 状态和 diff。
- 不执行构建；把未验证的 Avalonia/Android 能力明确写成 PoC/后续 Phase。

## 风险与回退

- 架构首选尚未实测：ADR 明确采用前硬门槛，Phase 00 提供验证和 WPF supersede 回退。
- Presentation 技术变化可能诱发 Core 重写：v0.6 明确 Core 与 Viewer KEEP/MOVE/ADAPT 顺序和禁止复制。
- 当前入口与旧 v0.5 冲突：README、INIT、AGENTS、项目基线均已切到 v0.6；v0.5 只保留历史用途。
- Android 依赖被低估：文档明确 Android 最终仍需 workload/SDK/JDK，只是延后到 Phase 13。

## 进度记录

- 2026-08-20：完成治理、架构、源码和官方技术资料读取。
- 2026-08-20：收敛 Avalonia 首选、WPF 回退和分阶段迁移策略。
- 2026-08-20：完成 v0.6、ADR、Phase 00 draft 与入口/基线同步。
- 2026-08-20：完成静态链接、旧表述、Phase 路径和源码状态复核。

## 发现、偏差与决策

- 本次桌面 Windows 环境没有可调用的 `dotnet`，且 WSL 访问被拒绝；由于用户只要求规划，没有申请安装或扩大权限。
- v0.5 的 MAUI 路线属于项目级冲突，不能只在 Phase 计划中绕开，因此新增 v0.6 并同步当前权威入口。
- 新 Presentation 本来就是重建范围，旧 Razor 零修改复用不应高于 Android 路线和 Core 复用。
- Local Viewer 的资源 transport 未被提前改写；现有 loopback 和 NativeWebView 资源映射进入 PoC 比较。

## 验证结果

- 当前入口 README、INIT、AGENTS 和 `openspec/project.md` 均指向 `ARCHITECTURE-v0.6.md`。
- v0.6 包含 Phase 00–13 的唯一映射；Phase 00 draft 路径存在，其余 Phase 计划按治理规则留待实施前创建。
- 新增/修改文档中的本地 Markdown 链接全部解析成功。
- 当前文档未残留 New MAUI Presentation、Windows MAUI 构建或 Phase 00–11 旧路线；README 对 v0.5 的唯一引用明确标为历史版本。
- `terraria-wiki-offline/` 的 `git status --short` 和 `git diff --stat` 均无输出。
- 未执行构建、运行、SQLite/FTS5、Viewer、数据包或 publish 验证；这些已进入 Phase 00 draft。

## 完成摘要

已完成项目级重新评估与规划：Windows MAUI 路线被 v0.6 替代，首选 Avalonia + NativeWebView，现有 C# Core 与 Viewer 资产为强制复用边界，WPF 为证据触发的回退。下一步只能在用户批准后执行 `phase-00-migration-baseline`，不得把本次规划误报为框架或迁移已验证。
