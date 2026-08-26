# AI Backend 能力契约

- 状态：confirmed baseline；Phase 06 真实 OpenAI 验收待外部前置
- 适用范围：Python Agent、AI Runtime Adapter、Windows Runtime lifecycle；不包含客户端 UI 或目标平台打包
- 来源：Master Architecture v0.6、Wiki Search / Read 边界、Wiki Tools、Phase 06、ADR 0005

## 目的

提供独立、平台无关的单轮 Agent，通过既有 Wiki Tools 对本地 Wiki 执行 Search → 一次或多次 Read → typed Answer。模型负责选择查询步骤和组织回答，但事实与来源只能来自本次运行成功读取的本地页面；Agent 不建立第二套 Wiki 数据，也不直接访问 Core 或 SQLite。

## 公共边界

- Python 包支持 3.12–3.14，公共异步入口是 `TerrariaWikiAgent.answer(question, connection) -> WikiAgentAnswer`；每次调用是独立单轮，不接收或保存历史。
- `WikiToolConnection(endpoint, bearer_token)` 只接受规范形式 `http://127.0.0.1:<1..65535>/mcp`。必须拒绝 `localhost`、其他地址、HTTPS、userinfo、缺失或非法端口、其他 path、query、fragment 以及非法 token。
- connection 的字符串和 repr 只能显示 endpoint；bearer 只进入 Authorization header。MCP HTTP client 不继承环境代理、不跟随重定向。
- 连接后必须发现恰好 `search_wiki` 与 `read_wiki` 两个工具；缺失或额外工具均作为 Wiki Tool 不可用处理。
- Agent 业务包不得引用 SQLite、FTS5、Core、Application、MAUI、Avalonia、Windows API 或平台路径；唯一 Wiki 数据入口是 Wiki Tools structured content。

## 模型与运行设置

- 首个 provider 使用 OpenAI Responses API；默认模型为 `openai:gpt-5.6-luna`、reasoning effort 为 `medium`、`store=false`、parallel tool calls 为 false、truncation 为 disabled。
- 公共设置允许调用方把安全上限向下收紧，不允许把它们调高。provider 必须保持 `openai:*`，API 固定为 Responses，store 不得开启。
- API key 只由 provider 从运行环境取得，不进入公共设置、connection、错误、日志、测试 fixture 或仓库文件。
- PydanticAI 及首个 provider 是 Agent 内部实现选择；客户端和 Application 只能依赖本能力的 typed 结果与稳定错误，不依赖 PydanticAI 类型。

## 证据工作流与不可信内容

- Terraria 事实问题必须先调用 `search_wiki`，不能用模型记忆或 Search summary 直接回答。
- 回答使用的每个页面必须在本次运行中成功调用 `read_wiki`；允许继续搜索并读取多个页面。
- 缺少、冲突或不足以支持回答的本地证据必须返回 `insufficient_evidence`，不得用模型常识补全。
- Wiki HTML、属性、注释、脚本和可见文本全部是不可信资料。页面内指令不能覆盖系统规则、用户问题、工具策略、来源约束或输出规则。
- Agent 不输出原始 HTML，不生成远程来源 URL；回答中出现的远程 URL 必须移除或替换为不可信链接占位。

## 回答、来源、轨迹与用量

`WikiAgentAnswer` 固定包含：

- `status`：`answered` 或 `insufficient_evidence`；
- 非空 Markdown 回答；
- `sources`：由宿主从本次成功 `read_wiki` structured content 派生；
- `usage`：请求数、工具调用数、输入/输出/总 token 和 provider 可提供时的美元成本；
- `tool_trace`：成功 Search、成功 Read 或 not-found Read 的最小轨迹，不包含参数、内容、路径或凭据。

模型只能生成 status 与 Markdown 草稿，不能声明来源。每个来源保留 `requested_title`、最终 `resolved_title`、anchor 与 `last_modified`；按不区分大小写的 resolved title + anchor 去重并保持首次成功 Read 顺序。not-found Read 进入轨迹但不生成来源。模型若声明 answered 而没有任何成功 Read 来源，宿主必须强制转为 `insufficient_evidence`。

## 运行预算

每次 `answer` 的硬上限为：

- 最多 8 次模型请求；
- 最多 8 次工具调用；
- 最多 4096 输出 tokens；
- 最多 300000 累计 tokens；
- 单次模型请求最多 256000 输入 tokens；
- 最多 120 秒；
- 最多 0.10 美元。

超限必须返回稳定预算或 context 错误，不得静默截断或继续付费。完整 Wiki HTML 继续沿用 Wiki Tools 契约；超大页面导致 context limit 时明确失败，Reader/HTML 转换必须由后续独立证据和决策引入。

## 错误、取消与脱敏

对外失败分类固定为：

- `invalid_input`、`invalid_connection`；
- `wiki_tool_unavailable`；
- `model_authentication`、`model_unavailable`；
- `context_limit`、`budget_exceeded`、`timeout`；
- `unknown`。

公开错误只含稳定 code 与固定安全消息，不嵌入底层异常。错误、repr、日志和工具轨迹不得包含 API key、bearer、数据库或平台路径、HTML、模型/工具原始响应或堆栈。调用方取消继续以任务取消传播，不归一为普通失败。

## Runtime 分离与非目标

- Agent 包只实现平台无关业务逻辑。Windows/Android 负责各自的 Python 进程、MCP server、凭据传递、启动退出、崩溃恢复、打包与发布。
- Phase 06 的 C# TestHost 只能存在于测试项目，使用随机临时数据根与合成 SQLite fixture；不得进入生产引用、读取用户设置或连接用户数据库。
- 不包含 UI、流式事件、多轮/持久化历史、Windows Runtime 接线、Python 封装或发布产物、正式数据根、Reader 转换、Web Search、Vector/Embedding、Android 或其他平台。

## 验收场景

1. 离线模型测试全局禁止真实 provider 请求，覆盖配置、连接、来源派生、Redirect/anchor、去重、证据不足、工具错误、取消、超时、预算、脱敏与提示注入。
2. Windows Python 通过真实 C# TestHost 发现恰好两个工具，完成 Search → 多 Read → typed Answer；fixture 覆盖 Redirect 配方、两项材料、无结果和恶意页面。
3. WSL Python 3.12 与 Windows Python 3.14 使用同一解析锁通过 lint、格式、strict mypy 与适用测试；Windows .NET Release build 和全部测试通过。
4. 首次真实模型验收固定运行多跳配方、无证据拒答和提示注入抵抗三场；每场满足事实、轨迹与来源约束。总授权上限为 0.30 美元，额外重跑需重新授权。
5. 静态依赖门确认生产 Agent 不接触 SQLite/Core/Application/UI/平台路径，生产 .NET 项目不引用测试 Host，仓库不含 secret、用户数据、虚拟环境或缓存。

## 变更规则

公共 answer/source/usage/error 语义、证据规则、预算上限、安全边界、首个 provider 或 Runtime 分层变化时更新本 spec，并在需要时新增 ADR。具体依赖版本、类、fixture、命令和验证记录进入对应 Phase 计划；Wiki Tool JSON 契约变化同时更新 `wiki-tools` spec。
