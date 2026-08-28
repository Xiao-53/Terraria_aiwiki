# ADR 0005：AI Backend 使用 PydanticAI 与可配置 OpenAI-compatible Provider

- 状态：accepted；通用 provider、跨语言链路与 DeepSeek V4 Flash 三场验收通过；Phase 07 改为可流式纯 Markdown、最终 typed answer 保持
- 日期：2026-08-24；2026-08-26 由用户决定修订 provider 范围；2026-08-27 由 Phase 07 补充流式输出边界；2026-08-28 由用户决定取消美元费用预算
- 决策范围：Python Agent 框架、OpenAI-compatible provider 边界、证据来源、运行限位与 Runtime 分离；不包含客户端接线或打包
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md`、`docs/adr/0006-use-stdio-python-sidecar.md`、`openspec/changes/archive/phase-06-ai-backend/plan.md`

## 背景

Phase 05 已用受限无状态 loopback MCP 暴露 Application Search/Read。Phase 06 需要建立最小单轮 Agent，让模型能搜索、读取多个本地页面并返回可验证来源，同时保持 Python 业务逻辑与 Windows/Android Runtime、Presentation 和 Wiki 数据实现分离。

原方案固定 OpenAI Responses 与单一模型。2026-08-26 用户明确要求不依赖单一供应商，而是允许 Runtime 配置采用 OpenAI 线协议的服务，并指定 DeepSeek V4 Flash 作为首个真实验收 endpoint。候选包括自行维护 Responses/Chat 两套 tool-calling 循环、在 PydanticAI 内使用固定供应商 provider，以及复用 PydanticAI 的两种 OpenAI-compatible model classes 并提供保守兼容 profile。

## 决策

1. Agent 使用 Python 3.12–3.14 与 `pydantic-ai-slim[mcp,openai]==2.31.0`，提交跨平台解析锁；升级必须独立验证 API、MCP、安全限制和测试行为。
2. Agent 不内置生产供应商默认值。Runtime 必须显式提供 `base_url`、模型名和 `responses` / `chat_completions` 线协议；两种协议复用同一 Agent、MCP、证据、运行限位与错误收口。
3. 远程 endpoint 只允许 HTTPS；HTTP 只允许字面 loopback 地址。API key 固定只从 `TERRARIA_AI_API_KEY` 环境变量读取，不进入 settings、URL、日志、错误或仓库。
4. 通用 provider 使用保守兼容 profile：不声明 strict tool schema 或 required tool choice，合并系统消息并使用 `system` 角色，不假设 Chat endpoint 支持 `max_completion_tokens`。可选 reasoning effort 只在显式配置时发送。
5. `store`、`truncation`、并行工具、reasoning、token 预计算和定价不是 OpenAI-compatible 服务的统一保证。宿主继续强制请求、成功工具调用、输出/累计 token 和时间限位以避免循环、协议超长和挂起；不再设置美元 cost limit，也不因 provider 返回的费用估算中止回答。
6. Agent 仅提供异步单轮 typed answer。为支持 Phase 07 正文流式，模型最终输出是纯 Markdown；宿主从成功 Read structured content 派生并去重实际来源，并根据成功 Read/固定不足证据正文归一 status。模型不能声明 status 或扩充来源。旧 `answer(question, connection)` 调用保持兼容，可选异步事件回调不改变最终 typed answer。
7. 事实工作流固定为先 Search、再 Read，可连续查询多个页面。Wiki HTML 全部视为不可信资料；无充分本地证据时返回不足，不允许使用模型记忆补全。
8. Python Agent 是平台无关业务包。Windows/Android Runtime 分别负责 provider 配置、进程、凭据交接、生命周期与打包；Phase 06 的 C# Host 只用于真实跨语言测试，禁止生产引用。
9. DeepSeek `deepseek-v4-flash` Responses 是首个真实 endpoint，只证明该具体组合；其他兼容服务必须分别通过工具调用、typed output、错误和用量验收后才能宣称可用。

## 选择理由

- PydanticAI 已提供 MCP client、完整事件图、OpenAI Responses/Chat model classes、provider adapter、测试模型和多维运行限位；使用单一内部 provider factory 不需要复制 Agent 循环。
- 显式 endpoint、模型和线协议把供应商选择留给 Runtime，同时保持客户端只依赖稳定 answer/source/usage/error 语义。
- 保守 profile 避免把 OpenAI 特有的 strict schema、required tool choice、developer role 或参数名称误认为兼容服务的共同能力。
- 来源由宿主派生，把可验证证据集合从模型生成文本中移出；更换 provider 不改变本地事实边界。

## 后果

正面后果：

- 同一 Agent 可连接通过验收的 OpenAI-compatible Responses 或 Chat endpoint，不复制 Search、Read、SQLite、Core 或工具循环。
- provider、模型与 API 线协议可以由 Windows/Android Runtime 分别配置，公共结果不依赖供应商 SDK 类型。
- 供应商切换继续受 endpoint 校验、环境 secret、固定错误、来源和请求/工具/token/时间限位保护。

负面后果与约束：

- “OpenAI-compatible”不是完整行为标准；工具 schema、typed output、reasoning、并行调用、错误、上下文和 usage 仍可能不兼容，必须逐 endpoint 实测。
- Agent 不再提供美元费用预算；用户需要通过供应商余额、额度或账单限额控制真实花费。provider 可提供的费用估算仍保留在 typed usage/协议中用于兼容与诊断，但 Presentation 默认只显示 token 花费。
- 兼容 endpoint 可能忽略 `store`、`truncation` 或并行设置；发送本地 Wiki 页面到远程服务前必须有明确的供应商数据处理披露。
- 完整 Wiki HTML 可能超过某些服务上下文；当前只提供明确 context-limit，不在缺乏质量证据时引入转换器。
- loopback MCP 是 Windows 首个 Wiki Tool transport 的既有选择，不自动成为 Android transport。

## 被否决方案

- 固定单一 OpenAI 或 DeepSeek provider：配置最少，但与用户确认的供应商中立目标冲突，并把阶段验收模型误写成长期产品依赖。
- 仅提供自定义 `base_url` 而继续套用 OpenAI 默认 profile：表面通用，实际可能发送兼容服务不支持的 strict schema、required tool choice 或参数；DeepSeek 首轮即以 HTTP 错误证明此风险。
- 自行编写 Responses 与 Chat tool-calling 循环：可完全控制协议差异，但会复制 MCP discovery/schema、typed output、重试、usage、测试替身和错误兼容，当前边界更大。
- 在 .NET 内实现首个 Agent：会偏离已确认 Python Agent 分层，并把 provider 业务与平台 Runtime 提前耦合。
- 预先清洗、摘要或向量化整个 Wiki：复制数据、改变事实边界并扩大 Phase；Reader 转换必须以后续真实 context/质量证据单独决策。
