# Terraria AI Wiki — 项目基线

> 项目方向、P0 护栏、Phase 顺序和全局决策以 `docs/architecture/ARCHITECTURE-v0.6.md` 为准。本文件只记录当前长期基线与治理关系，不复制详细 Roadmap。

## 产品基线

Terraria AI Wiki 将现有 Terraria Wiki Offline 逐步改造成新的 Terraria AI 应用：复用成熟的本地 Wiki 数据核心，建立可移植 C# Core、薄 Application、新客户端 Presentation 和 Python Agent。

Windows 新版本已经确定不继续使用 .NET MAUI。Avalonia + NativeWebView 已通过 Phase 00 的宿主选择门槛，并在 Phase 04 进入正式 Windows WinExe；动态 loopback Viewer、Application 组合、data-root/设置/锁 adapters 和两种 `win-x64` 发布形态已在 Windows 11 x64 验证。WPF + Blazor Hybrid 仍只作为未来出现独立硬阻断后的回退，不并行建设第二套客户端。

本地 Wiki 数据是 Terraria 机制、物品、配方和进度问题的主要事实来源。远程 LLM 负责决定查询步骤、阅读相关页面、执行多步分析并组织回答；不能只依赖模型记忆回答事实问题。

项目不以继续扩展旧 Presentation 为目标。Phase 09 已完成新产品接管，Phase 10 只退休旧宿主参考；其背后的 P0 数据、资源和 Local Viewer 能力继续由当前 Core/Application/Windows 产品保留。

## 源码基线

外层仓库是唯一产品源码仓库。根级 `src/`、`tests/`、`agent/`、`assets/` 与 `Terraria_Wiki.Core.slnx` 以产品提交 `d67d904` 建立内容基线；namespace、公共 API、数据格式和产品行为未因物理迁移改变。

旧宿主来源为 BigBearKing/terraria-wiki-offline 上游 `a1dd899`。Phase 10 已从当前 checkout 移除其参考 gitlink；旧实现、P0 行为和资产来源继续通过固定 URL/提交、ADR 0007、归档计划与资产说明追溯。产品 project、tests、build、publish 与 runtime 不得读取任何外部参考 checkout。

已核对的现有能力包括：

- MediaWiki 页面列表、页面内容和 Redirect 获取；
- 页面更新时间比较、增量更新和失败任务处理；
- HTML 清理、页面保存和搜索文本生成；
- SQLite v2 内容存储、章节/区域记录与 contentless FTS5 搜索；
- 活动 Wiki 单文件数据库导入导出、格式/身份校验和中断恢复；
- Wiki 资源、本地页面展示、页面收藏与 Reader 内存返回等应用能力；旧持久 WikiHistory 仅兼容保留并停止读写；
- 多 Wiki 数据源所需的现有身份和配置基础。

这些是 brownfield 改造的真实基线。具体调用关系和平台行为仍须在对应 Phase 中以代码和运行验证为准。

ADR 0012 已把开发期旧内容格式、`.pkg` 容器及其迁移入口退休。Windows 普通 UI 继续按 ADR 0011 导入/导出当前活动 Wiki 的单文件 v2 `.db`，并校验格式、加工版本、Wiki 身份与搜索数据一致性。

## 技术与平台基线

### 上游参考事实

- C#、.NET 10
- .NET MAUI 单项目
- Blazor Hybrid / Razor
- SQLite、FTS5、sqlite-net-pcl
- HtmlAgilityPack
- MediaWiki API
- Python 3.12–3.14 Agent，PydanticAI 2.31 与可配置 OpenAI-compatible Responses / Chat provider 边界

MAUI、Razor、MAUI XAML 和 `Platforms/*` 是旧源码宿主事实，不再是 Windows 目标技术承诺。旧 Razor Presentation 曾作为行为参考和过渡基线，Phase 10 后不再保留本地 checkout；Local Viewer 的 HTML/CSS/JS、页面与 WikiAsset 链路继续作为受控产品资产保留。

当前产品源码使用 .NET 10 portable Core、薄 Application、Avalonia Windows Host、独立 WikiTools 和 Python 3.12–3.14 Agent。Windows Viewer Web、bridge 与图标来自根级 `assets/windows/` 的受控字节快照；旧参考仓库不在当前 checkout，也不参与构建。

### 目标技术方向

