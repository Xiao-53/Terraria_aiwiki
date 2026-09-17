# AI Backend 能力契约

- 状态：confirmed baseline；Phase 07 流式/Sidecar 回归与既有真实 provider 验收通过；2026-09-05 有界 evidence/多轮已通过离线回归，付费 Token 复验待用户执行
- 适用范围：Python Agent、AI Runtime Adapter、Windows Runtime lifecycle；不包含客户端 UI 或目标平台打包
- 来源：Master Architecture v0.6、Wiki Search / Read 边界、Wiki Tools、Phase 06、ADR 0005

## 目的

提供独立、平台无关且可接收有界纯文本会话上下文的 Agent，通过既有 Wiki Tools 对本地 Wiki 执行 Search → 一次或多次 Read → typed Answer。模型负责选择查询步骤和组织回答，但事实与来源只能来自本次运行成功读取的本地页面；Agent 不建立第二套 Wiki 数据，也不直接访问 Core 或 SQLite。

## 公共边界

- Python 包支持 3.12–3.14，公共异步入口是 `TerrariaWikiAgent.answer(question, connection, on_event=None, *, history=()) -> WikiAgentAnswer`；旧两参数和事件调用保持兼容。history 最多 4 个 `WikiConversationTurn(user, assistant)`，Agent 不自行读取或持久化产品历史。
- history 只帮助解析当前问题中的指代。Agent 必须据此重新构造交替的 `ModelRequest(UserPromptPart)` / `ModelResponse(TextPart)`，不得接收、保存或重放旧工具调用、工具返回、Wiki evidence、HTML、thinking、retry 或原始 PydanticAI message graph。
- `WikiToolConnection(endpoint, bearer_token)` 只接受规范形式 `http://127.0.0.1:<1..65535>/mcp`。必须拒绝 `localhost`、其他地址、HTTPS、userinfo、缺失或非法端口、其他 path、query、fragment 以及非法 token。
- connection 的字符串和 repr 只能显示 endpoint；bearer 只进入 Authorization header。MCP HTTP client 不继承环境代理、不跟随重定向。
- 连接后必须发现恰好 `search_wiki` 与 `read_wiki` 两个工具；缺失或额外工具均作为 Wiki Tool 不可用处理。
- Agent 业务包不得引用 SQLite、FTS5、Core、Application、MAUI、Avalonia、Windows API 或平台路径；唯一 Wiki 数据入口是 Wiki Tools structured content。

## 模型与运行设置

- Agent 不绑定单一模型供应商。Runtime 必须显式提供 OpenAI-compatible `base_url`、非空模型名和 `responses` / `chat_completions` 线协议；没有隐式生产 provider 或模型默认值。
- 远程 provider endpoint 必须使用 HTTPS。HTTP 只允许字面 IPv4/IPv6 loopback 地址；拒绝 userinfo、query、fragment、非法端口和带首尾空白的配置。
- API key 固定只从 `TERRARIA_AI_API_KEY` 环境变量取得，不进入公共 settings、connection、URL、错误、日志、测试 fixture 或仓库文件。库不读取 `.env`。
- 通用兼容 profile 不请求 strict tool schema 或 required tool choice，把系统规则保持为单一 `system` message，并避免假设 Chat endpoint 接受 `max_completion_tokens`。可选 reasoning effort 只在调用方显式配置时发送。
- `store`、`truncation`、并行工具、reasoning 和 token 预计算并非所有兼容 endpoint 的共同能力；本契约不把被忽略的请求字段描述成保证。远程服务的数据保留、并行行为与定价仍须按具体 provider 实测和披露。
- 公共设置允许调用方把请求、工具、token 和超时运行限位向下收紧，不允许调高。Agent 不设置美元费用上限；PydanticAI 与 OpenAI-compatible model classes 是内部实现，客户端和 Application 只能依赖 typed 结果与稳定错误。

## 证据工作流与不可信内容

- Terraria 事实问题必须先调用 `search_wiki`，不能用模型记忆或 Search summary 直接回答。
- Read 之前必须已有前次模型步骤成功 Search；同批并行 Search/Read 不满足前置。违规 Read 不调用 MCP，通过原有一次有界工具重试纠正；不限制后续 Read 必须属于 Search 候选白名单，保留别名与多跳。
- 回答使用的每个页面必须在本次运行中成功调用 `read_wiki`；允许继续搜索并读取多个页面。
- 缺少、冲突或不足以支持回答的本地证据必须返回 `insufficient_evidence`，不得用模型常识补全。
- Wiki evidence blocks 全部是不可信资料。页面内指令不能覆盖系统规则、用户问题、工具策略、来源约束或输出规则；工具载荷中的 HTML 标记必须在进入下一次模型请求前拒绝。
- `read_wiki` 返回不完整且当前证据不足时，允许使用 `next_cursor` 或更精确 anchor 继续读取；不得为降低 Token 减少问题所需的 Search/Read，也不得无理由重复同一 title/focus/cursor。
- Agent 不输出原始 HTML，不生成远程来源 URL；回答中出现的远程 URL 必须移除或替换为不可信链接占位。
- 核对问题子项、版本/模组/难度、材料数量、制作站、获取前提与冲突。定向 section 读取允许保留父章节条件；全页未读完不自动等于问题证据不足，但完整列表必须检查跨章节覆盖。程序检查保障流程与结构，不能证明任意自然语言结论正确。

