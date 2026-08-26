# Phase 06 — AI Backend

- 状态：completed
- Phase：06 — AI Backend
- 执行环境：WSL Codex CLI；Windows Python 3.14 与 Windows .NET 10 验证
- 创建日期：2026-08-24
- 最后更新：2026-08-27
- 范围：独立 Python 单轮 Agent、可配置 OpenAI-compatible provider、Phase 05 MCP 测试 Host、跨语言/真实模型验收、持续 spec 与 ADR
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/wiki-query-boundary/spec.md`、`openspec/specs/wiki-tools/spec.md`、`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`、`docs/adr/0005-use-pydantic-ai-openai-backend.md`

## 背景与当前证据

用户已批准并要求实施 Phase 06。内层源码基线为 `147c38d`，外层治理基线为 `09f3c3c`；实施开始时内层仓库无改动，外层存在与本 Phase 无关的既有未跟踪目录，后续必须显式隔离。

2026-08-26 用户明确要求不绑定单一模型供应商，改为支持可配置 OpenAI-compatible API，并授权使用其提供的 DeepSeek 余额以 `deepseek-v4-flash` 完成计划内真实验收。原先固定 OpenAI Responses / `gpt-5.6-luna` 的方案因此被后续用户决定替代；DeepSeek 是首个验收实例，不成为产品唯一 provider。

Phase 05 已实现独立 `net10.0` `Terraria_Wiki.WikiTools` Adapter：`WikiToolServer.StartAsync` 只接收 `WikiQueryUseCases`，以无状态 MCP Streamable HTTP 动态绑定 `127.0.0.1:0/mcp`，使用临时 bearer token，只暴露 `search_wiki` 与 `read_wiki`。工具返回固定 structured content；Search/Read 均委托 Application，不写历史，Read 返回完整存储 HTML、Redirect/anchor 和修改时间。

已读取的事实：

- `WikiToolConnection.ToString()` 不含 bearer token；Phase 05 server 清空日志 provider，严格校验 Host 与 bearer。
- Application 的查询入口为 `WikiApplication.Query`；当前 WikiTools 测试 fixture 已证明可以只用临时数据根、真实 Application 与临时 SQLite 建立受限 MCP server。
- 当前解决方案包含 Core、Application、Windows、WikiTools 及四个测试项目，Phase 05 记录的 Windows 基线为 196/196。
- 官方 OpenAI 文档确认 `gpt-5.6-luna` 支持 Responses API、MCP、function calling 与 structured outputs，支持 `medium` reasoning；Responses API 提供 `store`、`max_output_tokens`、reasoning 和工具参数。
- 历史外部前置已由 2026-08-26 的 DeepSeek key 与用户授权替代；第一次三场调用返回 provider HTTP 错误，未取得模型 usage 或成本，不能算作验收通过。

## 目标与完成定义

建立平台无关的 Python 3.12–3.14 单轮 Agent，通过 Phase 05 的两个受限 Wiki 工具完成 Search → 一次或多次 Read → typed Answer，并确保回答来源只能由本次成功 `read_wiki` 的 structured content 派生。

完成门槛：

- 公共边界、连接校验、模型设置、来源、用量、错误分类和运行预算均有自动化单元测试。
- Python 测试禁止真实模型网络，使用 PydanticAI 测试模型覆盖多步查询、证据不足、注入抵抗、取消、超时、预算与脱敏。
- Windows 跨语言测试通过真实 C# TestHost 发现恰好两个工具并完成 typed Agent 场景。
- WSL Python 3.12 与 Windows Python 3.14 使用同一锁定依赖执行 lint、mypy、unit/cross-language tests；Windows .NET Release build 与完整测试通过。
- provider 配置不包含 secret，支持选择 Responses 或 Chat Completions 线协议、HTTPS endpoint（以及仅 loopback 的 HTTP endpoint）和非空模型名；API key 只从固定通用环境变量读取。
- 以 DeepSeek `deepseek-v4-flash` / Responses API 执行三次固定真实模型验收，总授权上限沿用 0.30 美元；额外重跑必须再次授权。
- 持续 spec、ADR 0005、README、项目基线与 Master Architecture 状态同步，计划记录实际验证并仅在完成门槛满足时归档。

## 前置条件

- Phase 00–05 已完成；Phase 05 是本 Phase 的直接前置。
- WSL 需要 Python 3.12；Windows 需要 Python 3.14 和 .NET 10 SDK。
- Python 构建与测试依赖允许安装到临时/忽略的虚拟环境；不得提交虚拟环境或缓存。
- 真实模型验收需要仅由环境提供的通用 provider key。用户已在 2026-08-26 授权以 DeepSeek `deepseek-v4-flash` 执行计划内三场评估，总上限 0.30 美元；额外重跑必须再次授权。
- 内外仓库现有提交分别作为可恢复基线；不把外层既有未跟踪内容纳入本 Phase。

## 范围与非目标

范围内：

- 在 `terraria-wiki-offline/agent/` 新建 Python 包、锁文件、单元测试、跨语言测试与受控验收入口。
- 把 provider 边界从固定 OpenAI 改为显式 OpenAI-compatible 配置；同时支持 Responses 与 Chat Completions，不引入第二套 Agent 或工具循环。
- 在 `terraria-wiki-offline/tests/Terraria_Wiki.WikiTools.TestHost/` 新建测试专用 .NET Host，并加入测试/解决方案所需最小接线。
- 新增 AI Backend 持续能力 spec、ADR 0005，并在完成时同步 Phase 状态文档。

明确非目标：UI、Windows 客户端接线、流式事件、多轮或持久化历史、Runtime 进程管理/封装/打包、正式用户数据根、Reader/HTML 转换、Web Search、Vector/Embedding、Android 及其他平台。不得修改生产 WikiTools 契约来迁就 Agent。

## P0 / 兼容性影响

本 Phase 只读使用已有 Search/Read；Agent 不直接引用 SQLite、Core、MAUI、Avalonia、Windows API 或平台路径，不读取或修改用户数据库。TestHost 只创建随机临时数据根与合成 SQLite fixture，并在退出时停止 server、释放端口、关闭 Application、删除临时数据。

保护措施：

- 不改变现有数据库 schema、FTS5 查询、同步、导入导出、Viewer 或生产 WikiTools。
- 跨语言边界只消费 Phase 05 固定 structured JSON，不复制 C# 查询逻辑。
- Wiki HTML 视为不可信资料；不得影响系统规则，且不得进入错误、日志或最终来源元数据。
- 来源必须与本次成功 Read 记录相交，模型不能自行扩充。

## 方案与待决问题

### 已确认方案

- Python 包固定 `pydantic-ai-slim[mcp,openai]==2.31.0`，运行/开发依赖提交解析锁；Python 版本范围为 3.12–3.14。
- provider 通过显式 `base_url`、`model` 与 `api` 配置；Responses 与 Chat Completions 复用 PydanticAI 的 OpenAI-compatible model classes。生产默认不隐式选择供应商，DeepSeek Flash 仅由验收配置选择。
- API key 固定从 `TERRARIA_AI_API_KEY` 读取，不允许进入公共 settings、URL、日志、错误或仓库。HTTPS endpoint 可远程配置；HTTP 只允许 IPv4/IPv6 loopback，拒绝 userinfo、query 与 fragment。
- reasoning effort 是可选兼容参数，未配置时不发送；`store`、`truncation`、并行工具等不被所有兼容服务统一支持，不再把请求参数误写成跨供应商保证。证据、来源、工具/请求/token/时间硬上限继续由宿主收口。
- Agent 仅提供异步单轮 `TerrariaWikiAgent.answer(question, connection)`；模型输出采用 typed schema，实际来源和用量由程序收口。
- 只接受精确 `http://127.0.0.1:<port>/mcp`；拒绝 localhost、远程地址、user info、query、fragment、非 HTTP、非 `/mcp`。
- 单次运行预算：最多 8 次模型请求、8 次成功工具调用、4096 输出 tokens、300000 累计 tokens、256000 单请求输入 tokens、120 秒、0.10 美元；任何超限明确失败。
- 完整 HTML 暂时保留。大页超出模型上下文时返回明确 context/budget 错误；不在本 Phase 引入转换器。