- Portable C#/.NET 10 Wiki Core；
- 薄 Application 用例边界；
- Avalonia Presentation / client host（Phase 04 已建立正式 Windows Foundation）；
- NativeWebView 通过受限动态 loopback 承载现有 Local Wiki Viewer；
- Windows data-root、设置、锁、外链和 Viewer Adapters 已实现最小链路；首次运行由 Windows 宿主在 LocalAppData 创建默认数据根，普通 UI 不再切换地址，数据管理页直接导入/导出当前活动 Wiki 的完整 `data.db`。Android Adapters 仅在已暂缓的 Phase 13 获重新授权后考虑实现；
- Windows 启动恢复 raw import journal 后检查 v2 格式与 Wiki 身份，并使用 Manager 统计快照；不扫描 HTML、不构建全库索引、不迁移旧内容目录。诊断和 smoke 使用固定阶段记录累计/阶段耗时，Viewer transport 故障与数据格式故障分别恢复；
- 独立 Wiki Tool Adapter 通过无状态、动态 loopback MCP 只暴露 Application Search / Read，并由 Phase 07 Windows Runtime 在 Viewer 之后按需启停；
- Python Agent 通过受限 MCP 执行 Search → Read → typed Answer；`read_wiki` 只向模型返回有界的 `wiki_blocks_v1` 章节化纯文本证据，实际来源由当前运行成功 Read 派生。每轮是新的 Agent 运行，可选上下文只有最近 4 轮 user/assistant 纯文本；业务逻辑与 Windows/Android Runtime 分离。
- Windows Avalonia Host 通过严格 NDJSON v2 长驻 stdio Sidecar 组合 WikiTools 与 Python Agent，并在本地拥有完整版本化会话、整段收藏和对话历史。正式 payload 使用只读内置 CPython 3.14.7 x64，用户级 `ai-runtime.json` v3 只保存 provider，API key 存入当前用户 Windows Credential Manager。设置页可编辑 DeepSeek 官方 API 或自定义 OpenAI-compatible provider 与思考强度，并在原子保存后显式重启同一 Runtime。Host 只把 key 注入所属 Sidecar 环境，AI 故障不影响离线 Search/Viewer。
- Windows 正式交付以 self-contained `win-x64` 普通每用户 Setup 和 portable zip 为主，应用二进制及后续更新只来自公开 GitHub Releases 的 `win-x64-stable` channel；不实施 Microsoft Store、Store identity/publisher/update、MSIX 或 App Installer。当前不配置商业代码签名。
- Windows Presentation 使用内部状态模型、XAML Chat/Wiki/Reader、安全原生 Markdown 和唯一共享 NativeWebView；普通页面宽屏默认主内容/Reader 双栏、窄屏按需覆盖 Reader，独立 Wiki 入口在同一工作区提供本地首页、全宽搜索结果和全宽正文，不引入第二套 Viewer 或公共 Application UI port。

产品平台范围是 Windows（当前）与 Android（未来）；其中 Phase 13 Android 已暂缓并退出当前执行队列，恢复时必须重新获得用户明确授权。iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均为明确非目标。WSL/Linux 只承担开发与平台无关检查，不构成 Linux 产品支持。源码中存在某个平台 target、条件编译或框架能力，不代表该平台已经成为产品承诺；扩大平台范围必须由用户重新授权并更新架构决策。

Windows 当前开发只允许引入 .NET SDK、普通 NuGet 依赖和 WebView2 等 Windows 目标所需组件；不得为了 Windows 开发安装 MAUI workload、Android SDK、JDK 或 Emulator。Phase 13 恢复并获用户明确授权前，不安装 Android workload、SDK、JDK 或 Emulator，也不创建 Android 实施计划；这类目标平台依赖可以延后和隔离，但无法由客户端框架消除。

主要开发执行者是安装在 WSL2 中的 Codex CLI。固定位置：

```text
Windows: C:\Users\Xiao\mypython\terraria_aiwiki
WSL:     /mnt/c/Users/Xiao/mypython/terraria_aiwiki
```

Codex CLI 必须从 WSL 工作区根启动，使产品源码与根 `AGENTS.md` 被加载。日常读取、编辑、搜索和平台无关检查在 WSL 中完成；Windows 客户端的运行、调试、构建和发布必须由 Windows 环境实际验证。

