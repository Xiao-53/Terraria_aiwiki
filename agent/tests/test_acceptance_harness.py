"""Offline regression for the manual paid-test wrapper, without application credentials."""

import asyncio

import httpx
import pytest
from openai import AsyncOpenAI
from pydantic_ai import models
from pydantic_ai.providers.openai import OpenAIProvider

from terraria_wiki_agent import WikiAgentSettings, WikiToolConnection
from terraria_wiki_agent import agent as agent_module

from .evidence_wire_fixture import EvidenceWireFixture
from .fakes import fake_toolset_factory, read_response, search_response
from .manual_evidence_acceptance import QUESTIONS, MeasuredAgent


@pytest.mark.parametrize("api", ["responses", "chat_completions"])
def test_usage_observer_preserves_real_sdk_stream_and_tool_dispatch(api, monkeypatch):
    wire = EvidenceWireFixture(
        api,
        [
            ("search_wiki", {"query": "页"}),
            ("read_wiki", {"title": "页"}),
        ],
        "测试回答",
    )

    async def run():
        async with httpx.AsyncClient(transport=httpx.MockTransport(wire)) as client:
            provider = OpenAIProvider(
                openai_client=AsyncOpenAI(
                    base_url="http://127.0.0.1/v1", api_key="offline", http_client=client
                )
            )
            monkeypatch.setattr(agent_module, "OpenAIProvider", lambda **kwargs: provider)
            monkeypatch.setenv("TERRARIA_AI_API_KEY", "offline")
            agent = MeasuredAgent(
                WikiAgentSettings(base_url="http://127.0.0.1/v1", model="offline", api=api)
            )
            agent._toolset_factory = fake_toolset_factory(
                lambda name, args: (
                    search_response("页") if name == "search_wiki" else read_response(args["title"])
                )
            )
            answer = await agent.answer(
                "问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test")
            )
            assert answer.status == "answered"
            assert answer.markdown == "测试回答"
            assert len(agent.requests) == 3
            assert all("error_type" not in request for request in agent.requests)
            assert sum(r["input_tokens"] for r in agent.requests) == answer.usage.input_tokens

    with models.override_allow_model_requests(True):
        asyncio.run(run())
    assert len(QUESTIONS) == 2
    assert all(len(session) == 5 for session in QUESTIONS)