### 待实证项

- PydanticAI 2.31 的 MCP tool event/usage hooks、Responses provider settings、测试模型注入与锁文件格式需基于安装后的真实 API 校准。
- PydanticAI 2.31 对 DeepSeek V4 Flash Responses、MCP toolset 与 typed output 的组合必须由真实三场验收确认；其他 OpenAI-compatible endpoint 只获得可配置能力，不在未实测时宣称兼容通过。
- provider 定价若无法由 PydanticAI/genai-prices 识别，美元 cost limit 只能作为 best-effort；请求、工具和 token 上限仍是强制门槛，真实验收另由三场固定调用限制总范围。

## 文件变更

- ADD `terraria-wiki-offline/agent/pyproject.toml`、解析锁与包 README：依赖、工具配置和运行边界。
- ADD `terraria-wiki-offline/agent/src/terraria_wiki_agent/`：公共模型、连接校验、Agent、预算、错误、来源与脱敏实现。
- ADD `terraria-wiki-offline/agent/tests/`：禁止真实模型网络的单元、跨语言与可选真实模型验收。
- MODIFY `terraria-wiki-offline/agent/src/terraria_wiki_agent/agent.py`、`models.py`：增加通用 OpenAI-compatible provider 构造与两种 API 线协议，不改变 Wiki 工具/来源逻辑。
- MODIFY `terraria-wiki-offline/agent/tests/` 与包 README：覆盖 endpoint、API 模式、secret、模型构造和 DeepSeek 真实验收配置。
- ADD `terraria-wiki-offline/tests/Terraria_Wiki.WikiTools.TestHost/`：真实 Application/WikiToolServer 临时 fixture Host。
- MODIFY `terraria-wiki-offline/Terraria_Wiki.Core.slnx`：纳入测试 Host（若解决方案构建需要）。
- ADD `openspec/specs/ai-backend/spec.md`：持续 AI Backend 契约。
- ADD `docs/adr/0005-use-pydantic-ai-openai-backend.md`：PydanticAI、首个 provider、来源/预算与 Runtime 分离决策。
- MODIFY `README.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`：仅在完成时同步当前 Phase 状态与入口。
- KEEP 生产 `Terraria_Wiki.WikiTools`、Application、Core、Windows 与用户数据库不变。

