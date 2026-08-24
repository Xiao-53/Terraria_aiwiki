# Terraria AI Wiki Workspace

这是一个基于现有 **Terraria Wiki Offline** 源码逐步建设的 Terraria AI 应用工作区，后续开发由 **WSL2 中安装的 Codex CLI** 执行。

项目会继续复用成熟的 C# Wiki 获取、更新、SQLite、FTS5、导入导出、资源和离线阅读能力；在其上建立可移植 Wiki Core、薄 Application、新的客户端 Presentation 与 Python AI。目标不是给旧界面简单增加 Chat，也不是重写已有 Wiki Core。

Windows 新版本已经确定不再使用 .NET MAUI。当前首选路线是 **.NET 10 portable Core + Avalonia Presentation + NativeWebView**；Phase 00 垂直 PoC 已在 Windows 11 x64 通过宿主选择门槛，Phase 01 已完成单一 portable Core 提取与旧 MAUI façade 接入。产品当前目标仅为 Windows，未来只考虑 Android，除此之外的产品平台全部为非目标。Windows 开发不要求安装 MAUI workload、Android SDK、JDK 或 Emulator；Android 工具链只在后续 Android Phase 引入。

## 先从这里开始

在 WSL 中从外层工作区根启动 Codex：

```bash
cd /mnt/c/Users/Xiao/mypython/terraria_aiwiki
codex
```

不要从内层 `terraria-wiki-offline/` Git 仓库启动主会话，否则外层 `AGENTS.md` 可能不在 Codex 的指令发现范围内。`AGENTS.md` 在 Codex 会话开始时加载；修改后请重启会话。

- 项目级方向、P0 护栏和 Phase 路线：[`docs/architecture/ARCHITECTURE-v0.6.md`](./docs/architecture/ARCHITECTURE-v0.6.md)
- Agent 强制工作规则：[`AGENTS.md`](./AGENTS.md)
- 新会话读取顺序：[`INIT.md`](./INIT.md)
- 技术与产品基线：[`openspec/project.md`](./openspec/project.md)
- 复杂任务计划规范：[`.agent/PLANS.md`](./.agent/PLANS.md)
- 客户端技术决策：[`docs/adr/0001-use-avalonia-client.md`](./docs/adr/0001-use-avalonia-client.md)
- Core 与数据根决策：[`docs/adr/0002-portable-core-and-data-root.md`](./docs/adr/0002-portable-core-and-data-root.md)
- 已完成 Phase 00 记录：[`openspec/changes/archive/phase-00-migration-baseline/plan.md`](./openspec/changes/archive/phase-00-migration-baseline/plan.md)
- 已完成 Phase 01 记录：[`openspec/changes/archive/phase-01-portable-wiki-core/plan.md`](./openspec/changes/archive/phase-01-portable-wiki-core/plan.md)
- 已完成 Phase 02 记录：[`openspec/changes/archive/phase-02-search-fix/plan.md`](./openspec/changes/archive/phase-02-search-fix/plan.md)
- 已完成 Phase 03 记录：[`openspec/changes/archive/phase-03-application-boundary/plan.md`](./openspec/changes/archive/phase-03-application-boundary/plan.md)
- 已完成 Phase 04 记录：[`openspec/changes/archive/phase-04-windows-client-foundation/plan.md`](./openspec/changes/archive/phase-04-windows-client-foundation/plan.md)
- Windows Viewer transport 决策：[`docs/adr/0003-use-loopback-local-viewer-transport.md`](./docs/adr/0003-use-loopback-local-viewer-transport.md)
- 持续能力契约：[`openspec/specs/`](./openspec/specs/)

Master Architecture 只保留项目方向、P0、分层和 Phase 大纲。持续能力契约进入 `openspec/specs/`；SQL、具体类和文件修改、接口签名、依赖选型、详细测试步骤与打包实现必须基于真实代码进入对应的 `openspec/changes/<change-id>/plan.md`。

## 产品方向

```text
New Avalonia Presentation
          │
          ▼
     Application
      ┌───┴────┐
      ▼        ▼
Existing    AI / Agent
Wiki Core      │
      ▲        │
      └─ Wiki Search / Read boundary
```

本地 Wiki 是 Terraria 事实查询的主要来源。典型链路为：

```text
用户问题
→ Agent 搜索本地 Wiki
→ 读取相关页面
→ 必要时继续查询
→ 基于 Wiki 事实回答并提供来源
```

项目长期采用：

```text
Reuse → Fix → Wrap → Extract when necessary → Rewrite last
```

不建设 Vector Search、Embedding Retrieval、Vector Database，也不建立第二套 LLM Wiki 数据库。

