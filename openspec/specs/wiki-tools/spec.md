# Wiki Tools 能力契约

- 状态：confirmed baseline；2026-09-05 `read_wiki` 已改为有界纯文本 evidence blocks
- 适用范围：Wiki Tool Adapter、AI Backend、Windows Runtime lifecycle；Android transport 另行决定
- 来源：Master Architecture v0.6、Wiki Search / Read 边界、Phase 05 AI Adapter、ADR 0004

## 目的

以薄、可替换且不复制数据的 Adapter 向 AI 暴露本地 Wiki Search / Read，使 Agent 通过与手动 UI 相同的 Application 用例获取事实，同时不接触 SQLite、Core service、持久化模型或全局应用状态。

## 公共运行边界

- Adapter 是单一 `net10.0` assembly，只在 project 层引用 Application；MCP/ASP.NET transport 依赖不得反向进入 Application/Core。
- 嵌入式入口是 `WikiToolServer.StartAsync(WikiQueryUseCases, WikiToolConsumerCoordinator, CancellationToken)`；本能力不要求独立产品 Host。
- server 的 `Connection` 提供当前动态 MCP endpoint 与临时 bearer token。token 不得进入 URL、日志或任意公开 `ToString()` 输出。
- `StopAsync` 与 `DisposeAsync` 幂等；停止时拒绝新请求、取消并排空在途请求、关闭 listener 并释放端口。
- `WikiToolConsumerCoordinator` 实现 `IWikiDataConsumerCoordinator`。server 未启动时 pause 是无副作用空操作；启动后 pause 拒绝新工具调用、取消并等待在途调用，最后一个 pause lease 释放后才恢复。

## 工具发现与 schema

- 工具发现结果必须恰好包含 `search_wiki` 与 `read_wiki`；不得顺带注册 resources、prompts 或其他工具。
- 两项工具都声明 read-only、non-destructive、idempotent、closed-world，并提供固定 input/output schema 与 structured content。
- JSON 字段使用本规范定义的 snake_case；字段删除、重命名、类型变化、状态/匹配字符串变化均属于能力契约变更。

### `search_wiki`

输入：

- `query`：必填字符串；空或仅空白输入沿用 Application/Core 空结果语义。
- `max_results`：可选整数；省略时使用 Application 当前设置，显式值固定为 `1..100`。

输出对象包含 `results` 数组；每项固定包含：

- `title`：搜索结果页面身份；
- `redirect_target`：Application Search 返回的 Redirect 目标；非 Redirect 结果保持空字符串；
- `summary`：现有纯文本摘要，不包含 HTML 标记；
- `match_type`：固定为 `exact_title`、`exact_redirect`、`title_prefix`、`redirect_prefix`、`title_contains`、`redirect_contains`、`body_fts` 之一；
- `highlights`：`start` / `length` 数组，使用 `summary` 的零基 .NET UTF-16 索引。

结果顺序、去重、limit、Redirect、摘要和高亮必须与对同一 `WikiQueryUseCases.SearchAsync` 的直接调用一致。Adapter 不重排、不补查、不转换、不截断；Search 不写 history。

### `read_wiki`

输入：

- `title`：必填非空页面标题，可包含 `#anchor`。
- `focus`：可选问题或主题，用于优先返回相关章节；中文匹配必须支持 bigram。
- `cursor`：可选非负 continuation 游标；继续读取时调用方保持相同 title/focus，并使用上次 `next_cursor`。

输出固定包含：

- `requested_title`、`requested_page_title`；
- `status`：`found` 或 `not_found`；
- `is_found`、`title`、`last_modified`；
- `content_format`：found 固定为 `wiki_blocks_v1`，not-found 为 null；
- `blocks`：有序 `{ section_path, kind, text }`，kind 只允许 `heading`、`paragraph`、`list_item`、`definition`、`table_row`、`code`；
- `included_characters`、`original_characters`、`is_complete`、`next_cursor`；
- `available_sections`：最多 64 项且文本合计最多 4096 字符；
- `is_redirect`、`redirect_from_title`、`redirect_target_title`、`redirect_target_anchor`、`anchor`。

