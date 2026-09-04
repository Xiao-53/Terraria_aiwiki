# ADR 0006：Windows AI Runtime 使用长驻 stdio Python Sidecar

- 状态：accepted；2026-09-04 Phase 11 修订为内置 Python 与 Credential Manager
- 日期：2026-08-27；最后修订 2026-09-04
- 决策范围：Windows Python 进程边界、非密钥配置、stdio 协议、生命周期与崩溃恢复；不外推 Android
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/changes/archive/phase-07-windows-runtime-smoke-test/plan.md`

## 背景

Phase 04 已建立 Avalonia Windows Host 与 Viewer，Phase 05/06 已建立嵌入式 loopback Wiki Tools 和平台无关 Python Agent。Phase 07 需要把三者接成发布链路，同时保留独立单轮、取消、流式进度、typed answer、来源和稳定错误，并确保 Python/provider 故障不影响离线阅读。

候选包括每次问题启动一次 Python、再增加一个 Python HTTP listener、把 Agent 重写到 .NET，以及由 Windows 拥有一个长驻 stdin/stdout 子进程。还必须决定 Python/provider 配置落点、secret 交接、MCP 生命周期和 Sidecar 崩溃后的恢复方式。

## 决策

1. Windows 为每个启用的 AI Runtime 启动一个长驻 Python Sidecar，使用严格 NDJSON v1 通过 stdin/stdout 通信；不增加第二个 HTTP listener。Wiki Tools 保持 ADR 0004 的动态 loopback MCP。
2. 进程命令固定为安装目录中的 `AgentRuntime\python\python.exe -I -B -u -X utf8 <published-launcher>`。`-B` 禁止写 bytecode；Windows 保存唯一进程句柄，正常 shutdown 超时后只能终止该所属进程树。
3. 协议只公开 run/tool 状态、最终正文增量和 typed completion。stdout 禁止任何非协议输出；thinking、工具参数/返回、HTML、stderr 与底层异常不跨越边界。
4. NDJSON 单行上限 256 KiB，消息使用 protocol version 与 request ID；严格拒绝未知字段、重复 ID、非法顺序、并发 answer、超长/不完整行和 stdout 污染。一次只允许一个活动问题，取消排空后可复用 Sidecar。
5. Windows 用户级 `ai-runtime.json` v3 只保存显式 OpenAI-compatible provider/model/API/reasoning。API key 使用当前用户 Windows generic Credential `TerrariaAIWiki.Windows/AI/APIKey`；UI 不回显已有值，并支持保留、替换和清除。v2 按 credential→durable v3 的顺序迁移，失败恢复旧凭据且不替换 v2。Host 读取后只写入所属 Sidecar 的 `TERRARIA_AI_API_KEY` 进程环境；key 不进入命令参数或 NDJSON。MCP endpoint/bearer 仍只经 initialize stdin 传入。
6. Windows release 携带固定 CPython 3.14.7 x64 embeddable Runtime、`uv.lock` 的生产依赖、Agent launcher 和源码。构建验证官方 ZIP SHA-256 与 uv 0.12.9，移除 pip/uv/tests/cache；运行期不安装或更新依赖。握手继续验证 Runtime 1 与 PydanticAI 2.31，依赖损坏只禁用 AI。
7. 前台启动按 Application → Viewer 首页就绪；AI 配置、WikiToolServer 与 Sidecar 在 Viewer 导航后独立后台启动。退出先统一取消，AI 分支按活动回答 → Sidecar → WikiToolServer 收尾并与 Viewer 停止并行，两个消费者都停止后才释放 Application。Application consumer 使用内部 composite 同时协调 Viewer 与 WikiTools，并逆序释放 pause leases。
8. Sidecar 的活动回答排空、shutdown 和正常进程退出共享 1.5 秒优雅期限；超时后只终止当前 Host 持有的进程树，进程等待与连接释放分别最多再等待 500 ms。AI 启动、重试和退出用同一内部同步门阻止晚到 Runtime 挂接。
9. Sidecar fatal/崩溃后停止对应 MCP server，不无限自动重启。只有显式“重试 AI”创建新 Sidecar 与新 MCP server，因而轮换端口和 bearer。
10. 本决策只适用于 Windows。Android Phase 必须根据设备 Runtime、进程模型、功耗、打包与网络限制独立决定，不能复用 Windows 路径或进程假设。

## 选择理由

- 长驻子进程避免每个问题重复导入 Python/PydanticAI，同时把 Python 崩溃、stdout 和取消边界隔离在 Windows 可拥有的进程中。
- stdio 不新增鉴权、端口、防火墙或第二个 localhost 服务；MCP listener 仍只承担标准 Search/Read 工具协议。
- 严格、小型 NDJSON 协议比把 PydanticAI/Python对象泄漏给客户端更稳定，也可由 fake process 和真实跨语言 fixture 覆盖。
- provider 与 key 仍不进入发布物；用户可通过设置页编辑同一本机 JSON，同时 Host 保持 key 不进入命令行、协议、错误和日志。DeepSeek 只是受控 UI 预设，不改变 ADR 0005 的 Agent provider-neutral 边界。
- 手动恢复阻止永久 crash loop；每次新建 MCP server 自动废弃旧端口/token。

## 后果

正面后果：

- Windows 客户端可流式显示 Search/Read 与正文，同时最终继续使用完整 `WikiAgentAnswer`。
- Python、依赖、provider、协议或模型故障可以单独降级，离线 Search/Viewer 不需要随 AI 关闭。
- Setup 与 portable 共享同一封闭 AgentRuntime，不要求终端安装 Python、pip、uv 或开发工具。

负面后果与约束：

- 内置 Python/native wheels/CA bundle 必须随完整应用版本更新；安装目录损坏通过可信 Setup repair 或完整更新恢复，不进行局部在线修复。
- v3 回退到旧版不会把 Credential Manager key 重新写回 v2；需要降级时用户必须重新配置 AI，但离线 Wiki 数据不受影响。
- Windows Host 需要维护流式顺序、取消/完成竞态、stderr drain、shutdown timeout 和进程树清理测试。
- stdio v1 是双端兼容契约；Python 与 C# 不能单边更改字段或时序。
- 完整 Wiki HTML 仍会发送给远程模型并可能触发高 token/context 问题；Reader 优化不属于本决策。

## 被否决方案

- 每个问题启动一次 Python：进程边界简单，但重复启动与依赖导入增加首段延迟，取消/连续提问和工具状态更难稳定收口。
- Python 再监听 HTTP：便于常规客户端，但新增端口、鉴权、Host/CORS、防火墙和崩溃清理面；当前只有一个宿主父进程，无需网络边界。
- 把 Python 嵌入 .NET 进程：部署与 ABI 风险更高，崩溃隔离和 Python 3.12–3.14 兼容更差。
- 把 Agent 重写到 .NET：推翻 ADR 0005、复制已验证的 MCP/provider/usage/error 行为，并扩大 Phase。
- 运行期下载 Python 或调用 pip/uv：会在启动时修改机器和访问网络，无法提供可重复、可回退的供应链边界；改为构建期从固定官方输入生成完整 Runtime。
- Sidecar 崩溃后无限重启：容易产生 crash loop、重复成本和旧 MCP 凭据生命周期不清；改为用户显式重试。
