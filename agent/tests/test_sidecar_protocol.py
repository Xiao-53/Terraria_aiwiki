from __future__ import annotations

import asyncio
import io
import json
import os
import subprocess
from pathlib import Path

import pytest

from terraria_wiki_agent import TerrariaWikiAgent
from terraria_wiki_agent.sidecar import (
    _ForbiddenStdout,
    _ProtocolWriter,
    _SidecarRuntime,
    _UnexpectedStdout,
    run_sidecar,
)
from terraria_wiki_agent.sidecar_protocol import (
    MAX_LINE_BYTES,
    SidecarProtocolError,
    encode_message,
    parse_command,
)

from .fakes import ScriptedModel, fake_toolset_factory, read_response, search_response

_INITIALIZE = {
    "version": 2,
    "type": "initialize",
    "requestId": "initialize-1",
    "provider": {
        "baseUrl": "https://api.example.test/v1",
        "model": "provider-model",
        "api": "responses",
        "reasoningEffort": None,
    },
    "wikiTools": {
        "endpoint": "http://127.0.0.1:1234/mcp",
        "bearerToken": "private-bearer",
    },
}


def _command(payload: dict[str, object]):
    return parse_command(encode_message(payload))


def _messages(output: io.BytesIO) -> list[dict[str, object]]:
    return [json.loads(line) for line in output.getvalue().splitlines()]


def _agent_factory(settings):
    scripted = ScriptedModel(
        [
            ("search_wiki", {"query": "月铸镐"}),
            ("read_wiki", {"title": "月铸镐"}),
        ],
        markdown="月铸镐需要材料并在工作台制作。",
    )

    def handler(name, args):
        if name == "search_wiki":
            return search_response("月铸镐")
        return read_response(args["title"], redirect=False)

    return TerrariaWikiAgent(
        settings,
        model=scripted.model,
        _toolset_factory=fake_toolset_factory(handler),
    )


def test_protocol_rejects_unknown_messages_fields_and_line_shapes() -> None:
    for payload in (
        {**_INITIALIZE, "unknown": True},
        {**_INITIALIZE, "type": "surprise"},
        {**_INITIALIZE, "version": 1},
        [],
    ):
        with pytest.raises(SidecarProtocolError) as error:
            parse_command(json.dumps(payload).encode() + b"\n")
        assert error.value.code == "invalid_message"

    with pytest.raises(SidecarProtocolError) as incomplete:
        parse_command(b"{}")
    assert incomplete.value.code == "incomplete_line"

    with pytest.raises(SidecarProtocolError) as oversized:
        parse_command(b"x" * MAX_LINE_BYTES + b"\n")
    assert oversized.value.code == "line_too_long"


