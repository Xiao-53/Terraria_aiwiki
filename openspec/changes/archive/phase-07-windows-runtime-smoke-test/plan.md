# Phase 07 — Windows Runtime Smoke Test

- 状态：completed；全部实现、回归、两种发布 smoke 与单次真实 provider 验收通过，已归档
- Phase：07 — Windows Runtime Smoke Test
- 执行环境：WSL Codex CLI 负责阅读、编辑、Python 3.12 与静态检查；Windows 11 x64 的 Python 3.12.3 与 .NET 10 负责目标平台 build/test/run/publish/真实 provider 验收
- 创建日期：2026-08-27
- 最后更新：2026-08-27
- 范围：Agent 流式事件、stdio NDJSON Sidecar、Windows AI Runtime/配置/生命周期、最小 AI 面板、发布与 Windows smoke
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`；`openspec/specs/ai-backend/spec.md`；`openspec/specs/wiki-tools/spec.md`；`openspec/specs/application-use-cases/spec.md`；`openspec/specs/local-wiki-viewer/spec.md`；`docs/adr/0003-use-loopback-local-viewer-transport.md`；`docs/adr/0004-use-loopback-mcp-wiki-tool-transport.md`；`docs/adr/0005-use-pydantic-ai-openai-backend.md`

## 背景与当前证据

用户提供已批准的 Phase 07 实施计划并要求在新上下文完成实施。实施前外层 HEAD 为 `5e12c1921bfc502350ea6cd6e8caba91868ddeb5`，内层 HEAD 为 `e18b7f89badacd01dbff2de65ad6317567a47f3b`；内层工作树干净，外层只有已声明不纳入本阶段提交的既有未跟踪内容。两个提交即本阶段恢复点。

已读取的真实实现证据：

- Python 公共入口当前为 `TerrariaWikiAgent.answer(question, connection)`，使用 PydanticAI 2.31 `Agent.run()`、typed output tool、`_RunEvidence.process_tool_call` 派生来源/trace，并在最终 Markdown 上一次性移除远程 URL；没有流式或 Sidecar。
- 锁定的 PydanticAI 2.31 源码确认 `run_stream_events()` 会完整运行 Agent 图、流出文本和函数工具事件、以 `AgentRunResultEvent` 收口，并通过 context manager/cancel handle 排空取消。
- Windows `WindowsClientRuntime` 当前只组合 Application + Viewer，Viewer coordinator 直接作为唯一 `IWikiDataConsumerCoordinator`；Phase 07 必须用小型组合 coordinator 同时协调 Viewer 和 WikiTools，且在后一个 pause 失败时逆序释放前一个 lease。
- `MainWindow` 是代码构造的 Phase 04 临时壳，已有 Viewer/search、启动重试和自动 smoke；可以局部增加 AI 面板而不引入新的 Presentation 框架或 Markdown 依赖。
- `WikiToolServer` 已提供动态 `127.0.0.1` endpoint、内存 bearer、严格 Host/token 和幂等 stop；Windows project 尚未引用 WikiTools。
- 现有 publish target 只复制 Viewer 资产。Phase 07 需从 `agent/` 唯一源码复制 Sidecar launcher、包源码、`pyproject.toml` 与 `uv.lock`，不复制 Python 本体或虚拟环境。

前置假设已实测收口：Windows Python 3.12.3 环境已按 `uv.lock` 准备；用户随后授权把有效 `TERRARIA_AI_API_KEY` 写入 Windows 用户环境，密钥未进入参数、配置、日志、smoke 结果或仓库。发布版真实 provider 验收通过后 Phase 07 完成归档。

## 目标与完成定义

1. 旧 `answer(question, connection)` 调用保持可用，新增可选异步事件回调并流出 `run_started`、`tool_started`、`tool_finished`、`text_delta`；最终仍返回完整 `WikiAgentAnswer`。
2. 最终模型输出使用纯 Markdown 文本；只有最终正文被流出。增量 URL 过滤、首尾空白处理与最终归一保证全部 `text_delta` 拼接严格等于 `answer.markdown`。来源、status、usage 与 trace 继续由宿主收口。
3. 新增长驻 stdio NDJSON v1 Sidecar，严格实现 initialize/answer/cancel/shutdown 与 ready/run/tool/delta/completed/cancelled/failed/fatal，逐行上限 256 KiB、单活动问题、顺序/ID/字段验证和安全错误。
4. 新增严格非密钥 `ai-runtime.json` v1、默认 LocalAppData 路径与 `--ai-config` 覆盖；Python 必须为存在的绝对 `python.exe`，握手确认 Python 3.12–3.14、协议和 PydanticAI 2.31。启动不下载依赖。
5. Windows 组合 Viewer + WikiTools consumer，按 Application → Viewer → WikiTools → Sidecar 启动，按活动回答 → Sidecar → WikiTools → Viewer → Application 退出；崩溃后停止 MCP 并只允许手动“重试 AI”创建新端口/token。
6. 临时 Windows 壳支持独立单轮提问、停止、重试、工具进度、只读流式正文、来源与 usage；AI 不可用不影响离线 Search/Viewer。
7. Windows Release build、完整既有及新增 tests、真实无外网跨语言 fixture、framework-dependent/self-contained publish 实际 smoke 全部通过；有效 key 存在时再以 DeepSeek `deepseek-v4-flash` 完成一次不自动重试、上限 0.03 美元的真实验收。
8. 更新持续 spec、ADR 0006、README/项目基线/架构当前状态；内层源码与外层治理分别提交，不 push。全部门槛满足后完成并归档本计划。

## 前置条件

- [x] Phase 00–06 已完成并归档；Phase 04 Windows Host、Phase 05 WikiTools 与 Phase 06 Agent 均存在真实验证记录。
- [x] 用户明确授权实施 Phase 07。
- [x] 内外层基线与工作树边界已确认；既有外层未跟踪内容保持排除。
- [x] PydanticAI 2.31 `run_stream_events()`、事件和取消语义已从锁定 tag 源码核对。
- [x] Windows Python 3.12.3 绝对路径已确认为 `C:\Users\Xiao\AppData\Local\Programs\Python\Python312\python.exe`；WindowsApps/`py` 默认 alias 仍显示 3.14，因此所有验证与配置使用该绝对路径。锁定依赖与用户授权的环境密钥均已通过真实发布验收。

## 范围与非目标

范围内：Agent 流式公共事件和最终收口、Sidecar launcher/protocol、Windows 内部 Runtime/配置/组合 coordinator/进程管理、最小 AI 面板、fake/真实测试 Sidecar、自动 smoke、publish 资产、持续 spec/ADR/状态文档。

非目标：AI Reader/HTML 精简、多轮/history、凭据 UI或 Windows Credential Manager、内置 Python/自动安装/安装器/签名、WebView2 bootstrap、最终 Presentation、P0 管理 UI、Android、其他平台、Vector/Embedding、SQLite/schema/FTS5/同步/导入导出/Viewer 内容协议变化。

## P0 / 兼容性影响

- Application/Core/Search/Read/SQLite 和 Viewer 资产保持不变；AI 仍只经 WikiTools → Application 查询同一活动数据。
- 组合 coordinator 获取 pause lease 时若任一消费者失败，必须逆序释放已取得 lease；成功时释放也按逆序，保证 Viewer/WikiTools 不遗留暂停或在途读。
- AI 配置、Python、密钥、provider 或 Sidecar 故障只改变 AI 状态，不阻断已初始化的 Viewer、搜索与数据根。
- bearer 只通过 initialize stdin，API key 只继承子进程环境；命令行、配置、stdout/stderr、公开错误和 smoke 日志不得含 secret、HTML 或工具参数。
- 所有自动 smoke 只使用合成/临时数据根；不读取或修改生产数据库与既有 package 原件。

## 方案与待决问题

- Agent 改用 `Agent[str]` 与明确的固定不足证据正文。收到 `FinalResultEvent(tool_name=None)` 后才转发该次模型请求的 TextPart；工具请求中的伴随文本、thinking、工具参数和返回值全部丢弃。
- 增量 Markdown sanitizer 持有未闭合 URL/协议前缀与尾随空白，只发布确定安全前缀；finalize 后与最终同一 sanitizer 结果比较。空/超长/不一致输出归一为安全 `model_unavailable`。
- 公共事件使用 frozen、extra-forbid 的判别式 Pydantic models；callback 串行 await 形成背压，callback 异常取消运行并映射安全 `unknown`。
- Sidecar 使用独立 stdin reader、唯一私有 stdout 协议 writer 和有界 stderr；严格 JSON 模型禁止未知字段。并发新 request 返回 `runtime_busy`，重复 ID/非法顺序/未知消息/超长行/非协议 stdout 触发 fatal 并退出。
- Windows 将协议解析与 OS Process owner 分离以便 fake stream/process 测试。Process 使用 `-I -u -X utf8 <published launcher>`，无 shell；正常 shutdown 超时后只对所属 `Process` 调用 `Kill(entireProcessTree: true)`。
- `ai-runtime.json` 只含 version/python/provider；C# 先做结构/路径/key 门，再启动 WikiTools 与 Sidecar。Sidecar 再做 Python/依赖/provider/MCP 构造握手。
- 无阻塞设计待决项。密钥缺失期间计划保持 in-progress 且未发请求；用户提供并授权写入环境后只执行一次真实 provider 回答，没有自动重试或第二次付费尝试。

## 文件变更

- MODIFY `terraria-wiki-offline/agent/src/terraria_wiki_agent/{models.py,agent.py,__init__.py}`：公共流式事件、纯文本输出、增量 sanitizer/最终收口。
- ADD `terraria-wiki-offline/agent/src/terraria_wiki_agent/{sidecar.py,sidecar_protocol.py}` 与 `agent/sidecar_launcher.py`：NDJSON v1 Runtime。
- MODIFY/ADD `terraria-wiki-offline/agent/tests/**`：流式、脱敏、callback、取消、协议和 Sidecar subprocess tests；新增测试专用 Runtime fixture。
- MODIFY `terraria-wiki-offline/src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj`：引用 WikiTools并复制 AgentRuntime 资产。
- ADD `terraria-wiki-offline/src/Terraria_Wiki.Windows/AI/**`：config、协议 DTO/session、process owner、AI coordinator/状态。
- ADD `terraria-wiki-offline/src/Terraria_Wiki.Windows/Hosting/CompositeWikiDataConsumerCoordinator.cs`。
- MODIFY `terraria-wiki-offline/src/Terraria_Wiki.Windows/Hosting/WindowsClientRuntime.cs` 与 `MainWindow.cs`：组合生命周期、最小 UI、smoke。
- ADD/MODIFY `terraria-wiki-offline/tests/Terraria_Wiki.Windows.Tests/**`：config、协议、fake process、coordinator、UI/runtime/smoke/boundary tests。
- ADD `openspec/specs/windows-ai-runtime/spec.md` 与 `docs/adr/0006-use-stdio-python-sidecar.md`。
- MODIFY `openspec/specs/ai-backend/spec.md`、`README.md`、`openspec/project.md`、`docs/architecture/ARCHITECTURE-v0.6.md`；完成后归档本计划。
- KEEP Core/Application、生产 WikiTools 契约、旧 MAUI、Viewer 内容、数据库和 package 格式。

## 执行步骤

- [x] 读取治理、架构、相关 specs/ADR、Phase 04–06 记录及真实 Agent/Windows/WikiTools 配置、入口、实现、调用方和测试；确认 Git 基线。
- [x] 核对 PydanticAI 2.31 锁定源码的完整事件流、文本/工具事件和取消行为；创建本活计划。
- [x] 实现 Agent 流式事件、纯 Markdown、安全增量过滤/最终收口和 Python 单元测试。
- [x] 实现严格 NDJSON Sidecar、launcher、依赖握手、取消/崩溃/协议测试与测试 fixture。
- [x] 实现 Windows config、组合 coordinator、WikiTools/Sidecar 生命周期、严格协议和 fake process tests。
- [x] 接入最小 AI 面板与自动 smoke，验证 AI 故障隔离、独立单轮和退出顺序。
- [x] 执行 WSL/Windows Python 门槛、Windows .NET build/full tests、真实无外网跨语言链路和两种 publish smoke。
- [x] 使用发布版与有效环境 key 执行一次 DeepSeek `deepseek-v4-flash` 真实验收，记录首段/总耗时/usage；未自动重试。
- [x] 更新 spec/ADR/入口状态。
- [x] 检查 diff、secret、生成物、临时文件和两个 Git 状态；验收专用 publish/data/log 与临时 Python 环境已清理，外层既有未跟踪内容未纳入。
- [x] 内层源码已提交为 `d328a04`；本计划与治理文件完成归档提交，不 push。

## 验证与回归计划

### WSL Python 3.12

- 以 `uv.lock` frozen 环境运行 `ruff check`、`ruff format --check`、strict mypy、pytest。
- 覆盖事件顺序、工具状态、正文分块、跨分块 URL、首尾空白、证据不足、usage/source/trace、取消、超时、预算、空/超长输出和 callback 故障。
- 覆盖协议合法流、未知字段/消息、重复 ID、并发、乱序、超长行、EOF、stdout 污染、shutdown、崩溃与安全错误。

### Windows 11 x64

- Windows Python 3.12.3 使用同一 lock 完成 lint/format/mypy/pytest 与 Sidecar subprocess tests。
- .NET 10 solution restore、Release build、全部既有 199 项及新增 tests；fake process 覆盖握手、stream、cancel、retry、关闭超时/进程树策略和严格协议。
- 真实 Application + WikiToolServer + Python 测试 Sidecar，以临时数据 root 完成无外网 Search → Read → 至少两段 answer delta → typed completion。
- 两种 `win-x64` publish 目录均检查 AgentRuntime 文件，并用同一配置 Python 环境实际启动、提问、取消、再次提问与退出清理。
- 真实 provider 单次“天顶剑怎么合成”必须有 Search/Read、至少两个正文增量、可信来源与 usage；上限 0.03 美元、不自动重试。

### 安全与边界

- 静态扫描确认生产 Agent 不接触数据库/UI/平台，Windows 只在组合根引用 Core HTTP transport且不直接访问 SQLite。
- 命令参数、配置、stdout/stderr、错误、smoke 日志和 Git diff 中无 API key、bearer、HTML、工具参数或用户路径；publish 不含 Python 本体/venv/cache。
- `git diff --check`、生成物/临时文件和两个仓库提交范围检查。WSL 检查不冒充 Windows 目标平台结果。

## 风险与回退

- 流式一致性：URL 可能跨 chunk，且最终 `.strip()` 会与已发 delta 冲突；使用保守尾部缓冲并以拼接等于最终正文作为硬 invariant，不满足即失败而非发出不一致 completion。
- 协议污染/死锁：stdout 独占、逐行上限、异步 stderr drain、严格顺序和有界 shutdown；任何不可信 stdout 终止当前 Sidecar并要求手动重试。
- 进程误杀：只保存自己启动的 `Process`，正常 shutdown 后才在超时分支 `Kill(entireProcessTree: true)`；不按名称扫描/终止其他 Python。
- 数据切换竞争：组合 pause 逆序回滚；Sidecar 崩溃先停止关联 WikiTools，重试总是新 server/token/port。
- provider/依赖：不升级 PydanticAI；不自动安装。配置/环境错误仅禁用 AI。
- 回退到内层 `e18b7f8` 与外层 `5e12c19`；无 schema 或用户数据迁移，新增 Runtime 文件可由独立提交整体回退。

## 进度记录

- 2026-08-27：用户提供批准的 Phase 07 计划并要求直接实施。按 `INIT.md` 读取入口、架构、项目基线、计划规范、相关 specs/ADR、Phase 04–06 完成记录及真实源码/测试。
- 2026-08-27：确认恢复基线外层 `5e12c19`、内层 `e18b7f8`，内层干净；既有外层未跟踪内容保持排除。锁定 PydanticAI 2.31 源码证明 `run_stream_events` 和 cancel/cleanup 语义可满足方案，计划进入 in-progress。
- 2026-08-27：用户说明 Windows 默认 Python 改为 3.12.3。实际枚举确认 3.12.3 位于 `C:\Users\Xiao\AppData\Local\Programs\Python\Python312\python.exe`；WindowsApps alias 与 `py` 默认仍解析 3.14.6。Phase 07 按配置契约使用前者绝对路径，不依赖 alias。
- 2026-08-27：Agent 改为 `run_stream_events()` + 纯 Markdown，并新增安全判别事件、增量 URL/空白 sanitizer、固定不足证据收口、callback/取消/超长和 Sidecar protocol tests；旧两参数 answer 调用保持兼容。
- 2026-08-27：Windows 新增严格 `ai-runtime.json`、NDJSON parser/client、所属进程 owner、AI lifecycle、Viewer/WikiTools composite coordinator 与最小 UI；AI 缺失/失败只改变面板状态。真实跨语言测试首次发现 Windows test/runtime 缺少 `Microsoft.AspNetCore.App` framework reference，补入 Windows Host 后通过。
- 2026-08-27：用 Windows Python 3.12.3 建立被 Git 忽略的 `agent/.venv` 并按 `uv.lock --frozen` 准备依赖。WSL/Windows Python 门槛、Windows 219 tests、Application + WikiTools + Python 无外网链路和既有 Python→C# MCP 链路全部通过。
- 2026-08-27：正式 Release WinExe 在缺少 AI 配置时通过离线 smoke；framework-dependent/self-contained 发布均包含 AgentRuntime，并借助测试专用 loopback Chat fixture 使用同一 Windows Python 环境完成取消、再次提问、Search/Read、2 个 delta、来源/usage 与退出清理。
- 2026-08-27：安全检查 Windows Process/User/Machine 三层环境均未发现 `TERRARIA_AI_API_KEY`。按既定门槛不发起真实 DeepSeek 请求、不把 fixture 冒充 provider 验收，Phase 07 保持未完成且不归档/提交。
- 2026-08-27：用户提供密钥并明确授权由 Codex 写入。密钥以 Windows 用户环境变量保存，未进入 `ai-runtime.json`、进程参数、协议、日志、smoke 结果或 Git；随后只执行一次发布版 DeepSeek 回答。
- 2026-08-27：framework-dependent 发布版以临时只读来源生成的 Wiki 快照回答“天顶剑怎么合成”，完成 Search/Read、88 个正文增量、1 个来源与 typed completion；真实 provider 门槛通过。内层源码提交为 `d328a04`，治理状态更新后归档本计划。

## 发现、偏差与决策

- Phase 06 typed output tool 不会提供可直接逐字显示的最终 Markdown；Phase 07 按批准计划改为纯文本 output，并由“成功 Read + 固定不足证据文本”在宿主归一 status。来源/trace 仍完全由 structured WikiTools 结果派生。
- Windows Application 当前只接受一个 consumer coordinator。新增内部 composite 是必要的最小组合，不增加 Application public port、公共持久模型或新 framework。
- PydanticAI 2.31 `run_stream_events()` 的 `FinalResultEvent` 可区分最终 text response；因此只有该事件对应的 TextPart 才能成为 `text_delta`，工具轮次伴随文本和 thinking 一律不转发。
- Windows 旧 portable solution 的 WikiTools TestHost runtimeconfig 已隐式包含 ASP.NET shared framework，但 Avalonia Windows Host 初次真实启动 server 时缺少该 framework。Windows project 显式增加 `Microsoft.AspNetCore.App` FrameworkReference 是承载既有 WikiTools Adapter 的必要 Runtime 依赖；没有引入 MAUI workload。
- 取消可能与 completed 恰好竞态。Windows client 在已发送 cancel 后若先读到 completed，必须再排空该 cancel 的 `request_not_active` 控制响应，并仍按调用方取消结束；否则残留控制消息会污染下一问题。该路径已有复用回归。
- 发布 AI lifecycle smoke 使用测试目录内的本机 Chat Completions fixture，只证明生产 launcher/Sidecar/协议/MCP/UI lifecycle，不冒充供应商兼容或付费验收；独立的发布版 DeepSeek 门槛随后以单次真实请求通过。

## 验证结果

- WSL Python 3.12 frozen env：`ruff check`、`ruff format --check`、strict `mypy --no-incremental` 全部通过；pytest `77 passed, 4 skipped`，其中 skipped 为需显式开关的真实 C#/付费场景。另启用真实 C# MCP 标记后离线跨语言 `1 passed`。一次并发 WSL/Windows mypy 因共享 `.mypy_cache` 触发 mypy 2.3.1 内部错误，改为独立无增量运行后通过，不是源码失败。
- Windows Python 3.12.3 frozen `agent/.venv`：ruff/format/mypy 通过；pytest `77 passed, 4 skipped`。Sidecar launcher subprocess 在 `-I -u -X utf8` 下通过。
- Windows `.NET 10`：restore 与 Release build 通过，0 warning / 0 error；完整 `Terraria_Wiki.Core.slnx` 为 Core 89 + Application 54 + WikiTools 28 + Windows 48 = `219/219`，0 skipped。
- 真实无外网链路：Windows Application + WikiToolServer + Windows Python Sidecar 完成 Search → Read → 至少 2 delta → typed answer/source/trace/usage，1/1 通过。
- 正式 build WinExe 缺少 AI 配置的 smoke 通过：Search 1、JS 1、CSS 1、image 1、audio 4、history 0、exit cleanup true，证明 AI 降级不影响离线链路。
- 最终源码重发的 framework-dependent `win-x64` 发布约 170 MB，self-contained 约 276 MB；两者 AgentRuntime 文件完整，基础 smoke 均通过。
- 最终源码重发后，两种发布的 AI lifecycle smoke 均通过取消 → 再提问 → Search/Read → 2 delta → 1 source → usage → cleanup。framework-dependent 首段/总耗时为 1132/1146 ms；self-contained 为 1133/1147 ms；fixture usage 均为 input 60、output 15、total 75。
- 已知 WebView2 退出仍打印 Phase 04 已记录的 `Chrome_WidgetWin_0` unregister 1412 诊断；进程、smoke result 与 cleanup 均成功。
- 发布版 DeepSeek `deepseek-v4-flash` 单次真实验收通过：`answered`，Search/Read 对应 6 个工具事件，88 个正文增量，1 个可信来源；usage 为 input 5975、output 390、total 6365，首段 6104 ms、总耗时 6774 ms，退出 cleanup true。按验收时官方缓存未命中输入 $0.14/百万、输出 $0.28/百万的上界估算，本次约 $0.000946，低于 $0.03；没有自动重试。
- 内外层 `git diff --check`、新增文件尾随空白和高风险 secret pattern 扫描通过；发布/日志/合成数据与临时 Python 环境已清理。外层既有 `.agents/.codex/experiments/phase inputs/archive changes` 等未跟踪内容保持不变且不纳入本阶段。

## 完成摘要

Phase 07 已完成：Windows 客户端、WikiTools 与 Python Agent 通过长驻严格 NDJSON Sidecar 接通，支持单轮流式正文、工具状态、来源、usage、取消、重试与故障隔离；WSL/Windows Python、Windows 219/219、无外网跨语言、两种 `win-x64` 发布和单次 DeepSeek 发布版验收全部通过。源码与治理分仓提交，计划归档，不 push。
