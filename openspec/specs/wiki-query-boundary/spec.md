# Wiki Search / Read 边界契约

- 状态：confirmed baseline
- 适用范围：Application、Presentation、Wiki Tool Adapter、AI Backend
- 来源：现有手动搜索/阅读能力与 AI 事实来源决策

## 目的

为新 UI 和 AI 提供同一套稳定 Wiki 查询能力，使调用方不依赖 SQLite、FTS5 表、全局 `App.*` 或具体 transport。

## 契约

### Search

- Search 使用现有本地 Wiki 数据和 SQLite FTS5 能力，并保留标题、Redirect 与正文检索的既有角色。
- 匹配类型与优先级固定为 `ExactTitle`、`ExactRedirect`、`TitlePrefix`、`RedirectPrefix`、`TitleContains`、`RedirectContains`、`BodyFts`；同级结果必须完全确定，跨来源重复结果只保留最高优先级匹配。
- 查询去除首尾空白并把连续 Unicode 空白折叠为单个空格；精确/前缀基于完整规范化查询，包含与正文多词查询要求所有词出现。`%`、`_`、反斜线、单双引号和其他符号均按字面输入处理。
- 全部查询词少于 3 个 Unicode scalar 时只搜索标题和 Redirect；混合长短词只以长词建立 FTS 候选，再在候选正文校验短词，不对正文执行全表短词扫描。
- Search 返回可识别页面的标题、Redirect 目标和纯文本摘要；`Highlights` 使用摘要字符串的零基 .NET UTF-16 起始位置与长度，不包含 HTML 标记。
- 陈旧 FTS 行和无法解析到现有页面的 Redirect 不得作为搜索结果返回。
- 去重与完整排序后才应用 `MaxResults`；`WikiSearchOptions.MaxResults` 允许范围固定为 `1..100`。
- 结果数量、查询选项和取消由调用参数或 Application 用例控制，不从 Core 内部读取 Presentation/平台设置。
- 取消不得被报告为无结果；数据源不可用通过稳定错误语义返回。搜索质量后续变化仍须由专门 Phase 和回归样本确认，不得以更换索引体系绕过局部问题。

### Read

- Read 通过稳定页面身份读取本地 Wiki 页面及必要来源信息。
- Redirect、锚点和未找到页面的语义必须明确，调用方不直接拼接数据库查询。
- Viewer、手动阅读和 AI 可以使用不同 Adapter，但读取同一份 Core 数据。

### 调用边界

- Presentation 通过 Application Search/Read 用例访问 Wiki。
- Wiki Tool Adapter 调用相同用例，不直接访问 `SQLiteAsyncConnection`、表或全局应用状态。
- AI transport、Python 进程和 Tool Calling 协议不得进入 Core；transport 可以替换而不改变 Search/Read 语义。
- 错误、取消、超时和不可用数据源必须以稳定结果或错误语义返回，不依赖 UI 弹窗完成流程。

### 非目标

- 不建设 Vector Search、Embedding Retrieval、Vector Database 或第二套 AI Wiki 数据。
- 不在本契约中固定接口签名、DTO 类名、SQL 或 transport。

## 验收场景

1. 同一查询通过手动 UI 与 Wiki Tool Adapter 获得语义一致的候选页面。
2. 七级标题、Redirect 与正文匹配顺序、同级确定排序、跨来源去重和无结果均有稳定回归样本。
3. 多词、Unicode 空白、短词/混合词及字面 `%`、`_`、反斜线、单双引号均有安全回归样本。
4. 摘要始终为纯文本，高亮范围合法且可按 .NET UTF-16 字符串索引安全切片。
5. `MaxResults` 的 `1..100` 边界和最终截断、取消、数据源不可用、坏 Redirect 与陈旧 FTS 均有明确回归。
6. Read 能返回代表性页面，正确处理 Redirect/锚点/不存在页面。
7. 调用方取消或数据源不可用时，不需要直接操作数据库或 UI 全局状态即可得到明确结果。
8. 静态依赖检查确认 Presentation、AI Backend 和 transport 不直接访问 SQLite 表。

## 变更规则

能力语义变化时更新本 spec；接口、DTO、SQL、排序权重和测试数据写入对应 Phase 计划。
