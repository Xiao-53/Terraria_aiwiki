# Terraria AI Wiki — 项目基线

> 项目方向、P0 护栏、Phase 顺序和全局决策以 `docs/architecture/ARCHITECTURE-v0.6.md` 为准。本文件只记录当前长期基线与治理关系，不复制详细 Roadmap。

## 产品基线

Terraria AI Wiki 将现有 Terraria Wiki Offline 逐步改造成新的 Terraria AI 应用：复用成熟的本地 Wiki 数据核心，建立可移植 C# Core、薄 Application、新客户端 Presentation 和 Python Agent。

Windows 新版本已经确定不继续使用 .NET MAUI。Avalonia + NativeWebView 已通过 Phase 00 的 Windows 11 x64 工具链、SQLite/WAL/FTS5、Local Viewer、数据包、平台路径与发布硬门槛，成为已验证的首选客户端方向。WPF + Blazor Hybrid 仍只作为未来出现独立硬阻断后的回退，不并行建设第二套客户端。

本地 Wiki 数据是 Terraria 机制、物品、配方和进度问题的主要事实来源。远程 LLM 负责决定查询步骤、阅读相关页面、执行多步分析并组织回答；不能只依赖模型记忆回答事实问题。

项目不以继续扩展旧 Presentation 为目标，但必须保留其背后的 P0 数据、资源和 Local Viewer 能力，直到新产品完成接管并通过独立决策允许改变。

## 源码基线

现有源码位于 `terraria-wiki-offline/`，来源为 BigBearKing/terraria-wiki-offline。该目录是独立源码仓库，工作区规则、架构、规范、计划和 ADR 均位于其外部。

已核对的现有能力包括：

- MediaWiki 页面列表、页面内容和 Redirect 获取；
- 页面更新时间比较、增量更新和失败任务处理；
- HTML 清理、页面保存和搜索文本生成；
- SQLite 内容存储与 FTS5 搜索；
- 数据包导入、导出和旧数据兼容；
- Wiki 资源、本地页面展示、历史和收藏等应用能力；
- 多 Wiki 数据源所需的现有身份和配置基础。

这些是 brownfield 改造的真实基线。具体调用关系和平台行为仍须在对应 Phase 中以代码和运行验证为准。

当前源码导入/导出实现使用 `.pkg`，旧仓库 README 仍写 `.pak`。Phase 00 没有取得历史 `.pak` 样本，因此不对它作额外格式承诺；当前 `.pkg` golden 与独立 writer/oracle 契约已经验证。后续若出现 `.pak` 样本，仍须显式检查，不能静默把任一扩展名当作不存在。

## 技术与平台基线

### 现有源码事实

- C#、.NET 10
- .NET MAUI 单项目
- Blazor Hybrid / Razor
- SQLite、FTS5、sqlite-net-pcl
- HtmlAgilityPack
- MediaWiki API
- Python Agent，PydanticAI 优先

MAUI、Razor、MAUI XAML 和 `Platforms/*` 是旧源码宿主事实，不再是 Windows 目标技术承诺。现有 Razor Presentation 作为行为参考和过渡基线；Local Viewer 的 HTML/CSS/JS、页面与 WikiAsset 链路仍是需要保留的资产。

### 目标技术方向

- Portable C#/.NET 10 Wiki Core；
- 薄 Application 用例边界；
- Avalonia Presentation / client host（Phase 00 Windows 11 x64 已验证）；
- NativeWebView 承载现有 Local Wiki Viewer；
- Windows Platform Adapters 当前实现，Android Adapters 后续实现；
- Python Agent 业务逻辑与 Windows/Android Runtime 分离。

产品平台范围是 Windows（当前）与 Android（未来）；iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均为明确非目标。WSL/Linux 只承担开发与平台无关检查，不构成 Linux 产品支持。源码中存在某个平台 target、条件编译或框架能力，不代表该平台已经成为产品承诺；扩大平台范围必须由用户重新授权并更新架构决策。

