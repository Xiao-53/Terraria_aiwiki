# Terraria AI Wiki — Master Architecture / Roadmap v0.6

> **文档类型**：项目级架构大纲 / 阶段路线母文件
> **项目状态**：实施中；Phase 00–05 已完成，Phase 06 已实现并等待真实模型验收
> **产品平台范围**：Windows（当前）/ Android（未来）
> **明确非目标**：除 Windows / Android 外的所有产品平台
> **替代版本**：本文件替代 `ARCHITECTURE-v0.5.md`

## 这份文件负责什么

本文件只定义跨 Phase 长期稳定的项目方向、P0 护栏、目标分层、迁移原则、阶段顺序和全局决策，用于派生各 Phase 的详细计划。

本文件不负责记录源码盘点、类与接口、SQL、项目目录、依赖版本、PoC 操作、测试 fixture、打包命令或完整验收步骤。它们分别属于：

- 已确认且持续有效的能力契约：`openspec/specs/`；
- 当前 Phase 的方案、文件和验证：`openspec/changes/<change-id>/plan.md`；
- 长期技术选择与后果：`docs/adr/`。

实现细节变化但项目方向未变时，不更新本文件。只有 P0、目标分层、Phase 顺序或平台方向变化时，才更新 Master Architecture。

---

## 1. 项目目标与 P0 护栏

项目最终形态是一个新的 Terraria AI 产品：建设新的 Presentation 和 AI 能力，同时继续复用 Terraria Wiki Offline 中成熟的本地 Wiki 数据系统。

总体原则：

> **把现有 Wiki 能力迁移为可移植 .NET Core，通过薄 Application 对外提供用例，以 Avalonia 建设新客户端，并把平台与 Viewer 实现留在 Adapter 边界。**

目标用户流程：

```text
用户问题
→ AI 判断需要哪些本地 Wiki 信息
→ 搜索并读取相关页面
→ 基于 Wiki 事实进行多步分析
→ 返回回答与可追溯来源
```

### P0：Existing Core Capabilities

迁移期间必须持续保护：

| 能力域 | 必须保留的能力 |
|---|---|
| Wiki 获取与同步 | 页面列表、页面下载、更新时间比较、增量更新、Redirect、失败任务与重试 |
| 内容处理与存储 | 页面解析与清理、SQLite 持久化、现有数据语义与兼容性 |
| 搜索 | 搜索索引生成、SQLite FTS5、标题与 Redirect 检索、手动搜索 |
| 数据可移植性 | 数据包导入、导出、完整性检查、旧数据迁移与兼容逻辑 |
| 资源与维护 | Wiki 资源、数据库维护、状态与容量统计 |
| 数据源 | 多 WikiBook 身份、配置和数据目录语义 |
| 离线阅读 | Local Wiki Viewer 的页面、资源、导航和消息桥能力 |

完整生命周期必须保持成立：

```text
获取 Wiki
→ 解析与清理
→ 写入现有数据库
→ 生成现有搜索索引
→ 增量更新与维护
→ 导入 / 导出
→ 供手动搜索、离线阅读和 AI 共用
```

隐藏或替换旧 UI 不等于删除底层能力。任何删除都必须证明新能力已接管、P0 回归通过、调用关系已确认、存在回退，并获得用户明确接受。

### 优先级与非目标

- **P0**：不破坏 Existing Core Capabilities 与已有数据。
- **P1**：完成 Portable Core、Application、新 Windows 客户端、AI 与产品化。
- **P2**：在稳定边界上增加扩展能力并适配 Android。

产品平台是闭集：当前只交付 Windows，未来只考虑 Android。iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他产品平台均为明确非目标；WSL/Linux 只承担开发与平台无关检查，不构成 Linux 产品支持。旧源码 target、条件编译或框架能力表不产生产品承诺；若未来改变范围，必须由用户重新授权并更新架构决策。

本项目不建设 Vector Search、Embedding Retrieval、Vector Database、第二套 LLM Wiki 数据库或预先总结整个 Wiki 的知识库；不为了换 UI 而重写已有数据库、搜索、同步、数据包或 Viewer 内容；不为未来需求预建大型接口体系、Registry、事件总线或插件框架。

