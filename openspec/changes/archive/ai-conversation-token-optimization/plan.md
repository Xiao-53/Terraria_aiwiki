# AI 会话与 Token 花费优化

- 状态：archived；实现与自动化验证已完成，真实付费/窗口验收未执行；用户于 2026-09-12 要求结束本 change，后续由新 plan 继续优化
- Phase：无；独立非 Phase 变更
- 执行环境：WSL2 Codex CLI 负责实施与平台无关检查；Windows 11 x64 负责 .NET/Avalonia 构建、运行与人工验收
- 创建日期：2026-09-05
- 最后更新：2026-09-12
- 范围：Wiki Tools 读取结构、Python Agent 多轮上下文、Windows Sidecar 协议、Chat/收藏/对话历史、本地会话持久化、旧 Wiki 浏览历史下线
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/application-use-cases/spec.md`、`openspec/specs/windows-productization/spec.md`、`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`、`docs/adr/0005-use-pydantic-ai-openai-backend.md`、`docs/adr/0006-use-stdio-python-sidecar.md`、`docs/adr/0010-use-bounded-local-ai-conversations.md`

## 背景与当前证据

### 已确认事实

- 用户截图中的一次回答使用输入 228096、输出 3323、合计 231419 Token；输入约占 98.6%。主要成本不是最终回答，而是 `read_wiki` 返回的完整原始 HTML 在 Agent 的多次模型请求中反复进入上下文。
- `WikiMcpTools.ReadWikiAsync` 当前把 Application `WikiReadResponse.Content` 原样映射为工具结果；持续 Wiki Tools 契约也明确规定 `content` 是完整 HTML、不转换、不截断。
- Python Agent 当前每个问题新建一次独立运行；同一运行中的工具调用和返回会作为消息图的一部分供后续模型请求使用。减少必要的 Search/Read 次数会直接损害复杂问题的证据完整性，却不能消除单次完整 HTML 的主要浪费。
- 当前 Windows Chat 只保存一个问题和一个回答；发送下一题会替换上轮 UI 状态，Sidecar NDJSON v1 `answer` 命令也只包含当前 `question`。
- DeepSeek Responses API 是无服务端会话的无状态接口；多轮需要客户端重新提供历史，`previous_response_id`、`conversation` 和自动 `truncation` 不受支持：<https://api-docs.deepseek.com/guides/responses_api/>。
- DeepSeek 上下文缓存自动工作并依赖相同前缀；缓存可降低部分计费与延迟，但不会缩短名义输入或消除上下文风险：<https://api-docs.deepseek.com/guides/kv_cache/>。
- PydanticAI 支持 `message_history` 和按最近消息裁剪，但保存其完整运行消息会包含旧工具调用与工具返回。本变更必须由宿主重新构造纯文本 user/assistant 历史，而不是持久化或重放原始 Agent 消息：<https://pydantic.dev/docs/ai/core-concepts/message-history/>。
- 当前“Wiki 资料库 → 历史记录”是持久化 Wiki 页面浏览历史；Reader 另有只在内存中使用的返回栈，两者不是同一能力。

### 用户于 2026-09-05 确认的决定

- 采用完整分阶段方案：先优化 Wiki 读取结构，再接入有界多轮、会话存储、新对话与 Token 可观测性。
- 不以减少工具调用次数作为 Token 优化手段；Search/Read 次数继续由问题与证据需要决定。
- 本地自动保存会话；应用每次启动仍进入空白新对话，用户从历史页主动恢复旧会话。
- 模型最多参考当前会话最近 4 轮文本；本地界面显示完整会话。
- “Wiki 资料库 → 历史记录”直接替换为“对话历史”。原持久 Wiki 浏览历史功能删除；旧 SQLite 表只为兼容保留并停止读写，不主动擦除旧行。
- 收藏保存整段会话。最多保留 100 个未收藏会话；收藏会话不自动淘汰，只允许用户手动处理。
- 实施完成后先交付开发版供用户验收，不直接 publish、Velopack 打包或发布。

## 目标与完成定义

1. `read_wiki` 不再把完整 HTML 放入模型上下文；它返回按章节组织、可定位、可继续读取且有固定大小上限的纯文本证据块。
2. 工具仍保持恰好 `search_wiki` 与 `read_wiki` 两项；现有最多 8 次工具调用继续只作为防循环安全上限，不下调、不作为优化指标。模型需要多个页面或后续证据块时可以继续调用。
3. 原始 HTML 继续由 Application/Viewer 使用，FTS5、Redirect、anchor、来源派生与唯一 Reader 行为不变。
4. Chat 在同一会话内连续展示多轮问答；每次请求只携带最多最近 4 轮纯文本问答，不携带旧工具调用、Wiki 证据块、HTML、thinking 或 provider 原始消息。
5. Chat 页头提供“新对话”；回答期间禁用，其他时间切换到空白上下文，不创建空记录。
6. 会话自动保存在 `%LocalAppData%\Terraria AI Wiki`；启动时不自动打开旧会话，历史页可恢复并继续。
7. “Wiki 资料库”的第二页签变为“对话历史”，支持筛选、打开、逐项删除和清空未收藏会话；收藏页保存并打开完整会话。
8. 旧单轮 Chat 收藏无损迁移为只有一轮的已收藏会话；新存储 durable 写入成功前不删除或覆盖旧收藏文件。
9. 使用相同 provider、模型与思考强度复测“1457法师添加了哪些物品”时，输入 Token 目标不高于 80000 且较 228096 至少下降 60%，同时保持答案条目与可信来源正确。工具调用次数不参与该门槛。
10. Windows Release 构建、全量测试和人工 UI/Token 验收完成后停止；不得生成新的安装包、portable 或更新制品。

## 前置条件

- 当前工作树已有 Phase 11 和先前 UI 修复。实施本计划前，用户必须先验收或独立保存这些修改；不得把现有改动误算成本 change，也不得创建夹带无关文件的提交。
- 实施开始前记录 Git 状态并建立只覆盖本变更的可恢复点。若现有改动无法安全分离，停止源码实施并先报告，不使用 reset/checkout/stash 覆盖用户工作。
- 真实 DeepSeek 请求会产生费用；自动化使用 fake provider 和合成 Wiki。最终真实 Token 门槛由用户使用已有凭据执行，Codex 不自行发起付费验收。

## 范围与非目标

### 范围

- Wiki Tool 的 AI 专用结构化证据转换与分页/继续读取契约。
- Agent 的受控多轮文本历史、来源约束和 cache usage 映射。
- Windows Host/Sidecar v2、会话存储、完整 Chat transcript、整段收藏和对话历史页。
- 持久 Wiki 浏览历史写入、查询、清空及 Presentation 入口的删除。
- 相关 specs、ADR、README、项目基线和 Phase 11 外部数据保留说明。

### 非目标

- 不增加 Vector Search、Embedding、Vector Database、第二套 LLM Wiki 数据、Web Search 或云端会话存储。
- 不让模型生成旧会话摘要；超过 4 轮的内容仍保存在本地，但不发送给模型。
- 不减少复杂问题所需的页面数量，不要求固定 Search/Read 次数，不通过拒绝必要工具调用换取低 Token。
- 不改变手动 Wiki 搜索的 Application 上限、Core FTS5 排序或完整 Wiki 数据。
- 不删除 Reader 的内存返回栈，不增加第二个 Viewer。
- 不实施 Android、Web、Linux、其他平台、打包或发布工作。

## P0 / 兼容性影响

- Application `Query.ReadAsync` 继续返回完整 HTML；证据转换只位于 Wiki Tool Adapter，Viewer 和其他调用方不受裁剪。
- `search_wiki` 的输入、结果数量范围、排序、摘要与高亮保持现有契约；不为节省 Token 缩减结果上限。
- `read_wiki` 工具 JSON 是明确的 breaking contract change；Python parser、跨语言 fixture、spec 与 ADR 必须在同一步更新。
- Windows Host 与 Python Sidecar 同步升级 NDJSON v2；版本不匹配安全拒绝，不支持新旧二进制混用。两者未来必须同包发布，但本 change 不执行打包。
- 删除 `WikiLibraryUseCases.GetHistoryAsync`、`RecordVisitAsync`、`ClearHistoryAsync` 及持久浏览历史 UI；保留 `WikiHistory` 模型、建表与 schema 容忍逻辑以继续接受旧数据库/数据包。旧行不读取、不修改。
- Viewer `SaveToTabHistory`、`WikiBackAsync` 和鼠标侧键返回属于临时阅读导航，继续保留并回归。
- 新会话数据继续位于安装根之外；更新、repair 和卸载不得自动删除。对话正文不得进入诊断、日志或 smoke 输出。

## 方案与待决问题

### Token 优化策略

| 方案 | 决定 | 说明 |
|---|---|---|
| 降低工具调用次数 | 不采用 | 多页面问题需要足够 Search/Read；调用次数不是截图中 228k 输入的根因 |
| 降低 Search 结果上限 | 不采用 | Search 返回的是短摘要，保持现有 `1..100` 和 Application 默认值 |
| 降低回答输出上限 | 不作为本变更手段 | 输出只占本次用量约 1.4%，现有 4096 上限继续作为安全边界 |
| 结构化 Wiki 证据块 | 采用，核心 | 从源头移除标签、属性、脚本、重复导航及无关章节，不传完整 HTML |
| 按需继续读取 | 采用 | 允许模型用同一 `read_wiki` 获取后续块，保留回答完整性 |
| 最近 4 轮纯文本上下文 | 采用 | 提供真实多轮，同时不重放旧工具结果 |
| Provider 前缀缓存 | 采用为观测项 | 保持稳定前缀并展示命中，但不把 best-effort 缓存当作 Token 缩减 |

### `read_wiki` 结构化读取契约

- 输入保留必填 `title`，继续支持 `页面#anchor`；新增可选 `focus`（当前问题或需要核对的主题）和可选非负 `cursor`。调用方必须在继续读取时保持相同 `title` 与 `focus` 并使用上次返回的 `next_cursor`。
- 输出继续包含 requested/resolved title、found/not-found、Redirect、anchor 与 last-modified，但删除完整 HTML `content`，改为：
  - `content_format: "wiki_blocks_v1"`；
  - `blocks`：按顺序排列的 `{ section_path, kind, text }`；
  - `included_characters`、`original_characters`、`is_complete`、`next_cursor`；
  - 有界的 `available_sections`，帮助模型选择显式 anchor，而不复制整页目录。
