# 整理工作区治理文档

- 状态：completed
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：工作区入口、Agent 规则、架构总纲位置、OpenSpec 项目基线与计划规范
- 关联规范/ADR：`AGENTS.md`、`INIT.md`、`.agent/PLANS.md`；无 ADR

## 背景与现状

用户要求优化 `AGENTS.md` 等工作区文件，并把 `ARCHITECTURE-v0.5.md` 放到合理位置。当前工作区根目录同时放置 README、Agent 规则、会话入口和架构总纲；`INIT.md` 的读取顺序没有包含架构总纲；`openspec/project.md` 仍含较早的 Viewer/媒体裁剪方向和旧平台描述；`.agent/PLANS.md` 没有把 Phase、Master Architecture 与 OpenSpec change 的关系写清。多份文件重复描述目标，但没有明确冲突时的文档优先级。

已完整阅读当前 `AGENTS.md`、`INIT.md`、`README.md`、`openspec/project.md`、`.agent/PLANS.md`、`ARCHITECTURE-v0.5.md` 和上一次已归档的变更计划。工作区根目录不是 Git 仓库；`terraria-wiki-offline/` 是独立 Git 仓库且当前无未提交变更。

## 目标

- 将版本化架构总纲移动到 `docs/architecture/ARCHITECTURE-v0.5.md`。
- 明确 README、AGENTS、INIT、Master Architecture、OpenSpec specs/changes、ADR 和计划文件各自唯一职责。
- 为文档冲突定义清晰优先级，避免 Agent 读取旧摘要后覆盖新决策。
- 更新所有受影响的相对路径、读取顺序和工作区目录说明。
- 修正 `openspec/project.md` 中与 v0.5 冲突的旧方向和平台基线。
- 强化 Phase 计划纪律，同时避免 AGENTS、Architecture 和 PLANS 三处重复维护同一细节。

## 非目标

- 不修改 `terraria-wiki-offline/` 源码、配置、依赖或测试。
- 不改变 v0.5 已确认的产品方向、P0 能力或 Phase 顺序。
- 不创建 Phase 00–11 的具体实施计划。
- 不新增 ADR、能力规范、roadmap、todo 或其他重复状态文件。

## 约束与风险

- `AGENTS.md` 必须保持可执行、明确且不过度复制架构正文。
- `INIT.md` 应是短入口，不承担长期项目说明。
- README 面向人类和新会话快速浏览，不复制全部架构决策。
- `openspec/project.md` 只保存长期基线和治理关系，不能与 Master Architecture 争夺 Phase 路线所有权。
- `.agent/PLANS.md` 只规定计划触发条件、格式和生命周期；Phase 目标由 Master Architecture 定义。
- 移动架构后必须更新所有引用，且不能留下根目录旧副本形成双重真相源。
- 工作区根不是 Git 仓库，因此需要用内容哈希、路径检查和源仓库状态代替根级 Git diff。

## 方案与决策

- 创建 `docs/architecture/`，将 v0.5 原文件移动到该目录并保留版本化文件名。
- 根目录只保留 `README.md`、`AGENTS.md`、`INIT.md` 和工作区级目录。
- 文档职责按以下方式划分：README 为入口；AGENTS 为强制工作规则；INIT 为读取顺序；Master Architecture 为方向/P0/Phase；OpenSpec project 为技术与治理基线；specs 为已确认契约；changes 为进行中计划；ADR 为长期技术决策。
- 冲突处理遵循：用户当前明确要求最高；其后为更具体且已批准的规范/ADR/计划；Master Architecture 控制项目方向；project/README/INIT 只作摘要，不得覆盖前者。
- 更新五份治理文件，但不改写架构正文，只移动其位置。

## 执行步骤

- [x] 完整读取现有治理文档、架构总纲和工作区结构。
- [x] 起草并检查五份优化后的治理文件。
- [x] 创建 `docs/architecture/`，写入原样 v0.5，并移除根目录旧副本。
- [x] 检查所有 Markdown 路径、读取顺序、文档职责和决策内容是否一致。
- [x] 确认 `terraria-wiki-offline/` 没有被修改。
- [x] 更新验证记录并归档本 change。

## 验证计划

- 搜索全部工作区 Markdown，确认不存在指向根 `ARCHITECTURE-v0.5.md` 的旧链接。
- 确认 `docs/architecture/ARCHITECTURE-v0.5.md` 存在，根目录同名文件不存在，且移动前后内容哈希一致。
- 检查文档读取顺序包含 AGENTS、INIT、README、Master Architecture、project 与任务相关资料。
- 搜索 `openspec/project.md` 中旧的 Viewer/媒体裁剪、iOS/MacCatalyst 目标和“AI 最后接入”等冲突表述。
- 确认 README、AGENTS、INIT、project 和 PLANS 不重复承载 Phase 详细路线。
- 执行 `git -C terraria-wiki-offline status --short`，确认源码仓库未变。

## 进度记录

- 2026-08-20：完成现状读取与冲突识别；确定 `docs/architecture/` 为 Master Architecture 的归属目录。
- 2026-08-20：完成 AGENTS、INIT、README、project 和 PLANS 草稿；统一职责、读取顺序、Phase 关系、平台基线和冲突处理。
- 2026-08-20：写回五份治理文件并移动架构总纲；完成路径、哈希、旧表述和源码状态检查。

## 发现与偏差

- 工作区根目录本身不是 Git 仓库，无法使用根级 Git diff 复核治理文档变更。

## 验证结果

- 五份治理文件和移动后的架构总纲与交付草稿的 SHA-256 分别一致。
- 根目录 `ARCHITECTURE-v0.5.md` 不再存在；`docs/architecture/ARCHITECTURE-v0.5.md` 存在，且内容哈希与移动前一致。
- README 中指向 Master Architecture、AGENTS、INIT、project 和 PLANS 的五个相对路径均存在。
- 活跃治理文档中未发现旧的根架构链接、固定 WSL 环境、Viewer/媒体逐步裁剪或 iOS/MacCatalyst 产品目标表述。
- `git -C terraria-wiki-offline status --short` 无输出，业务源码仓库未修改。
- 本次仅调整 Markdown 治理文件和文件位置，未执行 MAUI 构建或运行测试。

## 完成摘要

已将 Master Architecture 原样移动到 `docs/architecture/`，并优化 AGENTS、INIT、README、OpenSpec project 和 PLANS。工作区现在按“入口、强制规则、读取顺序、架构方向、项目基线、能力规范、变更计划、长期决策”分工，旧路径和过时方向已消除。业务源码、依赖、构建配置和 Phase 状态均未改变。
