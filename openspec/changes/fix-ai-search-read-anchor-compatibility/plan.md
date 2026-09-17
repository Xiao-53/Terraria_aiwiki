# 修复 AI Search / Read 章节 Anchor 兼容问题

- 状态：draft；原因调查已完成，修复方案待实施授权。本轮用户要求创建修复 plan，仅更新文档。
- Phase：无；optimize-ai-search-evidence 的独立缺陷修复。
- 执行环境：WSL 读取、编辑和静态检查；Windows 执行 .NET 与本地跨语言验证；仅主会话执行。
- 创建日期：2026-09-16；最后更新：2026-09-17。
- 范围：WikiTools 对同一章节多个真实 Anchor 的识别，不改变搜索或持久数据。
- 关联：[上游计划](../optimize-ai-search-evidence/plan.md)、[原验收](../optimize-ai-search-evidence/evaluation.md)、[Wiki Tools 契约](../../specs/wiki-tools/spec.md)、[AI Backend 契约](../../specs/ai-backend/spec.md)、[路线图](../post-phase-11-roadmap/plan.md)。

## 背景与已确认原因

2026-09-17 按用户要求沿当前源码检查，并只读核对 paid-optimized-five.json。六次失败请求使用的中文 Anchor 全部出现在前序 Search 返回的导航中，不能归因于模型猜测。原付费对照最终答案通过，但补读增加了调用与耗时；原始结果保持不变。

已确认调用链（路径均相对仓库根）：

1. src/Terraria_Wiki.Core/Data/WikiContentProcessor.cs 的 ExtractSearchRegions 对标题使用 FindAnchor；继续遍历内部文字节点时使用 FindNearestAnchor。因此同一章节的编码 id 和中文 id 可以分别成为搜索区域的 Anchor。
2. src/Terraria_Wiki.WikiTools/WikiMcpTools.cs 的 MapSearchResult 从代表区域及 EvidenceRegions 原样投影章节导航，不校验其是否被 formatter 识别。
3. src/Terraria_Wiki.WikiTools/WikiEvidenceFormatter.cs 的 NodeAnchor 只取标题自身或内部第一个 id/name，Section 只保存单个 Anchor。
4. section 模式用 Ordinal 精确匹配这个单值；只有没有 Anchor 时才按章节名称回退。中文 id 真实存在但未被保存，就被判为 anchor_found=false。

典型结构为同一 h2 下先放编码 id 的空 span，再放 id="制作" 的标题文字 span。现有 WikiSearchNavigationTests 使用标题只有一个 id 的简化结构，未覆盖这种多 id 情况。另需注意：page/旧 v1 的 OrderSections 使用归一化匹配，与 section 的精确匹配不同，修复不得顺手统一或改变其旧排序。

## 目标与完成定义

- 同一标题实际存在的编码、中文及其他有效定位标记均能精确选中同一章节；正文块、父条件与子章节范围一致。
- 不存在的标记仍返回空目标证据和目录；有真实标记时不把显示名称自动视为新别名，不增加模糊匹配。
- 目录仍为每节一个首选 Anchor，工具 JSON、字符预算、来源字段和分页绑定不变。
- Windows 定向回归与本地跨语言验证通过；原六次失败请求在固定隔离快照上直接读取成功，记录内容范围核对结果。
- 完成后回填上游与路线图，并按计划规范归档。未做模型对照时不得宣称 Token、延迟或回答质量改善。

## 前置条件与授权

本轮仅编写计划，不实施修复、不运行产品验证。用户明确要求实施后将状态改为 approved / in-progress；先检查工作树，为本次相关文件建立可恢复 Git 保存点，不夹带既有无关修改。

使用既有忽略目录 artifacts/ai-search-evidence-evaluation/2026-09-16/ 的隔离快照、manifest 与原始调用。实施时核对快照身份及文件可用性。若制品缺失，不以合成测试冒充真实六次复验，记录阻塞；不得自动改用活动数据库。