---

## 2. 客户端路线与目标分层

Windows 新版本不再以 .NET MAUI 为宿主。当前首选方向是：

```text
.NET 10 Portable Wiki Core
        +
thin Application
        +
Avalonia Presentation
        +
NativeWebView Local Viewer
        +
per-platform Adapters
```

Avalonia + NativeWebView 已通过 Phase 00 的 Windows 11 x64 工具链、SQLite/WAL/FTS5、Viewer、数据兼容、平台路径和发布门槛，是当前已验证的首选方案。生产迁移仍须按后续 Phase 小步执行；若未来出现新的独立硬阻断，再按 ADR 0001 评估 WPF + Blazor Hybrid 回退，不并行建设第二套客户端。

长期技术取舍与采用门槛见 `docs/adr/0001-use-avalonia-client.md`，具体 PoC 与验收见 `openspec/changes/archive/phase-00-migration-baseline/plan.md`。

### 目标依赖方向

```text
Avalonia Presentation
        │ calls
        ▼
Application Use Cases
        │ uses
        ▼
Portable Wiki Core

Local Viewer Adapter ───────→ Application / Core read boundary
Wiki Tool Adapter ──────────→ Application Search / Read use cases
AI Runtime Adapter ─────────→ AI Backend
Windows / Android Adapters ─→ inward-facing application ports
```

箭头表示调用或依赖。Adapter 依赖稳定的 Application/Core 边界；Core 不依赖 Avalonia、NativeWebView、loopback server、Python Runtime 或平台 API。

### 分层职责

| 层 | 稳定职责 | 依赖规则 |
|---|---|---|
| Avalonia Presentation | 产品导航、视图、交互和视觉表达 | 只调用 Application；Viewer 控件属于 Presentation/Host |
| Application | 查询、阅读、同步、数据管理、AI 与设置用例 | 保持薄；编排 Core，通过小型 ports 使用平台能力 |
| Portable Wiki Core | Wiki 数据生命周期、搜索、数据包、资源和数据兼容性 | 只包含平台无关 .NET 能力，不引用 UI 或目标平台 API |
| Local Viewer Adapter | 向 NativeWebView 提供现有 Viewer 内容与本地 Wiki 资源 | 依赖向内的读取边界；transport 可替换 |
| AI / Wiki Tool Adapters | 接入 Agent，并把 Search / Read 暴露给 AI | 不直接访问 SQLite，不复制 Core |
| Platform Adapters | 路径、设置、文件选择、剪贴板、外链、窗口、生命周期与打包 | Windows/Android 分开实现，共用层不包含平台假设 |

Portable Core 的物理边界以真实编译依赖为准：持久化模型和平台无关逻辑优先移动；仍读取宿主设置、UI 状态或平台 I/O 的服务必须先做小范围适配。不得把混合 UI 状态模型整体误认成 Core 数据模型。

---

## 3. 迁移原则

固定顺序：

```text
Characterize existing behavior and data
→ prove the lightweight host
→ move portable behavior with minimal semantic change
→ adapt only confirmed host edges
→ add thin Application use cases
→ validate the real Windows runtime
→ rebuild and take over Presentation
→ retire MAUI last
```

| 区域 | 总体策略 | 边界 |
|---|---|---|
| 持久化模型、SQLite、FTS5 | **KEEP + MOVE** | 保持数据与查询兼容；搜索修复独立进行 |
| 数据库与 Wiki 数据生命周期服务 | **KEEP + ADAPT** | 移除已证实的宿主依赖，不重写业务算法 |
| 数据包与旧数据迁移 | **KEEP + ADAPT** | 保持兼容格式，同时补足安全输入与回退 |
| Local Viewer 内容 | **KEEP** | HTML/CSS/JS 与 Wiki 资源继续复用；transport 由 PoC 决定 |
| Application | **ADD, THIN** | 只覆盖真实用例，不复制 Core |
| Avalonia Presentation | **REBUILD + TRANSITION** | 旧 UI 作为行为参考，逐项接管后再退休 |
| AI 与平台集成 | **ADD AS ADAPTERS** | 运行时和目标平台实现不反向污染共用逻辑 |

