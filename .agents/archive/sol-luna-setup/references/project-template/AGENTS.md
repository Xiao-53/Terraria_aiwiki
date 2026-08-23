# 本项目多代理政策（Sol 领导 + Luna 执行）

## 角色分工
- **主会话（Sol / gpt-5.6-sol）**：理解需求、任务拆解、冲突解决、最终整合、验收、兜底。始终保留最终决策权和 commit/PR/部署控制权。
- **luna_scout**：只读探索代码库、依赖、日志、文档。
- **luna_worker**：在明确文件范围内实现改动。
- **luna_critic**：对抗性审查正确性、安全、回归风险、测试缺口。
- **luna_tester**：按指定计划运行测试并报告证据。

## 规则
1. 只把边界清晰、可独立验证的任务委派给 Luna。
2. 给工人的 prompt 必须包含：目标、边界、约束、预期输出格式、证据要求。
3. 任何失败、越界、置信度低，立即 escalate 回主 Sol。
4. 并行时避免多个 worker 同时写同一文件。
5. 外部操作（commit、部署、发 PR）永远由 Sol 控制。
6. 默认并发 3–4；需要更高吞吐时再调高 `max_concurrent_threads_per_session`。

## 推荐闭环
Sol 规划 → 并行 spawn Luna（scout/worker/tester）→ Luna critic 审查 → Sol 整合验收 → Sol 提交。