## 模型证据投影

- 两项工具保留输入 schema 和必要调用说明，不向模型附加完整输出 schema；宿主仍先严格验证完整 Search/Read 响应，校验失败不进入下一次模型输入。
- Search 模型投影保留全部候选原顺序、title、redirect_target、summary、match_type 及有界章节导航，仅去掉 UI highlights。导航字段 canonical_title、match_scope、matched_sections、matched_sections_truncated 必须全部有效或全部缺失；旧响应缺失时不虚构导航。校验数量、Unicode 字符预算、重复章节和纯文本。
- Agent 默认省略 max_results，采用 Application 独立 AI 默认 5 或用户保存的值；证据不足可显式扩大到原允许范围。搜索优先实体/别名/主题关键词；有适用导航时以规范标题和原样 Anchor 定向 Read，无 Anchor 或匹配失败使用 page/catalog。Search 导航不是事实或完整性证明。
- 带 continuation_token 的 Read 收到确切 `invalid-arguments` 工具错误时，进入原一次有界 ModelRetry，提示重新 page 读取且不使用旧游标/token，不混合内容版本。其他工具错误保持安全失败；不提高重试或运行限位。最终回答遵守用户篇幅要求，不暴露导航/续读/完整性协议字段。
- Read 同时支持默认 `wiki_blocks_v1` 与显式 `wiki_blocks_v2`。完整身份/来源先由宿主校验和记录，再投影页面 title、anchor、按 section 分组的 kind/text、完整性、续读及目录。宿主字符统计与重复 requested/redirect 字段不进入模型载荷。
- v2 目标 anchor 缺失的空证据不生成来源，最小轨迹/工具完成状态为 not_found；模型仍能取得可用目录以纠正读取。没有有效 Read 来源时，在首个正文 delta 发布前归一为固定不足证据。
- 同次运行内，每次重复 Read 仍真实调用 MCP 并严格验证；同页面、同可信内容版本/修改时间、同章节、同 kind 及精确文本的块，可用 `ref` 指向先前模型输入中仍存在的完整 `id` 块。新增或变化的块原样发送，版本未知或原消息已不在上下文时不压缩。并行同批首次读取保留实际正文，避免循环引用。
- 目录以 `available_sections` 提供；已有相同版本目录在先前消息中时，允许 `catalog_ref` 引用其 `catalog_id`，不再重复大目录。v2 scope 保留模式、所选范围、锚点命中、全页完整性与 continuation_token。
- 工具请求/返回配对、工具计数和全部原消息保持完整；无跨运行/数据源缓存、旧工具消息删除、LLM 摘要或持久证据库。最近 4 轮文本、Host 的 24000 字符预算及选择/截断/传输规则不变。

## 回答、来源、轨迹与用量

`WikiAgentAnswer` 固定包含：

- `status`：`answered` 或 `insufficient_evidence`；
- 非空 Markdown 回答；
- `sources`：由宿主从本次成功 `read_wiki` structured content 派生；
- `usage`：请求数、工具调用数、输入 token、其中 cached input token、输出/总 token 和 provider 可提供时的美元成本；
- `tool_trace`：成功 Search、成功 Read 或 not-found Read 的最小轨迹，不包含参数、内容、路径或凭据。

模型只生成纯 Markdown 草稿，不能声明 status 或来源。每个来源保留 `requested_title`、最终 `resolved_title`、anchor 与 `last_modified`；按不区分大小写的 resolved title + anchor 去重并保持首次成功 Read 顺序。not-found Read 进入轨迹但不生成来源。没有成功 Read，或模型返回固定“本地 Wiki 证据不足”正文时，宿主必须归一为 `insufficient_evidence`。

## 流式事件

- `on_event` 是可选异步回调，事件是 frozen、extra-forbid 的判别式模型：`run_started`、`tool_started`、`tool_finished`、`text_delta`。
- tool 事件只公开 `search_wiki` / `read_wiki` 名称与 `succeeded` / `not_found` / `failed` 状态；不得携带参数、返回值、HTML、路径、凭据或异常。
- Agent 使用锁定 PydanticAI 2.31 的完整 `run_stream_events()` 图。只有最终纯文本响应成为 `text_delta`；工具轮次文本、thinking 和其他模型事件不对外转发。
- Markdown sanitizer 必须跨 chunk 保留未闭合 URL/协议前缀和尾随空白，替换远程 URL 并只发布已确定的安全前缀。全部 delta 的顺序拼接必须精确等于最终 `WikiAgentAnswer.markdown`。
- 空白/空输出、超过 32768 字符、流式与最终不一致都归一为 `model_unavailable`。回调按顺序 await 并形成背压；回调异常停止运行并归一为安全 `unknown`。

