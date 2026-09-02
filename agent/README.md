# Terraria Wiki Agent

Phase 06 的平台无关 Python AI Backend。它只通过 Phase 05 提供的受限 MCP
`search_wiki` / `read_wiki` 查询本地 Wiki，不直接访问 SQLite、Core、客户端或平台路径。

运行要求为 Python 3.12–3.14。依赖由 `uv.lock` 固定：

```bash
uv sync --frozen --dev
uv run ruff check .
uv run ruff format --check .
uv run mypy
uv run pytest
```

公共调用是异步单轮边界。旧两参数调用保持兼容，可选回调只收到安全的 run/tool
状态和最终正文增量：

```python
from terraria_wiki_agent import TerrariaWikiAgent, WikiAgentSettings, WikiToolConnection

connection = WikiToolConnection("http://127.0.0.1:12345/mcp", "runtime-token")
settings = WikiAgentSettings(
    base_url="https://api.example.com/v1",
    model="provider-model-name",
    api="responses",  # 或 "chat_completions"
)


async def on_event(event):
    print(event.type)


answer = await TerrariaWikiAgent(settings).answer(
    "月铸镐如何制作？",
    connection,
    on_event,
)
```

`base_url`、`model` 和线协议由 Runtime 显式配置，库不绑定单一供应商。远程 endpoint
必须使用 HTTPS；HTTP 只允许字面 loopback 地址。生产模型密钥只从
`TERRARIA_AI_API_KEY` 环境变量读取，不进入 Agent settings。库不读取 `.env`，不提供 CLI，
也不负责启动、停止或打包 Wiki Tool server / Python Runtime。兼容配置只表示可接线；
每个具体服务仍须用工具调用、typed completion、用量和错误场景实测。

Windows Phase 07 通过发布目录中的 `sidecar_launcher.py` 以 `-I -u -X utf8` 启动
长驻 Runtime。stdio 只承载严格 NDJSON v1；API key 仍只来自子进程环境，MCP
endpoint/bearer 只在 initialize stdin 中传递。Sidecar、Windows lifecycle 和
`ai-runtime.json` v2 属于 Windows Adapter；Windows Host 可从该用户级文件读取 key，再只向
所属 Sidecar 注入 `TERRARIA_AI_API_KEY`，不改变本包的平台无关业务边界。应用不会下载
Python 或安装依赖；配置的环境必须已按本目录 `uv.lock` 准备完毕。