## P0 护栏

任何阶段都必须保护完整 Wiki 数据生命周期及其消费者，包括：

- Wiki 获取、Redirect、增量更新和失败处理；
- 内容清理、SQLite、搜索索引和现有 FTS5 搜索；
- 数据包导入、导出、迁移和维护；
- Wiki 资源、多数据源和 Local Viewer backend；
- 手动搜索、离线阅读和未来 AI 对同一份本地数据的复用。

旧 UI 可以被新 Presentation 逐项接管，但隐藏入口不等于删除底层能力。完整边界以 Master Architecture 为准。

## 当前路线与状态

```text
Migration Baseline & Host Spike
→ Portable Wiki Core
→ Search Fix
→ Application Boundary
→ Windows Client Foundation
→ AI Adapter
→ AI Backend
→ Windows Runtime Smoke Test
→ Presentation Rebuild
→ P0 UI Takeover
→ Legacy MAUI Retirement
→ Windows Productization
→ Extensions
→ Android
```

**Phase 00–04 已完成，Phase 05 尚未开始。** Phase 04 已在内层仓库新增正式 `Terraria_Wiki.Windows` Avalonia WinExe 与独立 tests：Windows Host 可确认原位 data root、阻止旧/新客户端并发、组合唯一 `WikiApplication`，并完成搜索、选择结果、NativeWebView 页面、CSS/JS、图片、音频、Redirect/anchor、返回与主题最小链路。动态 loopback Viewer 和 framework-dependent/self-contained `win-x64` 产物均已在 Windows 11 x64 实测；当前客户端仍是最小 Foundation，不包含 AI、正式导航/视觉、完整 P0 管理 UI、安装器或 Android。

## 工作区结构

```text
terraria_aiwiki/
├─ README.md                         # 人类与新会话入口
├─ AGENTS.md                         # Agent 强制规则
├─ INIT.md                           # 读取顺序
├─ .agent/
│  └─ PLANS.md                       # 计划格式与生命周期
├─ docs/
│  ├─ architecture/
│  │  ├─ ARCHITECTURE-v0.5.md        # 被替代的历史版本
│  │  └─ ARCHITECTURE-v0.6.md        # 当前 Master Architecture / Roadmap
│  └─ adr/
│     ├─ 0001-use-avalonia-client.md # 客户端技术决策与验证门槛
│     ├─ 0002-portable-core-and-data-root.md # Core 与数据根决策
│     └─ 0003-use-loopback-local-viewer-transport.md # Windows Viewer transport
├─ openspec/
│  ├─ project.md                     # 技术与产品基线
│  ├─ specs/                         # 已确认能力契约
│  └─ changes/                       # 进行中与已归档变更
└─ terraria-wiki-offline/            # 现有源码仓库
```

## 开发环境与平台分工

现有源码位于 `terraria-wiki-offline/`，它的基线技术包括 C#、.NET 10、.NET MAUI、Blazor Hybrid / Razor、SQLite / FTS5、HtmlAgilityPack 和 MediaWiki API。MAUI/Blazor 是旧宿主事实，不再是 Windows 目标架构。

新目标客户端保留 .NET 10、SQLite/FTS5、HtmlAgilityPack、MediaWiki API 和现有 Local Viewer 资产，使用 Avalonia + NativeWebView。Phase 04 已把动态 loopback Viewer、Windows data-root/设置/锁 adapters 和最小搜索阅读壳接入生产项目；依赖升级、安装器、WebView2 bootstrap 与发布渠道仍须在对应后续 Phase 验证。

| 环境 | 职责 |
|---|---|
| WSL2 + Codex CLI | 主要开发会话、代码阅读、搜索、编辑、计划维护、测试与平台无关检查 |
| Windows | .NET/Avalonia 客户端构建、运行、调试、发布和目标平台验证；不安装 MAUI/Android 工具链 |
| Android 环境 | 仅在后续 Phase 安装 .NET Android workload、SDK/JDK，并在物理设备或必要的模拟器验证 |

固定路径：

```text
Windows: C:\Users\Xiao\mypython\terraria_aiwiki
WSL:     /mnt/c/Users/Xiao/mypython/terraria_aiwiki
```

产品平台范围是 Windows（当前）与 Android（未来）；iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均为明确非目标。WSL/Linux 只是开发执行环境，不代表 Linux 产品支持。项目目前以 Windows C 盘目录为唯一事实源。虽然 `/mnt/c` 的 I/O 可能慢于 WSL home，但 Codex 不得自行复制或迁移项目。WSL 中的检查不能被描述为 Windows 客户端 build/run/publish 通过，Android 支持也必须以后续目标平台实测为准。