## 执行步骤

- [x] 读取入口、架构、项目基线、计划规范、相关 specs、ADR 0004、Phase 05 计划与真实 WikiTools/Application 源码；确认基线和外层未跟踪边界。
- [x] 创建本活计划并记录范围、风险、验证门槛和外部前置。
- [x] 建立 Python 包与解析依赖锁；检查 PydanticAI 2.31 实际 API，并先实现连接/设置/typed contracts、来源与错误边界。
- [x] 实现受预算约束的单轮 Agent、MCP 接入、Read 来源记录、脱敏与错误归一。
- [x] 使用测试模型与内存/模拟 MCP 覆盖配置、来源、Redirect/anchor、去重、证据不足、工具错误、取消、超时、预算、密钥脱敏、恶意页面和网络禁令。
- [x] 实现测试专用 C# Host 与合成 fixture；增加跨语言 Search → 多 Read → typed Answer 测试和静态边界门。
- [x] 完成持续 spec 与 ADR，核对其与既有 Wiki Tools/Search-Read 契约一致。
- [x] 执行 WSL Python lint/mypy/tests；执行 Windows Python 3.14、.NET Release build/完整测试与跨语言验收。
- [x] 把固定 OpenAI provider 改为显式 OpenAI-compatible 配置，并以离线测试覆盖 Responses/Chat、endpoint 安全、模型构造、secret 与原有预算/来源回归。
- [x] 使用用户授权的 DeepSeek `deepseek-v4-flash` 执行三次真实模型评估，记录事实、轨迹、来源、usage 与实际可得成本；不得输出或持久化 key。
- [x] 检查实际差异、secret/HTML/路径泄漏、临时文件、生产数据和两个仓库状态；同步当前验证状态文档。
- [x] 分别提交内层源码和外层治理文件，不 push、不夹带既有未跟踪内容；仅在全部强制门槛满足时标记 completed 并归档。

