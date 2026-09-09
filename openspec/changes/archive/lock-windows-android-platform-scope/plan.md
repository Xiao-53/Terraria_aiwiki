# 锁定 Windows / Android 产品平台范围

- 状态：completed
- Phase：无
- 执行环境：文档治理；不执行平台构建或业务源码变更
- 创建日期：2026-08-20
- 最后更新：2026-08-20
- 范围：平台承诺、Phase 00 环境事实与执行前准备项
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`docs/adr/0001-use-avalonia-client.md`；`openspec/changes/phase-00-migration-baseline/plan.md`

## 背景与当前证据

用户明确产品平台范围：当前只支持 Windows，未来只考虑 Android；其余平台不在产品范围内。现有文档仍使用“iOS / MacCatalyst 暂不支持”等可被理解为将来可能支持的措辞，且 Phase 00 计划保留了已经被本次环境审计推翻的旧记录。

已完成只读环境检查：Windows 侧已安装 .NET 10 SDK、Avalonia 模板和 WebView2 Runtime；未安装 MAUI/Android workload、Android SDK、JDK 或 Emulator；WSL2 与 WSL Codex CLI 可用；NuGet 可访问；发现代表性现有 Wiki 数据库，但未发现 `.pkg` / `.pak` golden 样本；干净 Windows 发布验证环境仍待准备。

## 目标与完成定义

- 权威文档一致表达“Windows 当前、Android 未来、其他产品平台全部非目标”。
- 明确 WSL/Linux 是开发执行环境，不是产品目标平台。
- Phase 00 计划改用本次实际环境审计结果，并列出仍需用户准备的样本、干净 Windows 环境和支持矩阵选择。
- 不改变 Phase 00 的 draft 状态，不安装依赖，不创建 PoC，不修改 `terraria-wiki-offline/`。

## 前置条件

用户已授权修复上述文档，并要求列出其需要准备的内容。

## 范围与非目标

范围：`README.md`、`AGENTS.md`、`.agent/PLANS.md`、v0.6 架构总纲、项目基线、相关 ADR/spec 与 Phase 00 计划。

非目标：历史 v0.5、已归档计划、业务源码、依赖安装、Phase 00 PoC、Android 工具链和任何产品平台实现。

## P0 / 兼容性影响

仅修正文档边界和环境事实，不改变 P0 能力、数据格式或运行行为。真实数据库仍按原件只读、副本写入规则处理。

## 方案与待决问题

平台范围写成闭集：产品只包含 Windows 和未来 Android。iOS、macOS/MacCatalyst、Linux 桌面、浏览器/Web 及其他平台均为明确非目标；若未来改变，必须由用户重新授权并更新架构决策。

仍待用户提供或确认：`.pkg` golden 样本、可取得时的历史 `.pak`、干净 Windows 验证环境、最低 Windows 版本与 CPU 架构。

## 文件变更

- **MODIFY** `docs/architecture/ARCHITECTURE-v0.6.md`、`README.md`、`openspec/project.md`、`AGENTS.md`：统一平台边界。
- **MODIFY** `.agent/PLANS.md`：移除遗留 MAUI 验证措辞，收窄为 Windows/Android。
- **MODIFY** `docs/adr/0001-use-avalonia-client.md` 与相关 specs：明确决策适用平台。
- **MODIFY** `openspec/changes/phase-00-migration-baseline/plan.md`：更新环境事实和准备状态，保持 draft。
- **KEEP** `terraria-wiki-offline/**` 与历史 v0.5/归档记录。

## 执行步骤

- [x] 统一权威架构、项目入口、项目基线和 Agent 规则的平台表述。
- [x] 同步 ADR、能力规范和计划规范中的平台边界。
- [x] 用实际审计结果更新 Phase 00 的事实、前置条件、进度和验证结果。
- [x] 全文检查冲突措辞、确认 Phase 00 仍为 draft，并确认业务源码状态未变。

## 验证与回归计划

- 搜索所有现行治理文件中的 `iOS`、`MacCatalyst`、`Linux`、`暂不支持`、`MAUI` 与环境记录。
- 检查 Phase 00 状态、平台范围和未准备项是否一致。
- 检查 `terraria-wiki-offline/` Git 状态；不执行平台构建。

## 风险与回退

风险是把开发环境 WSL/Linux 与产品平台 Linux 混淆，或把旧源码 target 当成未来承诺。通过显式区分“开发执行环境”和“产品平台闭集”规避。所有改动均为文档，可按文件差异回退。

## 进度记录

- 2026-08-20：完成平台范围确认和环境只读审计，开始文档一致性修复。
- 2026-08-20：统一现行治理文档的平台闭集，更新 Phase 00 的实际环境证据与执行前准备状态。
- 2026-08-20：完成冲突措辞、Phase 00 状态、v0.6 大纲规模和业务源码状态检查。

## 发现、偏差与决策

- 用户已明确排除 Windows/Android 之外的全部产品平台，不再使用“暂不支持”措辞。
- 当前已安装环境足以开始 Windows Phase 00 的大部分本机探针，但样本和干净 Windows 发布验证环境尚未齐备。

## 验证结果

- 现行治理文档中未发现“暂不支持”、旧 `dotnet` 缺失、WSL 不可访问或 Windows MAUI 构建等过期表述。
- README、AGENTS、v0.6、项目基线、ADR 与 Phase 00 均明确 Windows 当前 / Android 未来的平台闭集。
- Phase 00 仍为 `draft`，未安装依赖、未创建 PoC、未运行平台测试。
- `ARCHITECTURE-v0.6.md` 为 249 行，仍保持总纲规模。
- `terraria-wiki-offline/` 的 `git status --short` 与 `git diff --stat` 均无输出，业务源码未变。

## 完成摘要

已锁定产品平台范围并修正 Phase 00 环境事实。用户仍需准备 `.pkg` golden、可选历史 `.pak`、干净 Windows x64 环境，并确认最低 Windows 版本/CPU 架构；其余已检查工具无需当前安装。