- `kind` 固定为 `heading`、`paragraph`、`list_item`、`definition`、`table_row`、`code`。表格必须转换为带表头名称的行，不能只拼接单元格而丢失列语义；物品属性 infobox 视为表格证据保留。
- 每次工具返回的全部 `blocks.text` 合计最多 16384 个 Unicode scalar（与 Python `len` 一致），`available_sections` 最多 64 项且合计最多 4096 个 scalar；任何单块超过剩余预算时按自然文本边界切分且不得切断 surrogate pair，并提供 `next_cursor`，不得静默宣称完整。
- 首次读取的选择顺序固定为：显式 anchor 章节 → 页面标题与导语 → 与 `focus` 命中的章节 → 其余正文顺序。匹配使用规范化大小写、字母数字词和中文 bigram，避免只按空格分词导致中文查询失效。
- HTML 解析时移除 script/style/noscript、注释、隐藏节点、纯导航、媒体标签和属性；解码实体、规范化空白、保留标题层级、段落、列表、定义与表格文本。相同导航/模板文本按规范化内容去重。
- 转换使用现有锁定的 HtmlAgilityPack 1.12.4；WikiTools 可显式引用同版本包，但不新增解析库或 Core project 引用，不把转换逻辑放入 Application/Core。
- Agent 提示允许在 `is_complete=false` 且当前证据不足时使用 `next_cursor` 或更精确的 `#anchor` 继续读；禁止无理由重复同一 title/focus/cursor。最多 8 次工具调用保持不变。

