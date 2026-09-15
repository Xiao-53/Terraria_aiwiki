# 增量同步验收记录

日期：2026-09-15；环境：Windows 11 x64 / .NET 10。所有真实更新使用 SQLite 只读快照生成的隔离副本；活动库未写入。

## 真实站点与副本

原库：`C:/Users/Xiao/AppData/Local/Terraria AI Wiki/WikiData/Terraria_Wiki_zh/data.db`。
验收副本及原始日志：`artifacts/incremental-sync/run-20260915-031824/`（大体积数据库与原始日志不提交）。

| 场景 | 请求 | 响应字节 | 总耗时 | 结果 |
| --- | ---: | ---: | ---: | --- |
| 旧检查基准，4359 页 | 64 | 7191736 | 44.189 秒 | 元数据与逐页 HTML 本地读取；不含旧 Redirect 请求，因此是保守下界 |
| 新版失败后完整恢复 | 256 | 16188874 | 176.601 秒 | 实际替换 19 页、12 个 Redirect、删除 1 项，失败 0 |
| 随后无变化增量 | 3 | 2899 | 1.726 秒 | 正文 parse 0，下载 0，失败 0，Changed=false |
| 真实媒体条件请求抽样 | 3 | 0 | 6.758 秒 | 3 个实际 304，失败 0 |

完整恢复检查 33069 项、跳过 33038 项。相比旧检查保守下界，本次无变化请求减少 95.31%，耗时减少约 96.09%；这是单次环境实测，不承诺固定速度。快速轮的锚点在重叠窗口之外仍有效，未因此退化为全量。

早期两次副本执行遇到 Redirect 查询成本和 HTTP 429，曾提交部分页面，但未推进成功状态。实现随后批量读取 Redirect、保留范围外旧有效映射并尊重 Retry-After；以上完整恢复是修正后的通过结果，早期失败不计为通过。

英文 Terraria、Calamitymod 的有界探测验证 recentchanges continuation、missing、embeddedin、imageusage、imageinfo；中文 Terraria 已完成实际同步。灰机返回 403，未绕过限制，也不宣称该来源通过。复现脚本为同目录 `probe-sites.ps1`。模块依赖、删除、移动及故障分支使用可重复 fixture 覆盖，不冒充真实线上编辑样本。

## 自动化验证

以下命令均在 Windows 执行，最新周期和导入改动后通过：

```powershell
dotnet test tests/Terraria_Wiki.Core.Tests/Terraria_Wiki.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~WikiIncrementalSyncTests|FullyQualifiedName~WikiDatabaseTransferTests|FullyQualifiedName~TransportModelTests|FullyQualifiedName~WikiDataLifecycleTests|FullyQualifiedName~SearchQueryTests|FullyQualifiedName~DatabaseServiceTests|FullyQualifiedName~DataPathsAndContentTests'
# 87 passed
dotnet test tests/Terraria_Wiki.Application.Tests/Terraria_Wiki.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~ApplicationSettingsTests|FullyQualifiedName~WikiDataUseCaseTests'
# 23 passed
dotnet test tests/Terraria_Wiki.Windows.Tests/Terraria_Wiki.Windows.Tests.csproj --no-restore --filter 'FullyQualifiedName~Phase09TakeoverTests|FullyQualifiedName~HostSettingsStoreTests'
# 14 passed
dotnet build src/Terraria_Wiki.Windows/Terraria_Wiki.Windows.csproj -c Release --no-restore
# 0 warnings, 0 errors
```

定向覆盖万页无变化三请求、三个变化页去重、删除与转换事务、收藏保留、FTS、递归依赖、分页异常、媒体失败恢复、SQLite 取消门闩、索引维护恢复。新增验证默认 14 天/任意正整数/null，禁用周期仍保留强制核对；兼容导入逐字保留状态并直接增量，不兼容状态失效后全量；导入后离线搜索、页面、收藏及媒体仍可读。保存其他设置不重置未展示的周期字段。

导入可信边界为既有 inspection token/hash、数据库 schema/wiki_id 校验，加上可选同步状态字段完整性、版本、API 与命名空间一致性。不是对任意恶意来源的密码学认证；下一次联网仍验证远端锚点。

真实测试入口在 `tests/Terraria_Wiki.Core.Tests/RealWikiSyncAcceptanceTests.cs`；显式设置 `TERRARIA_SYNC_SOURCE_DB` 和 `TERRARIA_SYNC_EVIDENCE_DIR` 才运行真实快照更新，`TERRARIA_SYNC_RESUME_ROOT` 可复用隔离副本。媒体抽样需 `TERRARIA_SYNC_MEDIA_ROOT`。未设置环境变量的测试返回不能作为线上验收证据。

## 人工验收与限制

用户亲自操作隔离副本窗口并确认“验收副本窗口运行完美”。这是人工反馈，不能扩写为逐项自动 GUI 断言；用户要求由本人操控电脑，后续未执行桌面控制。

真实完整恢复使用轻量模式，媒体实测仅抽样三项；未宣称全部约 2.5 万媒体重新核对通过。新增周期控件仍延期，当前配置字段是 `WikiFullScanIntervalDays`（默认 14，JSON null 关闭）。没有后台更新或计划任务；只有用户主动更新触发到期核对。未重新执行发布或安装流程，未变更依赖。
