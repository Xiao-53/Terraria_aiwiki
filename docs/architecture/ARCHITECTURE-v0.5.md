# Terraria AI Wiki — Master Architecture / Roadmap v0.5

> **文档类型**：项目级架构总纲 / 阶段路线母文件  
> **项目状态**：实施前规划  
> **当前目标平台**：Windows  
> **后续目标平台**：Android  
> **暂不支持**：iOS / MacCatalyst  
> **替代版本**：本文件替代 `ARCHITECTURE-v0.4.md`

## 这份文件负责什么

本文件只定义整个项目长期稳定的方向、边界和实施顺序，用于派生各 Phase 的详细实施计划。它回答：

- 最终产品要变成什么；
- 哪些现有能力不可破坏；
- 目标系统如何分层；
- 哪些区域复用、包装、重构或新增；
- 各阶段为什么按当前顺序推进；
- 哪些决策已经确定，哪些仍需验证；
- 每个 Phase 的计划文件必须包含什么。

本文件不负责记录具体 SQL、类名设计、文件变更清单、接口签名、组件结构、依赖版本、打包命令或详细测试步骤。这些内容必须在读取真实代码后写入对应 Phase 的 `plan.md`。

当实现细节变化但项目方向未变时，只更新 Phase 计划；当跨阶段边界、P0 能力、目标分层或平台方向发生变化时，才更新本文件。重要且长期有效的技术选择另写 ADR。

---

## 1. 项目目标与不可破坏能力

项目的最终形态不是“原离线 Wiki 加一个 Chat 页面”，而是一个新的 Terraria AI 产品：拥有新的 Presentation 和 AI 能力，同时把现有 Terraria Wiki Offline 中成熟的 Wiki 数据系统作为底层能力继续使用。

总体原则是：

> **复用成熟的 Wiki 数据核心，建立稳定的 Application 边界，逐步重做产品界面，并把 AI 作为独立能力接入。**

目标用户流程为：

```text
用户问题
→ AI 判断需要哪些本地 Wiki 信息
→ 搜索并读取相关页面
→ 基于 Wiki 事实进行多步分析
→ 返回回答与可追溯来源
```

### P0：Existing Core Capabilities

以下是 brownfield 改造期间必须持续保护的核心资产。后续可以重构其入口或内部实现，但不得在没有独立决策、迁移方案和回归证据的情况下删除、绕开或重复实现。

| 能力域 | 必须保留的能力 |
|---|---|
| Wiki 获取与同步 | 页面列表获取、页面下载、更新时间比较、增量更新、Redirect、失败任务处理 |
| 内容处理与存储 | 页面解析与清理、SQLite 持久化、现有数据语义与兼容性 |
| 搜索 | 搜索索引自动生成、SQLite FTS5、标题与 Redirect 检索、手动搜索能力 |
| 数据可移植性 | 数据包导入、导出、旧数据迁移与兼容逻辑 |
| 资源与维护 | Wiki 资源下载与存储、数据库维护、数据状态与容量统计 |
| 数据源 | 现有多 Wiki 数据源身份与配置能力 |
| 离线阅读 | Local Wiki Viewer 的底层服务、页面和资源读取能力 |

完整的数据生命周期必须保持成立：

```text
获取 Wiki
→ 解析与清理
→ 写入数据库
→ 生成搜索索引
→ 增量更新与维护
→ 导入 / 导出
→ 供手动搜索、离线阅读和 AI 共用
```

隐藏或替换旧 UI 入口不等于删除底层能力。旧 Presentation 只有在新界面完成接管且相关依赖已经验证后，才可按 Phase 计划清理。

### 当前产品优先级

- **P0**：任何阶段都不破坏 Existing Core Capabilities。
- **P1**：完成 Windows 版的搜索修正、Application 解耦、AI 问答、新 Presentation 和正式产品化。
- **P2**：在稳定边界之上增加其他 Toolset，并适配 Android。

### 全局非目标