### 多轮上下文与 Sidecar

- 新增平台无关 `WikiConversationTurn(user, assistant)`；`TerrariaWikiAgent.answer(question, connection, on_event=None, *, history=())` 保持旧两参数和既有 event 调用兼容。
- Windows `IChatRuntimeGateway.AnswerAsync` 增加只读历史参数；NDJSON v2 `answer` 增加严格的 `history: [{ user, assistant }]`。
- Host 只选择当前会话最近 4 个已回答或证据不足的终态轮次，并施加 24000 字符总预算。按最新轮次向前装入，最终按时间顺序发送；失败、取消、pending 轮次不进入历史。
- 本地保存完整问题和 Markdown；发给模型的历史若超过 24000 字符，只裁剪最旧入选轮次。最新一轮仍超限时保留完整问题并在段落边界裁剪回答，加入明确的本地截断标记。
- Python 由这些 DTO 重新构造 `ModelRequest(UserPromptPart)` / `ModelResponse(TextPart)`，不使用上次 `result.all_messages()`，因此旧 Search/Read、工具结果、HTML、reasoning 和 retry 消息不会跨轮。
- 历史只帮助解析“它、上面的版本、这些物品”等指代；任何新的 Terraria 事实回答仍必须在当前运行执行 Search → Read，并且来源只来自当前运行成功读取的页面。
- 不调整现有 8 request、8 tool calls、4096 output、300000 total、256000 per-request 和 120 秒安全上限。本 change 的实际 Token 门槛由结构化 evidence 和有界历史保证，硬限位不冒充优化结果。
- `WikiAgentUsage` 与 NDJSON answer 增加 `cached_input_tokens`；从 PydanticAI `RunUsage.cache_read_tokens` 映射。Windows 显示总输入、其中缓存、未缓存、输出和合计；缓存值为零时显示为未命中/不可用，不推算供应商账单。