def test_sidecar_streams_safe_events_and_completed_answer(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("TERRARIA_AI_API_KEY", "test-only-key")

    async def scenario():
        output = io.BytesIO()
        runtime = _SidecarRuntime(_ProtocolWriter(output), _agent_factory)
        await runtime.handle(_command(_INITIALIZE))
        await runtime.handle(
            _command(
                {
                    "version": 2,
                    "type": "answer",
                    "requestId": "answer-1",
                    "question": "月铸镐怎么制作？",
                    "history": [
                        {"user": "上一问", "assistant": "上一答"},
                    ],
                }
            )
        )
        assert runtime._active_task is not None
        await runtime._active_task
        return _messages(output)

    messages = asyncio.run(scenario())
    assert messages[0]["type"] == "ready"
    assert [message["type"] for message in messages[1:5]] == [
        "run_started",
        "tool_started",
        "tool_finished",
        "tool_started",
    ]
    completed = messages[-1]
    assert completed["type"] == "completed"
    answer = completed["answer"]
    assert isinstance(answer, dict)
    deltas = [message["delta"] for message in messages if message["type"] == "answer_delta"]
    assert len(deltas) >= 2
    assert "".join(deltas) == answer["markdown"]
    rendered = json.dumps(messages, ensure_ascii=False)
    assert "private-bearer" not in rendered
    assert "<p>" not in rendered


def test_concurrent_answer_is_busy_and_cancel_targets_the_active_run(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("TERRARIA_AI_API_KEY", "test-only-key")
    started = asyncio.Event()

    def factory(settings):
        scripted = ScriptedModel([("search_wiki", {"query": "wait"})])

        async def handler(name, args):
            del name, args
            started.set()
            await asyncio.Event().wait()
            return search_response()

        return TerrariaWikiAgent(
            settings,
            model=scripted.model,
            _toolset_factory=fake_toolset_factory(handler),
        )

    async def scenario():
        output = io.BytesIO()
        runtime = _SidecarRuntime(_ProtocolWriter(output), factory)
        await runtime.handle(_command(_INITIALIZE))
        await runtime.handle(
            _command(
                {
                    "version": 2,
                    "type": "answer",
                    "requestId": "answer-active",
                    "question": "等待",
                    "history": [],
                }
            )
        )
        await started.wait()
        await runtime.handle(
            _command(
                {
                    "version": 2,
                    "type": "answer",
                    "requestId": "answer-busy",
                    "question": "并发",
                    "history": [],
                }
            )
        )
        active = runtime._active_task
        await runtime.handle(
            _command(
                {
                    "version": 2,
                    "type": "cancel",
                    "requestId": "cancel-1",
                    "targetRequestId": "answer-active",
                }
            )
        )
        assert active is not None
        await active
        return _messages(output)

    messages = asyncio.run(scenario())
    busy = next(message for message in messages if message["requestId"] == "answer-busy")
    assert busy["type"] == "failed"
    assert busy["error"]["code"] == "runtime_busy"
    assert any(
        message["type"] == "cancelled" and message["requestId"] == "answer-active"
        for message in messages
    )


def test_duplicate_id_and_illegal_order_are_fatal(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("TERRARIA_AI_API_KEY", "test-only-key")

    async def duplicate():
        output = io.BytesIO()
        runtime = _SidecarRuntime(_ProtocolWriter(output), _agent_factory)
        await runtime.handle(_command(_INITIALIZE))
        await runtime.handle(_command(_INITIALIZE))
        return _messages(output)

    duplicate_messages = asyncio.run(duplicate())
    assert duplicate_messages[-1]["type"] == "fatal"
    assert duplicate_messages[-1]["error"]["code"] == "duplicate_request_id"

    async def illegal_order():
        output = io.BytesIO()
        runtime = _SidecarRuntime(_ProtocolWriter(output), _agent_factory)
        await runtime.handle(
            _command(
                {
                    "version": 2,
                    "type": "answer",
                    "requestId": "answer-before-ready",
                    "question": "问题",
                    "history": [],
                }
            )
        )
        return _messages(output)

    order_messages = asyncio.run(illegal_order())
    assert order_messages[-1]["error"]["code"] == "invalid_order"


def test_run_sidecar_shutdown_and_unexpected_eof(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("TERRARIA_AI_API_KEY", "test-only-key")
    shutdown = {
        "version": 2,
        "type": "shutdown",
        "requestId": "shutdown-1",
    }
    source = io.BytesIO(encode_message(_INITIALIZE) + encode_message(shutdown))
    output = io.BytesIO()
    assert asyncio.run(run_sidecar(source, output, agent_factory=_agent_factory)) == 0
    assert [message["type"] for message in _messages(output)] == ["ready"]

    source = io.BytesIO(encode_message(_INITIALIZE))
    output = io.BytesIO()
    assert asyncio.run(run_sidecar(source, output, agent_factory=_agent_factory)) == 1
    assert _messages(output)[-1]["error"]["code"] == "unexpected_eof"


def test_launcher_uses_stdout_only_for_protocol(monkeypatch: pytest.MonkeyPatch) -> None:
    launcher = Path(__file__).parents[1] / "sidecar_launcher.py"
    shutdown = {
        "version": 2,
        "type": "shutdown",
        "requestId": "shutdown-1",
    }
    environment = os.environ.copy()
    environment["TERRARIA_AI_API_KEY"] = "test-only-key"
    completed = subprocess.run(
        [os.sys.executable, "-I", "-u", "-X", "utf8", str(launcher)],
        input=encode_message(_INITIALIZE) + encode_message(shutdown),
        capture_output=True,
        check=False,
        env=environment,
        timeout=20,
    )
    assert completed.returncode == 0
    assert completed.stderr == b""
    messages = [json.loads(line) for line in completed.stdout.splitlines()]
    assert [message["type"] for message in messages] == ["ready"]


def test_stdout_guard_rejects_non_protocol_text() -> None:
    with pytest.raises(_UnexpectedStdout):
        _ForbiddenStdout().write("unexpected output")


def test_initialization_stdout_pollution_is_a_specific_fatal_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("TERRARIA_AI_API_KEY", "test-only-key")

    def polluted_factory(settings):
        del settings
        raise _UnexpectedStdout

    async def scenario():
        output = io.BytesIO()
        runtime = _SidecarRuntime(_ProtocolWriter(output), polluted_factory)
        await runtime.handle(_command(_INITIALIZE))
        return _messages(output)

    messages = asyncio.run(scenario())
    assert messages[-1]["type"] == "fatal"
    assert messages[-1]["error"]["code"] == "unexpected_stdout"