本项目明确不建设：

- Vector Search；
- Embedding Retrieval；
- Vector Database；
- 第二套供 LLM 使用的 Wiki 数据库；
- 预先由 LLM 总结整个 Wiki 的知识库；
- 为了 MCP 或“分层完整”而提前拆分、复制或重写现有 Wiki Core；
- 与当前 Phase 无关的插件框架、事件总线、搜索框架或自制运行时。

保留抽象边界不等于预建上述实现。

---

## 2. 目标架构

```text
┌──────────────── Presentation ────────────────┐
│ Chat │ Wiki │ Data Management │ Settings   │
└──────────────────────┬───────────────────────┘
                       │
                       ▼
┌───────────────── Application ────────────────┐
│ 面向界面的查询、数据管理、AI 与设置用例      │
└───────────────┬────────────────┬─────────────┘
                │                │
                ▼                ▼
┌──────── Existing Wiki Core ────┐   ┌──── AI Boundary ────┐
│ 获取 / 更新 / SQLite / FTS5    │   │ AI Client / Agent   │
│ 导入导出 / 资源 / Viewer backend│   │ Tool Calling        │
└────────────────────────────────┘   └─────────┬───────────┘
                                              │
                                              ▼
                                    ┌── Wiki Tool Adapter ──┐
                                    │ Search / Read         │
                                    └──────────┬────────────┘
                                               │
                                               └→ Application / Wiki Core

Platform Adapters：Windows Runtime 现在实现；Android Runtime 后续实现。
```

### 分层职责

| 层 | 稳定职责 | 依赖规则 |
|---|---|---|
| Presentation | 产品导航、页面、交互和视觉表达 | 只调用 Application，不直接依赖数据库或 AI 运行细节 |
| Application | 向 UI 和外部 Adapter 提供稳定的应用用例 | 编排现有能力，保持薄，不复制底层实现 |
| Existing Wiki Core | Wiki 数据生命周期、搜索、离线资源与 Viewer backend | 最大限度复用；变更必须保护数据和兼容性 |
| AI Boundary | 对话、Agent loop、Tool Calling 和模型接入 | 不直接访问 SQLite，通过 Wiki Tool Adapter 使用本地事实 |
| Wiki Tool Adapter | 把稳定的 Wiki 查询能力暴露给 AI | transport 可替换，协议不反向污染 Core |
| Platform Adapters | AI Runtime、生命周期、启动与目标平台集成 | Windows 和 Android 分开实现，不进入跨平台 Agent 逻辑 |

### 必须保持的依赖方向

- 新 UI 不直接访问 SQLite、数据库表、全局应用状态或具体 AI transport。
- Application 只包装当前用例所需能力，不为未来假设预建大量空接口。
- AI 通过稳定的 Search / Read 能力获取 Wiki 事实，不复制搜索、页面读取或同步逻辑。
- Local Viewer 是可保留和重新设计的 Presentation 能力，其底层继续复用现有 Wiki 数据与资源。
- Python Agent 的业务逻辑与 Python 如何在某个平台运行相互分离。
- Android 不继承 Windows 的进程、路径、localhost 或打包假设。

---

## 3. 复用、包装与重构策略

| 区域 | 总体策略 | 边界 |
|---|---|---|
| Wiki 获取、更新、Redirect、数据库、导入导出 | **KEEP** | 优先原样复用，必要变更进入专门 Phase |
| 现有搜索系统 | **KEEP + FIX** | 修复已确认问题，不重建搜索架构 |
| 资源管理与 Local Viewer backend | **KEEP** | 入口和界面可以调整，底层能力不删除 |
| Application | **ADD, THIN** | 只建立实际需要的稳定用例边界 |
| Wiki Tool Adapter | **ADD, THIN** | 只暴露 AI 第一阶段需要的查询能力 |
| AI Layer | **ADD** | 作为新产品能力建设，保持平台无关 |
| Presentation | **REBUILD + TRANSITION** | 新旧界面并存、逐项接管，最后才清理旧 Presentation |
| Platform Runtime | **ADD PER PLATFORM** | Windows 先行；Android 后续独立 PoC |

