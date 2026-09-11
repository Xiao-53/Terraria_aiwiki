# AI 证据流程与 Token 效率优化

- 状态：completed；源码、自动回归和用户指定的新版两个五轮真实验收完成；保留两项输出质量观察，不作新旧对照或Token下降结论
- Phase：无；独立 AI / Wiki Tools 变更
- 执行环境：WSL2 Codex CLI 负责阅读、编辑和平台无关验证；Windows 11 x64 负责 .NET、真实 C# MCP→Python 离线链路与开发输出验证
- 创建日期：2026-09-09
- 最后更新：2026-09-11
- 范围：工具说明与模型证据投影、定向读取、单次运行内重复证据处理、证据流程约束与质量评测
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/specs/wiki-query-boundary/spec.md`、`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`、`docs/adr/0005-use-pydantic-ai-openai-backend.md`、`docs/adr/0010-use-bounded-local-ai-conversations.md`、`openspec/changes/ai-conversation-token-optimization/plan.md`

## 背景与当前证据

用户要求在现有 AI 问答上继续优化 Token 消耗与答案质量，并明确排除历史上下文调整：每次请求携带最多最近 4 轮问答的方式保持不变。本 change 不实施已有启动优化和 Wiki 页面计划。

已确认事实：

1. 当前已移除模型上下文中的整页 HTML；`read_wiki` 返回 `wiki_blocks_v1`，正文 text 合计最多 16384 Unicode scalar，章节目录最多 64 项、合计 4096 scalar。此前优化已实现，但真实 provider 的 Token/质量复验仍待用户执行。
2. `WikiEvidenceFormatter.OrderSections` 仅按 anchor、导语、focus、剩余章节排序，然后填入预算。指定 `#配方` 不代表只读取配方；测试 `ChineseFocusAndAnchorPrioritizeRelevantSectionsWithoutDroppingRemainder` 明确保留其余正文。
3. `section_path` 在每块重复，分页时目录再次返回；Python `_RunEvidence.process_tool_call` 校验后将完整结果交给模型，来源/重定向/统计等宿主字段也进入上下文。
4. `include_return_schema=True` 已启用。核对本地锁定 PydanticAI 2.31.0 后确认，不原生支持返回 schema 的路径会将完整返回 schema 追加到工具说明中。关闭该选项不等于关闭 Python 结果校验。
5. MCP 同时提供文本和 structured content，但当前 SDK 在适用条件下优先 structured content；没有证据说它向模型重复发送两份 MCP JSON，不以此作为优化理由。
6. `_RunEvidence` 只去重最终来源，不去重工具载荷。单次 Agent 运行内保留累积消息；最近 4 轮历史不包含旧工具消息，这两个范围不同。
7. 先 Search、再 Read 和不重复读取主要靠提示词；程序归一成功主要依赖存在成功 Read 来源及固定不足证据正文，并未证明答案的每项事实受支持。
8. formatter 会移除媒体/隐藏节点。是否影响图标表达的数量、条件或物品名称尚需代表性 fixture 检查，不能把删得更多当成质量更高。

本会话已执行的离线诊断：使用现有 `ScriptedModel`、fake toolset，明确设置 `ALLOW_MODEL_REQUESTS=False`，Windows 虚拟环境 Python 以 `-B` 执行，无远程请求、无生产数据。

| 模拟输入 | 实际结果 | 能证明的边界 |
|---|---|---|
| 直接 Read，没有 Search；输出与证据无关的 `UNSUPPORTED_CLAIM` | `answered` | 先 Search 的规则尚未程序强制；有来源不等于有事实支持 |
| Search 后连续两次相同 Read | 后续模型消息中相同证据从 1 份变为 2 份 | 来源去重没有消除模型上下文中的重复证据 |

这些是流程边界测试，不代表真实模型发生同样错误的频率，也不能测出真实 Token 收益。

## 目标与完成定义

**2026-09-11 验收范围修订**：用户明确要求只验收优化后版本两个新会话各5轮，不测试旧版。下述原对照目标保留为历史方案；本次完成门槛改为新版真实运行、绝对Token/延迟、事实/来源/历史核对及如实记录质量问题，不要求执行被用户排除的旧版或额外重复付费测试。