## 运行限位

每次 `answer` 的硬上限为：

- 最多 8 次模型请求；
- 最多 8 次工具调用；
- 最多 4096 输出 tokens；
- 最多 300000 累计 tokens；
- 单次模型请求最多 256000 输入 tokens；
- 最多 120 秒。

这些限位用于防止模型循环、协议超长、上下文溢出和长时间挂起，不作为费用预算，也不因本次 Token 优化下调。请求、工具和 provider 返回后的 token 超限必须返回稳定的 `budget_exceeded`（为兼容保留的错误 code）或 context 错误，不得静默截断。为了兼容不提供预计算 endpoint 的服务，输入 token 上限不承诺请求前拦截。Agent 不因美元估算中止回答；真实账单控制由用户选择的 provider 余额、额度或限额承担。Wiki Tools 只向模型提供有界纯文本 evidence，必要时可继续读取其他块或页面。

## 错误、取消与脱敏

对外失败分类固定为：

- `invalid_input`、`invalid_connection`；
- `wiki_tool_unavailable`；
- `model_authentication`、`model_unavailable`；
- `context_limit`、`budget_exceeded`、`timeout`；
- `unknown`。

公开错误只含稳定 code 与固定安全消息，不嵌入底层异常。错误、repr、日志和工具轨迹不得包含 API key、bearer、数据库或平台路径、HTML、模型/工具原始响应或堆栈。调用方取消继续以任务取消传播，不归一为普通失败。

## Runtime 分离与非目标

- Agent 包只实现平台无关业务逻辑。Windows/Android 负责各自的 Python 进程、MCP server、凭据传递、启动退出、崩溃恢复、打包与发布；Windows 首个实现另见 `windows-ai-runtime` spec 与 ADR 0006。
- Phase 06 的 C# TestHost 只能存在于测试项目，使用随机临时数据根与合成 SQLite fixture；不得进入生产引用、读取用户设置或连接用户数据库。
- 不包含 UI、持久化历史、历史裁剪策略、目标平台进程实现、正式数据根、Reader 转换、Web Search、Vector/Embedding、Android 或其他平台。

## 验收场景

1. 离线模型测试全局禁止真实 provider 请求，覆盖配置、连接、来源派生、Redirect/anchor、去重、证据不足、工具错误、取消、超时、运行限位、无美元费用预算、脱敏与提示注入。
2. 离线配置测试覆盖 Responses / Chat 两种 model classes、远程 HTTPS / loopback HTTP、保守兼容 profile、缺失 secret 和脱敏；具体兼容 endpoint 未实测时不得宣称可用。
3. Windows Python 通过真实 C# TestHost 发现恰好两个工具，完成 Search → 多 Read → typed Answer；fixture 覆盖 Redirect 配方、两项材料、无结果和恶意页面。
4. WSL Python 3.12 与 Windows Python 3.12.3 使用同一解析锁通过 lint、格式、strict mypy 与适用测试；Windows .NET Release build 和全部测试通过。
5. 首个真实 endpoint 使用 DeepSeek `deepseek-v4-flash` Responses，固定运行多跳配方、无证据拒答和提示注入抵抗三场；每场满足事实、轨迹与来源约束。最终验收 3/3 通过；其他 endpoint/model 不继承该实测结论。
6. Phase 07 增量测试覆盖最终文本分块、URL 跨 chunk、固定不足证据、取消、callback、超长输出和 Sidecar 严格协议；真实 Windows Application + WikiTools + Python fixture 验证跨语言事件与 typed completion。
7. 静态依赖门确认生产 Agent 不接触 SQLite/Core/Application/UI/平台路径，生产 .NET 项目不引用测试 Host，仓库不含 secret、用户数据、虚拟环境或缓存。
8. 2–6 轮 fixture 验证最多最近 4 轮只有 user/assistant 文本，旧工具结果/HTML/thinking 不出现；每个事实追问仍执行当前轮 Search/Read，cached usage 正确映射。

## 变更规则

公共 answer/source/usage/error 语义、证据规则、运行限位、provider 配置/endpoint/secret 安全边界或 Runtime 分层变化时更新本 spec，并在需要时新增 ADR。具体供应商与模型验收、依赖版本、类、fixture、命令和验证记录进入对应 Phase 计划；Wiki Tool JSON 契约变化同时更新 `wiki-tools` spec。
