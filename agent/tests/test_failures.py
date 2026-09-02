import asyncio
from decimal import Decimal

import pytest
from pydantic_ai import ModelResponse, ToolCallPart
from pydantic_ai.exceptions import ModelHTTPError
from pydantic_ai.models.function import AgentInfo
from pydantic_ai.usage import RequestUsage

from terraria_wiki_agent import (
    TerrariaWikiAgent,
    WikiAgentError,
    WikiAgentErrorCode,
    WikiAgentSettings,
    WikiToolConnection,
)

from .fakes import (
    OfflineFunctionModel,
    ScriptedModel,
    fake_toolset_factory,
    read_response,
    search_response,
)

_CONNECTION = WikiToolConnection("http://127.0.0.1:1234/mcp", "wiki-bearer-secret")


def assert_error(coro, code: WikiAgentErrorCode) -> WikiAgentError:
    with pytest.raises(WikiAgentError) as captured:
        asyncio.run(coro)
    assert captured.value.code == code
    return captured.value


def test_invalid_question_is_normalized_before_tools_or_model() -> None:
    scripted = ScriptedModel([])
    agent = TerrariaWikiAgent(
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )

    error = assert_error(agent.answer("  ", _CONNECTION), WikiAgentErrorCode.INVALID_INPUT)
    assert scripted.calls == 0
    assert "wiki-bearer-secret" not in str(error)
    assert "wiki-bearer-secret" not in repr(error)


def test_wrong_tool_discovery_is_wiki_tool_unavailable() -> None:
    scripted = ScriptedModel([])
    agent = TerrariaWikiAgent(
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(
            lambda name, args: search_response(),
            names=("search_wiki", "read_wiki", "unexpected"),
        ),
    )

    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.WIKI_TOOL_UNAVAILABLE)
    assert scripted.calls == 0


def test_tool_error_is_redacted_and_normalized() -> None:
    secret = "database-path-secret"
    scripted = ScriptedModel([("search_wiki", {"query": "月铸镐"})])

    def handler(name, args):
        raise RuntimeError(f"{secret}: <html>private</html>")

    error = assert_error(
        TerrariaWikiAgent(
            model=scripted.model,
            _toolset_factory=fake_toolset_factory(handler),
        ).answer("问题", _CONNECTION),
        WikiAgentErrorCode.WIKI_TOOL_UNAVAILABLE,
    )
    rendered = str(error) + repr(error)
    assert secret not in rendered
    assert "html" not in rendered.lower()
    assert "traceback" not in rendered.lower()


def test_caller_cancellation_propagates_as_task_cancellation() -> None:
    started = asyncio.Event()
    scripted = ScriptedModel([("search_wiki", {"query": "月铸镐"})])

    async def handler(name, args):
        del name, args
        started.set()
        await asyncio.Event().wait()
        return search_response()

    async def scenario() -> None:
        agent = TerrariaWikiAgent(
            model=scripted.model,
            _toolset_factory=fake_toolset_factory(handler),
        )
        task = asyncio.create_task(agent.answer("问题", _CONNECTION))
        await started.wait()
        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task

    asyncio.run(scenario())


def test_timeout_is_distinct_from_caller_cancellation() -> None:
    scripted = ScriptedModel([("search_wiki", {"query": "月铸镐"})])

    async def handler(name, args):
        del name, args
        await asyncio.sleep(1)
        return search_response()

    agent = TerrariaWikiAgent(
        WikiAgentSettings(timeout_seconds=0.01),
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(handler),
    )
    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.TIMEOUT)


@pytest.mark.parametrize(
    "settings",
    [
        WikiAgentSettings(output_tokens_limit=1),
        WikiAgentSettings(total_tokens_limit=1),
        WikiAgentSettings(per_request_input_tokens_limit=1),
    ],
)
def test_streamed_token_budgets_fail_explicitly(settings: WikiAgentSettings) -> None:
    scripted = ScriptedModel([], markdown="several streamed output words")
    agent = TerrariaWikiAgent(
        settings,
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )
    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.BUDGET_EXCEEDED)