### 本地会话模型与迁移

- Windows 内部新增 `IChatConversationStore`，根目录固定为 `%LocalAppData%\Terraria AI Wiki\conversations`；使用版本化 `index.json` 和每会话独立 `<guid>.json`，复用现有临时文件、flush、原子 replace 与 backup 模式，不引入新数据库。
- 会话记录固定包含 ID、首问生成的标题、创建/更新时间、收藏状态和 turns；每轮包含 ID、问题、安全 Markdown、来源、最小工具轨迹、usage、状态与时间。
- 问题被接受后先 durable 保存 pending 轮次；完成、证据不足、取消或失败时再次写终态。异常崩溃遗留的 pending 在加载时归一为 interrupted，显示“上次回答中断”，且不发送给模型。
- 标题由首个问题规范化空白后截取 60 字符生成，不调用模型。空白新会话不分配 ID、不写文件。
- 保存后最多保留 100 个未收藏会话，自动删除最旧未收藏记录；已收藏会话不自动删除。为防御损坏/恶意文件，单会话最多 100 轮、已收藏会话最多 200 个；达到上限时页内提示用户手动整理，不删除已有收藏。
- 迁移仅在新 index 不存在时读取旧 `chat-favorites.json` v1；每项转换为一个单轮、已收藏、completed 会话。新 index 和会话文件全部 durable 后才完成迁移；旧文件原样保留供回退，后续启动以新 index 为唯一事实源。
- 存储读取/写入失败只禁用或降级本地历史，当前内存 Chat 和离线 Wiki 继续可用；公开状态不得包含路径、JSON、问题正文或异常细节。

### Windows UI

- Chat 改为单个可滚动 transcript：每轮用户问题和 AI 回答按时间排列，当前末轮承载流式 Markdown、工具状态、来源和 Token；历史轮次不重新执行 Agent。
- 页头右侧放置整段会话收藏按钮和“新对话”按钮。新对话在回答时禁用；其他时间保存当前状态、清空 transcript/context，若 composer 有未发送草稿则保留并重新聚焦，避免意外丢字。
- 应用启动固定显示空白 Chat，不恢复最近会话。打开历史或收藏后恢复完整 transcript；继续发送时使用该会话最近 4 轮模型上下文。
- “Wiki 资料库”保留“全部页面”，第二页签更名为“对话历史”；对话历史不依赖当前 Wiki 数据源即可读取。列表按更新时间倒序，显示标题、更新时间、轮数和收藏状态，支持本地筛选、打开、逐项删除和“清空未收藏”。
- 删除任何会话都先页内确认；删除已收藏会话同时取消其收藏。清空只删除未收藏会话。若删除的是当前内存会话，Chat 切换为空白新会话。
- 收藏页的“对话收藏”改为筛选同一会话库中的 `IsFavorite`，打开完整会话；取消收藏只改变状态，会话仍保留在历史中。Wiki 收藏保持不变。
- 设置的数据发送说明增加“当前问题、最多最近 4 轮文本和当前 Wiki 证据块会发送给所选远程模型”；完整本地历史不会自动上传。
- UI 复用现有 semantic resources、`toolbar-action`、`icon-button`、`list-row` 和安全 Markdown；不增加第六个侧栏入口、第二 Viewer、raw hex、图标包或嵌套卡片。新按钮满足 44×44 DIP、键盘焦点和 Automation Name，并验证 1280×820、900×600、Light/Dark 与 Reader 宽窄状态。