- 让模型收到更相关、少重复的证据与更短的必要工具说明；严格解析和完整来源元数据保留在宿主。
- 简单定向问题优先读取需要的章节；列表、比较、多跳问题仍可取得全部必要证据，不降低工具/请求上限或 Search 结果范围。
- 同一运行内重复请求/重叠证据不再无条件扩大模型输入；保留完整工具调用配对、原始证据可追溯性和必要上下文。
- 将先 Search 后 Read 的既有规则落实为程序约束，避免未满足流程却被标为成功；增强版本/模式/前提/列表覆盖提示与评测。
- 保持纯 Markdown 流式输出、最终 delta 拼接一致、宿主来源派生、固定不足证据语义、错误/取消/脱敏不变。
- **最近最多 4 轮历史、24000 字符预算、选择顺序、截断规则和传输方式全部 KEEP。** 不做相关性筛选、摘要、减少轮数或跨轮证据缓存。
- 修改前后以同一模型/配置/数据/历史进行对照；同时报告 Token、正确性、遗漏、误拒答和延迟。没有真实 provider 结果时，仅可宣称离线结构回归通过，不能宣称最终 Token/答案质量目标完成。

## 前置条件

- 原 `ai-conversation-token-optimization` change 已实现但未关闭真实验收；新计划在其源码上增量推进，不改写其历史验证，也不将其归档为完成。
- 规划时 HEAD 为 `530c653` 且有其他计划修改；实施时已是干净的 `09a4cf2`，既有工作已在该基线，不夹带其他 change 修改。
- 2026-09-11 用户明确授权创建分支并完成本计划；原 master 的 `09a4cf2` 为可恢复保存点。
- 离线测试不得使用真实密钥或调用付费 provider；真实对照由用户使用现有密钥执行，或取得单独明确的有限付费授权后执行。
- 不新增/升级依赖，不以获取额外模型或数据服务作为前置条件。

## 范围与非目标

范围内：现有两个 Wiki Tools 的证据选择/说明，Python 当前运行中的校验和模型输入投影，相关测试与持续契约。

非目标：

- 不修改 Chat 历史、BuildHistory、会话存储、收藏、NDJSON v2、Windows UI、Runtime 启动/退出或打包。
- 不修改 Core/Application 搜索、排序、摘要、limit、数据库、下载/更新、导入导出或 Viewer 原始 HTML。
- 不降低 8 请求、8 工具调用、4096 输出、300000 累计、256000 单请求输入和 120 秒既有限位；不以工具调用更少作为成功指标。
- 不新增 Web Search、向量/Embedding、第二套 Wiki 数据或持久证据库。
- 不新增固定“规划模型→摘要模型→审核模型”链，不每题额外花一次模型调用做审稿。
- 不默认裁剪当前运行中的旧工具消息、thinking 或用户历史；不重写 PydanticAI 工具循环。
- 不更改 provider、模型、思考强度或收费预算，不用缓存命中替代名义输入下降。

## P0 / 兼容性影响

- 手动 Search/Viewer 继续读取同一份 Core 数据；格式处理仅发生在 Wiki Tools / Python Adapter。
- Search MCP 输出原样保持与 Application 一致。Python 可以在验证完整结果后投影模型所需字段，但不得重排、减少候选或删除判断所需摘要。
- 新的定向读取是能力增量：不得静默把现有 `focus` 从排序改成过滤，或把仅某章节完整误报为全页完整。
- 来源先从完整、严格校验的工具响应派生，之后才精简模型载荷；模型不能新增来源或自行声明回答状态。
- 新旧 C# 工具与 Python parser 的契约变动必须成套验证；兼容模式/版本协商在实施第一步明确，不让旧客户端静默理解错误。
- 运行内去重不得跨数据源/运行复用；数据操作 pause、取消、失败或内容版本不明时保守重新读取，不把过期证据当当前事实。

## 方案与待决问题

### 1. 先建立输入构成与质量基线

- 在现有 fake/loopback provider 测试中捕获每次请求的结构，分别统计工具说明/schema、4 轮历史、本次 Search、Read、重复证据和其他消息的字符/字节。
- 字符/JSON 字节不是 Token；真实 Token 以 provider usage 为准，不拿 fixture 写死的 20/5 tokens 或简单字符除法证明节省。
- 每次请求统计仅用于受控测试；生产保留现有用量字段，不新增包含问题、证据或原始请求的日志。真实对照只汇总脱敏数值与评分。
- 基线先固定现有策略，再逐项比较，避免同时换模型、改历史或改变测试数据导致无法归因。

