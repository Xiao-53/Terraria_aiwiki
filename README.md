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
- 已完成 Phase 05 记录：[`openspec/changes/archive/phase-05-ai-adapter/plan.md`](./openspec/changes/archive/phase-05-ai-adapter/plan.md)
- 已完成 Phase 06 记录：[`openspec/changes/archive/phase-06-ai-backend/plan.md`](./openspec/changes/archive/phase-06-ai-backend/plan.md)
- 已完成 Phase 07 记录：[`openspec/changes/archive/phase-07-windows-runtime-smoke-test/plan.md`](./openspec/changes/archive/phase-07-windows-runtime-smoke-test/plan.md)
- 已完成 Phase 08 记录：[`openspec/changes/archive/phase-08-presentation-rebuild/plan.md`](./openspec/changes/archive/phase-08-presentation-rebuild/plan.md)
- 进行中 Phase 09 计划：[`openspec/changes/phase-09-p0-ui-takeover/plan.md`](./openspec/changes/phase-09-p0-ui-takeover/plan.md)
- Windows Viewer transport 决策：[`docs/adr/0003-use-loopback-local-viewer-transport.md`](./docs/adr/0003-use-loopback-local-viewer-transport.md)
- Wiki Tool transport 决策：[`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`](./docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md)
- AI Backend 决策：[`docs/adr/0005-use-pydantic-ai-openai-backend.md`](./docs/adr/0005-use-pydantic-ai-openai-backend.md)
- Windows AI Runtime 决策：[`docs/adr/0006-use-stdio-python-sidecar.md`](./docs/adr/0006-use-stdio-python-sidecar.md)
- AI Backend 能力契约：[`openspec/specs/ai-backend/spec.md`](./openspec/specs/ai-backend/spec.md)
- Windows AI Runtime 能力契约：[`openspec/specs/windows-ai-runtime/spec.md`](./openspec/specs/windows-ai-runtime/spec.md)
- Windows Presentation 能力契约：[`openspec/specs/windows-presentation/spec.md`](./openspec/specs/windows-presentation/spec.md)
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

**Phase 00–08 已完成，Phase 09 正在进行。** Windows Avalonia Client 默认进入 AI-first Presentation；当前首个 Phase 09 切片已把左侧星标入口改为统一收藏页，接通显式 AI 单轮收藏与现有 Wiki 收藏，并为共享 Reader 增加顶部 Wiki 入口、页面星标和有安全限位的可调分栏。顶部 Wiki 只展开 Reader，拖动分隔条不会自动关闭；启动失败时可重新选择数据文件夹，旧数据遗留 WAL 会先安全 checkpoint。Reader 默认首页已可直接收藏；Windows 用户级 `ai-runtime.json` v2 可保存 provider、Python 路径和本机 API key，并仅向所属 Sidecar 环境注入密钥。Chat 思考态已改为轻量连续内容流，并以预览 Enter 发送、Shift+Enter 换行；结果区显示输入、输出与合计 Token 花费。Agent 不再设置美元费用预算，防循环、协议长度、context 与超时运行限位继续生效。普通问答仍不自动持久化，Viewer 仍只有一个。Windows Release build 为 0 warning/0 error，当前 .NET 测试 249/249 通过；真实 3.4 GB 旧 Wiki 数据已完成启动恢复验证。完整 P0 管理 UI、多轮、内置 Python、安装器和 Android 尚未完成。

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
│     ├─ 0003-use-loopback-local-viewer-transport.md # Windows Viewer transport
│     ├─ 0004-use-loopback-mcp-wiki-tool-transport.md # Wiki Tool transport
│     ├─ 0005-use-pydantic-ai-openai-backend.md # AI Backend
│     └─ 0006-use-stdio-python-sidecar.md # Windows AI Runtime
├─ openspec/
│  ├─ project.md                     # 技术与产品基线
│  ├─ specs/                         # 已确认能力契约
│  └─ changes/                       # 进行中与已归档变更
└─ terraria-wiki-offline/            # 现有源码仓库
```

## 开发环境与平台分工

现有源码位于 `terraria-wiki-offline/`，它的基线技术包括 C#、.NET 10、.NET MAUI、Blazor Hybrid / Razor、SQLite / FTS5、HtmlAgilityPack 和 MediaWiki API。MAUI/Blazor 是旧宿主事实，不再是 Windows 目标架构。

新目标客户端保留 .NET 10、SQLite/FTS5、HtmlAgilityPack、MediaWiki API 和现有 Local Viewer 资产，使用 Avalonia + NativeWebView。Phase 04 已把动态 loopback Viewer、Windows data-root/设置/锁 adapters 和最小搜索阅读壳接入生产项目；Phase 05 已把无状态 loopback MCP Wiki Tools 隔离为只依赖 Application 的 Adapter；Phase 07 已实现并实测 Windows stdio Python Sidecar 与发布复制，当前 Windows 用户级配置为严格 v2；Phase 08 已用 XAML、内部状态模型、安全原生 Markdown 和唯一共享 Reader 重建 AI/Wiki 核心 Presentation；Phase 09 正以小切片接管 P0 UI，当前已实现统一收藏页、Chat/Wiki 收藏和受限可调 Reader 分栏。内置 Python、安装器、WebView2 bootstrap 与发布渠道仍须在对应阶段完成。

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
