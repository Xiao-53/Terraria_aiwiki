# 搜索结果按页合并与标题候选排序

- 状态：completed（源码、自动化验证、真实语料与 Windows 窗口人工验收完成）
- Phase：无
- 执行环境：当前 Windows Codex 会话；WSL 启动返回 E_ACCESSDENIED，使用 Windows 工具实施与 .NET 验证
- 创建日期：2026-09-14
- 最后更新：2026-09-14
- 范围：Core Search 查询排序、按页聚合、摘要选择、回归测试和契约
- 关联：docs/architecture/ARCHITECTURE-v0.6.md；openspec/project.md；openspec/specs/wiki-query-boundary/spec.md；docs/adr/0012-use-v2-region-indexed-wiki-database.md

## 背景与当前证据

用户搜索“雪人”出现多个同名结果，期望看到“雪人炮”等候选。当前 RegionSearch 按规范页与证据文本去重，身份命中使同页全部区域靠前，TitlePrefix/TitleContains 未参与排序。Application 与 Windows 原样显示结果。真实运行版本和活动库结果尚未复现。旧 enhance-local-wiki-search-ranking 已否决，不作为实施授权。

## 目标与完成定义

每规范页一个结果，先合并后应用上限；单概念搜索按精确身份、标题前缀、标题包含、正文候选排序；优先可读正文摘要。同页其他命中保存在 EvidenceRegions 中，点击仍定位一个真实代表区域。多概念四级匹配、Alias、Redirect、取消和高亮继续有效。

## 前置条件

用户已明确授权按上述方向优化。保存点 codex/search-results-baseline-20260914 指向 d2a29707ff6864fd67e21ccfa68f31a948f7dee9。现有 .codex/config.toml、post-phase-11-roadmap 和三个未跟踪 change 均不纳入本次修改。

## 范围与非目标

只改查询与相关契约，不改页面布局、数据库格式、索引、下载、依赖、AI 协议或发布包，不写活动数据库。

## P0 / 兼容性影响

改变搜索结果聚合粒度；DTO 不变，完整区域证据仍可用，Viewer HTML 与 Anchor 不变。手动 UI 和 WikiTools 使用同一顺序。更新原先允许同页多个结果的契约与测试。

## 方案与待决问题

在现有候选集内选每页最佳代表区域，合并证据集合。单概念搜索显式比较 MatchKind，正文段落优先于零碎结构；多概念保持原 scope/实体/结构评分。使用原区域表选标题候选的正文，不建立新索引。真实语料性能和摘要质量须单独实测，不以合成测试代替。

## 文件变更

- MODIFY src/Terraria_Wiki.Core/Services/DatabaseService.RegionSearch.cs
- MODIFY tests/Terraria_Wiki.Core.Tests/SearchQueryTests.cs
- MODIFY openspec/specs/wiki-query-boundary/spec.md、docs/adr/0012-use-v2-region-indexed-wiki-database.md、openspec/project.md
- ADD 本计划

## 执行步骤

- [x] 核对调用链、测试、契约并建立保存点。
- [x] 实现按页合并、单词标题排序和正文代表区域。
- [x] 添加雪人候选、上限、正文摘要、证据保留与导航回归。
- [x] 执行 Windows 测试/构建并检查差异、同步契约。

## 验证与回归计划

Windows .NET 搜索测试先行，再执行 solution tests/build；检查多词 scope、官方 Redirect、Alias、UTF-16 高亮、更新删除事务及跨层消费者。真实库和窗口未执行时明确报告。

## 风险与回退

单词查询改用正文优先可能改变既有首项摘要；多词结构证据保留原优先级。结果上限变为页面数。按保存点恢复本次相关源码与规范即可回退；无需数据库操作。

## 进度记录

2026-09-14：完成只读诊断与用户授权，开始实施。

2026-09-14：实现并通过搜索回归；更新 Search spec、ADR 和项目基线。真实区域仍作为代表结果，其他命中通过 EvidenceRegions 保留，不新增 UI 布局。

## 发现、偏差与决策

持续 spec 和 ADR 0012 原先允许同页多条；按本次明确用户要求修订查询聚合行为，区域索引和长期架构保持不变。

首轮新增测试失败：雪人炮的 MatchKind 为 BodyFts。定位为 C# raw SQL 中 ESCAPE 写成两个反斜杠，修正为单字符后 TitlePrefix 断言通过；补充 `%`、`_`、反斜杠字面匹配回归。标题候选此前并未正确参与匹配，不能仅归因于排名较低。

## 验证结果

- Windows `dotnet test tests/Terraria_Wiki.Core.Tests/Terraria_Wiki.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SearchQueryTests --verbosity minimal`：14/14（新增符号测试前）；首次 13/14 揭示上述 SQL 缺陷，修复后通过。
- Windows `dotnet test Terraria_Wiki.Core.slnx --no-restore -c Release --verbosity minimal`：Core 53/53、Application 51/51、WikiTools 45/45、Windows 163/163，共 312/312 通过。包含最终新增符号回归；真实库条件测试的提前返回边界见下。
- Windows `dotnet build Terraria_Wiki.Core.slnx --no-restore -c Release --verbosity minimal`：9 项目成功，0 warning / 0 error。
- 首次沙箱全套测试被 Avalonia 用户目录构建日志权限阻断；提权重试后构建通过，不属于源码编译失败。
- `git diff --check` 通过。没有设置真实库验收环境变量；真实库条件测试提前返回，不计作真实语料验证。未执行真实活动库搜索、性能或窗口人工验收，未重打安装/portable 包，未修改活动库。

## 完成摘要

用户已确认真实语料搜索与 Windows 窗口人工验收通过；活动数据库未被修改。

已完成单概念标题候选优先、标题 SQL 转义修复、正文摘要优先和按页合并；同页证据及代表区域真实导航保留，多概念回归通过。未改数据库格式或索引，无需重新下载。用户应使用本轮 Release 构建验证真实“雪人”查询体验；已有安装包不会自动包含源码变化。仅修改本计划列出的文件，其他用户改动保持原样。

## 本地 Release 补交付（2026-09-14）

用户反馈 Release 没有变化。核对发现 `artifacts/local-release/rebuild-local-wiki-search/Terraria_Wiki.Core.dll` 仍为 12:46:18 的旧文件（280064 字节），而新构建为 22:28:52（281600 字节）；上一轮仅 build，未更新该本地发布目录。

已执行 `dotnet publish src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj --no-restore -c Release -r win-x64 --self-contained true -o artifacts/local-release/rebuild-local-wiki-search --verbosity minimal`，退出码 0。发布目录与 Core Release 构建的 DLL SHA-256 均为 `C29268067430A2B475ECF70BA5EF844C185D7DD08CEA5CF13C2CC8954233E209`。既有 AgentRuntime/python 保留，不修改活动数据库；没有创建安装包或进行公开发布。此前“未更新本地发布目录”的状态由本节取代，真实窗口搜索体验仍待用户验证。
