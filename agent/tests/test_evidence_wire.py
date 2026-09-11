import asyncio
import json
import os

import httpx
import pytest
from openai import AsyncOpenAI
from pydantic_ai import models
from pydantic_ai.providers.openai import OpenAIProvider

from terraria_wiki_agent import (
    TerrariaWikiAgent,
    WikiAgentSettings,
    WikiConversationTurn,
    WikiToolConnection,
)
from terraria_wiki_agent import agent as agent_module

from .evidence_wire_fixture import EvidenceWireFixture
from .fakes import fake_toolset_factory, read_response, search_response
from .test_cross_language import real_test_host
from .test_evidence_efficiency import v2_read


@pytest.mark.parametrize("api", ["responses", "chat_completions"])
@pytest.mark.parametrize("real_mcp", [False, pytest.param(True, marks=pytest.mark.cross_language)])
def test_both_wire_protocols_preserve_calls_evidence_history_and_sources(
    api, real_mcp, monkeypatch
):
    if real_mcp and os.environ.get("TERRARIA_RUN_CROSS_LANGUAGE") != "1":
        pytest.skip("Explicit cross-language opt-in required")
    sequence = [
        ("search_wiki", {"query": "月镐"}),
        ("read_wiki", {"title": "月镐", "mode": "section"}),
        ("read_wiki", {"title": "月镐", "mode": "section"}),
    ]
    wire = EvidenceWireFixture(api, sequence, "12夜银锭、3棱镜核心，在远古操纵机制作。")
    events = []

    async def emit(event):
        events.append(event)

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(wire)) as client:
            provider = OpenAIProvider(
                openai_client=AsyncOpenAI(
                    base_url="http://127.0.0.1/v1", api_key="offline-fixture", http_client=client
                )
            )
            monkeypatch.setattr(agent_module, "OpenAIProvider", lambda **kwargs: provider)
            monkeypatch.setenv("TERRARIA_AI_API_KEY", "offline-fixture")
            settings = WikiAgentSettings(
                base_url="http://127.0.0.1/v1", model="offline-fixture", api=api
            )
            history = tuple(
                WikiConversationTurn(user=f"问题{i}", assistant=f"回答{i}") for i in range(4)
            )
            if real_mcp:
                async with real_test_host() as connection:
                    return await TerrariaWikiAgent(settings).answer(
                        "配方？", connection, emit, history=history
                    )
            return await TerrariaWikiAgent(
                settings,
                _toolset_factory=fake_toolset_factory(
                    lambda name, args: (
                        search_response("月镐")
                        if name == "search_wiki"
                        else fake_section_read(args["title"])
                    )
                ),
            ).answer(
                "配方？",
                WikiToolConnection("http://127.0.0.1:1234/mcp", "test"),
                emit,
                history=history,
            )

    # The actual provider transport is replaced with MockTransport above, never a real endpoint.
    with models.override_allow_model_requests(True):
        answer = asyncio.run(run())
    assert answer.status == "answered"
    assert answer.sources[0].resolved_title == "月铸镐"
    assert answer.sources[0].anchor == "配方"
    assert "".join(e.delta for e in events if e.type == "text_delta") == answer.markdown
    assert len(wire.requests) == 4
    final = wire.requests[-1]
    tools = final["tools"]
    assert len(tools) == 2
    for tool in tools:
        definition = tool.get("function", tool)
        assert "Return schema" not in definition.get("description", "")
        assert "requested_page_title" not in definition.get("description", "")
    messages = final["input"] if api == "responses" else final["messages"]
    outputs = (
        [item["output"] for item in messages if item.get("type") == "function_call_output"]
        if api == "responses"
        else [item["content"] for item in messages if item.get("role") == "tool"]
    )
    assert len(outputs) == 3
    first, repeated = json.loads(outputs[1]), json.loads(outputs[2])
    assert first["evidence"]
    assert all("ref" in block for group in repeated["evidence"] for block in group["blocks"])
    assert "requested_page_title" not in outputs[1]
    assert "highlights" not in outputs[0]
    for request in wire.requests:
        rendered = json.dumps(request, ensure_ascii=False)
        for i in range(4):
            assert f"问题{i}" in rendered and f"回答{i}" in rendered


def fake_section_read(title):
    result = read_response(title)
    result["content_format"] = "wiki_blocks_v2"
    result["scope"] = v2_read()["scope"]
    result["scope"]["selected_section"] = "月铸镐 > 配方"
    result["scope"]["sections"] = [{"path": "月铸镐 > 配方", "anchor": "配方"}]
    return result