所有方案按以下顺序评估：

```text
Reuse existing behavior
→ Fix locally when sufficient
→ Wrap behind a thin boundary
→ Extract only when evidence proves necessary
→ Rewrite only as the last option
```

出现以下情况时，当前 Phase 必须暂停并重新评估方案：

- 需要大规模移动现有 C# 文件；
- 需要复制数据库、搜索、同步或 Viewer backend；
- 为了某个 transport 强制拆分整个现有项目；
- 新增的架构胶水明显多于实际业务能力；
- 实现要求提前决定后续平台或后续 Toolset 的细节。

任何 `REMOVE` 都必须在对应 Phase 计划中证明：目标不属于 P0、调用关系已确认、新能力已经接管、回归已通过、存在回退方案，并且删除得到用户明确接受。

---

## 4. Phase Roadmap

默认按依赖顺序推进。每个 Phase 在实施前必须先生成并批准独立计划；本表中的“阶段出口”只是项目级门槛，不替代详细验证方案。

| Phase | 目标与阶段边界 | 前置 | 阶段出口 | 计划文件 |
|---|---|---|---|---|
| **00 — Baseline** | 建立 P0 能力和当前 Windows 行为的可重复基线；不做产品改造 | 无 | 已知能力、限制与验证环境有证据记录 | `openspec/changes/phase-00-baseline/plan.md` |
| **01 — Search Fix** | 修正现有搜索中已确认的问题，使手动 UI 与 AI 都可稳定复用；不更换搜索系统 | 00 | 搜索问题有回归证据，现有搜索能力无退化 | `openspec/changes/phase-01-search-fix/plan.md` |
| **02 — Application Boundary** | 建立 UI 所需的薄 Application 边界，逐步移除新 UI 对底层实现的直接依赖；不大规模抽取 Core | 00；通常在 01 后 | 新功能可通过稳定用例访问 Wiki 与数据管理能力 | `openspec/changes/phase-02-application-boundary/plan.md` |
| **03 — AI Adapter** | 以最小侵入方式向 AI 暴露 Wiki Search / Read；不复制 Core，不预建其他工具 | 01、02 | AI 可通过稳定边界查询和读取现有 Wiki | `openspec/changes/phase-03-ai-adapter/plan.md` |
| **04 — AI Backend** | 建立最小 Python Agent 与多步 Tool Calling，验证本地 Wiki 可作为事实来源；不做最终产品 UI | 03 | 代表性问题可完成 Search → Read → Answer 链路 | `openspec/changes/phase-04-ai-backend/plan.md` |
| **05 — Presentation Rebuild** | 建立新的 MAUI 产品壳、信息架构和核心页面，通过 Application 使用后端；保留旧 UI 作为过渡 | 02、04 | 新 Presentation 可以承载 Chat 与后续功能接管 | `openspec/changes/phase-05-presentation-rebuild/plan.md` |
| **06 — Runtime Smoke Test** | 接通 Windows AI Runtime、流式交互和生命周期，并尽早验证一次可打包运行；不视为正式发布 | 04、05 | 打包环境中 MAUI、AI 与本地 Wiki 的最小链路可启动 | `openspec/changes/phase-06-runtime-smoke-test/plan.md` |
| **07 — UI Takeover** | 让新 Presentation 逐项接管计划保留的 Wiki、数据管理和设置等能力；此阶段不先删除旧实现 | 05、06 | 新 UI 覆盖已确认的目标用户流程和 P0 入口 | `openspec/changes/phase-07-ui-takeover/plan.md` |
| **08 — Legacy Presentation Cleanup** | 只清理已经被替代、确认无依赖且获准删除的旧 Presentation；不清理 P0 Core | 07 | 不存在未说明的功能缺口或重复主路径 | `openspec/changes/phase-08-legacy-presentation-cleanup/plan.md` |
| **09 — Windows Productization** | 完成 Windows 正式运行配置、错误处理、安装打包和发布准备 | 06、08 | Windows 成品达到该 Phase 计划定义的发布门槛 | `openspec/changes/phase-09-windows-productization/plan.md` |
| **10 — Extensions** | 按真实需求增加 Web Search 或其他 Toolset；每项扩展独立决策，不反向改写 Wiki Core | 09 | 新能力通过现有边界接入且可独立关闭 | `openspec/changes/phase-10-extensions/plan.md` |
| **11 — Android** | 通过平台 PoC 选择 Android AI Runtime 与集成方式，复用 Application 和 Agent 业务边界 | 09；可在稳定后预研 | Android 方案以目标平台实测通过为准 | `openspec/changes/phase-11-android/plan.md` |