### 2. 精简工具说明与模型投影

- 对照测试 `include_return_schema=False` 与现状，保留两项工具的输入 schema、明确的字段/continuation 使用说明，以及 `_WikiSearchResponse` / `_WikiReadResponse` 严格校验。
- 流程固定为完整响应→严格校验→记录真实来源/轨迹→构造模型专用简洁表示。MCP 服务完整元数据与最终来源对象不因投影丢失。
- Read 模型投影保留页面身份、有效锚点、章节/正文、完整性范围、续读参数、版本/条件等必要事实；精简重复的 requested/resolved 元数据和无需模型计算的字符统计。
- 章节路径按组表达，避免每个短块重复长路径；目录在必要时提供，续页不重复大目录，但后续仍能定位尚未读章节。
- Search 投影保留所有候选原顺序、标题、重定向、摘要与匹配类型；可不向模型发送用于 UI 高亮的字符坐标。不改变 MCP Search 契约或默认 50/显式 1..100 范围。
- JSON 压缩或短说明不得导致 schema 理解、anchor/cursor 使用和错误恢复退化；两种线协议分别测试。

### 3. 新增显式定向章节读取

- 保留现有无参数调用的 `wiki_blocks_v1` 排序行为。候选为在同一个 `read_wiki` 增加显式读取模式：默认 page 沿用现状，section 读取指定 anchor 对应章节及其子章节；仍只有两个工具。
- 先依据结果/目录选择明确章节，再读取必要证据；已有精确 anchor 时可直接定向 Read，不强制每题先加一次目录调用。focus 只负责相关性排序，不作为可靠语义过滤器。
- 返回必须区分“所选章节完整”与“全页完整”，并保留选中范围、其他可读章节和继续获取原页的路径。具体字段/格式版本在实施前形成 JSON 示例并同步 parser/spec；不得直接复用旧 is_complete 含义造成歧义。
- cursor 必须绑定相同页面、focus、模式和范围；下一页不跳项/重复，失效或不匹配要可观察，不从错误 cursor 静默返回空证据。
- anchor 不存在时明确提示并提供可选章节/原页读取方式，不假装已读目标章节。版本/模式/配方条件位于父章节或表头时一并保留必要上下文。
- 配方、获取条件题优先定向；“所有新增物品”、完整列表、跨版本比较、多跳材料题根据证据继续展开，不以返回字数少为充分性判断。
- 当前媒体清理策略先做针对性 fixture 审查；若确实丢失图标中的物品/数量/条件，仅在 formatter 中保留必要文本语义，保持 HTML/脚本/远程指令拒绝，不恢复整页 HTML。

### 4. 单次运行内重复证据处理

- 首选在 `_RunEvidence` 中记录已读取请求和已发送的证据块，不删除 PydanticAI 既有消息。再次返回重叠块时可发送指向仍在上下文中的前次证据的简短引用，新增块照常发送。
- 第一步仍真实调用工具并严格验证，再去重模型投影；这样先消除 Token 重复，不把本地缓存命中冒充当前读取成功，也避免跳过数据可用性校验。
- 去重依据需包括当前运行、已解析页面、内容版本/修改时间、章节及精确文本；不得用去标点/数字规范化误合并不同概率、数量、难度或否定条件。跨章节相同文本默认保留上下文。
- 完全相同的请求仅发简短“与已读结果相同”及真实续读信息；如果内容变化、版本缺失/不可信、不同条件或原证据不在消息上下文中，则不压缩。
- 工具调用请求/返回必须一一配对；发生重复仍计入原限位，不能因本地去重无限绕过工具预算。
- 并行请求须避免双方都返回“见对方”而无人发送实际证据；引用只能指向已进入前次模型请求上下文的证据，否则当前批次至少保留一份完整内容。
- 不跨问题缓存证据、不修改 4 轮历史，不引入 LLM 摘要或持久证据存储。

### 5. 增强答案证据流程

