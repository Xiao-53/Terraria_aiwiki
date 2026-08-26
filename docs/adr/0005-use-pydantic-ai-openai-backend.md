# ADR 0005：首个 AI Backend 使用 PydanticAI 与 OpenAI Responses

- 状态：accepted；Phase 06 本地与跨语言验证通过，真实 OpenAI 验收待外部前置
- 日期：2026-08-24
- 决策范围：首个 Python Agent 框架、模型 provider、证据来源、安全预算与 Runtime 分离；不包含客户端接线或打包
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/changes/phase-06-ai-backend/plan.md`

## 背景

Phase 05 已用受限无状态 loopback MCP 暴露 Application Search/Read。Phase 06 需要建立最小单轮 Agent，让模型能搜索、读取多个本地页面并返回可验证来源，同时保持 Python 业务逻辑与 Windows/Android Runtime、Presentation 和 Wiki 数据实现分离。

候选包括直接维护 OpenAI Responses tool-calling 循环、采用通用 Agent 框架，以及在 .NET 内实现首个 Agent。当前需求还包含 MCP client、typed output、用量/成本限制、测试模型和 provider 错误处理；自行维护循环会复制这些横切行为，而把 Agent 放进 .NET 会提前改变既定 Python Agent 分层。

## 决策

1. 首个 Agent 使用 Python 3.12–3.14 与 `pydantic-ai-slim[mcp,openai]==2.31.0`，提交跨平台解析锁；升级必须独立验证 API、MCP、安全限制和测试行为。
2. 首个 provider 使用 OpenAI Responses API，默认 `openai:gpt-5.6-luna`、`medium` reasoning、`store=false`、parallel tool calls false、truncation disabled。API key 只由环境提供。
3. Agent 仅提供异步单轮 typed answer。模型负责工具选择和 Markdown 草稿，宿主从成功 Read structured content 派生并去重实际来源；模型不能扩充来源。
4. 事实工作流固定为先 Search、再 Read，可连续查询多个页面。Wiki HTML 全部视为不可信资料；无充分本地证据时返回不足，不允许使用模型记忆补全。
5. 每次运行使用请求、工具、输出、累计 token、单请求输入、时间和美元成本七项硬上限。任何超限明确失败，不静默截断；Phase 06 不新增 HTML/Reader 转换。
6. MCP 连接只接受精确 IPv4 loopback endpoint。HTTP client 不继承环境代理、不跟随重定向，bearer 不进入 URL、字符串、日志或错误。
7. Python Agent 是平台无关业务包。Windows/Android Runtime 分别负责进程、凭据交接、生命周期与打包；Phase 06 的 C# Host 只用于真实跨语言测试，禁止生产引用。

## 选择理由

- PydanticAI 提供 MCP Streamable HTTP client、typed output、provider adapter、可组合工具 hook、测试模型和多维用量限制，可在一个小包内实现当前边界。
- Responses API 原生支持工具调用、reasoning、输出限制和 `store` 控制；Luna 是用户指定的首验模型，`medium` 是本阶段固定平衡点。
- 来源由宿主派生，把可验证证据集合从模型生成文本中移出，既保留多步推理，也避免模型声称未读取页面。
- 平台 Runtime 分离使 Phase 07 可以在不修改 Agent 业务规则的前提下决定 Windows 进程、IPC、恢复与发布策略；Android 后续仍可独立决策。

## 后果

正面后果：

- Agent 只消费 Phase 05 的两个只读工具，不复制 Search、Read、SQLite 或 Core。
- 同一包可在 WSL Python 3.12 和 Windows Python 3.14 进行平台无关验证，真实 C# Host 可覆盖协议与 typed result。
- 安全预算、错误分类、来源和调用轨迹成为可自动回归的稳定边界。
- 模型/provider 可在明确变更后替换，而客户端不依赖 PydanticAI 内部类型。

负面后果与约束：

- 新增固定 Python 依赖树与 OpenAI 付费 provider，需要锁文件、双 Python 版本和真实账户验收。
- 模型结果仍有非确定性；代表性真实评估是 Phase 出口，离线模型不能替代。
- Wiki Tools 返回完整 HTML，大页可能超过 context；当前只提供明确 context-limit，不在缺乏质量证据时引入转换器。
- `store=false` 约束 provider 请求存储行为，但不能替代用户对发送本地 Wiki 页面到远程模型的知情与后续产品隐私设计。
- loopback MCP 是 Windows 首个 transport 的既有选择，不自动成为 Android transport。

## 被否决方案

- 直接编写 OpenAI Responses 循环：减少框架依赖，但需自行维护 MCP discovery/schema、typed output、重试、usage/cost、测试替身与错误兼容，当前总体边界更大。
- 在 .NET 内实现首个 Agent：可与 Windows Host 同进程，但偏离已确认 Python Agent 分层，并会把 provider/Agent 业务与平台 Runtime 提前耦合。
- LangChain 或其他更宽框架：当前只需单模型、两个工具和 typed answer，引入更大的抽象与依赖面没有已证实收益。
- 让模型直接生成来源：无法证明来源属于本次成功 Read，也容易产生标题或 URL 幻觉。
- 预先清洗、摘要或向量化整个 Wiki：复制数据、改变事实边界并扩大 Phase；Reader 转换必须以后续真实 context/质量证据单独决策。