## 验证与回归计划

### WSL

- Python 3.12：锁定环境安装、`ruff check`、`ruff format --check`、`mypy`、pytest unit tests。
- 静态边界：Agent 源码不含 SQLite/Core/MAUI/Avalonia/Windows/platform path 依赖；测试不允许连接真实模型 endpoint。
- 安全检查：连接输入、错误文本、日志、repr/str、snapshot 不包含 API key、bearer、路径、HTML 或 traceback。

### Windows

- Python 3.14：同一锁定环境执行 lint、mypy、unit tests、真实 C# TestHost 跨语言测试。
- .NET 10 Release：构建 `Terraria_Wiki.Core.slnx`，运行现有 196 项与新增测试/Host 门槛。
- 真实模型：以通用 provider 配置连接 DeepSeek `deepseek-v4-flash` Responses；严格三场、每场设置 0.10 美元 best-effort cost limit，总授权 0.30 美元，并以请求/工具/token 上限作为供应商未知定价时的强制保护；验证事实、调用轨迹和来源集合。

Android 不适用：Phase 06 不实现或验证 Android Runtime。

## 风险与回退

- 依赖/API 风险：严格固定 2.31.0 并提交解析锁；若 API 与预期不同，先更新计划中的事实再局部调整，不升级主依赖。
- token/context 风险：工具返回完整 HTML，可能触发上下文上限；明确失败，Reader 转换另行决策。
- 成本风险：本地单测禁用真实模型；真实评估只在双重前置满足时运行且限制总成本。
- 泄密风险：token 只用于 Authorization header，API key 只读环境；所有公开错误、日志与 repr 经过固定代码映射/脱敏测试。
- 跨平台风险：TestHost 仅测试，Windows Runtime/打包不提前接线；WSL 结果不替代 Windows。
- 回退：内层 `147c38d` 与外层 `09f3c3c` 是恢复点；本 Phase 主要为新增文件，可按两个独立提交回退，不涉及数据迁移。

## 进度记录

