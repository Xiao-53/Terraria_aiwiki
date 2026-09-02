# 添加 Windows AI Provider 设置

- 状态：completed
- Phase：无
- 执行环境：WSL Codex CLI；Windows 11 x64 build/test 与真实窗口验收
- 创建日期：2026-09-03
- 最后更新：2026-09-03
- 范围：Windows Avalonia 设置页、`ai-runtime.json` v2 读写、AI Runtime 显式重启、相关契约与测试
- 关联架构/规范/ADR：`docs/architecture/ARCHITECTURE-v0.6.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/windows-ai-runtime/spec.md`、`docs/adr/0005-use-pydantic-ai-openai-backend.md`、`docs/adr/0006-use-stdio-python-sidecar.md`

## 背景与当前证据

用户要求在设置页面增加 AI 配置二级菜单，当前只提供 DeepSeek 官方 API 与自定义 OpenAI-compatible provider，并允许调整思考强度。

当前事实：Windows Runtime 已严格读取 `ai-runtime.json` v2，provider 字段已包含 `baseUrl`、`model`、`api` 与可空 `reasoningEffort`；Python Agent 已能把显式 reasoning effort 交给 PydanticAI；`WindowsClientRuntime.RetryAiAsync` 能释放旧 Sidecar、重新读取同一路径配置并启动新 Runtime。现有设置页只能读写 Application 偏好，不能管理 AI 配置。

DeepSeek 官方当前文档确认 OpenAI-compatible base URL 为 `https://api.deepseek.com`，Responses API 支持 `reasoning.effort`，当前正式文本模型为 `deepseek-v4-flash` 与 `deepseek-v4-pro`。这是外部时效事实，不改变内部 provider-neutral Agent 契约。

## 目标与完成定义

- 设置页提供“常规设置 / AI 配置”两个二级入口，不新增全局导航。
- AI 配置只显示 DeepSeek 官方 API 与自定义 OpenAI-compatible 两种 provider 选择。
- DeepSeek 预设固定官方 base URL 与 Responses API，并提供 Flash/Pro 模型；自定义 provider 可填写安全 endpoint、模型及 Responses/Chat Completions。
- API key 使用遮蔽输入；Python 仍要求用户选择已有 `python.exe` 的绝对路径，不自动安装或修改环境。
- 思考强度可选择服务默认、关闭、低、高、最大，并映射到现有 v2 `reasoningEffort`。
- 显式保存时统一验证、原子写入当前有效 `ai-runtime.json` 路径，并在当前会话显式重启 AI；保存失败不损坏旧配置，重启失败不误报持久化失败。
- Windows 全量 build/test 通过，设置页在目标 Windows 窗口中完成浅色/深色与两种 provider 状态验收。

## 前置条件

- Phase 07 的 Windows AI Runtime、Phase 08/09 的 Presentation 与设置页已实现。
- 使用现有 Windows Python 3.12–3.14 环境；本变更不安装 Python 或依赖。
- DeepSeek 真实联网调用不作为保存 UI 的完成前置；已有 provider 真实 smoke 证据保持不变。

## 范围与非目标

允许修改 Windows AI 配置模型/存储、Settings Presentation、Shell/MainWindow 接线、测试及直接相关 spec/ADR。

非目标：增加其他具名供应商、Provider 自动发现、模型列表联网同步、API key 联网校验、Windows Credential Manager、内置 Python、依赖安装、Android UI、Agent/NDJSON/schema 版本修改、多轮或向量检索。

## P0 / 兼容性影响

- 继续使用同一 Application/Core、SQLite FTS5、Wiki Tools、单一 Viewer 与 Python Agent，不复制或绕开任何 P0 能力。
- 保持 `ai-runtime.json` v2 形状和 `--ai-config` 覆盖语义；既有手写自定义配置可载入并编辑。
- AI 保存/重启故障只影响 AI，离线 Search、Reader、数据管理和常规设置继续可用。
- API key 继续按 ADR 0006 的用户级 JSON 明文决策持久化，但 UI 必须遮蔽，错误、日志、命令行和 NDJSON 不得泄漏。

## 方案与待决问题

