# ADR 0010：使用本地完整会话与有界模型历史

- 状态：accepted
- 日期：2026-09-05
- 决策范围：Windows Chat 会话所有权、持久化、收藏/历史语义和模型历史隔离；不包含云同步、其他平台或 Wiki 数据复制
- 关联：`openspec/changes/ai-conversation-token-optimization/plan.md`、`openspec/specs/windows-presentation/spec.md`、`openspec/specs/windows-ai-runtime/spec.md`、`docs/adr/0005-use-pydantic-ai-openai-backend.md`

## 背景

原 Windows Chat 每次只显示一个问题与回答，收藏也只保存单轮；下一题不带上下文。与此同时，直接保留 PydanticAI 原始消息图会把旧工具调用、Wiki 内容、thinking 和 provider 对象重新发送，既扩大隐私边界，也会重复消耗大量输入 Token。产品需要让用户在本地看到、恢复和收藏完整对话，但远程模型只应获得足以理解追问的最小文本上下文。

原“Wiki 资料库 → 历史记录”持久保存页面浏览记录。它与 Reader 自己的内存返回栈无关，也不是问答会话历史；继续维护两个历史入口会混淆产品语义。

## 决策

1. Windows Host/Presentation 是会话的唯一所有者。每个会话保存稳定 ID、标题、创建/更新时间、整段收藏状态，以及按顺序排列的 user、assistant Markdown、终态、来源、usage 和时间。Sidecar 与 Agent 不持久化会话。
2. 当前问题进入运行前先保存 pending turn；completed、insufficient-evidence、failed、cancelled 和 interrupted 等终态再原子保存。启动时遗留 pending 归一为 interrupted，避免把不完整轮次伪装成成功回答。
3. 模型只接收当前问题和该会话最近最多 4 个 completed/insufficient-evidence 轮次的 user/assistant 纯文本，总预算 24000 字符。Host 从新到旧选择后按时间顺序发送；不得包含旧工具调用、工具结果、来源对象、HTML、thinking、stderr 或原始 PydanticAI/provider 消息图。失败、取消、pending 和 interrupted 不进入模型历史。
4. 本地 UI 显示完整 transcript；“新对话”立即切换为空白会话但保留尚未提交的输入草稿，应用启动始终进入空白新对话，不自动上传或恢复最近会话。历史或收藏可打开完整 transcript 并继续追问。
5. 收藏对象是整段会话。新存储位于 `%LocalAppData%\Terraria AI Wiki\conversations`，使用版本化 `index.json` 加独立会话 JSON；写入临时文件、flush 并原子替换。最多保留 100 个未收藏会话、每段 100 轮；收藏不自动淘汰，达到 200 个时拒绝新增收藏而不丢数据。
6. 旧 `chat-favorites.json` v1 只在新 index 不存在时迁移。每项成为一段单轮、completed、已收藏会话；只有新文件与 index durable 后迁移才算完成，旧文件原样保留用于回退，后续以新 index 为唯一事实源。
7. “Wiki 资料库 → 历史记录”改为“对话历史”。Application/Viewer 停止查询、写入和清空持久 WikiHistory；旧 SQLite 表与行仅为数据兼容保留，不主动删除。Reader 的内存返回栈、`SaveToTabHistory`、`WikiBackAsync` 与鼠标侧键返回保持不变。
8. 会话文件是本地用户数据，不进入日志、诊断、发布包或同步。设置页明确披露当前问题、最多最近 4 轮纯文本和本轮 Wiki evidence 会发送给所选 provider，完整本地历史不会自动上传。

## 选择理由

- Host 已负责 Presentation、保存位置和请求编排，拥有会话可以避免 Python Agent 形成第二个本地状态源。
- 最近 4 轮纯文本能支持代词、省略和连续追问，同时把事实检索继续留给当前轮 Search/Read，不重放最昂贵且最敏感的历史工具消息。
- index 与独立会话文件使单次保存、淘汰和损坏隔离保持有界；旧收藏迁移保留原文件，可以重试和回退。
- 对话历史取代浏览历史后，资料库仍保留页面列表与 Reader 返回，SQLite schema 不需要破坏性迁移。

## 后果与约束

- 本地磁盘会保存问答正文；用户可逐项删除、清除未收藏历史，取消收藏不会删除会话。
- 纯文本历史不是旧模型内部状态的精确重放；每个事实问题仍必须重新 Search/Read。8 次工具调用上限、Search 范围和输出安全上限不因会话能力降低。
- 单文件损坏应被跳过并保持其他会话可用；index 主文件损坏时可读取备份。不能因清理失败让 durable index 指向已删除文件。
- 当前决定只实现 Windows 本地存储。Android 若恢复必须另行选择平台存储；不增加云同步、账户、Web Search、Vector Search、Embedding 或第二套 Wiki 数据库。

## 被否决方案

- 重放 PydanticAI 原始消息图：最接近 provider 内部历史，但会携带旧工具结果、HTML、thinking 和不稳定 SDK 类型，违反 Token、隐私与协议边界。
- 由 Python Sidecar 保存会话：会形成第二个状态源，并把 Windows 文件生命周期和 UI 语义泄漏到平台无关 Agent。
- 把会话写入 Wiki SQLite：会混合用户对话与可导入/导出的内容数据库，扩大 schema 和数据包兼容范围。
- 云同步或账号存储：当前没有授权、身份、加密、冲突解决或数据处理范围。
