# Phase 05 — AI Adapter

- 状态：completed
- Phase：05 — AI Adapter
- 执行环境：WSL Codex CLI 负责阅读、编辑、静态检查和计划维护；Windows `dotnet.exe` 负责 solution restore、Release build 和全量 test；不安装 MAUI/Android/Python 工具链
- 创建日期：2026-08-24
- 最后更新：2026-08-24
- 范围：薄 Wiki Tool Adapter、无状态 MCP Streamable HTTP、消费者协调、独立测试、持续契约与 transport ADR
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/specs/wiki-query-boundary/spec.md`；`openspec/specs/application-use-cases/spec.md`；`openspec/specs/wiki-tools/spec.md`；`docs/adr/0002-portable-core-and-data-root.md`；`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`

## 背景与当前证据

用户已明确要求按提供的 Phase 05 计划实施。实施前外层 HEAD 为 `14b51b7`；内层 HEAD 为 `601063d`，与要求基线一致且工作树干净。外层已有 `.codex/`、experiments、临时 Search 探针、Phase 00 inputs 和若干治理归档等未跟踪内容，本 Phase 不修改、不暂存它们。

真实代码证据：

- `WikiQueryUseCases.SearchAsync` 已支持设置默认上限和显式 `1..100` 上限，并直接返回 Core 的确定顺序 `SearchResultItem`；空查询由 Core 返回空列表。
- `WikiQueryUseCases.ReadAsync` 已映射为无持久化模型泄漏的 `WikiReadResponse`，保留 requested/resolved title、found/not-found、Redirect、anchor、修改时间和原始存储 HTML，且 Query Read 不记录 history。
- Application 已有 `IWikiDataConsumerCoordinator`，活动库初始化、切换和破坏性数据操作可通过 pause lease 与消费者协调；Phase 05 只需新增独立 coordinator，不修改 Application/Core 公共契约。
- 当前 portable solution 包含 Core 89、Application 54、Windows 28 个测试；Phase 04 内层提交 `601063d` 是本阶段恢复点。
- 官方 MCP C# SDK 2.2.0 支持 `net10.0`、ASP.NET Core Streamable HTTP、显式无状态模式、属性式工具 schema、结构化输出和官方 C# client；依赖只需进入 Adapter/test projects。

## 目标与完成定义

1. 新增单一 `net10.0` `Terraria_Wiki.WikiTools` project 与独立 tests，加入 `Terraria_Wiki.Core.slnx`；生产 project 只引用 Application，并固定 `ModelContextProtocol.AspNetCore` 2.2.0。
2. `WikiToolServer.StartAsync(WikiQueryUseCases, WikiToolConsumerCoordinator, CancellationToken)` 启动嵌入式无状态 MCP server，精确绑定动态 IPv4 loopback `/mcp`，使用每次启动轮换的内存 bearer token，并严格校验 Host；不开放 CORS。
3. `search_wiki` 和 `read_wiki` 是发现结果中的仅有工具，具有固定输入/输出 schema；返回语义与直接 Application 调用一致，不访问 Core、SQLite、全局状态或 history。
4. 输入、Application/Core 稳定错误和未知内部异常映射为不泄漏路径、堆栈或数据库细节的 tool error；调用方取消保持取消。
5. coordinator pause 拒绝新调用、取消并等待在途调用；lease 释放后恢复。server stop/dispose 幂等并取消、等待请求、关闭 Kestrel 和释放端口；初始化期间 server 未挂载时 pause 是安全空操作。
6. Windows restore、0-warning Release build、既有 171 个测试及新增 WikiTools tests 全部通过；官方 C# MCP client 实际完成连接/发现/Search/Read。
7. 新增持续 spec 与 ADR，完成后只更新 README、项目基线和 Master Architecture 的阶段状态/transport 决策；内外层分别提交且不 push。

## 前置条件

- [x] Phase 00–04 已完成并归档；Phase 02/03 已提供稳定 Search/Read 与 Application 边界。
- [x] 用户明确授权实施 Phase 05。
- [x] 内层处于干净 `601063d`；外层无关未跟踪内容已识别并保持排除。
- [x] 已核对官方 `ModelContextProtocol.AspNetCore` 2.2.0 的无状态 HTTP、工具 schema、结构化输出、错误与 client API。
- 所有数据库写入只发生在测试临时目录；不发现、不打开、不修改生产数据。

## 范围与非目标

范围：WikiTools/test projects、两项 MCP tools、动态 loopback、bearer/Host 校验、请求协调和生命周期、solution、持续 spec、ADR 与 Phase 状态文档。

非目标：Python/PydanticAI、模型 API、Search → Read → Answer 质量验证、Windows UI 或 Runtime 接线、独立产品 Host、HTML 转换/截断、来源 URL、token/context 预算、resources、prompts、OAuth、TLS、远程监听、CORS、stdio、Windows publish/安装器和 Android transport。

## P0 / 兼容性影响

- Adapter 只调用已有 Application Query；不引用 Core project，不复制 SQLite/FTS5/Search/Read，也不改变数据、schema、package、Viewer 或旧客户端行为。
- Search 保留七级顺序、Redirect、纯文本摘要和 UTF-16 highlights；Read 返回完整原始存储 HTML，不记录 history。
- 数据切换/维护前 coordinator 取消并排空 AI 查询，避免活动 ContentDb 切换期间残留读取。
- transport 只属于嵌入式 Adapter；Windows 生命周期接线留 Phase 07，Android 不继承本决策。

## 方案与待决问题

- server 使用 ASP.NET Core Kestrel 与官方 MCP SDK 2.2.0，`Stateless = true`，只映射 `/mcp`；不注册 resources/prompts 或其他工具。
- endpoint 精确使用 `127.0.0.1:0`，请求 Host 必须等于实际 `127.0.0.1:<port>`；Authorization 必须是当前临时 bearer token，比较使用固定时间字节比较。
- token 使用 256-bit 密码学随机数与 base64url 表达，只存内存；connection 的 `ToString()` 仅显示 endpoint。
- MCP DTO 使用明确的 snake_case JSON 名称与稳定字符串状态/匹配类型；成功结果启用 output schema 和 structured content。
- 已知错误只返回稳定 code：非法输入、未初始化/Application 稳定 code、数据源不可用、paused/stopped；未知异常统一 `wiki-tool-failed`。日志不包含 token 或底层异常细节。
- 无阻塞待决问题。若 SDK 强迫修改 Application/Core 公共契约、复制数据或引入产品 Host，则停止并按计划回退到私有 loopback JSON 评估。

## 文件变更

- **ADD** `terraria-wiki-offline/src/Terraria_Wiki.WikiTools/**`：server、connection、coordinator、tool handlers/DTO/error mapping。
- **ADD** `terraria-wiki-offline/tests/Terraria_Wiki.WikiTools.Tests/**`：官方 client 集成、契约、安全、并发/取消/生命周期和边界测试。
- **MODIFY** `terraria-wiki-offline/Terraria_Wiki.Core.slnx`：加入两个 project。
- **ADD** `openspec/specs/wiki-tools/spec.md`、`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md` 与本计划。
- **MODIFY（完成时）** `README.md`、`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/project.md`：只更新状态和已确认 transport 决策。
- **KEEP** Core/Application/Windows/旧 MAUI 生产源码、所有数据库与 Viewer 资产。

## 执行步骤

- [x] 复读治理、相关 specs/ADR、Phase 03/04 记录及真实 Application Query/Core 查询 DTO/消费者实现/测试结构；确认 Git 基线。
- [x] 核对官方 MCP C# SDK 2.2.0 的 transport、schema、错误和 client API；创建本活计划。
- [x] 建立 WikiTools/test projects和 solution 条目，restore 固定依赖。
- [x] 实现稳定 tool DTO/mapping、错误边界与恰好两个工具。
- [x] 实现动态 loopback server、Host/bearer 防护、connection/token 轮换与幂等生命周期。
- [x] 实现 coordinator 的 pause/release、在途请求取消/排空和 server 未启动空操作。
- [x] 完成官方 C# client 集成、契约、安全、并发/取消/生命周期和静态边界测试。
- [x] 执行 Windows restore/build/full test 与 WSL 差异/依赖/whitespace/生成物/Git 状态检查。
- [x] 新增 spec/ADR，更新入口/架构/项目状态；记录验证、完成并归档计划，分别提交内外层且不 push。

## 验证与回归计划

### Windows portable solution

- `dotnet.exe restore Terraria_Wiki.Core.slnx`、Release build 0 warning/error、全量 test。
- 保留 Core 89/89、Application 54/54、Windows 28/28。
- 官方 C# MCP client 连接动态 endpoint，实际完成协议协商、list tools、Search、Read。
- 工具发现精确两个；名称、输入/output schema、structured content 固定。
- Search 对比直接 Application 的顺序、limit、Redirect、summary、七级 kind 和 highlights；空查询不写 history。
- Read 对比直接 Application 的 direct/Redirect/anchor/not-found/完整 HTML/修改时间，并验证不写 history。
- 缺失/错误 token、错误 Host、未知 tool、非法参数、数据源不可用和内部异常安全拒绝。
- 并发、调用方取消、pause/release、重复 stop/dispose、token 轮换、在途清理和端口释放。

### WSL 静态检查

- WikiTools project 仅引用 Application；无 Core project、SQLite、MAUI、Avalonia、Windows API、Python、持久化模型或全局 `App.*`。
- 无远程监听、固定端口、CORS、resources、prompts、来源 URL 或 HTML 处理实现。
- `git diff --check`、实际 diff、生成物/临时文件和两个 Git 状态/提交范围检查。
- WSL 结果不描述为 Windows 客户端验证。

### 明确不执行

模型 API、Python/PydanticAI、Windows publish/安装器、正式 Windows Host 接线与 Android 验证均不在 Phase 05。

## 风险与回退

- SDK 版本/协议变化：固定 2.2.0 并锁定工具 schema 与官方 client 集成回归；升级必须另行决策。
- loopback 被本机其他进程访问：动态端口、严格 Host 和每次启动临时 bearer token共同限制；token 不进入 URL、日志或 `ToString()`。
- pause/stop 与查询竞争：先关闭准入并取消当前 generation，再等待引用计数归零；lease/stop 设计为幂等。
- 完整 HTML 可能较大：本阶段忠实返回，截断/Reader 管线必须由 Phase 06 真实 Agent 质量证据决定。
- 回退单位是新增 WikiTools/test projects、solution 两项和本 Phase 治理文件；既有 Core/Application/Windows、数据库和用户数据无迁移或回滚。

## 进度记录

- 2026-08-24：用户提供已批准 Phase 05 计划并要求在新上下文实施；按 `INIT.md` 重读入口、架构、项目基线、相关 specs/ADR、Phase 03/04 与真实源码/测试。
- 2026-08-24：确认外层/内层基线为 `14b51b7`/`601063d`，内层干净，外层无关未跟踪内容保持排除；计划转为 `in-progress`。
- 2026-08-24：确认 Application Query 已完整承载 Search/Read 语义，无需改动 Application/Core；确认官方 MCP SDK 2.2.0 的无状态 Streamable HTTP 与 structured tool 支持。
- 2026-08-24：新增 WikiTools/test projects、动态 loopback、临时 bearer/Host 防护、两项 structured tools 和 consumer coordinator；Windows 首轮 restore/build 0 warning/error。
- 2026-08-24：SDK 实测确认抛出 `McpException` 会为 tool error 文本增加统一调用前缀；改为显式 `CallToolResult` 并通过 `OutputSchemaType` 保持成功 schema，从而返回精确稳定 error code。
- 2026-08-24：新增 25 个 tests，官方 C# client 以 `2025-11-25` 完成 initialize、无 session、list tools、Search/Read；安全、无 resources/prompts/legacy SSE、并发、取消、pause/release、重复启停、token 轮换、端口释放和边界测试通过。
- 2026-08-24：Windows solution restore、0-warning Release build 和 196/196 全量 tests 通过；WSL 依赖扫描、Windows 未接线检查、staged whitespace/生成物/Git 范围检查通过。
- 2026-08-24：内层 Phase 05 实现以提交 `147c38d` 保存；新增 Wiki Tools 持续 spec 与 ADR 0004，更新 README、Master Architecture 与项目基线；Phase 05 完成并归档，内外层分别提交且不 push。

## 发现、偏差与决策

- SDK 2.2.0 的当前无状态协议可不维护 HTTP session，同时仍由官方 client 处理协商；Phase 05 显式设置 stateless，避免依赖 SDK 默认值。
- 初始化前 Application 会调用 coordinator pause；因此未挂载 server 的 pause lease必须是无副作用空操作，不能让未来 server 以 paused 状态启动。
- SDK 属性式工具对类型化成功返回可自动生成 output schema，但精确 tool error 需要显式 `CallToolResult`；通过 `OutputSchemaType` 同时满足两者，不改变对外 schema。
- 官方 client 默认优先 2026-07-28 的无 initialize 协议；验收测试显式固定 2025-11-25，以实际覆盖计划要求的 initialize 链路，同时 server 仍保持无状态。

## 验证结果

- Windows `dotnet.exe restore Terraria_Wiki.Core.slnx --nologo`：通过，8 个 project 均为最新或成功还原；`ModelContextProtocol.AspNetCore` / client 固定为 2.2.0。
- Windows `dotnet.exe build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：通过，0 warnings / 0 errors。
- Windows `dotnet.exe test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`：Core 89/89、Application 54/54、Windows 28/28、WikiTools 25/25，共 196/196，0 skipped。
- 官方 C# MCP client 在新增 tests 中固定 `2025-11-25`，实际覆盖 initialize、list tools、Search 与 Read；两项工具的 schema、annotations 和 structured content 通过。
- Search 与直接 Application 在七级顺序、默认/显式 limit、Redirect、summary、match type 和 UTF-16 highlights 上逐项一致；Read 覆盖直接页、Redirect、anchor、not-found、原始 HTML、修改时间和不写 history。
- 安全/生命周期 tests 覆盖缺失/错误 token、错误 Host、无 CORS、未知工具、非法/缺失参数、未初始化、数据源不可用、未知内部异常、并发、调用方取消、pause/release、嵌套 lease、重复/冲突 start、重复 stop/dispose、token 轮换、在途清理和端口释放。
- WSL 静态/反射边界门通过：生产 project 只有 Application `ProjectReference` 与固定 MCP package，不含 Core project、SQLite/Core service、持久化模型、MAUI、Avalonia、Windows API、Python、Vector/Embedding、CORS 或远程监听；`Terraria_Wiki.Windows` 未接线 WikiTools。
- WSL 内层 `git diff --cached --check` 通过；build 的 `bin/obj` 均由既有 ignore 规则排除，提交范围只有 solution、新增 Adapter 和 tests。外层 diff/whitespace 与提交范围在归档后复核通过。
- 未执行且不在范围：模型 API、Python/PydanticAI、Windows publish/安装器、正式 Windows Host 生命周期接线、Android 与其他平台验证。

## 完成摘要

Phase 05 已完成。内层新增独立 `Terraria_Wiki.WikiTools` 与 tests，以官方 MCP SDK 2.2.0 的无状态动态 IPv4 loopback 和每次启动临时 bearer token，只把现有 Application `search_wiki` / `read_wiki` 暴露为固定 structured tools。Adapter 不访问 SQLite/Core service/全局状态，不转换页面 HTML，不写 history；consumer coordinator 在数据操作前拒绝新调用、取消并排空在途调用，server 生命周期和 token 均可安全重复回收。

Windows restore、0-warning Release build 与 196/196 tests 通过，官方 C# client 实际完成 initialize、发现、Search/Read。Core、Application、Windows、旧 MAUI、SQLite/schema、Search、数据包和用户数据均未修改；Python Agent、模型质量和 Windows Runtime 接线明确留给 Phase 06/07。