- 在 Settings 内使用现有 `segment-button` 样式实现二级菜单；常规设置布局保持原行为，AI 配置使用同一 820 DIP 内容宽度与页内状态/操作区。
- 给现有配置类型增加可编辑 draft 与原子 store；加载 draft 允许“文件不存在”返回安全的 DeepSeek 默认草稿，存在但不合法时仍拒绝并保持 UI 状态明确。
- 不给 v2 增加 provider kind：只有精确匹配官方 base URL、Responses 与当前 DeepSeek 模型时识别为 DeepSeek 预设，其他配置均归入自定义。
- 思考强度只公开跨两种当前选择可理解的五档；`null/none/low/high/max` 直接复用现有字段。手写 `medium/xhigh` 配置载入时在 UI 归并为高，保存后规范化为 high。
- 保存回调由 MainWindow 绑定当前 `_runtimeArguments` 的配置路径，并在写入成功后调用既有 `RetryAiAsync`。
- 待决问题：无；用户本次指令已批准该最小范围。

## 文件变更

- ADD `src/Terraria_Wiki.Windows/AI/AiRuntimeConfigurationStore.cs`：draft、路径解析、严格读取与原子保存。
- MODIFY `src/Terraria_Wiki.Windows/AI/AiRuntimeConfiguration.cs`：复用共享校验/文档转换，保持运行时读取行为。
- ADD `src/Terraria_Wiki.Windows/Presentation/Settings/AiSettingsViewModel.cs`：两种 provider、思考强度、状态和保存流程。
- ADD `src/Terraria_Wiki.Windows/Presentation/Settings/SecretTextBox.cs`：保持可输入/可绑定，同时向 Windows UI Automation 只公开遮蔽值。
- MODIFY `src/Terraria_Wiki.Windows/Presentation/Settings/SettingsViewModel.cs`：二级页面状态并组合 AI 设置。
- MODIFY `src/Terraria_Wiki.Windows/Presentation/Settings/SettingsView.axaml`：二级导航与 AI 表单。
- MODIFY `src/Terraria_Wiki.Windows/Presentation/Shell/ShellViewModel.cs`、`src/Terraria_Wiki.Windows/MainWindow.cs`：配置读写与当前 Runtime 重启接线。
- MODIFY `tests/Terraria_Wiki.Windows.Tests/` 下相关配置、设置与 Presentation 边界测试。
- MODIFY 直接相关 Windows AI Runtime / Presentation spec 与 ADR 0006。
- KEEP Agent、Application/Core、NDJSON、数据库/数据包、Viewer、参考 Submodule 不变。

## 执行步骤

- [x] 建立 v2 AI 配置 draft/store，共享验证并覆盖缺失、严格读取、原子保存和 secret 不泄漏测试。
- [x] 实现 AI Settings ViewModel 与二级设置页，覆盖 DeepSeek 预设、自定义 provider、思考强度、脏状态和保存/重启结果。
- [x] 通过 Shell/MainWindow 接到当前配置覆盖路径与 `RetryAiAsync`，保持 Chat 状态事件链和离线能力隔离。
- [x] 更新能力契约与 ADR，明确最小凭据 UI 已提前交付但 Credential Manager/安装器仍为非目标。
- [x] 执行 Windows build、全量 tests、静态边界检查与真实窗口浅/深色验收。
- [x] 检查差异、临时文件与 Submodule 状态，完成并归档计划，提交只包含本变更。

## 验证与回归计划

- WSL：`git diff --check`、定向源码/文档边界检查、工作树与 Submodule 指针检查。
- Windows：`dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`。
- Windows：`dotnet test Terraria_Wiki.Core.slnx -c Release --no-restore --nologo`。
- Windows：以隔离的数据根和假 key/config 启动真实 Avalonia 窗口，检查 1280×820 DIP、216 DPI、浅色/深色、DeepSeek/自定义表单、遮蔽 key、键盘焦点与滚动；不发起真实 provider 请求。
- Android：不适用，本变更只修改 Windows 平台实现。

## 风险与回退