Windows 当前开发只允许引入 .NET SDK、普通 NuGet 依赖和 WebView2 等 Windows 目标所需组件；不得为了 Windows 开发安装 MAUI workload、Android SDK、JDK 或 Emulator。Android workload、SDK 与 JDK 只有进入 Android Phase 后才安装；这类目标平台依赖可以延后和隔离，但无法由客户端框架消除。

主要开发执行者是安装在 WSL2 中的 Codex CLI。固定位置：

```text
Windows: C:\Users\Xiao\mypython\terraria_aiwiki
WSL:     /mnt/c/Users/Xiao/mypython/terraria_aiwiki
```

Codex CLI 必须从外层 WSL 工作区根启动，使根 `AGENTS.md` 被加载；不得默认从内层 `terraria-wiki-offline/` Git 仓库启动主会话。日常读取、编辑、搜索和平台无关检查在 WSL 中完成；Windows 客户端的运行、调试、构建和发布必须由 Windows 环境实际验证。

项目当前保留在 C 盘并通过 `/mnt/c` 访问。即使该位置的 WSL I/O 性能可能较低，也不得未经用户决定复制或迁移到 WSL home。必须同时考虑 Linux 工具语义与 Windows 文件系统的路径、大小写、换行和文件锁差异。

## 长期约束

- Existing Wiki Core 最大限度复用；新功能不复制搜索、数据库、同步、导入导出或 Viewer backend。
- Portable Core 不引用 MAUI、Avalonia、WPF、WinUI 或目标平台 UI API；Avalonia 只进入 Presentation/宿主。
- 新 Presentation 通过 Application 使用后端，不直接绑定数据库或平台 Runtime。
- Local Viewer Adapter、Wiki Tool Adapter 和平台 Adapter 依赖向内的 Application/Core 边界；Core 不依赖 WebView、loopback、Python Runtime 或平台 Adapter。
- AI 通过稳定的 Wiki Search / Read 边界获取本地事实，不直接建立第二套 Wiki 数据体系。
- 搜索继续使用现有 SQLite FTS5 能力并做局部修正，不重建搜索系统。
- Python Agent 业务逻辑与 Windows / Android Runtime 分离。
- Windows 新版本不使用 MAUI；旧 MAUI 项目保持 brownfield 基线，直到新客户端完成 P0 接管并获准退休。
- Vector Search、Embedding Retrieval、Vector Database 和第二套 LLM Wiki 数据库不属于项目范围。
- 变更必须小步、可验证、可回退；未验证设想不得写成已实现事实。

## 当前状态

Phase 00、Phase 01 与 Phase 02 已完成，验证记录位于 `openspec/changes/archive/phase-00-migration-baseline/plan.md`、`openspec/changes/archive/phase-01-portable-wiki-core/plan.md` 和 `openspec/changes/archive/phase-02-search-fix/plan.md`；Phase 03 尚未开始。Phase 01 已建立单一 `net10.0` portable Core、Core-only 测试面和旧 MAUI 兼容 façade，原位沿用 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db`。Phase 02 已在同一 SQLite FTS5 trigram 上固定七级 Search 排序、多词/短词语义、`MaxResults 1..100`、纯文本摘要与安全高亮，旧手动 UI 与未来 AI 共用同一 `WikiReader` 查询边界。Master Architecture v0.6 确定的 Phase 00–13 顺序和边界不变；Application、生产客户端、AI、Windows Runtime 与 Android 均未进入实施。

## 治理关系

| 内容 | 位置 |
|---|---|
| 项目入口与当前状态 | `README.md` |
| Agent 强制规则 | `AGENTS.md` |
| 读取顺序 | `INIT.md` |
| Master Architecture / Roadmap | `docs/architecture/ARCHITECTURE-v0.6.md` |
| 计划规范 | `.agent/PLANS.md` |
| 已确认能力契约 | `openspec/specs/` |
| 进行中计划与变更 | `openspec/changes/` |
| 已完成变更 | `openspec/changes/archive/` |
| 长期技术决策 | `docs/adr/` |

当事实或决策变化时，只更新负责该内容的文件；不要在多个入口文档复制并分别维护同一细节。
