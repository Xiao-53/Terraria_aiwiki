# 将产品源码与参考仓库分离

- 状态：completed
- Phase：无；Phase 09 的前置修正
- 执行环境：WSL Codex CLI；Windows 11 x64 restore/build/test/publish 与 smoke 验证
- 创建日期：2026-09-02
- 最后更新：2026-09-02
- 范围：外层 Git 产品源码基线、参考 Submodule、Windows 构建资产、仓库边界测试与治理文档
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/local-wiki-viewer/spec.md`、`docs/adr/0002-portable-core-and-data-root.md`、`docs/adr/0003-use-loopback-local-viewer-transport.md`、`docs/adr/0007-separate-product-source-from-reference.md`

## 背景与当前证据

用户确认新产品不应继续寄存在 BigBearKing/terraria-wiki-offline 的本地工作分支中。当前外层仓库 HEAD 为 `d73436bbf3b70af80d3518d9419d1f66f8a4fb02`；外层没有已跟踪改动，但有 `.agents/archive/terraria-dev-workflow/`、`.codex/`、`.tmp-searchprobe/`、`experiments/`、若干历史 change、`phase-00-inputs/` 等既有未跟踪内容，迁移基线不得夹带或删除它们。

内层仓库当前 HEAD 为干净的产品提交 `d67d904c32b853edb664b68121897a2f2a8f2655`，本地 `main` 比 `origin/main` 领先 24 个提交；`origin/main` 与指定参考点为 `a1dd89981d7604006ec91955d4d19e93cb933ce1`。产品文件位于内层受 Git 跟踪的 `src/`、`tests/`、`agent/` 与 `Terraria_Wiki.Core.slnx`。Windows project 当前仍通过 `../../Terraria_Wiki/...` 读取旧 MAUI 项目的 Viewer Web、`iframe-bridge.js` 和图标，因此参考仓库仍是构建依赖。

事实：本变更只迁移物理位置与构建资产所有权，不改变 namespace、公共 API、SQLite schema、数据根、包格式、Viewer transport、AI 协议或产品行为。Phase 09 保持 `in-progress`，功能开发在本迁移验证完成前暂停。

## 目标与完成定义

- 外层根级 `src/`、`tests/`、`agent/` 与 `Terraria_Wiki.Core.slnx` 精确继承内层 `d67d904` 的受跟踪产品快照。
- Windows 构建所需 Viewer、bridge 和应用图标成为外层受控、可追溯的资产快照；正常 build/runtime 不读取参考仓库。
- `terraria-wiki-offline/` 注册为外层 Git Submodule，并固定到纯上游 `a1dd899`；内层保留本地恢复分支 `archive/new-app-before-extraction` 指向 `d67d904`。
- 增加仓库边界测试，阻止 product project、tests 或 publish target 引用 `terraria-wiki-offline/` 或旧 `../../Terraria_Wiki` 路径。
- Windows Release restore/build/test 为 0 warning、0 error；framework-dependent 与 self-contained `win-x64` publish 都包含 ViewerAssets、AgentRuntime、图标与 manifest；既有合成数据根 smoke 通过且不调用付费 Provider。
- 冻结 Python 环境中的 Ruff、format check、strict mypy 与离线 pytest 通过；不迁移 `.venv`。
- 验证通过后归档本 change，并在外层创建且只创建一个产品基线提交 `refactor(repo): establish standalone application baseline`；不 push。

## 前置条件

- Phase 00–08 已完成；Phase 09 仍为 `in-progress`，本变更只是其前置修正。
- 外层恢复点 `d73436b`、产品快照 `d67d904`、上游参考点 `a1dd899` 可解析。
- 两个仓库均无未提交的受跟踪改动；既有外层未跟踪内容必须原样保留且不得暂存。
- Windows .NET 10 工具链与既有冻结 Python 环境可用；若环境缺失，只能如实记录，不能推定通过。

## 范围与非目标

范围内：从指定提交导出受跟踪产品文件；提取当前实际使用的 Viewer/bridge/icon 资产；调整 Windows project 资产路径；新增边界测试；增加 `.gitignore`、`.gitattributes`、来源/许可证说明；将参考目录注册为固定 Submodule；更新入口、基线、架构、spec、ADR 与 Phase 09 路径记录；执行迁移清单、构建、测试、发布和 smoke 验证；建立外层基线提交。

非目标：嫁接内层 24 个产品提交历史；修改 namespace、公共 API、数据库/索引/数据根/包格式、AI 或 Viewer 协议、产品 UI/行为；推进 Phase 09 功能；安装 MAUI/Android 工具链；删除上游源码、既有用户数据或外层未跟踪内容；push 任何仓库。

## P0 / 兼容性影响

- Core、Application、WikiTools、Windows 与 Agent 源文件先按 `d67d904` 原样导出；只有 project 的资产路径、边界测试和必要治理文件允许内容变化。
- 数据库、FTS5、同步、包、资源读取和 Viewer 请求语义不变；合成数据根 smoke 验证启动与 Viewer 资产链路。
- 参考仓库不再参与 build/runtime，但保留为只读上游证据；gitlink 可精确恢复到 `a1dd899`。
- 迁移前后生成路径/对象清单和 hash 清单，防止漏文件、额外生成物或意外内容漂移。

## 方案与待决问题

- 使用 `git archive d67d904` 只导出 `src/`、`tests/`、`agent/` 和 solution；不从工作树复制，避免 `bin/obj/.venv/cache`。
- 将旧项目中当前 build 直接读取的资源按字节快照放入外层受控资产目录，并在来源说明中记录源路径、上游提交、产品迁移提交、数量/hash 与许可证。
- Windows project 只改用外层资产相对路径；`agent/` 仍保持现有相对结构。
- 在既有 Windows boundary tests 中加入仓库隔离断言，避免引入新测试框架。
- 使用本地 branch 作为内层恢复点；清理只在预览后处理 `src/`、`tests/`、`agent/` 中的迁出生成物/缓存。随后将工作树切到 `origin/main`，再由外层把现有目录登记为 Submodule。
- 待决问题：无；Submodule URL 沿用 BigBearKing 官方 origin，未来是否在 Phase 10 保留或移除另行决定。

## 文件变更

- ADD `src/**`、`tests/**`、`agent/**`、`Terraria_Wiki.Core.slnx`：来自 `d67d904` 的产品快照。
- ADD 外层受控 Viewer/bridge/icon 资产目录及来源/许可证说明。
- MODIFY `src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：指向外层受控资产，不再读取旧 MAUI 项目。
- MODIFY Windows boundary tests：增加 Submodule/旧路径依赖禁令。
- ADD `.gitignore`、`.gitattributes`、`.gitmodules`：外层 .NET/Avalonia/Python 忽略、文本属性与固定参考仓库登记。
- ADD `docs/adr/0007-separate-product-source-from-reference.md`：替代 ADR 0002/0003 中已过时的物理源码与构建资产部分，不改变数据根或 Viewer transport。
- MODIFY `README.md`、`INIT.md`、`AGENTS.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`：外层是唯一产品源码仓库，Submodule 只读参考。
- MODIFY `openspec/specs/local-wiki-viewer/spec.md`：发布资产来自外层受控快照，参考 Submodule 不得成为 build/runtime 依赖。
- MODIFY `openspec/changes/phase-09-p0-ui-takeover/plan.md`：记录暂停、产品基线与新路径；不改写既有历史路径。
- KEEP 其他归档 Phase 计划中的历史路径与行为记录。

## 执行步骤

- [x] 核对外层/内层 Git 状态、恢复点、产品提交、上游提交和既有未跟踪内容。
- [x] 建立内层恢复分支与迁移前产品/资产清单、hash 证据。
- [x] 从 `d67d904` 导出产品快照到外层根级目录，不复制生成物或环境。
- [x] 提取 Viewer/bridge/icon 受控资产，记录来源与许可证，调整 Windows project 路径。
- [x] 增加仓库边界测试、外层 `.gitignore` 与 `.gitattributes`。
- [x] 更新 ADR、spec、架构、项目基线、入口、Agent 规则与 Phase 09 计划。
- [x] 预览并清理内层迁出目录中的生成物/缓存；切到 `a1dd899` 并注册固定 Submodule。
- [x] 对迁移前后源文件及 96 个资产执行名称、数量、hash 和允许差异校验。
- [x] 在 Windows 执行 root solution Release restore/build/test、两种 `win-x64` publish 与合成数据根 smoke。
- [x] 使用冻结 Python 环境执行 Ruff、format、strict mypy 和离线 pytest。
- [x] 执行静态边界、secret/数据库/包/缓存/生成物、diff、Submodule 与双仓库状态检查。
- [x] 完成并归档本计划；逐项暂存已确认范围，创建单一外层基线提交且不 push。

## 验证与回归计划

- WSL：`git ls-tree`/`git archive` 清单；SHA-256 资产清单；迁移前后源文件 hash 对比；`rg` 检查 product project/test/publish target 不引用 Submodule 或 `../../Terraria_Wiki`；`git diff --check`；secret、数据库、`.pkg/.pak`、WAL/SHM、缓存、`bin/obj`、`.venv` 暂存检查。
- Windows：从外层 root solution 执行 Release restore/build/test，记录 warning/error 和各 test assembly 数量；执行 framework-dependent 与 self-contained `win-x64` publish，核对 ViewerAssets、AgentRuntime、图标和 manifest；运行既有合成数据根 smoke，不调用 Provider。
- Python：使用已冻结且位于仓库外/忽略范围的环境执行 Ruff、format check、strict mypy 与离线 pytest；不创建或迁移产品 `.venv`。
- Git：最终外层提交只包含迁移产品、资产、边界测试、Submodule 与治理更新；`git submodule status` 固定 `a1dd899`；内层 detached/reference 工作树干净；外层既有未跟踪内容仍未暂存。

## 风险与回退

- 误夹带用户未跟踪内容：全程逐路径暂存，禁止 `git add .`；提交前使用 cached diff 与未跟踪清单复核。
- 资产遗漏导致 build/publish 可编译但运行失败：迁移前后对 96 个实际依赖资产做名称、数量、hash 校验，并核对两种 publish 输出。
- 内层本地产品历史暂时不可见：`archive/new-app-before-extraction` 永久指向 `d67d904`，可重新 checkout；不删除 object、不 push。
- Submodule 转换损坏工作树：先确认 clean、创建恢复 branch、预览清理；外层可回到 `d73436b`，内层可回到恢复 branch。
- Windows/Python 验证失败：保留 change 为 `in-progress`，不归档、不创建基线提交；修复仅限迁移引入的问题。

## 进度记录

- 2026-09-02：用户批准实施既定迁移方案；暂停 Phase 09 功能开发。
- 2026-09-02：读取治理入口、Master Architecture、项目基线、ADR 0002/0003、Local Viewer spec、Phase 09 与相关已归档 Phase 计划。
- 2026-09-02：确认外层 `d73436b` 无已跟踪改动但有必须保留的未跟踪内容；确认内层 `d67d904` 工作树干净、领先 `origin/main` 24 个提交，指定上游点 `a1dd899` 可解析。
- 2026-09-02：确认 Windows project 的遗留构建依赖是旧项目 Viewer Web、`iframe-bridge.js` 与两个图标；Agent 已位于产品快照的根级相对布局。
- 2026-09-02：建立 `archive/new-app-before-extraction` 恢复分支；用 `git archive` 导出 175 个产品基线文件，并迁入 94 个外部 Viewer/bridge/icon 资产。除 project 和 3 个迁移边界测试外，其余产品文件与 `d67d904` 字节一致。
- 2026-09-02：内层清理预览只列出 `bin/obj` 与 Python/test caches；清理后 detached 到 `a1dd899` 并登记/吸收为标准 Submodule，恢复分支仍可解析。
- 2026-09-02：Windows root solution restore/build 0 warning/0 error；首次 full test 暴露 `ViewerServerTests` 仍读取旧根级资产，改用受控快照并扩大边界回归后 260/260 通过。
- 2026-09-02：两种 `win-x64` publish 均含 93 个 ViewerAssets 文件和 9 个 AgentRuntime 文件；framework-dependent/self-contained 合成数据 smoke 均通过且 `ai: null`。
- 2026-09-02：使用仓库外临时环境执行 `uv sync --frozen --dev`；Ruff lint/format、strict mypy 通过，pytest 77 passed / 4 skipped。所有构建、发布、数据库、日志、venv/cache 与临时目录已清理。

## 发现、偏差与决策

- 决策：不将既有外层未跟踪治理/实验/输入目录纳入本基线提交，也不删除它们。
- 决策：不触发 Terraria UI Skill；本变更只迁移仓库与资产所有权，不设计或实质改变 Avalonia 页面/视觉组件。
- 发现：96 文件发布支持集由 94 个外部快照文件加产品中已有的 `viewer-host.html` 与 manifest 组成；94 文件 manifest hash 为 `5ac242971b3781303b8ee52c6105da31ee6e8d4eb42382658c431c3ed32e443a`，96 文件为 `59a57f6e08a6ad0a9cb37d48cc7abf8d7eba1fbee01a8ebad2493e642fe35231`。
- 偏差：首次 full test 发现一个测试 fixture 未随生产 project 改路径，已局部修正并新增静态边界回归；没有产品行为变化。
- 偏差：WSL shell 临时变量不会自动传给 Windows 进程，第一次 fixture 实际跳过并导致 smoke 报告数据根缺失；改用 Windows PowerShell 显式设置后重新生成合成根，两种发布 smoke 均通过。失败报告与验证输出随后清理。
- 偏差：现有用户级 Python Runtime 没有 dev tools；未污染该环境，改用仓库外临时 `uv sync --frozen --dev` 环境执行完整检查。

## 验证结果

- Git 前置检查：外层 HEAD `d73436b`，内层产品 HEAD `d67d904`，上游参考 `a1dd899`；两个仓库均无已跟踪改动。
- 迁移清单：`src/`、`tests/`、`agent/` 为 174 个文件，加 root solution 共 175 个；只有 Windows project、`PresentationBoundaryTests`、`WindowsClientBoundaryTests` 与 `ViewerServerTests` 是授权迁移差异，其余逐字节一致。
- 资产：94 个外部快照文件逐字节一致；加 outer host/manifest 的 96 文件支持集数量与 aggregate hash 一致。产品 `_common/wiki-app-common.js` 相对上游 `a1dd899` 的唯一差异来自产品提交 `5d1d52f`，因此按 `d67d904` 固定。
- Windows：实际 root restore 与 Release build 通过，0 warning / 0 error；Core 91、Application 54、WikiTools 28、Windows 87，共 260/260 tests 通过。
- Publish：framework-dependent 约 161 MB、self-contained 约 267 MB；均含 ViewerAssets、AgentRuntime、托管 Avalonia icon 资源和原生 manifest resource。
- Smoke：两种发布均为 Search 1、JS 1、CSS 1、image 1、audio 2、history 0、exit cleanup true、`ai: null`；只使用 8 KiB Manager/76 KiB content 的合成数据根。Chromium 仍打印既有 unregister 1412 诊断，但结果与清理成功。
- Python：Python 3.12.3、`uv sync --frozen --dev`；Ruff lint 通过、19 files formatted、strict mypy 6 files 通过、pytest 77 passed / 4 skipped（跨语言/付费 Provider 未启用）。
- 静态与安全：product build/source 无 Submodule 或旧 `../../Terraria_Wiki` 引用；无 secret、数据库、package、WAL/SHM、cache、build output 或 `.venv` 留在迁移范围；`git diff --check` 通过。
- Git：Submodule detached 且干净，gitlink 为 `a1dd899`；恢复分支为 `d67d904`；既有外层未跟踪目录逐项确认仍存在且不纳入迁移范围。

## 完成摘要

迁移已完成。外层仓库现在统一拥有产品源码、测试、Agent、受控 Windows 资产与 root solution；参考 Submodule 干净固定到 `a1dd899`，本地恢复分支保留 `d67d904`。产品与资产清单、Windows 260/260 tests、两种 publish smoke、冻结 Python 回归及静态安全审计均通过；Phase 09 保持 `in-progress`，后续从外层 root solution 继续。最终只保留一个外层基线提交 `refactor(repo): establish standalone application baseline`，未 push。