- 在当前运行追踪成功 Search；未满足先 Search 的 Read 通过现有有界重试机制纠正，不执行未经允许的读取。并行批次中 Search 尚未成功时不能视为已满足前置。
- 成功 Search 不要求某页面必须原样出现在第一批结果中；保持多跳、别名、Redirect 和 anchor 可用，不引入过严的候选白名单误拒答。
- 在输出可发布之前确认本次运行满足既有流程且有成功 Read。纠正消耗现有重试/请求预算，不新增不受限的自动重试。
- 提示词明确核对当前问题的子项、版本/模组/难度、材料数量/制作站/获取前提和冲突；只回答本地证据支持的内容，证据不足继续调用或沿用固定不足证据终态。
- 全页未读完不自动等于问题证据不足；反过来某页 found 也不等于完整回答有依据。列表题必须验证覆盖范围，不能因找到几项就声称列全。
- 本阶段保持纯 Markdown 最终输出及宿主来源派生。结论与证据块逐项映射先用于测试评分；不直接新增模型生成的 status/sources、引用 UI 或第三个工具。
- 程序性检查能保证顺序、引用存在和结构，不能证明任意自然语言结论正确。任意无依据答案目前能通过的语义缺口，通过更准确证据、提示和质量评测降低；不得宣称已被确定性校验彻底解决。
- 如逐结论运行时引用校验仍有必要，先作为后续明确契约决定，解决流式已输出后无法撤回的问题，不在本轮顺带改 Sidecar/Presentation。

待决项：定向读取字段/兼容表示；安全重复引用的标识与并行策略；返回 schema 精简后两种协议的可靠性。这些通过现有框架的最小 PoC/测试收敛，不预建新框架。

## 文件变更

实际文件范围如下；另见同目录 `evaluation.md` 的固定 Oracle 与对照记录：

| 文件 | 动作与职责 |
|---|---|
| `agent/src/terraria_wiki_agent/agent.py` | MODIFY：schema 选项、严格校验后的投影、运行内证据去重、Search 前置、提示词 |
| `src/Terraria_Wiki.WikiTools/WikiEvidenceFormatter.cs` | MODIFY：显式章节范围和完整性/续读，保留原 page 模式 |
| `src/Terraria_Wiki.WikiTools/WikiMcpTools.cs`、`WikiToolContracts.cs` | MODIFY：read 输入说明及定向输出契约；Search 不变 |
| `agent/tests/fakes.py`、`test_agent_answers.py`、`test_failures.py` | MODIFY：新增格式、违规顺序、重复证据、并行及流式回归 |
| `agent/tests/test_cross_language.py` | MODIFY：真实 C# 工具的定向/分页/parser 验证；原 openai_fixture_server.py 保持不变并复用其 Chat fixture |
| `agent/tests/test_evidence_efficiency.py` | ADD：单次运行模型输入构成、重复证据与覆盖样例对照 |
| `agent/tests/evidence_wire_fixture.py`、`test_evidence_wire.py` | ADD：Responses/Chat 内存 HTTP 线协议 fixture，接 fake/真实 C# MCP 验证 |
| `tests/Terraria_Wiki.WikiTools.TestHost/WikiToolTestHost.cs` | MODIFY：增加仅测试合成版本/模式与跨页枚举资料，不读取真实数据 |
| `evaluation.md` | ADD：固定质量 Oracle、载荷基线、真实 provider 待验收步骤 |
| `agent/tests/manual_evidence_acceptance.py`、`test_acceptance_harness.py` | ADD：仅显式运行的Windows真实provider验收脚本及两协议离线回归；不进入产品Runtime，默认pytest不读取应用密钥或调用付费服务 |
| `tests/Terraria_Wiki.WikiTools.Tests/WikiEvidenceFormatterTests.cs`、`WikiToolContractTests.cs` | MODIFY：定向范围、复杂表格、完整性、分页及旧调用兼容 |
| `openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md` | MODIFY：实施时同步投影、程序流程、定向阅读与去重契约 |
| `docs/adr/0005-use-pydantic-ai-openai-backend.md` | MODIFY：同步宿主验证后投影及运行内证据引用，不改框架/provider/历史 |

KEEP：`models.py` 公共 answer/source/usage、Sidecar v2、Windows Chat/BuildHistory/会话/设置/UI、Core/Application、Search、Viewer、依赖锁与打包。当前两个其他 draft 和前一 Token change 不夹带修改；README 仅在有真实实施结果时按需更新状态。

## 执行步骤

