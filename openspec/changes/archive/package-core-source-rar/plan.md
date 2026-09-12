# 核心源码 RAR 打包

- 状态：archived；一次性备份已完成，可复用流程已转为项目 Skill
- Phase：无
- 执行环境：WSL Codex CLI；最终 RAR 产物写入 Windows 桌面 `C:\Users\Xiao\Desktop`（WSL：`/mnt/c/Users/Xiao/Desktop`）；压缩工具与完整性验证需在具备 RAR 创建能力的环境执行
- 创建日期：2026-09-12
- 最后更新：2026-09-12
- 范围：探索当前仓库并生成一份只包含实际产品源码文件的 RAR 压缩包
- 关联架构/规范/ADR：`AGENTS.md`、`INIT.md`、`README.md`、`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`docs/adr/0007-separate-product-source-from-reference.md`、`docs/release/windows.md`

## 背景与当前证据

### 已确认事实

- 产品源码的事实边界是根级 `src/`、`tests/`、`agent/`、`assets/` 与 `Terraria_Wiki.Core.slnx`；旧 MAUI 参考仓库不属于当前 checkout 的产品输入。
- 当前仓库包含 `artifacts/`、各项目 `bin/`/`obj/`、Python 缓存与虚拟环境、`.tmp-searchprobe`、`phase-00-inputs/`、`experiments/`、发布/安装验证残留等非核心内容。
- `assets/windows/` 含当前产品使用的 Viewer、bridge 与图标快照；其是否全部纳入源码包需要按“产品源码依赖”而不是目录名称判断。
- 发布脚本与发布说明属于构建/发布支持，不等于发布制品；用户要求排除 release，因此默认不纳入 `artifacts/`、发布制品、数据目录和安装包，但是否保留 `src/Terraria_Wiki.Windows/Packaging/` 源码需在 allowlist 审查中明确。
- 当前工作区存在用户既有改动：`openspec/changes/optimize-ai-evidence-flow/` 正在被移除并归档；本变更不得暂存、修改或夹带这些状态。
- 当前 WSL 环境未发现 `rar`、`unrar` 或 `7z` 命令；不能在计划阶段宣称已生成或验证 RAR。

### 待确认/需实施时核实

- 用户已确认：`tests/` 不纳入，只纳入源码，其余内容一律不纳入；实施时只需冻结源码扩展名和路径清单。
- RAR 创建工具及版本、输出目录和归档文件名；输出应放在仓库外或明确排除路径，避免归档自身进入归档。
- 用户已确认这是一次备份：原始文件、构建物、数据和工作区内容均不得删除、移动或覆盖；最终压缩包放到桌面。
- 是否需要为归档附带文件清单、SHA-256 和排除项报告；建议一并生成临时审计报告，但不把临时报告或压缩包加入源码包。

## 目标与完成定义

1. 形成一份经过审查的核心源码 allowlist，以及明确的排除规则。
2. 生成一个不包含 release 制品、数据包/数据库、参考代码、构建输出、缓存、虚拟环境、实验输入、归档计划和其他非核心材料的 `.rar`。
3. 对 RAR 内容执行列表检查：路径无越界、无禁止类别、无归档自身、无明显生成物，并记录文件数、总大小和 SHA-256。
4. 不修改产品源码、数据、发布配置或用户现有改动；若当前环境没有 RAR 工具，停在工具缺口并向用户说明，不以 ZIP 冒充 RAR。
5. 备份完成后原目录内容保持不变；RAR 只作为新增产物写入桌面，若桌面已有同名文件则先停止并避免覆盖。

## 前置条件

- [x] 已读取项目入口、架构、项目基线和计划规范。
- [x] 已读取产品源码与参考边界 ADR、Windows 发布说明及当前 Phase 11 计划中的打包边界。
- [x] 用户确认：`tests/` 不纳入，只纳入源码，其余内容一律不纳入。
- [x] 找到可复现的 RAR 创建工具；使用 WinRAR 7.23 x64 完成创建和读取验证。

## 范围与非目标

### 唯一允许纳入的内容

- `src/` 中实际产品实现源码文件。
- `agent/src/` 中 Python Agent 实现源码。
- 源码扩展名和具体路径在实施步骤中冻结；项目文件、配置、锁文件、文档和静态资源均不纳入。

### 明确排除

- 所有 release/install/publish 产物、portable/Setup/update 包、`artifacts/` 和其他分发制品。
- 整个 `tests/` 目录及所有测试源码、fixture 和测试资源。
- `assets/` 目录、Viewer、bridge、图标及其他非源码资源。
- 根级文件、solution、`.csproj`、锁文件、README、ADR、spec、计划、许可证和声明文件。
- Wiki 数据库、数据包、下载数据、缓存数据、SQLite/数据库文件、`phase-00-inputs/` 等输入快照。
- 旧实现、参考仓库、参考 checkout、历史归档资料和不属于产品运行时的实验目录。
- `bin/`、`obj/`、`.venv/`、`__pycache__/`、`.pytest_cache/`、`.mypy_cache/`、`.ruff_cache/`、`.tmp-*`、日志、临时目录和已有压缩包。
- `.git/`、工作区内部状态、用户个人配置、密钥、凭据和任何未纳入 allowlist 的治理/历史文件。

### 非目标

- 不清理、删除、移动或重构仓库现有文件。
- 不重建 Windows release、不运行发布上传、不修改版本、许可证或 release 渠道配置。
- 不把“源码包”变成可运行安装包，也不承诺它包含数据或发布所需全部依赖。

## P0 / 兼容性影响

- 本变更只读并复制文件，不改动 Wiki 数据生命周期、SQLite/FTS5、数据包、Viewer、AI Runtime 或平台行为。
- 归档必须排除所有真实用户数据和数据包，避免把可变数据误当作源码交付。
- 复制资产时保留相对路径、大小写和文件字节；不执行格式化、换行转换或构建。

## 方案与待决问题

建议方案：先从 Git 跟踪文件建立候选集合，再按产品边界逐项筛选；最终将筛选结果写成可审查的 manifest，使用 RAR 工具从一个临时 staging 目录创建压缩包，随后用独立命令列出归档内容并计算校验和。

替代方案是按目录直接递归压缩，但会把构建输出、缓存、数据和历史材料混入归档，无法满足用户的排除要求，因此不采用。

待决问题：

- 用户已确认测试目录、资产、文档、配置和其他非源码内容全部排除。
- RAR 输出固定为 `/mnt/c/Users/Xiao/Desktop/terraria-aiwiki-core-source-<date>.rar`；创建前检查同名文件，禁止覆盖。

## 文件变更

- ADD：`.agents/skills/package-core-source-rar/`（可复用 Skill、调用元数据与确定性打包脚本）。
- ARCHIVE：`openspec/changes/package-core-source-rar/plan.md` → `openspec/changes/archive/package-core-source-rar/plan.md`。
- MODIFY：无；实施时优先只读，不修改源码。
- KEEP：现有用户改动、源码、数据、发布物和治理文件原样保留。
- REMOVE：无。
- 实施阶段的临时 staging、manifest 和审计报告放在 `/tmp`；最终 `.rar` 只新增到 `/mnt/c/Users/Xiao/Desktop`，不写入仓库。

## 执行步骤

- [x] 1. 读取 Git 跟踪文件并检查候选目录；建立纳入/排除清单，确认没有数据包、发布物、参考代码和生成物。
- [x] 2. 检查当前工作树、文件权限、符号链接和敏感文件；未发现需要纳入的非源码内容。
- [x] 3. 创建 `/tmp` 临时 staging 目录，只复制 manifest 中的 115 个源码文件并保持相对路径与字节内容；未删除或移动源文件。
- [x] 4. 检查桌面无同名备份后，使用 WinRAR 7.23 x64 创建 `/mnt/c/Users/Xiao/Desktop/terraria-aiwiki-core-source-20260912.rar`。
- [x] 5. 列出 RAR 内容并应用排除规则；归档包含 115 个文件及目录项，无禁止路径或归档自身。
- [x] 6. 计算 RAR SHA-256；原有内容未删除、移动或覆盖。
- [x] 7. 最终检查 `git status`、计划状态和归档位置。

## 验证与回归计划

- WSL：`git ls-files`/文件清单审查、排除模式检查、敏感文件检查、归档内容列表、SHA-256、`git status`。
- RAR 环境：使用对应 RAR/解压工具执行 archive listing；必要时抽样解压到临时目录并与 staging/manifest 对比字节哈希。
- Windows：若 RAR 工具仅存在于 Windows，需在 Windows 目标环境完成创建或读取验证；这不等同于 Windows 产品 build/release 验证。
- 不执行 .NET build、Python 测试或 Windows 发布回归，因为本变更不修改产品代码且用户只要求源码归档。
- 若 RAR 工具无法提供，记录为未完成的外部前置条件，不宣称压缩包已生成。

## 风险与回退

- **误纳入数据/发布物**：采用 manifest、禁止后缀/目录复查和 archive listing；发现后删除临时归档并重建，不改源码。
- **漏纳入运行时资产**：对 `src/Terraria_Wiki.Windows` 的项目文件、Viewer/bridge、图标和 Agent 文件做项目引用核对；不凭“源码”字面排除必需资产。
- **RAR 工具差异**：记录工具名和版本；使用独立列表/解压验证，避免只依赖创建命令成功返回。
- **工作区污染**：临时材料放 `/tmp`，最终归档放桌面；不得删除或移动原有文件，只可清理本次新建的临时 staging。
- **桌面同名覆盖**：创建前检查目标路径；若同名文件存在则停止并报告，不覆盖既有备份。
- **用户既有改动被夹带**：不使用全仓库 staging，不执行 `git add -A`，manifest 只读取明确路径。

## 进度记录

- 2026-09-12：完成入口、架构、项目基线、计划规范、产品/参考边界、Phase 11 发布说明和目录盘点；发现工作树已有用户归档改动，且当前环境未安装 RAR 工具。
- 2026-09-12：建立本 draft 计划；未生成压缩包，未修改产品源码。
- 2026-09-12：用户要求把已验证流程固化为项目 Skill；新增固定源码白名单、未跟踪源码防遗漏、临时 staging、禁止覆盖、WinRAR 完整性检查和 SHA-256 报告脚本，并归档本计划。

## 发现、偏差与决策

- 2026-09-12：采用“显式 manifest + 临时 staging + RAR 内容复核”方案；拒绝直接递归压缩整个仓库。
- 2026-09-12：用户确认不纳入 `tests/`，只纳入实际源码；项目文件、资产、文档、配置和其他非源码内容全部排除。
- 2026-09-12：用户确认这是备份操作；原内容不删除，RAR 产物写入 Windows 桌面。

## 验证结果

- 已完成：源码清单审查、RAR 创建、RAR 内容列表检查、完整性测试和 SHA-256。
- 归档：115 个源码文件；RAR 内容列表 152 行（包含目录项）。
- 文件：`C:\Users\Xiao\Desktop\terraria-aiwiki-core-source-20260912.rar`。
- SHA-256：`db05d1719e2d538a299b03f33edfc96b00bdca00563cd13dfbd554040ac29136`。
- 完整性：WinRAR `t` 检查通过。
- 可复用流程：`.agents/skills/package-core-source-rar/` 已建立；`skill-creator` quick validator 与 Bash 语法检查通过，dry-run 正确识别 115 个源码文件且没有修改中的产品源码。
- 前向测试：在 `/tmp` 使用 WinRAR 7.23 实际生成 115 文件、152 条目的临时 RAR，`Rar.exe t` 通过并输出 SHA-256；随后只删除该次临时测试目录，没有创建或覆盖桌面产物。
- 未执行：产品 build/test/release 验证；本次仅为源码备份。

## 完成摘要

本次源码备份已完成。RAR 位于 Windows 桌面；原文件、构建物、数据和工作区既有改动均未删除、移动或覆盖。相同边界的后续请求改由项目级 `package-core-source-rar` Skill 执行，本计划完成并归档。