原十轮付费授权已结束。本计划的验证不调用 DeepSeek 或其他真实模型 API。

## 拟采用方案与兼容性

### 局部修改 formatter

- 在清理不安全/非正文节点后的标题结构上，收集标题自身及属于该标题的后代节点上的非空 id；保留既有 name 定位标记兼容能力，不收集正文、相邻标题或已移除的隐藏/编辑节点。
- 内部新增真实 Anchor 集合，按 Ordinal 去重；不得通过显示标题、URL 解码、大小写折叠或字符串相似度生成新别名。
- 保留现有 NodeAnchor 选择优先级作为首选 Anchor；目录仍输出该单值，不把全部别名暴露给模型。
- v2 精确匹配使用集合成员判断，scope.anchor_found 与 selected_section 使用同一个匹配结果。没有有效真实标记时保留现有精确章节名称回退。
- Section 的父子关系、证据块生成、预算、去重和清理保持原实现。旧 v1/page 的排序仍使用原首选 Anchor/名称规则，不把新别名集合顺带接入归一化排序。
- 来源仍保留实际请求解析出的 Anchor；不修改 Python 来源去重策略，不增加协议字段。
- continuation 仍绑定原 requested title、resolved title、原请求 Anchor、focus、mode 和内容版本。同一章节的两个别名不意味着可以交叉使用 token；换别名必须重新读取。
- 不同章节若含重复真实 id，沿用现有文档顺序首个精确命中的行为，不合并章节或扩大证据范围，并用测试固定行为。

### 方案取舍与范围

采用内部多标记识别，因为能直接接住现有 Search 导航，也能覆盖 Redirect 指向次级真实标记的情况。暂不采用改 Search 强制返回首选标记（需重新定位标题或涉及索引）、按显示名称模糊匹配（可能读错重名章节）、对外增加别名字段（增加载荷及协议成本）。

非目标：Core 加工/区域索引、数据库格式与迁移、排序、同步、Viewer、AI 提示词、会话格式、缓存、依赖、工具数量、运行限位、Android 和发布。不修复任意正文元素 Anchor 的章节映射；若调查发现完成此缺陷必须扩大边界，应记录证据并重新确认范围。

## 文件变更（实施候选）

- MODIFY src/Terraria_Wiki.WikiTools/WikiEvidenceFormatter.cs：内部标记集合、精确匹配；保留首选目录与旧排序。
- MODIFY tests/Terraria_Wiki.WikiTools.Tests/WikiEvidenceFormatterTests.cs：多 id/name、负例、范围与续读回归。
- MODIFY tests/Terraria_Wiki.WikiTools.Tests/WikiSearchNavigationTests.cs：真实标题结构的 Core 加工 → Search → section Read 集成用例，含 Redirect。
- MODIFY candidate tests/Terraria_Wiki.WikiTools.TestHost/WikiToolTestHost.cs 与 agent/tests/test_cross_language.py：仅补真实 MCP 多标记解析/来源的最小 fixture 与断言，复用现有宿主。
- MODIFY openspec/specs/wiki-tools/spec.md：实施时明确精确接受同一章节真实标记及单首选目录，不改变 JSON。
- MODIFY 本计划、上游计划与路线图：只更新实际进度、证据和依赖状态。
- KEEP Core、Application、Python 产品源码、WikiToolContracts、依赖、数据库及原始付费验收附件。

## 执行步骤

- [x] 建立独立问题记录并链接原始验收。
- [x] 按用户要求核对当前调用链和六次失败记录，确认原因，形成局部修复草案。
- [ ] 获得实施授权、检查工作树并建立相关文件恢复点。
- [ ] 先在现有 formatter / 导航测试中加入多 id 真实结构，确认修复前因次级标记不命中而失败。
- [ ] 实施内部集合匹配，保持首选目录、范围和续读绑定；同步持续契约。
- [ ] Windows 执行受影响测试、构建测试宿主，运行定向 Python 跨语言测试。
- [ ] 用固定快照复查原六次失败读取，记录 Anchor、选中范围与证据结果；不重跑模型问答。
- [ ] 检查 diff、临时制品和测试证据；完成后回填上游、路线图并归档。

