# Terraria AI Wiki Workspace

这是一个基于现有 **Terraria Wiki Offline** 源码逐步建设的 Terraria AI 应用工作区，后续开发由 **WSL2 中安装的 Codex CLI** 执行。

外层仓库现在是唯一产品源码仓库，根级 `src/`、`tests/`、`agent/`、`assets/` 与 `Terraria_Wiki.Core.slnx` 构成产品基线。`terraria-wiki-offline/` 只作为固定到上游提交的只读参考 Submodule，不参与正常 build/runtime。

项目会继续复用成熟的 C# Wiki 获取、更新、SQLite、FTS5、导入导出、资源和离线阅读能力；在其上建立可移植 Wiki Core、薄 Application、新的客户端 Presentation 与 Python AI。目标不是给旧界面简单增加 Chat，也不是重写已有 Wiki Core。

Windows 新版本已经确定不再使用 .NET MAUI。当前首选路线是 **.NET 10 portable Core + Avalonia Presentation + NativeWebView**；Phase 00 垂直 PoC 已在 Windows 11 x64 通过宿主选择门槛，Phase 01 已完成单一 portable Core 提取与旧 MAUI façade 接入。产品当前目标仅为 Windows，未来只考虑 Android，除此之外的产品平台全部为非目标。Windows 开发不要求安装 MAUI workload、Android SDK、JDK 或 Emulator；Android 工具链只在后续 Android Phase 引入。

## 先从这里开始

在 WSL 中从外层工作区根启动 Codex：

```bash
cd /mnt/c/Users/Xiao/mypython/terraria_aiwiki
codex
```

不要从 `terraria-wiki-offline/` 参考 Submodule 启动主会话，否则会把参考仓库误当作产品根，并漏掉外层产品源码与 `AGENTS.md`。`AGENTS.md` 在 Codex 会话开始时加载；修改后请重启会话。

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
- 已完成 Phase 09 记录：[`openspec/changes/archive/phase-09-p0-ui-takeover/plan.md`](./openspec/changes/archive/phase-09-p0-ui-takeover/plan.md)
- Windows Viewer transport 决策：[`docs/adr/0003-use-loopback-local-viewer-transport.md`](./docs/adr/0003-use-loopback-local-viewer-transport.md)
- Wiki Tool transport 决策：[`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`](./docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md)
- AI Backend 决策：[`docs/adr/0005-use-pydantic-ai-openai-backend.md`](./docs/adr/0005-use-pydantic-ai-openai-backend.md)
- Windows AI Runtime 决策：[`docs/adr/0006-use-stdio-python-sidecar.md`](./docs/adr/0006-use-stdio-python-sidecar.md)
- 产品源码与参考仓库分离决策：[`docs/adr/0007-separate-product-source-from-reference.md`](./docs/adr/0007-separate-product-source-from-reference.md)
- 已完成产品源码分离记录：[`openspec/changes/archive/separate-product-from-reference/plan.md`](./openspec/changes/archive/separate-product-from-reference/plan.md)
- AI Backend 能力契约：[`openspec/specs/ai-backend/spec.md`](./openspec/specs/ai-backend/spec.md)
- Windows AI Runtime 能力契约：[`openspec/specs/windows-ai-runtime/spec.md`](./openspec/specs/windows-ai-runtime/spec.md)
- Windows Presentation 能力契约：[`openspec/specs/windows-presentation/spec.md`](./openspec/specs/windows-presentation/spec.md)
- Windows UI 规范与新页面工作流：[`.agents/skills/terraria-ui/SKILL.md`](./.agents/skills/terraria-ui/SKILL.md)
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

产品源码已从参考仓库迁到外层根级目录，Windows build/publish/runtime 不再读取固定上游 Submodule；迁移基线与 Phase 09 P0 UI Takeover 均已完成 Windows 回归，下一阶段是 Phase 10 Legacy MAUI Retirement。

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

**Phase 00–09 已完成。** Windows Avalonia Client 默认进入 AI-first Presentation，并由五个真实入口接管问答、收藏、Wiki 资料库、数据管理和设置。收藏/Reader、全部页面与历史、Wiki 切换与统计、下载更新、失败处理、数据库维护、`.pkg` 两阶段导入与安全导出、强类型设置和启动错误恢复均复用现有 Application/Core 与唯一共享 Reader；Presentation 不直接访问 SQLite。设置页现含常规与 AI 配置二级菜单，可编辑 DeepSeek 官方 API 或自定义 OpenAI-compatible provider、选择思考强度并在保存后重启当前 AI Runtime；配置继续使用严格 v2。活动数据变化会刷新依赖页面并按需重绑同一个动态 loopback Viewer；无页面数据时仍可进入管理和设置。Windows Release build 为 0 warning/0 error，当前 .NET 测试 274/274 通过；Light/Dark、1280×820、900×600、数据源确认和设置状态已在目标 Windows 实际窗口检查。真实 3.4 GB 旧 Wiki 数据启动恢复证据继续有效。多轮、内置 Python、Credential Manager、安装器、发布渠道和 Android 尚未完成，旧 MAUI 参考清理由 Phase 10 决定。

## 工作区结构

```text
terraria_aiwiki/
├─ README.md                         # 人类与新会话入口
├─ AGENTS.md                         # Agent 强制规则
├─ INIT.md                           # 读取顺序
├─ Terraria_Wiki.Core.slnx           # 产品 solution
├─ src/                              # C# Core/Application/Windows/WikiTools
├─ tests/                            # .NET 自动化测试
├─ agent/                            # Python Agent 与冻结依赖
├─ assets/windows/                   # Windows Viewer、bridge 与图标快照
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
│     ├─ 0006-use-stdio-python-sidecar.md # Windows AI Runtime
│     └─ 0007-separate-product-source-from-reference.md # 源码所有权
├─ openspec/
│  ├─ project.md                     # 技术与产品基线
│  ├─ specs/                         # 已确认能力契约
│  └─ changes/                       # 进行中与已归档变更
└─ terraria-wiki-offline/            # 只读上游参考 Submodule
```

## 开发环境与平台分工

产品源码位于外层根级目录；参考 Submodule 保存 BigBearKing 上游的 .NET MAUI/Blazor Hybrid 旧宿主，仅用于行为与来源核对。MAUI/Blazor 是旧宿主事实，不参与当前 Windows 产品构建。

新目标客户端保留 .NET 10、SQLite/FTS5、HtmlAgilityPack、MediaWiki API 和现有 Local Viewer 资产，使用 Avalonia + NativeWebView。Phase 04 已把动态 loopback Viewer、Windows data-root/设置/锁 adapters 和最小搜索阅读壳接入生产项目；Phase 05 已把无状态 loopback MCP Wiki Tools 隔离为只依赖 Application 的 Adapter；Phase 07 已实现并实测 Windows stdio Python Sidecar 与发布复制，当前 Windows 用户级配置为严格 v2；Phase 08 已用 XAML、内部状态模型、安全原生 Markdown 和唯一共享 Reader 重建 AI/Wiki 核心 Presentation；Phase 09 已完成收藏、资料库、数据管理、设置、导入导出、维护和错误流程的 P0 UI 接管。设置页随后增补了 DeepSeek 官方 API / 自定义 OpenAI-compatible provider 与思考强度配置入口，仍复用同一 v2 和显式 Runtime 重试。内置 Python、Credential Manager、安装器、WebView2 bootstrap 与发布渠道仍须在对应阶段完成。

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