### 待决问题

- 无。上述数据保留、会话范围、历史入口、读取结构、工具调用策略和验收边界均已确定；新事实若推翻契约，必须先更新本计划并报告用户。

## 文件变更

- **ADD** `src/Terraria_Wiki.WikiTools/WikiEvidenceFormatter.cs` candidate；**MODIFY** `WikiMcpTools.cs` 与 tool response models：结构化块、focus/cursor、固定大小与继续读取。
- **MODIFY** `agent/src/terraria_wiki_agent/{models.py,agent.py,sidecar_protocol.py,sidecar.py}`：历史 DTO、纯文本 message history、v2 协议和 cached usage；保持工具/请求限位不变。
- **MODIFY/ADD** `src/Terraria_Wiki.Windows/{AI,Hosting,Presentation}` 与 `MainWindow.cs`：协议 v2、会话 store、多轮 Chat、新对话、整段收藏和对话历史页。具体类拆分服从现有职责，不创建新 product project。
- **MODIFY** `src/Terraria_Wiki.Application/UseCases/WikiLibraryUseCases.cs`、Application DTO、`ViewerBridge.cs` 及直接测试：移除持久 Wiki 浏览历史调用；**KEEP** Core `WikiHistory` model、建表/schema 兼容和 Viewer 内存返回栈。
- **MODIFY/ADD** `tests/Terraria_Wiki.WikiTools.Tests`、`agent/tests`、`tests/Terraria_Wiki.Windows.Tests` 与 Application/Core 相关回归：结构、协议、会话、迁移、删除和 UI 边界。
- **MODIFY** 关联 specs、ADR 0004/0005/0006、`openspec/project.md`、`README.md` 和 Phase 11 持久数据说明；**ADD candidate** 一项长期 ADR 固化“Windows Host 持久化完整会话、Agent 只接收最近四轮纯文本”的决定。Master Architecture 的平台与 Phase 顺序不变。
- **KEEP** FTS5、Wiki 数据获取/更新/包、原始页面 HTML、Viewer assets、AI provider 配置、Credential Manager、Android 暂缓状态及所有无关现有改动。

## 执行步骤

- [x] **关闭前置工作并建立恢复点**：用户先验收/保存当前 UI 与 Phase 11 工作；记录干净的本 change 起点。预期：可独立回退且无夹带。
- [x] **固定真实证据与基准**：从现有/合成 Wiki 提取超长版本页、表格、anchor、Redirect 和恶意 HTML fixture，记录原始字节、当前 tool JSON 字节和 fake usage。预期：可以重复证明压缩率与答案信息完整度。
- [x] **实现结构化 `read_wiki`**：增加 formatter、block schema、focus/cursor 和 continuation，更新 Wiki Tools contract/ADR。预期：单次 evidence 有硬字符上限，相关章节和表格语义可恢复，工具数量与 Search 契约不变。
- [x] **更新 Agent 消费与质量规则**：验证新 read schema，允许必要 continuation，保持 Search → Read、当前轮来源和八次工具限位。预期：多页/多块问题不因优化被提前截断。
- [x] **升级多轮协议**：增加 history DTO、纯文本 Pydantic messages、NDJSON v2 与 cache usage。预期：最近 4 轮生效，旧工具结果不会出现在下一轮模型请求。
- [x] **实现本地会话 store 与迁移**：完成目录布局、原子写入、pending 恢复、100 条淘汰、收藏保护和 v1 收藏迁移。预期：崩溃/损坏不丢旧文件，不阻止内存 Chat。
- [x] **重构 Chat 与收藏**：展示完整 transcript，增加新对话和整段收藏，处理流式、取消、失败、打开历史及当前会话删除。预期：连续追问不会清除旧轮次且不存在跨会话泄漏。
- [x] **替换资料库历史页**：接入对话历史，删除持久 Wiki 浏览历史 UI/Application 写入查询；保留 Viewer 返回栈。预期：第二页签只展示 AI 会话，页面阅读不再写 WikiHistory。
- [x] **同步持续文档**：更新实际完成的 specs、ADR、README、项目基线和 Phase 11 数据保留描述；不得把实施细节堆入 Master Architecture。
- [x] **执行自动验证并交付开发版**：完成 WSL 与 Windows Release 开发版验证，更新本计划结果并提供用户验收步骤。预期：不调用真实 provider，不 publish、不打包、不发布。
- [ ] **用户付费/人工验收**：用户使用既有 API Key、相同模型与思考强度执行指定问题，核对 Token、答案、来源与实际窗口。此项需要外部费用和人工判断，不由 Codex 自行执行。