路线中的两次“打包”目的不同：Phase 06 用于尽早暴露 MAUI、Python、AI 与本地数据组合后的集成风险；Phase 09 才负责新 UI 接管和旧 Presentation 清理后的正式产品化。

除非独立计划明确证明可以安全并行，否则不得跳过前置 Phase。Phase 10 与 Phase 11 是后续路线，不得为了它们在 Windows 第一版预建复杂框架。

---

## 5. 全局决策与开放决策

### 已决定

| 主题 | 项目级决策 |
|---|---|
| 产品形态 | 新 Presentation + AI + Application，复用 Existing Wiki Core |
| 当前平台 | Windows |
| 后续平台 | Android |
| 暂不支持平台 | iOS、MacCatalyst |
| Wiki 数据核心 | 继续使用和保护现有 C# 能力 |
| 数据库 | 继续使用现有 SQLite 数据体系，不建第二套 AI 数据库 |
| 搜索 | 继续复用现有 FTS5 搜索并做局部修正 |
| 向量检索 | 永久不实现 Vector Search、Embedding Retrieval 或 Vector Database |
| UI 与后端 | 新 UI 通过 Application 使用后端能力 |
| Presentation | 允许明显重构，但采用新旧并存、逐项接管的迁移方式 |
| 数据生命周期 | 下载、更新、索引、导入导出、资源与迁移能力属于 P0 |
| Local Viewer | 底层能力保留，Presentation 可重新设计 |
| 第一阶段 Wiki Tools | Search 与 Read 两类稳定能力 |
| 数据源身份 | 优先复用现有 Wiki 数据源身份，不建立重复来源体系 |
| AI 实现方向 | Python Agent；PydanticAI 优先；Agent 业务逻辑与平台 Runtime 分离 |
| MCP | 只可能是 Adapter，不是项目 Core，也不是强制方案 |
| 页面读取 | 先验证现有页面内容是否足够；证据不足时不提前建设转换管线 |
| 变更方式 | Reuse → Fix → Wrap → Extract → Rewrite |

### 仍然开放

| 开放问题 | 负责收敛的 Phase | 决策依据 |
|---|---|---|
| AI 与 C# 的首个 transport | 03 | 对现有源码侵入、调试性、生命周期与打包验证 |
| 页面内容是否需要 Reader Adapter，以及采用什么实现 | 04 | 真实页面、token、表格与回答质量测试 |
| 官方 Wiki URL 的生成或持久化方式 | 03 / 04 | 现有数据、来源可靠性与兼容性 |
| Windows Python Runtime 的最终启动与封装方式 | 06 / 09 | 打包、升级、故障恢复和体积实测 |
| 是否有必要拆分现有 C# project | 02 或后续专门决策 | 只有实际边界无法在原项目内保持时才考虑 |
| History / Favorites 在新产品中的定位 | 05 / 07 | 产品信息架构和用户流程 |
| Web Search 的 provider 与 Toolset 形式 | 10 | 当期模型、服务能力、成本和来源质量 |
| Android 使用远程还是内嵌 AI Runtime | 11 | Android 依赖兼容、体积、性能和平台 PoC |

