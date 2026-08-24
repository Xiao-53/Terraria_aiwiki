# ADR 0004：首个 Wiki Tool transport 使用受限无状态 loopback MCP

- 状态：accepted；Phase 05 Windows portable solution 验证通过
- 日期：2026-08-24
- 决策范围：首个 C# Wiki Tool Adapter transport、鉴权与生命周期；不外推 Android，不包含 Python/Windows Host 接线
- 关联：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/wiki-tools/spec.md`、`openspec/changes/archive/phase-05-ai-adapter/plan.md`

## 背景

Phase 03 已建立稳定的 `WikiQueryUseCases.SearchAsync` / `ReadAsync`，Phase 05 需要让后续 AI Backend 使用同一查询边界。transport 必须保留完整 Search/Read 语义，不得直接访问 SQLite、复制 Core 或提前决定 Python Runtime/Windows 打包生命周期。

候选包括私有 loopback JSON、独立子进程 stdio 和标准 MCP Streamable HTTP。当前 Adapter 只需由后续 Host 嵌入并向同机 AI Runtime 暴露两项无状态只读工具，不需要 server-to-client requests、资源订阅或 session 状态。

## 决策

1. 首个 Wiki Tool transport 使用官方 `ModelContextProtocol.AspNetCore` 2.2.0，固定版本并隔离在单一 `net10.0` Adapter assembly；升级必须另行验证和决策。
2. 使用 MCP Streamable HTTP 的显式无状态模式，只映射 `/mcp`；只注册 `search_wiki` 与 `read_wiki`，不注册 resources、prompts、OAuth、legacy SSE 或额外工具。
3. Kestrel 精确绑定 IPv4 `127.0.0.1:0`。每次启动生成 256-bit 临时 bearer token，只存内存；Host 必须精确等于实际 `127.0.0.1:<port>`，token 使用固定时间比较，不提供 CORS。
4. server 是可嵌入 runtime，不新增独立产品 Host。Phase 05 不修改 `Terraria_Wiki.Windows`；Windows Client、Python Runtime 与 server 的启动/退出组合留 Phase 07。
5. Adapter 只调用 `WikiQueryUseCases`，Search/Read structured output 忠实映射 Application；不转换或截断 HTML，不生成来源 URL，不记录 history。
6. `WikiToolConsumerCoordinator` 作为 Application consumer port：pause 关闭新调用准入、取消并排空在途调用，lease 释放后恢复；server 未启动时 pause 是安全空操作。
7. 本决策不外推 Android。Android Phase 必须根据设备内 Runtime、网络权限、功耗、打包和目标设备实测独立决定 transport，不能把 ASP.NET/loopback 假设写进共用逻辑。

## 选择理由

- MCP 为后续 Agent 提供标准工具发现、input/output schema、结构化结果、取消和官方 client，不需要维护私有协议与双端解析器。
- 无状态 HTTP 与当前两项只读工具匹配，不引入 session store、长连接通知或额外生命周期状态。
- 动态 loopback 加临时 bearer token 可以在不建立远程服务、证书或 OAuth 的前提下约束本机访问；严格 Host 防止宽松主机名边界。
- 独立 Adapter project 使 transport package 不反向污染 Application/Core，并允许 Android 后续替换 transport。

## 后果

正面后果：

- AI、手动 UI 和 Viewer 继续使用同一份 Application/Core 数据；没有第二套 Wiki 数据或 Search 实现。
- 官方 client 可直接进行工具发现和结构化调用，Phase 06 不需要自定义 C# 协议客户端。
- server 可由测试或后续 Windows Runtime 嵌入；动态端口、token 轮换、pause 和 stop 均可自动回归。
- transport 依赖集中在 Adapter assembly，Core/Application/Windows 公共契约保持不变。

负面后果与约束：

- Windows Runtime 运行期间将存在一个仅 loopback 可达的 HTTP listener，Host/token/端口释放和 SDK schema 必须持续回归。
- 官方 SDK 是新的固定依赖；2.2.0 之后的升级可能改变 schema 或协议行为，不能使用浮动版本。
- 完整 HTML 可能占用较多 Agent context；本阶段不猜测截断规则，Phase 06 必须用真实 Agent 质量证据决定是否新增 Reader 转换边界。
- token 是本机进程间临时凭据，不是远程用户身份体系；不得借此扩大为远程 MCP 服务。

## 被否决方案

- 私有 loopback JSON：可减少一个 package，但需自行定义工具发现、schema、错误、取消和双端兼容；当前官方 MCP 已提供更小且可验证的标准边界。若 SDK 未来强迫修改 Application/Core 或显著扩大结构，可重新评估此回退。
- stdio 作为首个 transport：需要立即确定独立进程 Host、stdio 所有权、崩溃恢复与 Python/Windows Runtime 生命周期，提前侵入 Phase 07；当前嵌入式 loopback 更符合阶段边界。
- 直接在 Windows Avalonia Host 内实现工具：会把 AI transport 与 Presentation/Windows lifecycle 提前耦合，且妨碍 Phase 05 portable 集成测试。
- 直接访问 Core/SQLite：绕过稳定 Application 边界、复制错误/活动数据源语义并破坏消费者协调，违反 P0 护栏。
- 远程监听、TLS 或 OAuth：Phase 05 没有远程服务需求，会扩大攻击面和产品范围。