- 原子保存失败：同目录临时文件 + replace/backup，失败保留旧配置并清理临时文件。
- 保存后 AI 重启失败：保留已保存配置，页面明确显示 AI 尚未就绪；Chat 通过既有安全状态事件更新，离线能力不受影响。
- 旧自定义配置识别错误：只有严格匹配官方组合才显示 DeepSeek，其余全部保守归入自定义。
- key 泄漏：PasswordBox/遮蔽输入，不把值写入状态、异常或对象字符串；测试覆盖。
- 回退：恢复本变更提交；v2 配置格式未变，无数据迁移或不可逆操作。

## 进度记录

- 2026-09-03：读取架构、相关 specs/ADR、现有 AI 配置/Runtime/Settings/Shell/MainWindow 和测试；核对 DeepSeek 官方当前 API 文档；确认从提交 `ac96636` 的干净 tracked 状态开始，用户既有未跟踪文件不纳入范围。
- 2026-09-03：实现 v2 draft/store、二级设置页、DeepSeek/自定义 provider、Python 选择、API key、思考强度与保存后显式重启；新增配置、ViewModel、Presentation 和 secret 自动化测试。
- 2026-09-03：Windows 真实窗口首次安全检查发现 Avalonia 12 默认密码 TextBox 虽视觉遮蔽，但 UI Automation ValuePattern 仍返回原值；增加专用 automation peer 后复验只返回同长度圆点。
- 2026-09-03：完成 Windows Release build、274/274 tests、216 DPI 目标窗口浅/深色与 1280×820 / 900×600 DIP 检查；隔离数据、假 key、截图与临时脚本均已删除。

## 发现、偏差与决策

- 决定不升级 `ai-runtime.json` schema：provider 类型仅为 UI 预设，自定义配置仍是事实来源。
- 决定 DeepSeek 预设使用 Responses API，以直接复用现有 reasoning effort 语义和已验收的 V4 Flash 组合。
- Terraria UI Skill 要求沿用现有语义 token、共享按钮/分段样式、820 DIP 内容宽度、Automation 名称，并在真实 Windows 窗口完成浅/深色验收；本方案据此不增加新视觉系统。
- 发现：Avalonia 12.1.1 的 `TextBoxAutomationPeer` 直接公开 `Owner.Text`，`PasswordChar` 只处理视觉遮蔽；因此采用局部 `SecretTextBoxAutomationPeer`，保留 UIA ValuePattern 的写入能力但把读取和变更事件值都映射为圆点，并隐藏模板子 automation peers。

## 验证结果

- Windows Release build：`dotnet build Terraria_Wiki.Core.slnx -c Release --no-restore --nologo` 通过，0 warning / 0 error。
- Windows 全量 .NET tests：274/274 通过（Core 91、Application 54、WikiTools 28、Windows 101）。
- 定向测试覆盖：缺失配置 DeepSeek 默认草稿、v2 durable 原子写入、非法 provider 保留旧文件、secret `ToString`/UIA 遮蔽、DeepSeek 与自定义 provider 映射、Responses/Chat、思考强度、Python picker、保存后 Runtime 状态和 Presentation 二级菜单边界。
- 静态检查：`git diff --check` 通过；页面未增加 raw hex、第二个 Viewer、依赖、schema 或协议字段。
- 目标 Windows 窗口：Windows 11 x64、216 DPI 下检查 1280×820 DIP Light DeepSeek、1280×820 DIP Dark 自定义，以及 900×600 DIP 底部滚动；provider、模型/API 条件字段、思考强度、状态和保存按钮均可见，key 视觉与 UI Automation 均不公开原值。
- 真实 provider：未请求；窗口验收使用临时合成 Wiki 数据、缺失测试 Python 与假 key，只验证配置 Presentation 和 AI Disabled 隔离。所有临时目录与脚本已删除。

## 完成摘要

Windows 设置页已增加常规/AI 二级菜单，只提供 DeepSeek 官方 API 与自定义 OpenAI-compatible provider。DeepSeek 预设锁定官方 endpoint/Responses 并提供 V4 Flash/Pro；自定义项支持 endpoint/model/Responses/Chat，双方均可调服务默认、关闭、低、高、最大思考强度。配置继续使用严格 v2，原子保存后重启同一 AI Runtime；key 在视觉、对象字符串和 Windows UI Automation 中均保持遮蔽。Windows build、274/274 tests 与目标窗口验收通过，公共 API、Agent/NDJSON、SQLite、数据包和 Viewer 不变。