- [x] 获实施授权后标为 in-progress；确认 Git 保存点和既有修改边界。
- [x] 固定评测问题/数据/历史，补充当前流程缺口与工具说明/载荷基线；对照边界与质量 Oracle 见 evaluation.md。
- [x] 先完成模型说明/元数据投影，验证保留严格校验、全部 Search 候选、来源和两种协议。
- [x] 明确定向读取 JSON 示例与兼容语义，成套实现 C# formatter/契约、Python parser 与测试。
- [x] 实现本次运行内重复证据投影，覆盖版本变化、并行、续页和调用配对。
- [x] 加入 Search→Read 程序前置和证据覆盖提示，确保在正文流式发布前处理违规路径。
- [x] 运行离线、C# MCP 跨语言、Windows build/tests，逐项比较结构收益与证据保留，不下调限位或历史；真实语义质量不由 scripted 测试代替。
- [x] 按用户后续明确授权执行**仅新版两个独立五轮会话**，复用现有DeepSeek配置和密钥，不设费用上限；回填绝对Token、质量、延迟及脚本故障费用未知项，旧版对照已被用户排除。
- [x] 更新 spec/必要 ADR、计划和验证记录，检查 diff/临时数据/进程，独立提交保存本记录及当前实现；不打包、push 或发布。

## 验证与回归计划

### 离线与协议

- 运行现有 ruff、format check、strict mypy 与 pytest；默认禁止真实 provider，保留付费测试显式 opt-in。
- Windows Release solution build 0 warning / 0 error；WikiTools 定向与全量 .NET tests；真实 C# MCP→Python 非付费链路。
- 证明完整 MCP 响应先校验再投影；伪造字段、HTML、错误来源、无效 cursor/anchor 安全处理，最终来源不能由模型扩充。
- 证明正常 Search→Read 可用；直接 Read、并行未完成 Search、无 Read 直接回答不会作为满足证据流程的成功路径。
- 重复结果只在模型历史已有足够原证据时压缩；重复与唯一调用均遵守原预算；取消/失败/并行不死锁、不漏事件。
- 验证流式正文与最终 Markdown 精确一致，远程链接过滤、prompt injection、固定不足证据、超长/超时行为不回归。
- **专门回归最近 4 轮输入逐项一致**：同一会话输入，改前改后历史文本、顺序、轮数和 24000 字符边界相同。

### 证据质量样例

- 简单属性、配方/制作站/数量、获取前置、两页面比较、多跳材料链。
- 长页目标章节位于开头/中间/末尾；“全部新增物品”列表跨节/跨页；中文 focus、真实 anchor 与别名。
- 不同版本、模组、难度下的冲突事实；父章节限定、表格多级表头、rowspan/colspan、嵌套表格和图标语义。
- 证据不存在、仅部分支持、已读无关页、历史回答含错误事实、页面包含不可信指令。
- 每个样例列出应支持的事实/条件、不得出现的结论、允许不足证据的边界；不得只验证“输出包含一个正确词”。

### 真实 Token / 质量对照

- 同 provider、模型、思考强度、问题、本地数据快照和完全相同的最近 4 轮历史；先记录当前源码，再逐阶段对照。
- 使用覆盖上述类型的固定题集，各题重复运行并报告波动。模型请求/工具次数用于解释结果，不作为越少越好的评分。
- 指标：累计 input、cached/uncached input、output/total、延迟、正确事实比例、列表遗漏、无依据结论、错误版本混用和误拒答。
- 相同合成调用序列下，精简投影/重复读取的模型载荷必须变小；必要证据零丢失。真实题集总体输入需下降且质量不退化，允许复杂题为补足证据增加用量。
- 不预先承诺统一百分比；首轮基线后记录具体目标。没有真实用量不得用 JSON 大小下降宣称 Token 下降。
- 不将已缓存 Token 当成免费，也不依据 provider 未提供的字段推算账单。

## 风险与回退

- 定向读取漏条件/列表项：保留原 page 模式与续读路径，评测覆盖父章节和全量枚举；不得静默宣称全页完整。
- 简化 schema 影响模型调用：用两协议与真实 provider 对照，必要时恢复原 schema 说明；本地严格校验始终保留。
- 去重造成证据悬空/版本混用：仅压缩当前运行前次消息中仍存在的同版本证据，不确定则原样发送。
- 更强流程规则增加误拒答/重试：不限制多跳候选身份，利用原有限重试，比较成功率及额外 Token。
- 新读取契约与旧 parser 不兼容：保留默认模式并明确格式兼容策略，C#/Python 成套回退；不修改持久会话/数据库，既有历史仍可读。
- 性能收益小于复杂度：逐阶段验收，未证明收益的候选不合入，不扩展为多模型/摘要/向量系统。