## 验证与回归计划

### Wiki 证据与 Token

- 长 HTML 中相关章节位于开头、中间、末尾时均能通过 focus/anchor 找到；中文问题使用 bigram 后仍能命中“法师武器”等无空格词组。
- 表格、rowspan/colspan 的可支持子集、列表、定义、实体、重复导航、隐藏节点、script/style、注释和提示注入转换稳定；不可支持结构以安全纯文本降级，不返回标签/属性。
- 每次 `blocks.text` ≤16384 字符、section catalog ≤4096 字符；cursor 无重复/遗漏、越界安全拒绝、页面在一次运行中稳定时分页可重现。
- Search 和直接 Application 调用继续逐项一致；工具发现仍恰好两项，工具调用上限仍为 8。
- fake model 捕获每次请求，断言完整原始 HTML 从未出现；同一运行后续请求只包含已返回的 evidence blocks。
- 用户以同一 provider/model/effort 运行原问题，核对输入 ≤80000、较基线下降 ≥60%、工具调用次数不设下降要求、条目和来源正确。

### Agent 与协议

- 覆盖单轮、2–6 轮追问、代词/省略话题、最近 4 轮边界和 24000 字符裁剪；每个事实轮仍重新 Search/Read。
- 捕获下一轮模型消息，确认只有 user/assistant 文本，没有旧 tool call、tool return、HTML、reasoning、system 重复或 failed/cancelled turn。
- 覆盖 v2 initialize/answer/stream/completed、额外字段、非法 history、超长 line、v1/v2 混用、取消、callback failure、Sidecar crash/retry 和脱敏。
- `cached_input_tokens` 从 provider/Pydantic usage 映射并通过 C#/Python 跨语言 fixture；缺失/零值不会显示虚假节省。

### 会话、迁移与 UI

- 覆盖首次发送、终态更新、graceful cancel、崩溃 pending、原子主/backup 恢复、单会话损坏、存储不可写和并发保存。
- 覆盖 100 个未收藏会话淘汰、收藏不自动删除、200 收藏上限、100 轮上限、逐项删除、清空未收藏和当前会话被删除后的重置。
- 覆盖旧单轮收藏一次迁移、迁移中断重试、新库优先、旧文件保留、取消收藏后仍在历史，以及完整会话从历史/收藏打开并继续追问。
- 启动时历史仍存在但 Chat 必须为空；新对话不写空记录，保留未发送草稿，回答中不可触发。
- Enter/Shift+Enter/IME、发送后输入清空、流式 Markdown、停止、来源打开、Reader 恢复、设置隐藏 Reader 和 API Key 遮蔽现有回归继续通过。
- Wiki 页面跳转不再写持久浏览历史；旧 WikiHistory 行保持不变；Viewer Back/侧键返回仍通过。
- Windows 11 x64 执行 Release build 0 warning/0 error、全量 .NET tests；真实窗口检查 Light/Dark、1280×820、900×600、1100 DIP 宽窄 Reader、滚动、Tab/focus、Automation Name 和 44×44 DIP 命中区。

### 交付边界

- 只提供开发输出和可复现验证命令给用户验收。
- 不运行 publish profile、Velopack、Setup、portable zip、update package、GitHub upload 或 Release 操作。

## 风险与回退

- **证据遗漏**：结构化选择可能漏掉复杂表格或靠后章节。先以 formatter 独立提交和质量 fixture 阻断；失败时回退 formatter/tool contract，不影响原始 Wiki 数据与 Viewer。
- **多次分块仍累积**：模型读取多个块时当前 run 仍会累计证据，但每块有硬上限且只按需返回；不得为压低数字禁止必要 continuation。
- **长会话语境丢失**：第 5 轮后最旧内容不进入模型。完整记录仍在 UI，本阶段不增加额外摘要模型或隐式费用。
- **协议不兼容**：v2 必须 Host/Sidecar 成对更新并严格 handshake；独立提交便于整体回退到 v1。
- **本地隐私**：会话正文以本地版本化文件保存而非 Credential Manager。文件不得进入日志、诊断、包或同步；设置页必须说明最近 4 轮会发送给 provider。
- **迁移损坏**：旧收藏文件始终保留，新 store durable 完成后才切换；任何失败回退旧收藏读取或内存模式。
- **浏览历史下线**：不主动擦除用户数据库，避免不可逆迁移；若将来要求物理清除旧表，必须建立独立数据迁移计划。
- **当前脏工作树**：无法安全隔离时不开始源码实施；不得通过 destructive Git 操作清理用户改动。