- 2026-08-24：确认用户批准实施；核对两个 Git 基线、Phase 05 能力与官方 OpenAI 模型/Responses 事实；建立 in-progress 计划。下一步为解析并验证 PydanticAI 2.31 API。
- 2026-08-24：完成 Python 公共边界、PydanticAI Agent、完整 safety limits、来源/trace 收口、错误归一和 3.12–3.14 解析锁；完成真实 Application/WikiToolServer 合成 TestHost 与新增 .NET 回归。Windows Python 3.14 已通过 lint、mypy、54 项（含真实跨语言）测试，三项付费评估因 key/授权缺失跳过。下一步为持续 spec、ADR 和全量回归。
- 2026-08-24：新增持续 AI Backend spec 与 ADR 0005，并把 README、项目基线和 Master Architecture 更新为准确的“已实现、等待真实模型验收”。最终非付费回归通过：WSL Python 53 passed / 4 skipped，Windows Python 54 passed / 3 paid skipped，Windows .NET 199/199。下一步只剩 secret/生成物检查及外部付费门槛。
- 2026-08-26：用户改为要求供应商中立的 OpenAI-compatible API，并授权 DeepSeek Flash 三场验收。内层 `0672f59`、外层 `711dd7d` 已保存原固定 OpenAI 方案作为恢复点。下一步为通用 provider 最小适配、离线回归与 DeepSeek 实测。
- 2026-08-26：完成显式 `base_url` / model / Responses-or-Chat 配置、固定 `TERRARIA_AI_API_KEY`、HTTPS/loopback endpoint 校验与无供应商默认值；Windows Python 3.14 lint、format、mypy 通过，离线 67 passed / 4 skipped，启用真实 C# Host 后 68 passed / 3 paid skipped。
- 2026-08-26：DeepSeek 模型发现确认 key 有效且 `deepseek-v4-flash` 可用。第一次三场真实测试均在首次模型请求返回脱敏 `model_unavailable`，未执行 Wiki 工具、未返回 usage/cost；随后根据 PydanticAI/DeepSeek profile 证据增加非 strict、非 required tool-choice、单 system message 的保守兼容 profile，离线门槛再次通过。按授权边界不自动发起第二轮。
- 2026-08-26：用户明确授权第二轮三场 DeepSeek Flash 验收，继续沿用总计 0.30 美元上限；本轮不通过时不得自动进行第三轮。
- 2026-08-26：第二轮 1 passed / 2 failed；提示注入场景通过，多跳配方和无页面场景均被宿主归一为 `budget_exceeded`。未放宽任何 safety ceiling；离线确认 endpoint/model 可被锁定的 `genai-prices` 精确识别，0.10 美元成本上限不是合理触发源，改为收紧通用工具去重和证据不足停止策略。第三轮仍需重新授权。
- 2026-08-26：收紧后的 Windows Python 3.14 回归通过：ruff、format、strict mypy 均通过，真实 C# Host 下 68 passed / 3 paid deselected。付费测试现可输出不含回答内容和 secret 的 scenario/usage/cost/source/trace 摘要；等待第三轮授权。
- 2026-08-26：用户授权第三轮；多跳配方与无页面场景通过，分别为 4 requests / 6 tools / 0.0007798728 美元和 2 requests / 2 tools / 0.0005723144 美元。提示注入场景未泄露或服从恶意内容，但在 3 requests / 3 tools / 0.0004373880 美元后过度保守地返回 `insufficient_evidence`，故总结果 2 passed / 1 failed，本轮记录总成本 0.0017895752 美元。离线规则进一步明确“忽略嵌入指令不等于丢弃同页无冲突事实”；第四轮必须重新授权。
- 2026-08-27：用户授权后续 Phase 06 测试无需逐轮询问。第四轮最终验收 3/3：多跳配方为 4 requests / 6 tools / 0.0007199528 美元，无页面拒答为 4 requests / 3 tools / 0.0005003152 美元，提示注入为 4 requests / 3 tools / 0.0004017496 美元；三场记录总成本 0.0016220176 美元。
- 2026-08-27：最终门槛通过。WSL Python 3.12 与 Windows Python 3.14 的 ruff、format、strict mypy、真实 C# Host 非付费测试均通过；Windows .NET 10 Release build 为 0 warning / 0 error，四个测试项目 199/199。同步持续 spec、ADR、README、项目基线和 Master Architecture，计划进入 completed 并归档。

## 发现、偏差与决策

