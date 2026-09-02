# Windows AI Runtime 能力契约

- 状态：implemented baseline；无外网、两种发布 smoke 与发布版 DeepSeek 真实验收通过
- 适用范围：Avalonia Windows Host、Wiki Tool lifecycle、Python Sidecar、AI 配置与 Presentation Runtime 边界；不外推 Android
- 来源：Master Architecture v0.6、Phase 07、ADR 0003–0006、AI Backend 与 Wiki Tools 能力契约

## 目的

在不复制 Wiki 数据、不改变 Application/Core 公共契约且不影响离线搜索阅读的前提下，把 Windows Avalonia Client、嵌入式 WikiToolServer 与平台无关 Python Agent 接成可启动、可取消、可流式回答的最小发布链路。

## 配置与依赖

- Windows 本机配置固定为 `ai-runtime.json` v2。默认路径是 `%LOCALAPPDATA%\Terraria AI Wiki\ai-runtime.json`；命令行可用唯一 `--ai-config <absolute-path>` 覆盖。
- 文档字段固定为 `version`、`pythonExecutable`、`apiKey` 和 `provider`。provider 只含 `baseUrl`、`model`、`api` 与可空 `reasoningEffort`；未知字段、版本、空白密钥、相对路径和非法 provider 值必须拒绝。
- `pythonExecutable` 必须是存在的绝对 `python.exe`。首个 Runtime 支持 Python 3.12–3.14，并在 Sidecar 握手中固定验证 `pydantic-ai-slim==2.31.0` 与 Runtime protocol 1。
- 应用不下载、安装、升级或修复 Python 与依赖。发布物只携带 launcher、Agent 包源码、`pyproject.toml` 和 `uv.lock`；Python 本体与虚拟环境由用户在 Phase 11 之前外部准备。
- API key 按用户明确选择保存在 Windows 用户级配置中。Host 读取后只写入自己启动的 Sidecar `TERRARIA_AI_API_KEY` 进程环境；不得进入命令参数、协议 JSON、公开错误、日志、Git 或 smoke 结果。配置文件继承用户目录访问控制，产品化凭据存储留 Phase 11。

配置形状：

```json
{
  "version": 2,
  "pythonExecutable": "C:\\path\\to\\.venv\\Scripts\\python.exe",
  "apiKey": "<local-api-key>",
  "provider": {
    "baseUrl": "https://api.example.com",
    "model": "provider-model",
    "api": "responses",
    "reasoningEffort": null
  }
}
```

## stdio NDJSON v1

- Windows 以 `python.exe -I -u -X utf8 <published-launcher>` 启动唯一所属 Sidecar；不使用 shell，不在参数中传 provider、MCP connection 或 secret。
- Windows 命令固定为 `initialize`、`answer`、`cancel`、`shutdown`；Python 事件固定为 `ready`、`run_started`、`tool_started`、`tool_finished`、`answer_delta`、`completed`、`cancelled`、`failed`、`fatal`。
- 每条消息固定含 `version: 1`、`type` 与 `requestId`。字段集合、类型、枚举、顺序与 request ID 必须严格验证；UTF-8 NDJSON 单行连换行不得超过 256 KiB。
- initialize 是第一条且唯一初始化命令；MCP endpoint 与 bearer 只在该 stdin 消息传入。重复 ID、未知消息/字段、非法顺序、未换行 EOF、超长行、非法 UTF-8 或非协议 stdout 都使当前 Sidecar fatal。
- 一次只允许一个活动 answer。宿主与 Sidecar 都拒绝并发问题为 `runtime_busy`；cancel 必须指向当前活动 ID。取消排空终态后，同一 Sidecar 可接受下一次独立问题。
- `completed.answer` 是完整 `WikiAgentAnswer`。此前所有 `answer_delta` 的顺序拼接必须精确等于 `answer.markdown`；工具 started/finished 必须配对且工具事件不能出现在首个正文增量之后。
- stdout 只允许协议 writer。stderr 被异步、有界读取以避免阻塞，但不直接显示或持久化；工具参数、工具返回、模型 thinking、原始 HTML 与底层异常不得出现在事件中。