所有方案遵循：

```text
Reuse → Fix → Wrap → Extract when necessary → Rewrite last
```

若方案开始要求复制 Core、大量搬动 C# 文件、预建后续框架，或把 Windows/Android/Viewer/AI transport 假设写入共用层，必须暂停并重新评估更小方案。

---

## 4. Phase Roadmap

默认按依赖顺序推进。每个 Phase 在实施前必须建立并批准独立计划；本表只定义阶段边界和出口。

| Phase | 目标与边界 | 前置 | 阶段出口 | 计划文件 |
|---|---|---|---|---|
| **00 — Migration Baseline & Host Spike** | 锁定 P0 基线，以 Windows 垂直切片验证首选轻量宿主；不正式迁移 Core | 无 | 已完成：Avalonia + NativeWebView 通过 Windows 11 x64 门槛 | `openspec/changes/archive/phase-00-migration-baseline/plan.md` |
| **01 — Portable Wiki Core** | 逐步移动持久化模型和平台无关 Wiki 能力，只适配已证实的宿主边缘 | 00 | 已完成：单一 `net10.0` Core 在无 MAUI/UI 引用下通过兼容回归 | `openspec/changes/archive/phase-01-portable-wiki-core/plan.md` |
| **02 — Search Fix** | 在可测试 Core 上修正已确认搜索问题，保留 SQLite FTS5 | 01 | 已完成：手动搜索与未来 AI 共用的七级查询行为有 Core 回归证据 | `openspec/changes/archive/phase-02-search-fix/plan.md` |
| **03 — Application Boundary** | 建立 UI 所需的薄用例边界，移除新 UI 对数据库和全局状态的直接依赖 | 01；通常在 02 后 | 已完成：查询、阅读、同步、数据管理和设置可由稳定用例调用 | `openspec/changes/archive/phase-03-application-boundary/plan.md` |
| **04 — Windows Client Foundation** | 建立 Avalonia Windows 壳、Windows Adapters 和 Viewer 最小链路 | 00、03 | 已完成：Windows 可启动、搜索、打开页面并加载本地资源 | `openspec/changes/archive/phase-04-windows-client-foundation/plan.md` |
| **05 — AI Adapter** | 以最小侵入向 AI 暴露 Wiki Search / Read | 02、03 | 已完成：无状态 loopback MCP 可通过 Application 查询和读取现有 Wiki | `openspec/changes/archive/phase-05-ai-adapter/plan.md` |
| **06 — AI Backend** | 建立最小 Python Agent 与多步 Tool Calling；不做最终 UI | 05 | 验证中：实现与非付费门槛已通过；三场真实模型验收待 key 与付费授权 | `openspec/changes/phase-06-ai-backend/plan.md` |
| **07 — Windows Runtime Smoke Test** | 接通客户端、AI Runtime、流式交互、生命周期和最小发布产物 | 04、06 | 发布环境中客户端、AI 与本地 Wiki 最小链路可启动 | `openspec/changes/phase-07-windows-runtime-smoke-test/plan.md` |
| **08 — Presentation Rebuild** | 建设新的产品信息架构和核心页面，通过 Application 使用后端 | 04、07 | 新 Presentation 可承载 Chat、Wiki 和后续接管流程 | `openspec/changes/phase-08-presentation-rebuild/plan.md` |
| **09 — P0 UI Takeover** | 新 UI 逐项接管 Wiki、数据管理、设置、导入导出、维护和错误流程 | 08 | 新客户端覆盖计划内 P0 入口和目标用户流程 | `openspec/changes/phase-09-p0-ui-takeover/plan.md` |
| **10 — Legacy MAUI Retirement** | 只清理已被替代、确认无依赖且获准删除的旧 Presentation/宿主 | 09 | 新客户端无功能缺口，旧宿主可回退或归档 | `openspec/changes/phase-10-legacy-maui-retirement/plan.md` |
| **11 — Windows Productization** | 完成错误恢复、安装打包、升级和发布准备 | 07、10 | Windows 成品达到发布门槛 | `openspec/changes/phase-11-windows-productization/plan.md` |
| **12 — Extensions** | 按真实需求增加其他 Toolset；每项独立决策 | 11 | 扩展通过既有边界接入且可独立关闭 | `openspec/changes/phase-12-extensions/plan.md` |
| **13 — Android** | 在目标环境验证客户端、数据、Viewer、平台行为和 AI Runtime | 11；可在边界稳定后预研 | Android 方案以目标平台实测为准 | `openspec/changes/phase-13-android/plan.md` |

