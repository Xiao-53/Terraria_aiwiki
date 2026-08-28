# Terraria AI Wiki — 项目基线

> 项目方向、P0 护栏、Phase 顺序和全局决策以 `docs/architecture/ARCHITECTURE-v0.6.md` 为准。本文件只记录当前长期基线与治理关系，不复制详细 Roadmap。

## 产品基线

Terraria AI Wiki 将现有 Terraria Wiki Offline 逐步改造成新的 Terraria AI 应用：复用成熟的本地 Wiki 数据核心，建立可移植 C# Core、薄 Application、新客户端 Presentation 和 Python Agent。

Windows 新版本已经确定不继续使用 .NET MAUI。Avalonia + NativeWebView 已通过 Phase 00 的宿主选择门槛，并在 Phase 04 进入正式 Windows WinExe；动态 loopback Viewer、Application 组合、data-root/设置/锁 adapters 和两种 `win-x64` 发布形态已在 Windows 11 x64 验证。WPF + Blazor Hybrid 仍只作为未来出现独立硬阻断后的回退，不并行建设第二套客户端。

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
- Python 3.12–3.14 Agent，PydanticAI 2.31 与可配置 OpenAI-compatible Responses / Chat provider 边界

MAUI、Razor、MAUI XAML 和 `Platforms/*` 是旧源码宿主事实，不再是 Windows 目标技术承诺。现有 Razor Presentation 作为行为参考和过渡基线；Local Viewer 的 HTML/CSS/JS、页面与 WikiAsset 链路仍是需要保留的资产。

### 目标技术方向

- Portable C#/.NET 10 Wiki Core；
- 薄 Application 用例边界；
- Avalonia Presentation / client host（Phase 04 已建立正式 Windows Foundation）；
- NativeWebView 通过受限动态 loopback 承载现有 Local Wiki Viewer；
- Windows data-root、设置、锁、外链和 Viewer Adapters 已实现最小链路，Android Adapters 后续实现；
- 独立 Wiki Tool Adapter 通过无状态、动态 loopback MCP 只暴露 Application Search / Read，并由 Phase 07 Windows Runtime 在 Viewer 之后按需启停；
- Python 单轮 Agent 通过受限 MCP 执行 Search → Read → typed Answer，实际来源由成功 Read 派生；业务逻辑与 Windows/Android Runtime 分离；
- Windows Avalonia Host 通过严格 NDJSON v1 长驻 stdio Sidecar 组合 WikiTools 与 Python Agent，使用外部 Python 环境和用户级 `ai-runtime.json` v2；Host 将其中的 key 仅注入所属 Sidecar 环境，AI 故障不影响离线 Search/Viewer。
- Windows Presentation 使用内部状态模型、XAML Chat/Wiki/Reader、安全原生 Markdown 和唯一共享 NativeWebView；宽屏默认 AI/Reader 双栏，窄屏按需覆盖 Reader，不引入第二套 Viewer 或公共 Application UI port。

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

Phase 00–08 已完成；Phase 09 P0 UI Takeover 正在进行。首个切片已用左侧统一收藏页接管显式 AI 单轮收藏与既有 Wiki 收藏，并为唯一共享 Reader 增加顶部 Wiki 入口、页面星标和有安全限位的可调分栏；默认 Reader 首页已可直接收藏，顶部 Wiki 只展开 Reader，分隔拖动不自动关闭。Chat 思考态使用轻量连续内容流，预览 Enter 发送且 Shift+Enter 换行；结果区显示输入、输出与合计 Token 花费。Agent 已取消美元费用预算，仍保留防循环、协议长度、context 与超时运行限位。启动失败可重新选择数据根，旧内容数据库 WAL/SHM 通过 SQLite checkpoint 安全合并后再进入既有兼容迁移；公共 API、SQLite schema 与 Viewer 协议不变。Windows 用户级 AI 配置已升级为 v2，本机 key 只注入所属 Sidecar 环境。当前 Windows Release build 为 0 warning/0 error，.NET 测试 249/249 通过，真实 3.4 GB 旧 Wiki 数据已完成启动恢复。完整 P0 UI 接管、多轮、内置 Python、安装器、Windows Productization 与 Android 尚未实施。

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