## 进度记录

- 2026-09-11：用户明确授权读取应用现有 DeepSeek 配置和 Credential Manager 密钥，执行两个新会话、每个5轮的真实 provider 验收，不设费用上限；不据此扩大会话/轮数或更改原运行硬限位。密钥仅进入测试进程内存，结果只保存脱敏指标与评分。开始准备基线/优化后对照，原无付费授权的描述为此前状态。
- 2026-09-11：用户纠正为**仅测试优化后版本，两个独立新会话各5轮，不测试旧版**。已停止基线准备，不作前后 Token 下降结论。采用当前 C# TestHost 固定合成数据便于逐项核对，测试脚本为 `agent/tests/manual_evidence_acceptance.py`，不会进入默认 pytest；严格保留应用的 DeepSeek V4 Flash / Responses / low 配置、原运行限位与真实会话历史，不因验收结论继续追加新的付费会话。
- 2026-09-11：两次新会话各5轮有效运行完成，9回答/1正确拒答，输入202526、缓存输入108032、输出6490、合计209016 Token，耗时61.177秒。配方与模式核心事实正确，列表20/20；记录孤立尾字和协议字段/冗余说明两项输出问题。测试统计脚本先前11次启动/定位请求失败且用量未知，可能已付费，单独披露，不混入成功轮次指标。修复统计脚本并增加离线回归后完成有效会话，不再追加付费调用。

- 2026-09-11：用户授权完成计划；从干净的 `09a4cf256f9e6f9f63c544b6a9c484ffdff7a58f` 创建 `optimize-ai-evidence-flow` 分支，原 master 是可恢复保存点。不使用子代理，不调用付费 provider。
- 2026-09-11：完成 v1/v2 兼容、显式 section/父章节上下文、绑定 continuation、精确证据/目录引用、Search 步骤前置、schema 精简和两协议内存 HTTP 测试。检查发现原 formatter 会丢失图标 alt，并将数字标点归一后去重；已局部修正，并补充嵌套表格/多级表头 oracle。
- 2026-09-11：首轮 Python 104 项离线通过、5 项 fake/真实 MCP 协议测试通过；Windows 全量 .NET 344/344。并行构建与 TestHost 一度产生 DLL 复制重试 warning，已停止交叉执行并顺序重建，0 warning/0 error。新增最后的重叠/悬空引用与真实分页验证正在执行，最终数字以末尾验证结果为准。

### 实施契约决定

- 省略 `mode` 继续返回原 `wiki_blocks_v1`，保留 page 排序和整数 cursor；显式 `mode=page|section` 选择 `wiki_blocks_v2`，旧 parser 对新格式明确拒绝。新 Python 同时严格解析 v1/v2，旧工具仍可用。
- v2 保留原完整来源字段并增加 `scope`：`mode`、`selected_section`、`anchor_found`、`page_complete`、`content_version`、`continuation_token` 和带真实 anchor 的 `sections`。`is_complete` 表示本次选择范围已遍历；只有 `scope.page_complete` 表示全页完成。section 包含根导语和父章节正文作为限定上下文。
- 例：输入 `{"title":"月铸镐#配方","mode":"section"}`；输出 `content_format=wiki_blocks_v2`，`is_complete=true`，`scope={mode:section, selected_section:"月铸镐 > 配方", anchor_found:true, page_complete:false, ...}`。锚点不存在时 `anchor_found=false`、无目标正文、返回可用目录；不生成成功来源。
- v2 续读同时传回整数 `cursor=next_cursor` 与 `continuation_token`；token 绑定请求页面、完整 HTML、focus、anchor、mode，变化或缺失即 `invalid-arguments`。不引入服务端缓存。
- 模型重复引用只指向 `RunContext.messages` 中已有的成功工具返回；并行批次未进入先前模型请求的内容不作为引用目标。每次依然真实调用 MCP 并先验证，精确正文、章节、页面和版本共同判定。

- 2026-09-09：只读审查当前 Agent、Wiki formatter/MCP、上下文构造、锁定 SDK 与测试，执行两项非付费离线流程模拟。
- 2026-09-09：用户要求创建优化计划，并明确不改最多 4 轮历史；创建本 draft，源码实施尚未开始。

## 发现、偏差与决策

