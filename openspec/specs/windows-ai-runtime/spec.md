# Windows AI Runtime 能力契约

- 状态：implemented；Phase 11 内置 Runtime、Credential Manager 与 v3 迁移已通过 Windows 回归；2026-09-05 已升级 NDJSON v2/受限历史
- 适用范围：Avalonia Windows Host、Wiki Tool lifecycle、内置 Python Sidecar、AI 配置与 Presentation Runtime 边界；不外推 Android
- 来源：Master Architecture v0.6、Phase 07/11、ADR 0003–0006、AI Backend 与 Wiki Tools 能力契约

## 目的

在不复制 Wiki 数据、不改变 Application/Core 公共契约且不影响离线搜索阅读的前提下，把 Windows Avalonia Client、嵌入式 WikiToolServer 与平台无关 Python Agent 接成可启动、可取消、可流式回答的最小发布链路。

## 配置与依赖

- Windows 本机配置固定为严格 `ai-runtime.json` v3。默认路径是 `%LOCALAPPDATA%\Terraria AI Wiki\ai-runtime.json`；命令行可用唯一 `--ai-config <absolute-path>` 覆盖。
- v3 文档字段固定为 `version` 与 `provider`。provider 只含 `baseUrl`、`model`、`api` 与可空 `reasoningEffort`；未知字段、版本和非法 provider 值必须拒绝。Python 路径和 API key 不进入 v3。
- Windows 设置页可以读取并显式保存同一 v3 文件。当前 UI 只提供 `DeepSeek 官方 API` 与 `自定义 OpenAI-compatible` 两种选择：DeepSeek 预设固定 `https://api.deepseek.com` 与 Responses API，并提供 `deepseek-v4-flash` / `deepseek-v4-pro`；自定义项要求用户显式填写 endpoint、model 与 Responses / Chat Completions。provider 类型是 UI 预设，不写入 schema；不能严格识别为该官方组合的既有配置一律作为自定义项显示。
- 思考强度 UI 固定为服务默认、关闭、低、高、最大，分别保存为 `null`、`none`、`low`、`high`、`max`。自定义兼容服务可以忽略或拒绝该参数，必须按 endpoint 分别验证，不能因 UI 可选择而宣称通用支持。
- Host 只从安装目录 `AgentRuntime\python\python.exe` 定位内置 CPython。Phase 11 固定 Python 3.14.7 x64 embeddable、官方 URL/SHA-256、uv 0.12.9 与 `uv.lock` 生产依赖，并在 Sidecar 握手中验证 `pydantic-ai-slim==2.31.0` 与 Runtime protocol 2。运行期不下载、不调用 pip/uv，也不允许用户改用外部 Python。
- API key 只保存为当前 Windows 用户 generic Credential，固定 target 为 `TerrariaAIWiki.Windows/AI/APIKey`，UTF-8 blob 不超过 Credential Manager 的 2560-byte 上限。设置页不读取或回显已有真实值与长度，以固定遮蔽标记明确表示凭据已保存：空底层输入保留、新输入替换，显式清除会删除凭据并停用 AI，但保留 provider 配置。
- v2 首次读取执行可重试迁移：先严格验证 provider/key，再写 Credential Manager，最后 durable 写入并原子替换 v3；任一步失败都保留旧文件并恢复先前凭据。Host 只把读取到的 key 写入所属 Sidecar 的 `TERRARIA_AI_API_KEY` 进程环境；key 不进入参数、NDJSON、UI、诊断、Git 或 smoke 结果。

配置形状：

```json
{
  "version": 3,
  "provider": {
    "baseUrl": "https://api.example.com",
    "model": "provider-model",
    "api": "responses",
    "reasoningEffort": null
  }
}
```

## stdio NDJSON v2

- Windows 以内置 `python.exe -I -B -u -X utf8 <published-launcher>` 启动唯一所属 Sidecar；`-B` 禁止安装目录产生 bytecode，不使用 shell，不在参数中传 provider、MCP connection 或 secret。
- Windows 命令固定为 `initialize`、`answer`、`cancel`、`shutdown`；Python 事件固定为 `ready`、`run_started`、`tool_started`、`tool_finished`、`answer_delta`、`completed`、`cancelled`、`failed`、`fatal`。
- 每条消息固定含 `version: 2`、`type` 与 `requestId`。字段集合、类型、枚举、顺序与 request ID 必须严格验证；UTF-8 NDJSON 单行连换行不得超过 256 KiB。v1/v2 二进制不得混用。
- initialize 是第一条且唯一初始化命令；MCP endpoint 与 bearer 只在该 stdin 消息传入。重复 ID、未知消息/字段、非法顺序、未换行 EOF、超长行、非法 UTF-8 或非协议 stdout 都使当前 Sidecar fatal。
- 一次只允许一个活动 answer。宿主与 Sidecar 都拒绝并发问题为 `runtime_busy`；cancel 必须指向当前活动 ID。取消排空终态后，同一 Sidecar 可接受下一次独立问题。
- `answer` 固定包含当前 `question` 与 `history`；history 最多 4 项，每项只能含非空 `user` / `assistant` 纯文本。Host 从当前会话最近的已回答/证据不足终态构造，不发送失败、取消、pending、来源、工具轨迹、HTML、thinking 或原始 provider 消息。
- `completed.answer` 是完整 `WikiAgentAnswer`。此前所有 `answer_delta` 的顺序拼接必须精确等于 `answer.markdown`；工具 started/finished 必须配对且工具事件不能出现在首个正文增量之后。
- `completed.answer.usage` 包含 `cached_input_tokens`，且必须在 `0..input_tokens`；零表示未命中或 provider 不提供，不推算账单节省。
- stdout 只允许协议 writer。stderr 被异步、有界读取以避免阻塞，但不直接显示或持久化；工具参数、工具返回、模型 thinking、原始 HTML 与底层异常不得出现在事件中。