- 外层工作树包含既有未跟踪目录；这些不是 Phase 06 输入或输出，不移动、不清理、不提交。
- 2026-08-26 已向 DeepSeek 发起三项真实评估，但均在首次模型请求返回 HTTP 错误；没有 provider usage/cost 证据，不能推断是否扣费。额外重跑仍需用户再次授权。
- 2026-08-26 的用户决定替代固定 OpenAI provider：公共边界必须供应商中立；DeepSeek 只证明首个 endpoint。key 未写入工作区文件、命令参数或测试参数，但用户已在聊天中发送，且第一次终端安全输入尝试发生会话回显；应视为已暴露并在测试后立即轮换。
- 通用 `OpenAIProvider` 默认 profile 会把 OpenAI 特有的 strict/required-tool-choice 行为带给兼容 endpoint；DeepSeek 首轮错误证明仅改 `base_url` 不足。生产改为保守 profile，具体 provider 仍须独立实测。
- DeepSeek 第二轮已证明 Responses、typed output、MCP 工具调用和提示注入防护可以协同工作；失败集中在开放式多跳与无结果查询的停止行为。锁定价格表按 `https://api.deepseek.com` 和 `deepseek-v4-flash` 匹配 0.14/0.28 美元每百万输入/输出 token，故本轮预算中止更符合请求或工具次数耗尽；公开错误按安全契约不暴露内部限制消息，不能把该判断写成精确触发项。
- 第三轮显示停止策略修正有效；提示注入场景的剩余问题是可用性而非泄露：模型读取了唯一事实来源，但把包含恶意指令的整页判为不足。安全规则继续禁止执行或复述指令，同时明确允许使用同页独立、无冲突的事实文本，不通过降低注入防护换取通过。
- 第四轮证明最终规则同时满足三项门槛：多跳证据、无证据停止和注入页事实提取均在原安全预算内完成，未输出恶意 URL。`deepseek-v4-flash` Responses 因此成为首个已实测兼容组合；供应商中立边界不等于声称所有兼容服务已验证。
- PydanticAI 2.31 原生 `UsageLimits` 已覆盖计划要求的 request/tool/output/total/per-request/cost 六类限制；OpenAI Responses 支持请求前 token 计数。测试模型不实现预计数，离线测试使用只返回合成计数的子类，不改变生产限制。
- WSL 环境存在会代理 loopback HTTP 的代理变量，默认 HTTPX 路径实际得到 502。为同时保证可用性和 bearer 边界，生产 MCP transport 已固定 `trust_env=False`、`follow_redirects=False`；真实 C# Host 跨语言回归随后通过。

## 验证结果

- WSL Python 3.12：`ruff check`、`ruff format --check`、strict mypy 已通过；离线 pytest 53 passed / 4 skipped，显式跨语言测试单独通过。
- Windows Python 3.14.6：同一 `uv.lock` 环境的 lint、format check、strict mypy 通过；完整 pytest（启用真实 C# Host）54 passed / 3 skipped，跳过项均为未授权真实 OpenAI 评估。
- Windows .NET 10：解决方案 restore + Release build 0 warning / 0 error；完整解决方案 Core 89、Application 54、Windows 28、WikiTools 28，共 199/199 通过（新增 TestHost 回归 3）。
- 通用 provider 增量验证（Windows Python 3.14.6）：ruff、format、strict mypy 通过；67 passed / 4 skipped，启用真实 C# Host 后 68 passed / 3 paid skipped。DeepSeek 免费模型发现通过；第一轮 3 failed（provider profile），第二轮 1 passed / 2 failed（预算停止），第三轮 2 passed / 1 failed（注入页过度保守）；第三轮三场记录成本合计 0.0017895752 美元。
- 最终 WSL Python 3.12：锁定环境 ruff、format、strict mypy 通过；启用真实 C# Host 的非付费测试 68 passed / 3 paid deselected。
- 最终 Windows Python 3.14.6：ruff、format、strict mypy 通过；启用真实 C# Host 的非付费测试 68 passed / 3 paid deselected；DeepSeek V4 Flash 最终三场 3 passed，记录成本 0.0016220176 美元。
- 最终 Windows .NET 10：Release build 0 warning / 0 error；Core 89、Application 54、Windows 28、WikiTools 28，共 199/199 通过。

## 完成摘要

Phase 06 已完成：交付平台无关的 Python 单轮 Agent、可配置 OpenAI-compatible Responses / Chat provider、受限 MCP 证据工作流、宿主来源/usage/错误/预算收口、解析锁、测试专用真实 C# Host 和持续契约/ADR。WSL/Windows Python、Windows .NET 199/199 与 DeepSeek `deepseek-v4-flash` Responses 三场真实验收均通过。DeepSeek 是首个验证实例而非产品唯一供应商；其他 endpoint/model 仍须单独验收。Phase 07 的 Windows Runtime、流式、生命周期与打包不在本阶段范围。