## 最小验证与回归

### Windows 自动化

1. 同一标题含编码 id + 中文 id；标题自身和内部节点均有 id；现有 name 标记；重复标记去重。各有效标记返回一致证据范围，目录仍保留原首选项。
2. 不存在 id、仅显示名称、大小写不同、其他章节标记、清理掉的隐藏/编辑节点不得误中目标；无 id/name 的旧名称回退仍有效。重名章节不按文字串联，重复 id 不合并范围。
3. 保留根导语、父条件和子章节，排除无关兄弟章节；沿用现有 formatter 范围、清理、表格和预算用例。
4. 对每个别名单独续读无重复遗漏；交换别名、修改 focus/mode/title 或内容版本后旧 token 被拒绝。旧 v1/page 排序和目录预算保持有效。
5. 真实加工器生成多标记导航；中文与编码导航都直接读成功。覆盖 Redirect 的目标 Anchor 及显式 Anchor 优先语义，来源身份不被改写。
6. 用真实 Windows TestHost 验证 Python 严格解析、导航与成功 Read 来源；使用离线模型/直接 MCP，不发送真实 provider 请求。

先运行 WikiEvidenceFormatterTests、WikiSearchNavigationTests 及受影响契约/宿主测试；仅在出现跨模块风险时扩大范围。C# 测试与 TestHost 在 Windows Release 构建；Python 若有修改，只运行受影响 lint、format、类型与定向测试。无客户端启动或发布变化，不默认运行 Avalonia GUI smoke、全量产品测试或安装测试。

### 固定快照复验

使用现有 Windows WikiToolTestHost --snapshot 入口，通过本地 MCP 按原 title/focus/mode 复查六次请求：Q02 木剑#制作；Q05 天顶剑#制作；Q06 猪龙鱼公爵#召唤、Guide:猪龙鱼公爵攻略#生成条件；Q10 木剑#制作、木剑#配方。重复标题保留不同问题 focus，不能只计五个唯一目标。

每次要求 anchor_found=true、正确 selected_section、非空目标证据，并核对与该章节编码标记读取的正文/范围一致；有 continuation 时按原绑定读取。可定向复用原 Search 参数检查导航链，不扫描全库、不重建索引、不修改活动数据库。结果保存于既有忽略的验收目录，计划记录命令、快照身份和实际结论；保持旧付费记录不变。

## 风险与回退

主要风险是错误扩大标记归属、改变首选目录、把别名视为同一个 continuation 请求或放宽模糊匹配。通过只收集标题结构、保留首选字段、精确匹配及负例覆盖控制。

内部标记集合只在一次 HTML 读取内存在，无持久缓存或迁移；不得遍历整页来为每个标题重复收集标记。实施时检查循环范围避免非必要性能退化。

回退仅撤销本次 formatter、测试和契约增量，保留既有 AI 导航、关键词提示词与用户工作树修改；不回滚数据库、原验收或整份包含其他改动的文件。

## 进度与验证结果

- 2026-09-16：按用户要求建立独立 draft，暂不调查或实施。
- 2026-09-17：用户随后要求查找原因；已只读核对 Core / WikiTools、现有测试及六次原始失败调用。源码确认单值丢失真实别名；尚未运行修复前后回归。
- 2026-09-17：用户要求创建修复 plan，扩充本既有计划，不另建重复 change。仅文档规划；产品实现、Windows 测试、真实快照复验和付费模型复验均未在本轮执行。

## 完成摘要

修复草案已形成，尚未实施，缺陷仍未关闭。下一步为按本计划实施并取得离线、Windows 跨语言及固定快照证据；原 AI 优化计划保持未完成，不提前归档。