def test_callback_failure_is_redacted_and_normalized() -> None:
    async def fail_callback(event) -> None:
        del event
        raise RuntimeError("callback-path-secret")

    agent = TerrariaWikiAgent(
        model=ScriptedModel([]).model,
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )
    error = assert_error(
        agent.answer("问题", _CONNECTION, fail_callback),
        WikiAgentErrorCode.UNKNOWN,
    )
    assert "callback-path-secret" not in str(error) + repr(error)


def test_oversized_streamed_output_is_a_stable_model_failure() -> None:
    scripted = ScriptedModel([], markdown="x" * 32_769)
    agent = TerrariaWikiAgent(
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )
    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.MODEL_UNAVAILABLE)


def test_request_limit_stops_a_model_loop() -> None:
    calls = 0

    async def respond(messages, info: AgentInfo) -> ModelResponse:
        nonlocal calls
        del messages, info
        calls += 1
        return ModelResponse(
            parts=[ToolCallPart("search_wiki", {"query": "loop"})],
            usage=RequestUsage(input_tokens=1, output_tokens=1, cost=Decimal("0.000001")),
        )

    agent = TerrariaWikiAgent(
        model=OfflineFunctionModel(respond),
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )
    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.BUDGET_EXCEEDED)
    assert calls == 8


def test_tool_call_limit_rejects_projected_parallel_calls_before_execution() -> None:
    executions = 0

    async def respond(messages, info: AgentInfo) -> ModelResponse:
        del messages, info
        return ModelResponse(
            parts=[ToolCallPart("search_wiki", {"query": str(index)}) for index in range(9)],
            usage=RequestUsage(input_tokens=1, output_tokens=1, cost=Decimal("0.000001")),
        )

    def handler(name, args):
        nonlocal executions
        executions += 1
        return search_response()

    agent = TerrariaWikiAgent(
        model=OfflineFunctionModel(respond),
        _toolset_factory=fake_toolset_factory(handler),
    )
    assert_error(agent.answer("问题", _CONNECTION), WikiAgentErrorCode.BUDGET_EXCEEDED)
    assert executions == 0


@pytest.mark.parametrize(
    ("status", "body", "code"),
    [
        (401, {"error": {"message": "api-key-secret"}}, WikiAgentErrorCode.MODEL_AUTHENTICATION),
        (403, None, WikiAgentErrorCode.MODEL_AUTHENTICATION),
        (
            400,
            {"error": {"code": "context_length_exceeded", "message": "<html>huge</html>"}},
            WikiAgentErrorCode.CONTEXT_LIMIT,
        ),
        (429, {"error": {"message": "rate limited"}}, WikiAgentErrorCode.MODEL_UNAVAILABLE),
        (503, {"internal_path": "C:\\private\\model"}, WikiAgentErrorCode.MODEL_UNAVAILABLE),
    ],
)
def test_model_http_errors_are_classified_without_leaking_provider_body(
    status: int,
    body: object,
    code: WikiAgentErrorCode,
) -> None:
    secret = "api-key-secret"

    async def fail(messages, info):
        del messages, info
        raise ModelHTTPError(status, "gpt-5.6-luna", body)

    agent = TerrariaWikiAgent(
        model=OfflineFunctionModel(fail),
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response()),
    )
    error = assert_error(agent.answer("问题", _CONNECTION), code)
    rendered = str(error) + repr(error)
    assert secret not in rendered
    assert "private" not in rendered
    assert "html" not in rendered.lower()


def test_successful_read_is_required_even_if_search_and_model_claim_an_answer() -> None:
    scripted = ScriptedModel(
        [("search_wiki", {"query": "月铸镐"})],
        status="answered",
        markdown="12夜银锭。",
    )
    agent = TerrariaWikiAgent(
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(lambda name, args: search_response("月铸镐")),
    )
    answer = asyncio.run(agent.answer("问题", _CONNECTION))
    assert answer.status == "insufficient_evidence"
    assert answer.sources == ()


def test_read_fixture_does_not_leak_html_into_source_metadata() -> None:
    scripted = ScriptedModel(
        [
            ("search_wiki", {"query": "月铸镐"}),
            ("read_wiki", {"title": "月铸镐"}),
        ]
    )

    def handler(name, args):
        return search_response("月铸镐") if name == "search_wiki" else read_response(args["title"])

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=scripted.model,
            _toolset_factory=fake_toolset_factory(handler),
        ).answer("问题", _CONNECTION)
    )
    assert "<" not in repr(answer.sources)