- 重点是减少无关及重复证据，不压缩用户正常多轮交流，也不减少必要工具调用。
- 当前来源列表去重与模型输入去重是两个不同问题；本方案在严格来源记录之后处理后者。
- 先 Search 后 Read 可以程序验证，任意答案的语义正确性不能仅靠 found/source 数量验证；质量承诺以评测为准。
- 完整返回 schema 与简洁模型说明属于可试验的 Adapter 选择，不需要升级 SDK 或牺牲结果类型校验。
- v2 的 token 绑定内容 hash 而非缓存页面，节省重复序列化整页 HTML；v1 原有整数 cursor 为兼容保留，不能宣称旧协议也具有 v2 的绑定保证。
- 目录有界且不代表页面所有章节；精简投影保留 page continuation 路径，长列表必须自行验证覆盖。未采用每题额外模型审稿或摘要。
- 为取得更准确的每请求基线，ScriptedModel 记录消息时改用深拷贝，防止后续图更新污染先前快照。真实线协议用 MockTransport 捕获，所有网络出口限定在真实本地 MCP，无远程 provider。

## 验证结果

- Windows 虚拟环境 Python：ruff check、ruff format --check（22 文件）、strict mypy（6 源文件）通过；离线 pytest **107 passed / 7 deselected**。7 项为4项跨语言及3项付费测试，付费项未执行。
- Windows 真实 C# TestHost：原多跳 fixture、v2 section/缺 anchor/全范围分页与两协议 fake/真实 MCP 矩阵合计 **6 passed / 3 paid deselected**；其中4项使用真实 MCP，2项为内存模型线协议+fake 工具。使用 PowerShell 显式设置测试开关；WSL 前缀环境变量未透传到 Windows 的初次尝试按 skip 处理，未误报为通过。
- Windows `dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore` 最终 **0 warning / 0 error**。`dotnet test ... --no-build --no-restore` **344/344**：Core 97、Application 56、WikiTools 45、Windows 146。早期构建/TestHost 并发有1项 DLL 文件锁重试 warning，后续顺序构建消除，未修改依赖或提高等待限位。
- 固定合成序列：description 2641→47 bytes；Search 5693→4893 bytes；首 Read 6809→6507 bytes；重复 Read 6809→179 bytes；最终请求工具返回合计19311→11579 bytes。保留全部50候选、精确配方文本和四轮历史。每请求快照、单位及局限详见 `evaluation.md`，不把字节变化称为真实 Token 下降。
- Windows Chat、BuildHistory、Core/Application、公共 models、Sidecar v2、锁文件、打包和其他 changes 对基线无差异；现有四轮/24000字符/段落边界测试通过。开发输出的 Agent 源与工作区逐字一致；真实 TestHost 测试进程已全部退出。
- `git diff --check` 通过；无新增依赖、生产日志、原始请求留档、secret、用户数据库或临时 fixture 数据进入提交；未 publish、打包、push 或发布。
- 后续获用户明确付费授权，已完成新版真实DeepSeek **10/10有效轮次**（9回答、1正确拒答），两会话分别按0/1/2/3/4轮实际历史输入；十轮流式拼接一致。固定合成资料核心事实和20项列表通过核对，两个输出质量观察详见evaluation.md。本轮未做旧版或真实用户Wiki/窗口测试，不能宣称Token相对下降。
- 新验收包装器在首次尝试有usage属性调用错误；11次启动/定位请求用量未保存且可能已计费。仅测试脚本修复后，两协议离线统计回归通过，默认离线pytest **109 passed / 7 deselected**，未添加产品依赖或修改生产行为。真实TestHost进程已退出，密钥/配置未修改，旧版临时源文件已删除（可从Git基线恢复）。

## 完成摘要

已在独立分支实现并自动验证：严格校验后精简模型证据、显式定向章节及父条件、绑定续读、运行内安全重复引用和程序强制Search→Read；最近最多4轮、24000字符策略及原限位不变。用户后续授权并收窄为仅新版两个五轮真实验收，该范围已完成，数据与结论见evaluation.md。核心事实/完整列表/来源/流式通过本题集核对，孤立尾字和冗余协议说明作为两项表达质量观察保留；formatter过滤了本次注入指令，不能外推为模型层注入抵抗。未测试旧版，不作节费/Token下降结论；测试脚本前期11次用量未知请求可能产生额外费用，已明确披露。计划按更新后的验收范围标为completed，不打包、不push、不发布。