## 进度记录

- 2026-09-05：完成现有 Wiki Tool、Agent、Sidecar、Chat、收藏、资料库历史、Application/Core 数据链路与持续规范调查。
- 2026-09-05：用户确认本地自动保存、整段收藏、最近 4 轮、100 个未收藏会话、启动始终新对话、对话历史替换原浏览历史及旧表兼容保留。
- 2026-09-05：用户进一步确认 Token 优化不降低工具调用次数，核心改为结构化读取并禁止每次传入完整 HTML；计划据此删除 Search/tool/output 降额方案，增加 evidence blocks 与 continuation。
- 2026-09-05：用户明确授权实施，并授权 Codex 将既有 Phase 11/UI 修改先独立提交；已创建前置 checkpoint `8a15398`，只包含原有 19 个 tracked 修改，未夹带 untracked 治理/实验目录或本 change 文件。
- 2026-09-05：已实现 `wiki_blocks_v1`、focus/anchor/cursor、有界章节目录与 16384 字符 evidence 上限；合成长页基准验证首块 JSON 小于旧完整 HTML tool JSON 的 25%，且末尾目标物品仍优先进入首块。
- 2026-09-05：Python Agent 与 Windows Sidecar 已同步升级 v2；Agent 只重建 user/assistant 文本消息，拒绝 HTML block/章节路径/目录，保留 8 次 request/tool-call 与原输出/总 Token 安全上限，并映射 cached input usage。Python 离线测试当前 82 passed、4 个受控跨语言/付费项按 marker 跳过。
- 2026-09-05：已实现版本化会话目录、原子 index/backup、pending→interrupted、100 个未收藏淘汰、200 收藏/100 轮限位、旧 v1 收藏可重试迁移与损坏隔离；Chat、收藏和资料库共享同一 store，启动保持空白，新对话保留草稿。
- 2026-09-05：资料库第二页已替换为对话历史；Application 和 Viewer 已停止 WikiHistory 查询/写入，旧表/行与 Core schema 兼容保留，Reader 内存返回/侧键链路未删除。受影响 specs、ADR 0001/0004/0005/0006、ADR 0010、README、项目基线及 Phase 11 说明已同步。
- 2026-09-05：最终 Python `ruff`/format/strict mypy 通过，离线 pytest 82 passed / 4 skipped；真实 C# MCP→Python 非付费测试另行通过 1/1。内置 CPython 的 Application→WikiTools→Sidecar 离线链路首次冷启动受 Defender 扫描超过 15 秒，未改代码直接重跑通过（1/1，13 秒）。
- 2026-09-05：Windows dotnet Release solution build 9 项目 0 warning / 0 error；对同一输出执行全量测试 324/324（Core 92、Application 52、WikiTools 37、Windows 143）。开发输出中的 Agent/Sidecar 源与工作区逐字一致，EXE 和内置 CPython 均存在。
- 2026-09-06：人工启动发现 Chat 输入框在 Runtime 尚未 Ready 时被错误设为只读；修复为启动中/未配置/失败时可编辑并保留草稿、仅发送禁用，回答期间仍只读，补充 Presentation 行为回归与持续契约。定向测试 3/3、Windows Release build 0 warning / 0 error、全量测试 325/325 通过。
- 2026-09-12：用户要求归档本 change，并将另建新 plan 继续优化。真实问题、Token 门槛和窗口验收未在本 change 内执行；禁止的 publish/Velopack/Setup/portable/update/upload 均未运行。

## 发现、偏差与决策

