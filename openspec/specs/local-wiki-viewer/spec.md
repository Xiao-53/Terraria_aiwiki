# Local Wiki Viewer 能力契约

- 状态：confirmed baseline；Windows Phase 04 transport 已确认
- 适用范围：Application/Core 读取边界、Local Viewer Adapter、Windows 与后续 Android 客户端
- 来源：现有 Local Wiki Viewer P0 行为；Master Architecture v0.6

## 目的

在替换 MAUI/Blazor 宿主时继续复用现有 Wiki 页面、静态资源和数据库资源，并防止 WebView 或 transport 反向污染 Core。

## 契约

### 必须保留的行为

- Viewer 能显示现有 Wiki 页面 HTML、样式和脚本。
- Viewer 能加载数据库中的图片、音频等 WikiAsset，以及应用随包分发的公共/站点资源。
- 内部 Wiki 链接、页面导航、主题同步和 JS↔C# 消息链路保持可用。
- Viewer 使用与手动搜索和 AI 相同的活动 Wiki 数据，不复制页面或资源数据库。

### 依赖方向

- NativeWebView 控件和 Viewer 生命周期属于 Presentation/Host。
- Local Viewer Adapter 依赖 Application/Core 提供的只读页面与资源能力；Core 不依赖 NativeWebView、HTTP、固定端口或平台 WebView API。
- Viewer transport 可以是 loopback server、平台资源映射或其他经验证方案，但不得改变 Core 数据契约。

### Transport 与生命周期

- loopback 是可接受的基线候选，不因“localhost”本身被提前否决；端口、启动、停止和健康状态必须由 Adapter 管理。
- transport 必须处理端口冲突、重复启动、并发实例、前后台切换、窗口关闭和异常恢复，启动失败不得静默伪装为 Viewer 成功。
- Windows Host 只有在首页 Redirect 已返回且正文 DOM 已写入后才把 Viewer 标为可交互；同一 Base URI 的重复 Ready 状态不得触发第二次导航或替换正在等待的就绪信号。Viewer 未就绪时 Reader 命令必须禁用或明确失败，不能静默丢失点击。
- Windows 退出先取消 Viewer 生命周期并停止 WebView；loopback 必须主动关闭已接受的 socket，使 client drain 有明确上限，然后才释放共享 Application 数据源。
- 本地资源映射只有在目标平台实际支持响应内容、相对 URL、媒体、脚本和消息桥后才可采用；平台支持表不能替代项目验证。
- WebView 的 origin、CORS、外部导航和消息来源必须限制到完成 Viewer 所需的最小范围。

### Windows 已确认实现

- Windows 使用动态 `127.0.0.1:0` loopback，只接受精确当前 Host 的 GET/HEAD；拒绝 absolute target、路径穿越、反斜线、冒号和 NUL，不提供宽松 CORS。
- build/publish 只从产品仓库的受控快照复制 Viewer HTML/CSS/JS、公共资源和 `iframe-bridge.js`。快照必须记录来源、固定提交、数量/hash 与许可提示；任何上游参考 checkout 不得成为 build/runtime 依赖。
- `/src/*` 只通过 Application `Query.ReadAssetAsync`；页面与 Redirect 只通过 `Query.ReadAsync`；页面成功读取后由宿主显式调用 `Library.RecordVisitAsync`。
- outer host 与 C# 分别校验精确 origin/source、`req/res`、request ID 和方法；HTTP(S) 外链交给系统浏览器，未知或未支持方法返回可观察错误。
- pause lease 停止 listener；释放后仅在宿主仍存活时按当前活动 Wiki 重启。退出后不得遗留 listener 或继续访问已释放 Application。
- Avalonia 站点首屏通过内部 `ViewerReady` 方法报告 DOM 已写入；该方法不扩展公共 Viewer 业务能力。WebView stop 最多等待 500 ms，listener 主动关闭活动连接并最多排空 1 秒。
- Windows 默认依赖系统 Evergreen WebView2；framework-dependent/self-contained .NET 均不等于捆绑 WebView2。安装器、bootstrap 和签名仍属于 Phase 11。
- 以上行为由 Windows 11 x64 验证支持；Android transport 仍保持开放，不能从 Windows 结论外推。

### 接管与回退

- 新 Viewer 通过页面、资源、内部链接、主题、消息和生命周期回归前，旧 Viewer backend 不得删除。
- Windows 与 Android 分别验证；Windows 结论不能外推为 Android 已通过。

## 验收场景

1. 打开代表性页面并加载 CSS、JS、数据库图片和音频。
2. 从 Viewer 内部链接跳转到另一个 Wiki 页面，并正确处理锚点与返回。
3. C# 调用 JS、JS 向宿主发消息，来源和错误路径可观察。
4. transport 端口冲突或初始化失败时，应用报告明确错误且可以恢复或回退。
5. 多次打开/关闭、前后台切换和应用退出后，不留下不可控的 listener 或错误活动数据源。
6. Windows 精确拒绝错误 Host、非 GET/HEAD、路径穿越、非法消息来源/ID 和未知方法；缺失 WebView2、数据源不可用与 transport 失败均可观察。

## 变更规则

本 spec 只定义持续行为。Windows transport 长期决策见 `docs/adr/0003-use-loopback-local-viewer-transport.md`；产品资产所有权见 `docs/adr/0007-separate-product-source-from-reference.md`；NativeWebView 包版本与具体 Adapter 文件结构仍写入对应 Phase 计划。