Adapter 仍通过现有 `WikiReadResponse` 取得完整存储 HTML，但只在 WikiTools 内使用锁定的 HtmlAgilityPack 1.12.4 转换。模型工具结果不得包含完整 HTML、标签、属性、脚本、注释、隐藏/导航或媒体节点；Application 与 Viewer 仍获得原始完整 HTML。标题层级、段落、列表、定义、代码、带列名的表格行和 infobox 语义必须保留。每次全部 `blocks.text` 合计最多 16384 个 Unicode scalar（与 Python `len` 对有效 Unicode 字符串的计数一致），分块不得切断 surrogate pair；超出时返回非空 `next_cursor`，不得把截断误报为 complete。首次排序是显式 anchor → 页面标题/导语 → focus 命中章节 → 其余正文；not-found 是成功结构化结果。Read 不写任何持久 Wiki 浏览历史。

## 错误与取消

- 缺失/空必填输入、越界 `max_results` 等 Adapter/Application 输入错误返回 `invalid-arguments` tool error。
- 已确认 Application 错误映射为不含内部消息的稳定 code，包括 `wiki-not-initialized`、`wiki-operation-in-progress`、`wiki-unknown-source`；其他 Application 错误使用 `wiki-application-failed`。
- Core 数据源不可用返回 `wiki-data-unavailable`；不得包含数据库路径、SQLite 信息、内部异常或堆栈。
- pause/stopped 分别返回 `wiki-tool-paused` / `wiki-tool-stopped`；未知内部错误统一返回 `wiki-tool-failed`。
- 调用方取消保持取消，不转换为空结果或普通 tool error。由 pause/stop 发起的内部取消使用对应稳定 tool error，或随 transport shutdown 结束。
- MCP malformed request、参数反序列化失败与未知工具由官方 SDK 作为安全协议/tool error 拒绝，不得暴露内部路径、堆栈或数据库细节。

## Transport 与安全

- 首个 transport 是官方 MCP Streamable HTTP 的显式无状态模式，只绑定动态 `127.0.0.1:0` 并只映射 `/mcp`。
- HTTP `Host` 必须精确匹配实际 `127.0.0.1:<port>`；缺失或错误 bearer token 返回 401，错误 Host 在进入 MCP 前拒绝。
- token 每次启动使用密码学安全随机数重新生成，只存内存并以固定时间比较；旧 token 不得用于后续 server 实例。
- 不提供远程监听、固定端口、TLS、OAuth、CORS、legacy SSE、resources 或 prompts。
- 本 transport 是可嵌入 runtime。Phase 07 Windows Host 在 Viewer enable 后启动 server，把 endpoint/bearer 仅经 Sidecar initialize stdin 交给 Python，并在 Sidecar fatal、手动重试和应用退出时停止对应 server。
- Android 不继承本 loopback/ASP.NET transport 决策，必须在 Android Phase 根据目标环境独立决定和验证。

## 非目标

- Python/PydanticAI、模型 API、Search → Read → Answer 质量规则和来源生成；
- Reader HTML 转换、模型摘要管线和向量检索；
- Vector Search、Embedding Retrieval、Vector Database 或第二套 Wiki 数据；
- Windows UI、Python Sidecar 协议与安装器；Android transport/Runtime 验证。

## 验收场景

1. 官方 C# MCP client 可对动态 endpoint 完成 initialize/协商、list tools、Search 和 Read。
2. 工具集合、名称、输入 schema、output schema、annotations 和 structured JSON 字段固定。
3. Search 与直接 Application 调用在七级顺序、limit、Redirect、摘要、匹配类型和 UTF-16 highlights 上逐项一致。
4. Read 覆盖直接页面、Redirect、显式/Redirect anchor、not-found、修改时间、focus/cursor 和不写 history；工具结果不含原始 HTML。
5. 长页相关章节位于开头/中间/末尾时可由 focus/anchor 找到；continuation 无重复遗漏，单次 blocks 和章节目录均不越界，表格/infobox 保留列语义。
6. 缺失/错误 token、错误 Host、非法参数、未知工具、数据源不可用和内部异常均安全拒绝。
7. 并发、调用方取消、pause/release、重复 stop/dispose、token 轮换、在途清理和端口释放有自动化回归。
8. 静态/反射门确认 Adapter project 不引用 Core project，且不访问 SQLite、Core service、持久化模型、MAUI、Avalonia、Windows API 或 Python。
9. Windows 组合 consumer 在数据操作前同时排空 Viewer 与 WikiTools，失败时逆序释放已取得 lease；Sidecar 崩溃/重试会停止旧 server 并轮换端口/token。

## 变更规则

工具名称、输入/输出 JSON、错误 code、生命周期或安全边界变化时更新本 spec；SDK 版本、具体类、测试 fixture 与平台命令进入对应 Phase 计划。transport 的长期取舍见 ADR 0004。
