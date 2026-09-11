"""Controlled payload measurements: UTF-8 bytes are deliberately not called tokens."""

import asyncio
import copy
import json
from types import SimpleNamespace

import pytest
from pydantic import ValidationError
from pydantic_ai.messages import ModelRequest, ModelResponse, TextPart, ToolCallPart, ToolReturnPart

from terraria_wiki_agent import (
    TerrariaWikiAgent,
    WikiAgentError,
    WikiConversationTurn,
    WikiToolConnection,
)
from terraria_wiki_agent.agent import _EventEmitter, _RunEvidence, _WikiReadResponse

from .fakes import (
    OfflineFunctionModel,
    ScriptedModel,
    fake_toolset_factory,
    read_response,
    search_response,
)


def payload_bytes(value):
    return len(json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))


def test_fixed_sequence_payload_measurement():
    history = tuple(WikiConversationTurn(user=f"问题{i}", assistant=f"回答{i}") for i in range(4))
    script = ScriptedModel(
        [
            ("search_wiki", {"query": "配方"}),
            ("read_wiki", {"title": "月镐"}),
            ("read_wiki", {"title": "月镐"}),
        ]
    )
    raw_read = read_response("月镐", content="需要12夜银锭、3棱镜核心，在远古操纵机制作。" * 100)
    raw_search = search_response(*(f"候选{i}" for i in range(50)))

    def handler(name, args):
        return raw_search if name == "search_wiki" else raw_read

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=script.model, _toolset_factory=fake_toolset_factory(handler)
        ).answer("配方？", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"), history=history)
    )
    returns = [
        part
        for message in script.messages[-1]
        if isinstance(message, ModelRequest)
        for part in message.parts
        if isinstance(part, ToolReturnPart)
    ]
    sizes = [payload_bytes(part.content) for part in returns]
    descriptions = sum(
        len(tool.description.encode("utf-8")) for tool in script.infos[0].function_tools
    )
    requests = []
    for index, messages in enumerate(script.messages):
        current_returns = tool_returns(messages)
        assert len(current_returns) == index
        requests.append(
            {
                "history_text_bytes": sum(
                    len(m.parts[0].content.encode("utf-8")) for m in messages[:8]
                ),
                "tool_return_bytes": sum(payload_bytes(p.content) for p in current_returns),
                "input_schema_bytes": sum(
                    payload_bytes(t.parameters_json_schema)
                    for t in script.infos[index].function_tools
                ),
                "instructions_bytes": len(script.infos[index].instructions.encode("utf-8")),
            }
        )
    print(
        json.dumps(
            {
                "description_bytes": descriptions,
                "tool_payload_bytes": sizes,
                "raw_search_bytes": payload_bytes(raw_search),
                "raw_read_bytes": payload_bytes(raw_read),
                "requests": requests,
            },
            ensure_ascii=False,
        )
    )
    assert answer.status == "answered"
    assert len(returns) == 3
    assert sizes[0] < payload_bytes(raw_search)
    assert sizes[1] < payload_bytes(raw_read)
    assert sizes[2] < sizes[1] / 10
    assert descriptions < 2641
    assert returns[0].content["results"] == [
        {key: value for key, value in item.items() if key != "highlights"}
        for item in raw_search["results"]
    ]
    assert returns[1].content["evidence"][0]["blocks"][0]["text"] == raw_read["blocks"][0]["text"]
    assert returns[2].content["evidence"][0]["blocks"] == [{"ref": "e1"}]
    for messages in script.messages:
        assert [message.parts[0].content for message in messages[:8]] == [
            text for turn in history for text in (turn.user, turn.assistant)
        ]


def v2_read(title="页#配方"):
    result = read_response(title, title="页", redirect=False)
    result["content_format"] = "wiki_blocks_v2"
    result["scope"] = {
        "mode": "section",
        "selected_section": "页 > 配方",
        "anchor_found": True,
        "page_complete": False,
        "content_version": "A" * 64,
        "continuation_token": None,
        "sections": [{"path": "页 > 配方", "anchor": "配方"}],
    }
    return result


@pytest.mark.parametrize(
    "mutation",
    [
        lambda r: r.update(content_format="wiki_blocks_v1"),
        lambda r: r["scope"].update(page_complete=True),
        lambda r: r["scope"].update(continuation_token="A" * 64),
        lambda r: r["scope"].update(selected_section=None),
        lambda r: r["scope"].update(anchor_found=False),
        lambda r: r["scope"].update(content_version="unknown"),
        lambda r: r["scope"]["sections"][0].update(anchor="<script>x</script>"),
        lambda r: r.update(extra="forged"),
    ],
)
def test_v2_scope_rejects_ambiguous_or_unsafe_contracts(mutation):
    result = v2_read()
    mutation(result)
    with pytest.raises(ValidationError):
        _WikiReadResponse.model_validate(result)


