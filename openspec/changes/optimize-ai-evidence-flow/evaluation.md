# 证据流程验收记录

本记录属于本 change 的评测附件，不是独立计划。源码基线为 `09a4cf256f9e6f9f63c544b6a9c484ffdff7a58f`，实现分支为 `optimize-ai-evidence-flow`。所有已执行用例只使用合成内容、离线模型或内存 HTTP transport；未调用真实 provider。

## 固定载荷对照

`agent/tests/test_evidence_efficiency.py::test_fixed_sequence_payload_measurement` 固定 50 个 Search 候选、相同中文配方正文、两次相同 Read、完整四轮问答（问题0/回答0 至 问题3/回答3）。JSON 使用 UTF-8、非 ASCII 转义关闭、紧凑分隔符；统计单位是字节，不是 Token。

| 构成 | 基线字节 | 实现字节 |
|---|---:|---:|
| 两个 fake 工具 description（含原输出 schema） | 2641 | 47 |
| 50 个 Search 候选 | 5693 | 4893 |
| 第一次 Read | 6809 | 6507 |
| 第二次同版本 Read | 6809 | 179 |
| 最后请求携带的工具返回合计 | 19311 | 11579 |

这些是相同合成序列的结构对照。description 使用 fake 的输入说明，不能外推为生产 C# 说明字节；生产工具说明包含新增 section/continuation 规则。真实 C# MCP 以及 Responses/Chat 两种序列化路径另外通过非付费协议测试，验证不再追加输出 schema、完整来源仍由宿主派生、旧正文保留在历史、重复正文变为引用、最终 delta 等于完成正文。

实现的四次请求工具返回字节分别为0、4893、11400、11579；相同基线序列为0、5693、12502、19311。每请求相同四轮历史纯文本56字节；实现 input schema 为385字节，系统说明3295字节（新增证据覆盖提示，不能假装该项没有增加）。这些构成统计不包括供应商内部 tokenization、隐式模板、thinking 或缓存费用，不提供估算 Token。

Search 字段下降只来自 highlights，不改变候选或摘要。第一次 Read 精简宿主字段；第二次 Read 仍真实执行工具、严格解析、计入运行限位，只有模型投影省略重叠正文。4 轮历史文本及顺序逐请求断言一致；Windows 原 BuildHistory 及其 24000 字符/段落截断测试原样运行。

## 质量 Oracle 与可证明范围

| 固定问题/场景 | 必须支持的事实或行为 | 不得得出的结论 | 已执行证据 |
|---|---|---|---|
| 月镐配方及两种材料来源 | 月铸镐；12 夜银锭；3 棱镜核心；远古操纵机；夜银锭由4月光矿在精金熔炉制作；核心来自地下圣域棱镜守卫 | 混淆数量、制作站或来源页 | 原真实 C# 多跳 fixture；两个协议的新定向/重复读取测试 |
| 专家模式合成配方 | 专家模式、击败月亮领主后、12夜银锭、3棱镜核心、远古操纵机 | 将普通模式的“不能制作”解释成专家模式结论；省略前置 | C# section 测试及真实 MCP parser 测试 |
| 长页首/中/末目标章节 | 真实 id 与显示标题不同仍可选择；父章节、根导语、子章节完整 | 将无关兄弟章节当作所选配方；把 section 完整当全页完整 | 三位置 formatter 用例 |
| 全部新增物品 | 分页收集物品00–19，每项唯一序号恰好一次 | 第一页找到几项就宣称列全；目录64项等于全页只有64节 | formatter 分页和真实 MCP continuation 用例 |
| 复杂表格和图标 | 星云法杖：专家模式数量1、概率0.5%；月光法典：数量2、概率5%；嵌套配方材料夜银锭12 | 合并 0.5% 与 05%；丢失图标物品名或否定词；恢复隐藏内容 | 多级表头、rowspan/colspan、嵌套表格、alt、精确文本 fixture |
| 缺页、错误 anchor、历史含旧事实 | 空证据不生成来源；目录可供重选；当前轮重新 Search/Read | 没读到目标却 answered；用旧历史替代当前证据 | 缺页/缺 anchor、固定不足正文、四轮历史回归 |
| 直接 Read 或并行 Search/Read | Read 不执行；原一次重试可纠正为前步 Search、后步 Read | 将尚未完成或同批 Search 当作前置；预先流出无依据正文 | 两种 Search 时序、恢复和流式断言 |
| 重复/重叠、版本或条件变化 | 先前完整块仍在模型输入才引用；新增块照常；目录引用不丢 continuation | 循环引用、悬空引用、版本混用、跳过实际 MCP 或预算 | 串行/并行、版本/时间/章节/精确文本变化、上下文移除、预算回归 |
| 提示注入、URL、已读无关页 | 不可信内容规则、远程链接清理、宿主来源约束保持 | 把程序顺序或存在来源当作语义正确性证明 | 既有安全/流式回归；自然语言事实充分性仍需人工/provider 评测 |

离线 scripted 答案是预置文本，不能证明模型理解或答案质量提升。formatter 用例检查事实是否保留，流程用例检查顺序/结构，协议用例检查发送载荷与输出一致性，三者不等同于真实模型评分。

## 真实 provider 对照（待执行）

计划明确要求用户自行执行，或另行授权有限付费调用。当前未获得该授权，不读取产品密钥、不发付费请求，也不把已有 Phase 06 付费 opt-in 当作本 change 授权。

执行时固定 provider、模型、思考强度、配置、同一本地数据快照、同一问题及完全相同的最近4轮历史；在基线提交和本分支分别运行，每题至少3次。题集覆盖上表，并在真实数据中加入简单属性、跨页比较、跨版本冲突、仅部分支持和不存在证据问题。真实问题的预期事实必须先由同一快照人工核对，不能用模型自己的答案评分。

各轮只记录脱敏 scenario ID、提交、状态、请求/工具次数、input/cached input/output/total、延迟、应支持事实总数/正确数、列表应列/实际正确/遗漏数、无依据结论数、错误版本数、误拒答。provider 未提供 cached 或费用字段时标为不可用，不用字符除法或推算账单补全。原始问答和资料不进入生产日志或提交。

目标：总体真实 input 下降、事实正确率和覆盖不退化、无依据结论/版本混用/误拒答不增加，并报告各题波动和延迟；复杂题允许为补足证据增加工具调用。对照结果尚为空，不能宣称 Token、节费或最终答案质量目标完成。

## 可复现非付费验证（Windows PowerShell，仓库根）

```powershell
dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo
dotnet test Terraria_Wiki.Core.slnx -c Release --no-build --no-restore --nologo
Set-Location agent
.\.venv\Scripts\python.exe -B -m ruff check .
.\.venv\Scripts\python.exe -B -m ruff format --check .
.\.venv\Scripts\python.exe -B -m mypy
.\.venv\Scripts\python.exe -B -m pytest -q -m 'not paid_provider and not cross_language'
$env:TERRARIA_RUN_CROSS_LANGUAGE = '1'
.\.venv\Scripts\python.exe -B -m pytest tests/test_cross_language.py tests/test_evidence_wire.py -q -m 'not paid_provider'
```

构建和真实 TestHost 测试应顺序执行，避免 Windows DLL 文件锁。测试完成后 TestHost 自行关闭进程和删除其随机临时数据库；不触碰用户数据库、配置、会话或安装包。
