# Terraria AI Wiki Workspace

这是一个基于现有 **Terraria Wiki Offline** 源码逐步建设的 Terraria AI 应用工作区，后续开发由 **WSL2 中安装的 Codex CLI** 执行。

当前仓库是唯一产品源码仓库，根级 `src/`、`tests/`、`agent/`、`assets/` 与 `Terraria_Wiki.Core.slnx` 构成产品基线。旧 MAUI 参考 Submodule 已在 Phase 10 退休；上游 URL、固定提交、资产来源和回退方式保留在 ADR 0007 与归档计划中。

项目会继续复用成熟的 C# Wiki 获取、更新、SQLite、FTS5、导入导出、资源和离线阅读能力；在其上建立可移植 Wiki Core、薄 Application、新的客户端 Presentation 与 Python AI。目标不是给旧界面简单增加 Chat，也不是重写已有 Wiki Core。

Windows 新版本已经确定不再使用 .NET MAUI。当前首选路线是 **.NET 10 portable Core + Avalonia Presentation + NativeWebView**；Phase 00 垂直 PoC 已在 Windows 11 x64 通过宿主选择门槛，Phase 01 已完成单一 portable Core 提取与旧 MAUI façade 接入。产品当前目标仅为 Windows，未来只考虑 Android，除此之外的产品平台全部为非目标。Windows 开发不要求安装 MAUI workload、Android SDK、JDK 或 Emulator；已暂缓的 Phase 13 获用户重新明确授权前不引入 Android 工具链。

## 先从这里开始

在 WSL 中从外层工作区根启动 Codex：

```bash
cd /mnt/c/Users/Xiao/mypython/terraria_aiwiki
codex
```

`AGENTS.md` 在 Codex 会话开始时加载；修改后请重启会话。

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
- 已完成 Phase 10 记录：[`openspec/changes/archive/phase-10-legacy-maui-retirement/plan.md`](./openspec/changes/archive/phase-10-legacy-maui-retirement/plan.md)
- Windows Viewer transport 决策：[`docs/adr/0003-use-loopback-local-viewer-transport.md`](./docs/adr/0003-use-loopback-local-viewer-transport.md)
- Wiki Tool transport 决策：[`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`](./docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md)
- AI Backend 决策：[`docs/adr/0005-use-pydantic-ai-openai-backend.md`](./docs/adr/0005-use-pydantic-ai-openai-backend.md)
- Windows AI Runtime 决策：[`docs/adr/0006-use-stdio-python-sidecar.md`](./docs/adr/0006-use-stdio-python-sidecar.md)
- 产品源码与参考仓库分离决策：[`docs/adr/0007-separate-product-source-from-reference.md`](./docs/adr/0007-separate-product-source-from-reference.md)
- Windows GitHub Releases 分发决策：[`docs/adr/0008-use-github-releases-windows-distribution.md`](./docs/adr/0008-use-github-releases-windows-distribution.md)
- 正在实施的 Phase 11 记录：[`openspec/changes/phase-11-windows-productization/plan.md`](./openspec/changes/phase-11-windows-productization/plan.md)
- Windows 产品化能力契约：[`openspec/specs/windows-productization/spec.md`](./openspec/specs/windows-productization/spec.md)
- Windows 发布与本地验收手册：[`docs/release/windows.md`](./docs/release/windows.md)
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

产品源码已迁到当前仓库根级目录，Windows build/publish/runtime 不读取旧 MAUI checkout；Phase 10 已移除参考 gitlink。Phase 11 Windows Productization 正在实施，本地产品化实现与 Windows 11 验证已完成，公开 GitHub Release 所需输入和外部渠道矩阵仍待关闭。

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

暂缓：Android（Phase 13；须用户重新明确授权后恢复）
```

**Phase 00–10 已完成，Phase 11 正在实施。** Windows Avalonia Client 的五个入口继续通过现有 Application/Core 与唯一共享 Reader 提供 P0 能力，Presentation 不直接访问 SQLite。Phase 11 已在本机 Windows 11 x64 完成普通每用户 Setup、portable zip、self-contained `win-x64` payload、内置 CPython 3.14.7、Credential Manager + 严格 `ai-runtime.json` v3、Evergreen WebView2 前置/恢复、显式 GitHub stable 更新状态和有界诊断实现；本地 N→N+1、同版本 repair、安装/portable smoke、卸载、Light/Dark 与宽窄窗口验收均通过。最终 Windows Release 构建为 0 warning/0 error，292/292 .NET tests 通过。公开发行仍等待目标 GitHub `owner/repo`、正式版本/tag、项目软件许可证和素材权利核对；GitHub 渠道矩阵、WebView2 缺失干净机与 Windows 10 条件门槛尚未执行，因此当前本地制品不标记为 `publicationReady`，也不宣称 Phase 11 已完成。Phase 12 仅按真实需求决定是否实施；Phase 13 Android 已暂缓。

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
│     ├─ 0007-separate-product-source-from-reference.md # 源码所有权
│     └─ 0008-use-github-releases-windows-distribution.md # Windows 分发
├─ openspec/
│  ├─ project.md                     # 技术与产品基线
│  ├─ specs/                         # 已确认能力契约
│  └─ changes/                       # 进行中与已归档变更
```

## 开发环境与平台分工

产品源码位于仓库根级目录；BigBearKing 上游 .NET MAUI/Blazor Hybrid 旧宿主只通过 ADR 0007 记录的 URL 与固定提交追溯，不保留在当前 checkout。MAUI/Blazor 是旧宿主事实，不参与当前 Windows 产品构建。

新目标客户端保留 .NET 10、SQLite/FTS5、HtmlAgilityPack、MediaWiki API 和现有 Local Viewer 资产，使用 Avalonia + NativeWebView。Phase 04–10 已完成 Windows Foundation、Wiki Tools、AI Runtime、Presentation、P0 UI 接管和旧 MAUI 参考退休。Phase 11 将 stdio Sidecar 固定到只读内置 CPython Runtime，用户级配置升级为严格 v3，API key 存入 Windows Credential Manager；同一 self-contained payload 经 Velopack 生成普通 Setup、portable 和完整/增量更新制品，应用只从公开 GitHub Releases 的 stable channel 显式检查更新。WebView2 继续使用 Evergreen Runtime，其用户数据固定在应用安装目录之外。公开发布参数和外部渠道验证仍按当前 Phase 11 计划关闭。

| 环境 | 职责 |
|---|---|
| WSL2 + Codex CLI | 主要开发会话、代码阅读、搜索、编辑、计划维护、测试与平台无关检查 |
| Windows | .NET/Avalonia 客户端构建、运行、调试、发布和目标平台验证；不安装 MAUI/Android 工具链 |
| Android 环境 | Phase 13 已暂缓；只有用户重新明确授权恢复后，才安装 .NET Android workload、SDK/JDK 并在物理设备或必要的模拟器验证 |

固定路径：

```text
Windows: C:\Users\Xiao\mypython\terraria_aiwiki
WSL:     /mnt/c/Users/Xiao/mypython/terraria_aiwiki
```

产品平台范围是 Windows（当前）与 Android（未来）；iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均为明确非目标。WSL/Linux 只是开发执行环境，不代表 Linux 产品支持。项目目前以 Windows C 盘目录为唯一事实源。虽然 `/mnt/c` 的 I/O 可能慢于 WSL home，但 Codex 不得自行复制或迁移项目。WSL 中的检查不能被描述为 Windows 客户端 build/run/publish 通过，Android 支持也必须以后续目标平台实测为准。