@pytest.mark.parametrize("change", ["version", "timestamp", "text", "section", "missing_version"])
def test_changed_or_unversioned_evidence_is_not_replaced_with_reference(change):
    args = {"title": "页#配方"}
    if change != "missing_version":
        args["mode"] = "section"
    script = ScriptedModel(
        [
            ("search_wiki", {"query": "页"}),
            ("read_wiki", args),
            ("read_wiki", args),
        ]
    )
    count = 0

    def handler(name, args):
        nonlocal count
        if name == "search_wiki":
            return search_response("页")
        result = v2_read()
        count += 1
        if change == "missing_version":
            result.pop("scope")
            result["content_format"] = "wiki_blocks_v1"
            result["last_modified"] = None
        elif count == 2:
            if change == "version":
                result["scope"]["content_version"] = "B" * 64
            elif change == "timestamp":
                result["last_modified"] = "2026-09-11T00:00:00Z"
            elif change == "text":
                result["blocks"][0]["text"] = "数量不同：0.5%，不能制作。"
                result["included_characters"] = len(result["blocks"][0]["text"])
            elif change == "section":
                result["blocks"][0]["section_path"] = "页 > 大师模式"
        return result

    asyncio.run(
        TerrariaWikiAgent(
            model=script.model, _toolset_factory=fake_toolset_factory(handler)
        ).answer("配方？", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    returns = tool_returns(script.messages[-1])
    assert "text" in returns[-1].content["evidence"][0]["blocks"][0]
    assert count == 2


def tool_returns(messages):
    return [
        part
        for message in messages
        if isinstance(message, ModelRequest)
        for part in message.parts
        if isinstance(part, ToolReturnPart)
    ]


def test_direct_read_is_retried_without_invoking_tool_or_streaming_unsupported_answer():
    script = ScriptedModel([("read_wiki", {"title": "页"})], markdown="UNSUPPORTED_CLAIM")
    calls = []
    events = []

    def handler(name, args):
        calls.append(name)
        return read_response(args["title"])

    async def emit(event):
        events.append(event)

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=script.model, _toolset_factory=fake_toolset_factory(handler)
        ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"), emit)
    )
    assert calls == []
    assert answer.status == "insufficient_evidence"
    assert answer.sources == ()
    assert "".join(e.delta for e in events if e.type == "text_delta") == answer.markdown
    assert "UNSUPPORTED_CLAIM" not in repr(events)


def test_direct_read_can_recover_via_bounded_search_retry():
    script = ScriptedModel(
        [
            ("read_wiki", {"title": "页"}),
            ("search_wiki", {"query": "别名"}),
            ("read_wiki", {"title": "多跳页"}),
        ]
    )
    calls = []

    def handler(name, args):
        calls.append(name)
        return (
            search_response("其他标题") if name == "search_wiki" else read_response(args["title"])
        )

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=script.model, _toolset_factory=fake_toolset_factory(handler)
        ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    assert answer.status == "answered"
    assert calls == ["search_wiki", "read_wiki"]


@pytest.mark.parametrize("search_delay", [0, 0.02])
def test_search_and_read_in_same_batch_cannot_satisfy_ordering(search_delay):
    index = 0
    calls = []

    async def respond(messages, info):
        nonlocal index
        index += 1
        if index == 1:
            return ModelResponse(
                parts=[
                    ToolCallPart("search_wiki", {"query": "页"}),
                    ToolCallPart("read_wiki", {"title": "页"}),
                ]
            )
        return ModelResponse(parts=[TextPart("UNSUPPORTED_CLAIM")])

    async def handler(name, args):
        calls.append(name)
        if name == "search_wiki":
            await asyncio.sleep(search_delay)
            return search_response("页")
        return read_response(args["title"])

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=OfflineFunctionModel(respond), _toolset_factory=fake_toolset_factory(handler)
        ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    assert calls == ["search_wiki"]
    assert answer.status == "insufficient_evidence"


def test_parallel_repeated_reads_always_send_real_content_before_references():
    index = 0
    snapshots = []
    calls = []

    async def respond(messages, info):
        nonlocal index
        snapshots.append(copy.deepcopy(messages))
        index += 1
        if index == 1:
            return ModelResponse(parts=[ToolCallPart("search_wiki", {"query": "页"})])
        if index == 2:
            return ModelResponse(
                parts=[
                    ToolCallPart("read_wiki", {"title": "页"}, tool_call_id=f"r{i}")
                    for i in range(2)
                ]
            )
        if index == 3:
            return ModelResponse(
                parts=[ToolCallPart("read_wiki", {"title": "页"}, tool_call_id="r2")]
            )
        return ModelResponse(parts=[TextPart("回答")])

    async def handler(name, args):
        calls.append(name)
        await asyncio.sleep(0)
        return search_response("页") if name == "search_wiki" else read_response(args["title"])

    asyncio.run(
        TerrariaWikiAgent(
            model=OfflineFunctionModel(respond), _toolset_factory=fake_toolset_factory(handler)
        ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    batch = tool_returns(snapshots[2])[1:]
    assert all("text" in part.content["evidence"][0]["blocks"][0] for part in batch)
    assert tool_returns(snapshots[-1])[-1].content["evidence"][0]["blocks"] == [{"ref": "e1"}]
    assert calls == ["search_wiki", "read_wiki", "read_wiki", "read_wiki"]


def test_missing_section_produces_no_source_or_successful_answer():
    script = ScriptedModel(
        [("search_wiki", {"query": "页"}), ("read_wiki", {"title": "页#配方", "mode": "section"})]
    )
    result = v2_read()
    result.update(blocks=[], included_characters=0)
    result["scope"].update(anchor_found=False, selected_section=None)
    answer = asyncio.run(
        TerrariaWikiAgent(
            model=script.model,
            _toolset_factory=fake_toolset_factory(
                lambda name, args: search_response("页") if name == "search_wiki" else result
            ),
        ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    assert answer.status == "insufficient_evidence"
    assert answer.sources == ()
    assert answer.tool_trace[-1].status == "not_found"


def test_overlap_references_only_existing_blocks_and_reuses_catalog_without_losing_continuation():
    evidence = _RunEvidence(_EventEmitter(None))
    first = v2_read()
    first.update(is_complete=False, next_cursor=1)
    first["scope"]["continuation_token"] = "B" * 64
    context = SimpleNamespace(messages=[])
    projected = evidence._project_read(context, _WikiReadResponse.model_validate(first))
    context.messages = [ModelRequest(parts=[ToolReturnPart("read_wiki", projected)])]
    second = v2_read()
    second["blocks"].append(
        {"section_path": "页 > 配方", "kind": "paragraph", "text": "新增：困难模式。"}
    )
    second["included_characters"] = sum(len(b["text"]) for b in second["blocks"])
    overlapping = evidence._project_read(context, _WikiReadResponse.model_validate(second))
    assert overlapping["evidence"][0]["blocks"] == [{"ref": "e1"}]
    assert overlapping["evidence"][1]["blocks"][0]["text"] == "新增：困难模式。"
    assert overlapping["catalog_ref"] == projected["catalog_id"]
    assert "available_sections" not in overlapping
    assert overlapping["next_cursor"] is None
    assert projected["scope"]["continuation_token"] == "B" * 64
    # An external history processor dropping the original must never leave dangling refs.
    context.messages = []
    restored = evidence._project_read(context, _WikiReadResponse.model_validate(second))
    assert all("text" in b for g in restored["evidence"] for b in g["blocks"])
    assert "available_sections" in restored


def test_duplicate_reads_still_exhaust_the_original_request_or_tool_budget():
    script = ScriptedModel(
        [("search_wiki", {"query": "页"})] + [("read_wiki", {"title": "页"})] * 8
    )
    with pytest.raises(WikiAgentError) as error:
        asyncio.run(
            TerrariaWikiAgent(
                model=script.model,
                _toolset_factory=fake_toolset_factory(
                    lambda name, args: (
                        search_response("页")
                        if name == "search_wiki"
                        else read_response(args["title"])
                    )
                ),
            ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
        )
    assert error.value.code == "budget_exceeded"


def test_forged_read_request_identity_is_rejected_before_source_or_next_request():
    script = ScriptedModel([("search_wiki", {"query": "页"}), ("read_wiki", {"title": "页"})])
    with pytest.raises(WikiAgentError) as error:
        asyncio.run(
            TerrariaWikiAgent(
                model=script.model,
                _toolset_factory=fake_toolset_factory(
                    lambda name, args: (
                        search_response("页")
                        if name == "search_wiki"
                        else read_response("伪造请求")
                    )
                ),
            ).answer("问题", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
        )
    assert error.value.code == "wiki_tool_unavailable"
    assert len(script.messages) == 2