开放问题不是 Agent 可以自行固化的默认许可。Phase 计划必须列出候选、证据和决策门槛；涉及长期或跨阶段影响的选择在确认后写入 ADR，并在必要时回写本文件。

---

## 6. Phase 计划文件规范

### 存放与生命周期

每个 Phase 使用独立 OpenSpec change：

```text
openspec/changes/phase-XX-<name>/
└─ plan.md
```

如该 Phase 需要提案、规范增量或 ADR 草案，应与其计划关联，不得把实施细节塞回本文件。完成并验证后，按项目治理规则移入：

```text
openspec/changes/archive/phase-XX-<name>/
```

计划状态遵循：

```text
draft → approved → in-progress → completed → archived
```

没有批准的 Phase 计划，不得开始该 Phase 的源码修改。生成计划不等于授权实施。

### 每份计划的最低内容

计划必须遵循 `.agent/PLANS.md`，并至少明确：

1. **目标与阶段出口**：本阶段解决什么，怎样才允许进入下一阶段。
2. **前置条件**：依赖的 Phase、环境、数据和未完成事项。
3. **当前代码依据**：实际阅读过的配置、入口、调用方、实现、模型与测试；事实和假设分开。
4. **范围与非目标**：允许修改的能力域、不得触碰的区域和后续 Phase 才处理的内容。
5. **P0 影响**：本阶段可能影响哪些 Existing Core Capabilities，以及对应保护措施。
6. **待决问题**：需要 PoC 或用户选择的事项、候选方案、证据要求和决策点。
7. **实施方案**：足以指导执行的步骤、依赖关系和小步提交顺序。
8. **文件变更**：基于真实代码列出 `ADD / MODIFY / KEEP / REMOVE candidate`；不得凭架构图虚构文件。
9. **验证与回归**：具体测试、构建、平台验证、P0 回归和无法执行的验证。
10. **风险与回退**：数据、兼容、平台、依赖、打包与删除风险，以及恢复方法。
11. **完成定义**：可审查、可验证、没有用模糊措辞代替结果。

具体 SQL、接口签名、类名、目录调整、测试输入、打包方式和依赖版本，只有在相关代码与环境完成核对后，才能写入 Phase 计划。

### 生成计划时给 Agent 的边界

生成某个 Phase 的计划时，Agent 必须：

- 先读取本文件、项目治理文件、已完成 Phase 的结果以及当前相关代码；
- 只计划用户指定的 Phase，不顺带实施，也不扩展到后续 Phase；
- 优先复用真实存在的能力，不把目标架构中的概念自动翻译成大量新类或新 project；
- 对未验证事项保留候选和决策门槛，不把猜测写成既定事实；
- 为所有可能影响 P0、数据格式、平台或删除行为的步骤提供明确回退；
- 如发现本文件与真实代码或已确认产品方向冲突，先报告并更新决策，不静默偏离。

### 文档更新规则

| 发生的变化 | 应更新的位置 |
|---|---|
| 某一 Phase 的实现、文件或测试细节变化 | 对应 `plan.md` |
| 已确认的能力契约变化 | `openspec/specs/` |
| 长期技术决策或重大取舍 | `docs/adr/` |
| P0、目标分层、Phase 顺序或平台方向变化 | 本文件 |
| 项目入口和当前状态变化 | 根 `README.md` |

---

## 最终方向

项目成功不以“新增了多少层、多少服务或多少文件”为标准，而以以下结果为准：

- Existing Wiki Core 的成熟能力得到保护和复用；
- 新 Presentation 不再被数据库和平台实现绑死；
- AI 能通过稳定的 Search / Read 边界使用本地 Wiki 事实；
- Windows 可以完成可靠产品化；
- 后续 Toolset 与 Android 能从既有边界扩展；
- 每个阶段都小步、可验证、可回退，不用一次性重写原项目。

> **本文件定义方向、边界与顺序；各 Phase 计划定义实现。**
