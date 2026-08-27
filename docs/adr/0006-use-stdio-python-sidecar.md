# ADR 0006：Windows AI Runtime 使用长驻 stdio Python Sidecar

- 状态：accepted；Windows 无外网、两种 `win-x64` 发布 smoke 与发布版 DeepSeek 真实验收通过
- 日期：2026-08-27
- 决策范围：Windows Python 进程边界、非密钥配置、stdio 协议、生命周期与崩溃恢复；不外推 Android
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-ai-runtime/spec.md`、`openspec/specs/ai-backend/spec.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/changes/archive/phase-07-windows-runtime-smoke-test/plan.md`

## 背景

Phase 04 已建立 Avalonia Windows Host 与 Viewer，Phase 05/06 已建立嵌入式 loopback Wiki Tools 和平台无关 Python Agent。Phase 07 需要把三者接成发布链路，同时保留独立单轮、取消、流式进度、typed answer、来源和稳定错误，并确保 Python/provider 故障不影响离线阅读。

候选包括每次问题启动一次 Python、再增加一个 Python HTTP listener、把 Agent 重写到 .NET，以及由 Windows 拥有一个长驻 stdin/stdout 子进程。还必须决定 Python/provider 配置落点、secret 交接、MCP 生命周期和 Sidecar 崩溃后的恢复方式。

## 决策

1. Windows 为每个启用的 AI Runtime 启动一个长驻 Python Sidecar，使用严格 NDJSON v1 通过 stdin/stdout 通信；不增加第二个 HTTP listener。Wiki Tools 保持 ADR 0004 的动态 loopback MCP。
2. 进程命令固定为 `python.exe -I -u -X utf8 <published-launcher>`。Windows 保存唯一进程句柄；正常 shutdown 超时后只能终止该所属进程树。
3. 协议只公开 run/tool 状态、最终正文增量和 typed completion。stdout 禁止任何非协议输出；thinking、工具参数/返回、HTML、stderr 与底层异常不跨越边界。
4. NDJSON 单行上限 256 KiB，消息使用 protocol version 与 request ID；严格拒绝未知字段、重复 ID、非法顺序、并发 answer、超长/不完整行和 stdout 污染。一次只允许一个活动问题，取消排空后可复用 Sidecar。
5. 非密钥 `ai-runtime.json` v1 只保存绝对 `python.exe` 与显式 OpenAI-compatible provider/model/API/reasoning。API key 只从 `TERRARIA_AI_API_KEY` 子进程环境读取；MCP endpoint/bearer 只经 initialize stdin 传入。
6. Windows 不携带或安装 Python。构建与发布只复制 Agent launcher、源码和解析锁；握手验证 Python 3.12–3.14、Runtime 1 与 PydanticAI 2.31，依赖不匹配只禁用 AI。
7. 启动按 Application → Viewer → WikiToolServer → Sidecar；退出按活动回答 → Sidecar → WikiToolServer → Viewer → Application。Application consumer 使用内部 composite 同时协调 Viewer 与 WikiTools，并逆序释放 pause leases。
8. Sidecar fatal/崩溃后停止对应 MCP server，不无限自动重启。只有显式“重试 AI”创建新 Sidecar 与新 MCP server，因而轮换端口和 bearer。
9. 本决策只适用于 Windows。Android Phase 必须根据设备 Runtime、进程模型、功耗、打包与网络限制独立决定，不能复用 Windows 路径或进程假设。

## 选择理由

- 长驻子进程避免每个问题重复导入 Python/PydanticAI，同时把 Python 崩溃、stdout 和取消边界隔离在 Windows 可拥有的进程中。
- stdio 不新增鉴权、端口、防火墙或第二个 localhost 服务；MCP listener 仍只承担标准 Search/Read 工具协议。
- 严格、小型 NDJSON 协议比把 PydanticAI/Python对象泄漏给客户端更稳定，也可由 fake process 和真实跨语言 fixture 覆盖。
- 配置与 secret 分离允许发布物保持供应商中立，并避免 key 进入命令行或磁盘 JSON。
- 手动恢复阻止永久 crash loop；每次新建 MCP server 自动废弃旧端口/token。

## 后果

正面后果：

- Windows 客户端可流式显示 Search/Read 与正文，同时最终继续使用完整 `WikiAgentAnswer`。
- Python、依赖、provider、协议或模型故障可以单独降级，离线 Search/Viewer 不需要随 AI 关闭。
- 两种 Windows 发布形态共享同一外部 Python 环境和同一 AgentRuntime 内容；不把虚拟环境或安装器提前带入 Phase 07。

负面后果与约束：

- 用户在 Phase 11 产品化之前必须自行准备严格兼容的 Python 环境和环境 key；应用不会自动修复依赖。
- Windows Host 需要维护流式顺序、取消/完成竞态、stderr drain、shutdown timeout 和进程树清理测试。
- stdio v1 是双端兼容契约；Python 与 C# 不能单边更改字段或时序。
- 完整 Wiki HTML 仍会发送给远程模型并可能触发高 token/context 问题；Reader 优化不属于本决策。

## 被否决方案

- 每个问题启动一次 Python：进程边界简单，但重复启动与依赖导入增加首段延迟，取消/连续提问和工具状态更难稳定收口。
- Python 再监听 HTTP：便于常规客户端，但新增端口、鉴权、Host/CORS、防火墙和崩溃清理面；当前只有一个宿主父进程，无需网络边界。
- 把 Python 嵌入 .NET 进程：部署与 ABI 风险更高，崩溃隔离和 Python 3.12–3.14 兼容更差。
- 把 Agent 重写到 .NET：推翻 ADR 0005、复制已验证的 MCP/provider/usage/error 行为，并扩大 Phase。
- 自动下载 Python 或 pip/uv 安装：会在应用启动时修改机器和访问网络，难以提供可重复、可回退的依赖与供应链边界；留 Phase 11 独立决定。
- Sidecar 崩溃后无限重启：容易产生 crash loop、重复成本和旧 MCP 凭据生命周期不清；改为用户显式重试。