## 生命周期与恢复

- 前台启动顺序固定为 Application 初始化 → Viewer enable → Viewer 首页 DOM 就绪；AI 配置读取、WikiToolServer 和 Python Sidecar ready 在 Viewer 导航后进入独立后台启动，不阻塞离线 Reader 可交互。AI 配置或 Runtime 错误不能回滚已经可用的 Application/Viewer。
- Application 使用内部组合 consumer coordinator 同时暂停 Viewer 与 WikiTools；按声明顺序取得 pause lease，成功和失败回滚都按逆序释放。
- 正常退出先统一取消 Host 生命周期；AI 分支执行“取消活动回答并排空 → Sidecar shutdown → 超时后仅终止所属进程树 → WikiToolServer”，同时 Viewer 分支停止 WebView/transport，两个消费者都结束后才释放 Application。
- Sidecar 的活动回答、shutdown 与进程退出共享 1.5 秒优雅期限；期限耗尽后只终止所属进程树，并以 500 ms 期限收尾进程等待和连接释放。后台启动、显式重试和退出由同一内部同步门协调，退出后不得挂接晚到的 Runtime。
- Sidecar fatal、协议错误或意外退出后，停止其关联 WikiToolServer；不无限自动重启。用户显式“重试 AI”时先释放旧 Runtime，再创建新的 MCP server，因此必须轮换端口与 bearer。
- 设置页保存先统一验证 Credential Manager 与 v3 durable 临时文件，再原子替换配置并调用同一个显式重试入口。验证或写入失败保留旧文件/凭据与当前 Runtime；写入成功但重启失败时保留新配置并如实显示 AI 尚未就绪，不把持久化成功误报为失败。
- AI 配置页面和 Credential Manager 操作采用窗口生命周期，不依赖 Wiki 数据根已经连接；无有效数据根时仍可输入、替换或清除 key 并保存 provider。此时不启动 Sidecar，只显示选择有效数据根后再启动的安全状态。
- 只有宿主启动并保存句柄的 Python `Process` 可以在 shutdown 超时后使用 `Kill(entireProcessTree: true)`；不得按进程名扫描或终止其他 Python。

## Presentation Runtime 边界与故障隔离

- Presentation 提供有界多轮问题、发送、停止、重试 AI、Search/Read 工具状态、安全流式 Markdown、可信来源与 usage；具体布局、会话持久化、Markdown allowlist 和共享 Reader 行为由 `openspec/specs/windows-presentation/spec.md` 约束。
- 每次发送只携带 Host 选择的最近 4 轮纯文本；本地完整 transcript 与当前运行的状态由 Presentation/Host 管理，Sidecar 不持久化会话。
- 缺少配置、key、Python、锁定依赖或 provider 不可用时，只把 AI 面板置为 Disabled/Failed；离线 Search、Viewer 与已有数据能力继续可操作。
- 向用户显示的 Runtime 错误只来自固定安全 code/message。provider 原始错误、stderr、路径、bearer、HTML、工具参数和响应不进入 UI。

## 验收场景

1. 严格配置测试覆盖默认/覆盖路径、v2→v3、未知字段、非法 provider/key、凭据 preserve/replace/clear/rollback，并确认 key 不进入参数、v3 或对象字符串表示。
2. fake Sidecar 覆盖 ready 握手、事件顺序、typed completion、调用方取消、完成/取消竞态、复用、fatal、协议污染与所属进程终止策略。
3. 真实 Windows Application + WikiToolServer + Windows Python 测试 Sidecar，以临时数据完成无外网 Search → Read → 至少两个 delta → sources/trace/usage completion。
4. AI 配置缺失时正式 Avalonia WinExe 仍通过 Search/Viewer/assets/返回/退出 smoke。
5. Setup 与 portable 的同一 self-contained `win-x64` payload 都包含固定 Agent 源码和内置 Python；无系统 Python/uv/pip 时通过取消、再次提问、工具进度、流式完成和退出清理，运行后安装目录清单不变。
6. 只有用户另行授权真实 provider 请求并提供有效本机 key 时，才对明确 provider/model 做一次不自动重试的发行候选补充验收；记录首段、总耗时与 usage，不在日志保存问题、正文或 secret。没有授权或 key 时以真实内置 Python + fake provider 完成可重复的产品化门槛，并明确不宣称该候选已重新实测在线模型。
7. 设置页覆盖缺失配置的 DeepSeek 默认草稿、既有自定义配置载入、两种 provider 切换、思考强度映射、key 遮蔽、非法值不覆盖旧文件，以及保存后显式重启的成功/失败状态。

## 非目标与变更规则

本能力不包含会话持久化实现、运行期 Python/依赖安装、Provider/模型在线发现、API key 联网校验、Reader HTML 精简、商业签名、Android、Vector/Embedding 或 Wiki 数据格式变化。

协议版本、消息字段/顺序、配置 schema、Python/依赖范围、secret 边界、进程终止策略或启动退出顺序变化时必须更新本 spec 与 ADR 0006。Android Runtime 必须另行决定，不能继承 Windows stdio、路径、loopback 或打包假设。