## 生命周期与恢复

- 前台启动顺序固定为 Application 初始化 → Viewer enable → Viewer 首页 DOM 就绪；AI 配置读取、WikiToolServer 和 Python Sidecar ready 在 Viewer 导航后进入独立后台启动，不阻塞离线 Reader 可交互。AI 配置或 Runtime 错误不能回滚已经可用的 Application/Viewer。
- Application 使用内部组合 consumer coordinator 同时暂停 Viewer 与 WikiTools；按声明顺序取得 pause lease，成功和失败回滚都按逆序释放。
- 正常退出先统一取消 Host 生命周期；AI 分支执行“取消活动回答并排空 → Sidecar shutdown → 超时后仅终止所属进程树 → WikiToolServer”，同时 Viewer 分支停止 WebView/transport，两个消费者都结束后才释放 Application。
- Sidecar 的活动回答、shutdown 与进程退出共享 1.5 秒优雅期限；期限耗尽后只终止所属进程树，并以 500 ms 期限收尾进程等待和连接释放。后台启动、显式重试和退出由同一内部同步门协调，退出后不得挂接晚到的 Runtime。
- Sidecar fatal、协议错误或意外退出后，停止其关联 WikiToolServer；不无限自动重启。用户显式“重试 AI”时先释放旧 Runtime，再创建新的 MCP server，因此必须轮换端口与 bearer。
- 只有宿主启动并保存句柄的 Python `Process` 可以在 shutdown 超时后使用 `Kill(entireProcessTree: true)`；不得按进程名扫描或终止其他 Python。

## Presentation Runtime 边界与故障隔离

- Phase 08 Presentation 提供单轮问题、发送、停止、重试 AI、Search/Read 工具状态、安全流式 Markdown、可信来源与 usage；具体布局、Markdown allowlist 和共享 Reader 行为由 `openspec/specs/windows-presentation/spec.md` 约束。
- 每次发送前清除上次正文、工具状态、来源与 usage；问题之间不传递、不显示为会话历史，也不持久化。
- 缺少配置、key、Python、锁定依赖或 provider 不可用时，只把 AI 面板置为 Disabled/Failed；离线 Search、Viewer 与已有数据能力继续可操作。
- 向用户显示的 Runtime 错误只来自固定安全 code/message。provider 原始错误、stderr、路径、bearer、HTML、工具参数和响应不进入 UI。

## 验收场景

1. 严格配置测试覆盖默认/覆盖路径、未知字段、非法 Python/provider/key，并确认 key 不进入参数或配置对象字符串表示。
2. fake Sidecar 覆盖 ready 握手、事件顺序、typed completion、调用方取消、完成/取消竞态、复用、fatal、协议污染与所属进程终止策略。
3. 真实 Windows Application + WikiToolServer + Windows Python 测试 Sidecar，以临时数据完成无外网 Search → Read → 至少两个 delta → sources/trace/usage completion。
4. AI 配置缺失时正式 Avalonia WinExe 仍通过 Search/Viewer/assets/返回/退出 smoke。
5. framework-dependent 与 self-contained `win-x64` 发布目录都包含同一 AgentRuntime 文件，并用同一配置 Python 环境通过取消、再次提问、工具进度、流式完成和退出清理。
6. 有有效本机配置 key 时，对明确 provider/model 进行一次不自动重试的发布验收；记录首段、总耗时与 usage，不在日志保存问题、正文或 secret。没有 key 时阶段保持未完成，不把离线 fixture 当作真实 provider 结果。

## 非目标与变更规则

本能力不包含多轮、凭据 UI/Windows Credential Manager、内置 Python、自动安装、Reader/HTML 精简、安装器、签名、WebView2 bootstrap、Android、Vector/Embedding 或数据格式变化。

协议版本、消息字段/顺序、配置 schema、Python/依赖范围、secret 边界、进程终止策略或启动退出顺序变化时必须更新本 spec 与 ADR 0006。Android Runtime 必须另行决定，不能继承 Windows stdio、路径、loopback 或打包假设。