项目当前保留在 C 盘并通过 `/mnt/c` 访问。即使该位置的 WSL I/O 性能可能较低，也不得未经用户决定复制或迁移到 WSL home。必须同时考虑 Linux 工具语义与 Windows 文件系统的路径、大小写、换行和文件锁差异。

## 长期约束

- Existing Wiki Core 最大限度复用；新功能不复制搜索、数据库、同步、导入导出或 Viewer backend。
- Portable Core 不引用 MAUI、Avalonia、WPF、WinUI 或目标平台 UI API；Avalonia 只进入 Presentation/宿主。
- 新 Presentation 通过 Application 使用后端，不直接绑定数据库或平台 Runtime。
- Local Viewer Adapter、Wiki Tool Adapter 和平台 Adapter 依赖向内的 Application/Core 边界；Core 不依赖 WebView、loopback、Python Runtime 或平台 Adapter。
- AI 通过稳定的 Wiki Search / Read 边界获取本地事实，不直接建立第二套 Wiki 数据体系。
- 搜索使用 ADR 0012 的单套区域级 contentless FTS5；不增加向量库、第二份正文或并行旧索引。
- Python Agent 业务逻辑与 Windows / Android Runtime 分离。
- Windows 新版本不使用 MAUI；Phase 10 已在 P0 接管证据基础上移除旧 MAUI 参考 gitlink，不得把它重新引入产品 build/runtime。
- Vector Search、Embedding Retrieval、Vector Database 和第二套 LLM Wiki 数据库不属于项目范围。
- 变更必须小步、可验证、可回退；未验证设想不得写成已实现事实。

## 当前状态

产品源码已以 `d67d904` 内容建立独立基线，固定上游参考从未参与迁移后的 build/runtime；Phase 10 已移除其本地 gitlink。移除后的 Windows Release build 为 0 warning/0 error，275/275 .NET tests 与两种 publish smoke 均通过；冻结 Python 回归沿用此前无源码变化的证据。

Phase 00–10 已完成，Phase 11 Windows Productization 已按用户决定关闭并归档；Phase 12 按真实需求决定是否实施，Phase 13 Android 继续暂缓。Windows Avalonia Shell 现有六个入口继续只经现有 Application/Core 能力运行，Presentation 不直接访问 SQLite；新增“Wiki”入口复用原搜索与唯一共享 Reader，在同一工作区切换真实本地首页、全宽结果和全宽正文。其首版已进入本地主线并通过自动化验证，但真实窗口视觉验收未通过，后续由新 plan 继续优化。Phase 11 已实现内置 CPython、Credential Manager + 严格 v3、Velopack 普通 Setup/portable/full+delta、Evergreen WebView2 bootstrap/外置用户数据、公开 GitHub stable 更新状态和有界诊断，并在实际 Windows 11 x64 完成本地安装、portable、N→N+1、repair、卸载、P0 smoke 与目标窗口验收；公开仓库/版本/许可输入、干净机与真实 GitHub 渠道矩阵、Windows 10 可选门槛和发布审计未完成，已转入 `post-phase-11-roadmap`，当前候选仍为 `publicationReady: false`。已完整关闭并归档的 AI 会话 Token 优化 change 把 AI 边界升级为 `wiki_blocks_v1`、NDJSON v2 和最多最近 4 轮纯文本，并增加本地完整会话、整段收藏与对话历史；后续 Token/答案质量工作另建 plan。Windows 本地 Wiki 已转为 v2 内容格式：启动恢复 raw import journal 后检查格式与身份，不扫描 HTML 或迁移旧内容；Application/Viewer 继续使用完整 HTML、Redirect 和唯一 Reader，旧 `.pkg` 已退休。各变更测试结果与未验证边界在相应归档计划中单独记录。最新未打包源代码会自动创建 Windows 默认数据根，空内容时直接进入数据管理页；普通 UI 不再更改路径，改为安全导入/导出当前 Wiki 的单文件 `data.db`。设置页临时隐藏共享 Reader，已保存 API Key 以固定遮蔽状态显示且不回显真实值。此前的 `0.11.0-local.22` 安装与 portable 候选不含这些最新源码变更，并按要求未重建。未来公开发行工作必须另建 change 并取得必要输入与外部写入授权。

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
| 已完成或明确关闭的变更 | `openspec/changes/archive/` |
| 长期技术决策 | `docs/adr/` |

当事实或决策变化时，只更新负责该内容的文件；不要在多个入口文档复制并分别维护同一细节。