Windows 当前开发不安装 MAUI/Android 工具链。Android workload、SDK 与 JDK 只在 Phase 13 引入；文档支持矩阵不能替代目标平台实测。

---

## 5. 全局决策与开放问题

### 已决定

| 主题 | 项目级决策 |
|---|---|
| Windows 宿主 | 新 Windows 版本不使用 MAUI |
| 产品形态 | 新 Presentation + AI + Application，复用 Existing Wiki Core |
| 产品平台范围 | Windows 当前；Android 未来；除此之外全部为明确非目标 |
| Core | 单一平台无关 .NET 10 Wiki Core；旧宿主通过 façade 复用，不复制第二套实现 |
| 数据根 | Core 接收宿主显式绝对根，原位沿用 `<root>/Manager.db` 与 `<root>/<DataFolder>/data.db` |
| 数据库与搜索 | 保留现有 SQLite/FTS5 并局部修正 |
| 客户端首选 | Avalonia + NativeWebView；Phase 00 Windows 11 x64 门槛已通过 |
| Application | 保持薄，只表达真实用例和必要 ports |
| Viewer | 内容与数据链路保留；Windows 使用受限动态 loopback，Android 后续独立验证 |
| AI 事实来源 | 通过稳定 Search / Read 使用同一份本地 Wiki 数据 |
| 首个 Wiki Tool transport | 官方 MCP 无状态动态 loopback + 临时 bearer；只属于 Adapter，Android 不继承 |
| 旧 UI | 作为行为/过渡参考，完成 P0 接管后才允许退休 |
| 变更方式 | Reuse → Fix → Wrap → Extract → Rewrite |

### 仍然开放

| 开放问题 | 收敛 Phase |
|---|---|
| Windows 安装、WebView2 bootstrap 与发布渠道 | 11 |
| Windows Python Runtime 封装 | 07 / 11 |
| Android 平台行为与设备内/远程 AI Runtime | 13 |

开放问题不是默认许可。长期选择确认后写 ADR；影响 P0、分层、Phase 或平台方向时同步更新本文件。

---

## 6. 计划、规范与验证规则

- 每个 Phase 使用 `openspec/changes/phase-XX-<name>/plan.md`，遵循 `.agent/PLANS.md`。
- 计划必须基于真实配置、入口、调用方、实现、模型、样本与测试，区分事实、推断和假设。
- 跨 Phase 持续有效的能力契约写入 `openspec/specs/`，不堆回 Master Architecture。
- 数据库、索引、数据包、迁移、删除和目标平台行为必须有相称的安全保护、回退与实测证据。
- Windows 客户端只有在 Windows 实际 build/run/publish 后才能宣称通过；Android 只有在目标环境实测后才能宣称通过。
- 旧源码缺少自动化测试；移动高风险代码前必须先建立 characterization tests。

---

## 最终方向

项目成功不以新增多少层、接口或 project 衡量，而以以下结果为准：

- Existing Wiki Core 和已有数据得到保护与复用；
- 新 Presentation 不依赖数据库、全局状态或具体平台 Runtime；
- Viewer 继续使用同一份本地页面与资源；
- AI 通过稳定 Search / Read 使用本地事实；
- Windows 可可靠发布，Android 可在稳定边界上后续适配；
- 每个阶段小步、可验证、可回退。

> **本文件只定义方向、边界与顺序；specs 定义持续契约，Phase 计划定义实现。**