- 228096 输入与 3323 输出证明优化优先级必须是工具载荷，而不是回答长度。
- 固定截断一整页虽然能降低 Token，但可能把目标章节截掉；最终采用“章节化 + focus/anchor + cursor continuation”，让模型可以多次小读而不是一次吞整页。
- 多轮历史不复用 PydanticAI 原始 message graph，因为它会把旧工具结果一起带回模型；Windows Host 持久化完整产品会话，Python 只接收有界文本投影。
- DeepSeek cache 是自动且 best-effort 的辅助收益；验收测总输入下降，不能用 cache hit 掩盖仍然巨大的上下文。
- 用户要求删除的是持久 Wiki 浏览历史，不是 Reader 返回能力；计划明确保留后者。
- 前置脏工作树已由用户授权独立保存为 `8a15398`；本 change 从该提交后的工作树继续，原有 untracked 文件保持不动。
- 原 formatter 若按整页正文全局去重，会错误吞掉不同章节中相同但合法的内容；实现已收窄为“同章节、同 kind、同规范化文本”去重，避免用 Token 优化牺牲证据完整性。
- 常见 infobox 没有独立列名行，而是每行 `<th>属性</th><td>值</td>`；formatter 已对这类行保留“属性: 值”，同时继续支持标准表头及 rowspan/colspan。
- 对话历史带筛选时，“清空未收藏”仍会删除筛选外记录；实现必须从 store 取得完整待删 ID 后通知 Chat，不能只根据当前可见列表重置当前会话。
- 内置 CPython 首次冷启动可能受 Windows Defender 扫描影响接近 15 秒握手门槛；同一未修改输出重跑 13 秒通过。本 change 不扩大为 Runtime timeout 调整，最终手工验收需如实观察首次 AI 启动状态。
- .NET `string.Length` 与 Python `len` 对补充平面字符的计数不同；evidence 计数与分块已统一为 Unicode scalar，并新增 emoji 回归，避免 C# 合法结果被 Python 误判或切断 surrogate pair。
- Chat 的草稿编辑能力不能与 Runtime Ready 混为同一状态；启动中或故障时应允许用户录入草稿，只有发送命令需要 Ready，回答期间才将 composer 设为只读。

## 验证结果

- 计划阶段已完成相关源码、spec、ADR、计划规范和 Windows UI 规范的只读核对。
- 创建计划前 `git status --short` 已确认工作树存在先前用户授权范围内的修改；本次只新增本 change 的 `plan.md`，未修改业务源码。
- Python `ruff check`、`ruff format --check`、strict `mypy` 通过；离线 `pytest` 为 82 passed / 4 skipped，其中 1 项真实 C# MCP 跨语言测试另以明确环境开关执行并通过，3 个付费 provider 场景继续禁止。
- Windows 最终定向/全量覆盖 WikiTools 37、Application 52、Core 92、Windows 143；solution 合计 324/324，包含完整 200 收藏压力、会话迁移/损坏/删除、历史截断、Unicode scalar 分块、协议 v2、Viewer 不写旧 history 和 UI 静态边界。
- 使用 Release 输出中的内置 CPython 3.14.7 执行真实 Application→WikiTools→Python Sidecar 离线 fixture，第二次通过 1/1；第一次冷启动握手超时已如实记录，未修改用户数据或调用远程模型。
- Windows Release solution build 为 0 warning / 0 error，同一输出的全量 tests 为 324/324。开发输出已包含逐字一致的最新 Agent/Sidecar 源、应用 EXE 与内置 CPython。
- 2026-09-06 输入修复定向 Presentation 测试 3/3；Windows Release solution build 再次为 0 warning / 0 error，全量 tests 为 325/325（Core 92、Application 52、WikiTools 37、Windows 144）。
- 实际窗口与真实 provider 尚未执行；不得据此宣称最终 Token 门槛已通过。它们按用户明确要求留给现有 API Key 的人工验收。

## 完成摘要

源码、持续契约与 Release 开发输出已经实现并通过自动验证：保持足够 Search/Read 调用，通过分章节、按主题、可继续读取的纯文本 evidence blocks 从源头移除模型上下文中的完整 HTML；同时增加最近 4 轮模型文本上下文、完整本地会话、新对话、整段收藏和对话历史页，并下线持久 Wiki 浏览历史。真实 Token/答案/来源与窗口门槛尚未由用户使用现有 key 验收，因此本 change 不标记为 `complete`；用户于 2026-09-12 主动结束并归档，剩余优化与验收交由后续新 plan 承接。本 change 未进行打包或发布。
