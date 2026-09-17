import asyncio
import copy
from types import SimpleNamespace

import pytest
from fastmcp.exceptions import ToolError
from pydantic import ValidationError
from pydantic_ai import ModelRetry
from pydantic_ai.messages import ModelRequest, ToolReturnPart

from terraria_wiki_agent import TerrariaWikiAgent, WikiToolConnection
from terraria_wiki_agent.agent import (
    _EventEmitter,
    _RunEvidence,
    _WikiSearchResponse,
    _WikiToolUnavailable,
)

from .fakes import ScriptedModel, fake_toolset_factory, read_response, search_response


def navigation_response():
    response = search_response("别名")
    response["results"][0].update(
        canonical_title="规范页",
        match_scope="same_section",
        matched_sections=[{"section_path": "规范页 > 配方", "anchor": "recipe-real"}],
        matched_sections_truncated=False,
    )
    return response


def test_legacy_search_has_no_invented_navigation():
    parsed = _WikiSearchResponse.model_validate(search_response("页"))
    assert "canonical_title" not in parsed.results[0].model_dump(exclude_none=True)


@pytest.mark.parametrize(
    "code,token,recoverable",
    [
        ("invalid-arguments", "old-token", True),
        ("invalid-arguments", None, False),
        ("wiki-tool-paused", "old-token", False),
        ("private exception detail", "old-token", False),
    ],
)
def test_only_invalid_continuations_enter_existing_bounded_retry(code, token, recoverable):
    evidence = _RunEvidence(emitter=_EventEmitter(None))
    evidence._search_steps.add(1)

    async def fail(name, args):
        raise ToolError(code)

    with pytest.raises(ModelRetry if recoverable else _WikiToolUnavailable) as error:
        asyncio.run(
            evidence.process_tool_call(
                SimpleNamespace(run_step=2),
                fail,
                "read_wiki",
                {"title": "页", "cursor": 1, "continuation_token": token},
            )
        )
    assert not evidence.sources
    assert "private exception detail" not in str(error.value)


@pytest.mark.parametrize(
    "change",
    [
        {"canonical_title": None},
        {"canonical_title": ""},
        {"canonical_title": "<script>bad</script>"},
        {"match_scope": "complete"},
        {"matched_sections_truncated": "false"},
        {"matched_sections": [{"section_path": "配方", "anchor": "<b>bad</b>"}]},
        {"matched_sections": [{"section_path": "a", "anchor": "x"}] * 2},
        {"matched_sections": [{"section_path": str(i), "anchor": ""} for i in range(6)]},
        {"matched_sections": [{"section_path": "😀" * 2048, "anchor": "x"}]},
    ],
)
def test_malformed_navigation_fails_closed(change):
    raw = navigation_response()
    raw["results"][0].update(change)
    with pytest.raises(ValidationError):
        _WikiSearchResponse.model_validate(raw)


@pytest.mark.parametrize(
    "missing", ["canonical_title", "match_scope", "matched_sections", "matched_sections_truncated"]
)
def test_partial_navigation_fails_closed(missing):
    raw = navigation_response()
    del raw["results"][0][missing]
    with pytest.raises(ValidationError):
        _WikiSearchResponse.model_validate(raw)


def test_navigation_unicode_budget_and_empty_anchor():
    raw = navigation_response()
    raw["results"][0]["matched_sections"] = [{"section_path": "😀" * 2048, "anchor": ""}]
    assert _WikiSearchResponse.model_validate(raw).results[0].matched_sections[0].anchor == ""


def test_search_projection_retains_navigation_but_only_reads_create_sources():
    raw = navigation_response()
    script = ScriptedModel(
        [
            ("search_wiki", {"query": "别名 配方"}),
            ("read_wiki", {"title": "规范页#recipe-real", "mode": "section"}),
        ]
    )
    calls = []

    def handler(name, args):
        calls.append((name, copy.deepcopy(args)))
        if name == "search_wiki":
            return raw
        result = read_response(
            args["title"],
            title="规范页",
            anchor="recipe-real",
            content="材料12个。",
            redirect=False,
        )
        result.update(
            content_format="wiki_blocks_v2",
            scope={
                "mode": "section",
                "selected_section": "配方",
                "anchor_found": True,
                "page_complete": False,
                "content_version": "A" * 64,
                "continuation_token": None,
                "sections": [{"path": "配方", "anchor": "recipe-real"}],
            },
        )
        return result

    answer = asyncio.run(
        TerrariaWikiAgent(
            model=script.model, _toolset_factory=fake_toolset_factory(handler)
        ).answer("别名配方？", WikiToolConnection("http://127.0.0.1:1234/mcp", "test"))
    )
    projected = next(
        part.content
        for message in script.messages[-1]
        if isinstance(message, ModelRequest)
        for part in message.parts
        if isinstance(part, ToolReturnPart) and part.tool_name == "search_wiki"
    )["results"][0]
    assert "highlights" not in projected
    assert projected["canonical_title"] == "规范页"
    assert projected["matched_sections"] == raw["results"][0]["matched_sections"]
    assert "max_results" not in calls[0][1]
    assert calls[1][1]["title"] == "规范页#recipe-real"
    assert len(answer.sources) == 1
    assert answer.sources[0].resolved_title == "规范页"
