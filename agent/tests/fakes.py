from __future__ import annotations

import inspect
from collections.abc import Awaitable, Callable, Sequence
from decimal import Decimal
from typing import Any, Self

from mcp import types as mcp_types
from pydantic_ai import ModelResponse, TextPart, ToolCallPart
from pydantic_ai.mcp import MCPToolset
from pydantic_ai.models.function import AgentInfo, DeltaToolCall, FunctionModel
from pydantic_ai.models.test import TestModel
from pydantic_ai.usage import RequestUsage

from terraria_wiki_agent import WikiAgentSettings, WikiToolConnection

type ToolHandler = Callable[[str, dict[str, Any]], dict[str, Any] | Awaitable[dict[str, Any]]]


class OfflineFunctionModel(FunctionModel):
    def __init__(self, function: Any):
        async def stream(messages: list[Any], info: AgentInfo):
            response = function(messages, info)
            if inspect.isawaitable(response):
                response = await response
            assert isinstance(response, ModelResponse)
            for index, part in enumerate(response.parts):
                if isinstance(part, TextPart):
                    midpoint = max(1, len(part.content) // 2)
                    yield part.content[:midpoint]
                    if part.content[midpoint:]:
                        yield part.content[midpoint:]
                elif isinstance(part, ToolCallPart):
                    yield {
                        index: DeltaToolCall(
                            name=part.tool_name,
                            json_args=part.args_as_json_str(),
                            tool_call_id=part.tool_call_id,
                        )
                    }
                else:
                    raise AssertionError(f"Unsupported scripted part: {type(part).__name__}")

        super().__init__(function, stream_function=stream)

    async def count_tokens(self, *args: Any, **kwargs: Any) -> RequestUsage:
        del args, kwargs
        return RequestUsage(input_tokens=1)


class OfflineTestModel(TestModel):
    async def count_tokens(self, *args: Any, **kwargs: Any) -> RequestUsage:
        del args, kwargs
        return RequestUsage(input_tokens=1)

    async def request(self, *args: Any, **kwargs: Any) -> ModelResponse:
        response = await super().request(*args, **kwargs)
        response.usage.cost = Decimal("0")
        return response


_SEARCH_OUTPUT_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "results": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "title": {"type": "string"},
                    "redirect_target": {"type": "string"},
                    "summary": {"type": "string"},
                    "match_type": {"type": "string"},
                    "highlights": {"type": "array", "items": {"type": "object"}},
                },
                "required": [
                    "title",
                    "redirect_target",
                    "summary",
                    "match_type",
                    "highlights",
                ],
            },
        }
    },
    "required": ["results"],
}

_READ_OUTPUT_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "requested_title": {"type": "string"},
        "requested_page_title": {"type": "string"},
        "status": {"type": "string"},
        "is_found": {"type": "boolean"},
        "title": {"type": ["string", "null"]},
        "content": {"type": ["string", "null"]},
        "last_modified": {"type": ["string", "null"]},
        "is_redirect": {"type": "boolean"},
        "redirect_from_title": {"type": ["string", "null"]},
        "redirect_target_title": {"type": ["string", "null"]},
        "redirect_target_anchor": {"type": ["string", "null"]},
        "anchor": {"type": ["string", "null"]},
    },
    "required": [
        "requested_title",
        "requested_page_title",
        "status",
        "is_found",
        "title",
        "content",
        "last_modified",
        "is_redirect",
        "redirect_from_title",
        "redirect_target_title",
        "redirect_target_anchor",
        "anchor",
    ],
}


def tool_definitions(names: Sequence[str] = ("search_wiki", "read_wiki")) -> list[mcp_types.Tool]:
    definitions = {
        "search_wiki": mcp_types.Tool(
            name="search_wiki",
            description="Search the local Wiki.",
            inputSchema={
                "type": "object",
                "properties": {
                    "query": {"type": "string"},
                    "max_results": {"type": ["integer", "null"]},
                },
                "required": ["query"],
            },
            outputSchema=_SEARCH_OUTPUT_SCHEMA,
        ),
        "read_wiki": mcp_types.Tool(
            name="read_wiki",
            description="Read one local Wiki page.",
            inputSchema={
                "type": "object",
                "properties": {"title": {"type": "string"}},
                "required": ["title"],
            },
            outputSchema=_READ_OUTPUT_SCHEMA,
        ),
        "unexpected": mcp_types.Tool(
            name="unexpected",
            inputSchema={"type": "object", "properties": {}},
        ),
    }
    return [definitions[name] for name in names]


class FakeWikiToolset(MCPToolset):
    """MCPToolset test double that exercises the production process callback."""

    def __init__(
        self,
        handler: ToolHandler,
        process_tool_call: Any,
        names: Sequence[str] = ("search_wiki", "read_wiki"),
    ):
        super().__init__(
            "http://127.0.0.1:1/mcp",
            process_tool_call=process_tool_call,
            tool_error_behavior="error",
            include_return_schema=True,
            prefer_tasks=False,
        )
        self._handler = handler
        self._definitions = tool_definitions(names)

    async def __aenter__(self) -> Self:
        return self

    async def __aexit__(self, *args: Any) -> bool | None:
        return None

    async def list_tools(self) -> list[mcp_types.Tool]:
        return self._definitions

    async def direct_call_tool(
        self,
        name: str,
        args: dict[str, Any],
        *,
        metadata: dict[str, Any] | None = None,
        use_task: bool = False,
    ) -> Any:
        del metadata, use_task
        value = self._handler(name, args)
        return await value if inspect.isawaitable(value) else value


def fake_toolset_factory(
    handler: ToolHandler,
    names: Sequence[str] = ("search_wiki", "read_wiki"),
) -> Any:
    def factory(
        connection: WikiToolConnection,
        evidence: Any,
        settings: WikiAgentSettings,
    ) -> MCPToolset:
        del connection, settings
        return FakeWikiToolset(handler, evidence.process_tool_call, names)

    return factory


def search_response(*titles: str) -> dict[str, Any]:
    return {
        "results": [
            {
                "title": title,
                "redirect_target": "",
                "summary": f"{title} summary",
                "match_type": "exact_title",
                "highlights": [],
            }
            for title in titles
        ]
    }


def read_response(
    requested_title: str,
    *,
    title: str = "月铸镐",
    anchor: str | None = "配方",
    content: str = "<p>需要12夜银锭、3棱镜核心，在远古操纵机制作。</p>",
    redirect: bool = True,
) -> dict[str, Any]:
    requested_page_title = requested_title.partition("#")[0]
    return {
        "requested_title": requested_title,
        "requested_page_title": requested_page_title,
        "status": "found",
        "is_found": True,
        "title": title,
        "content": content,
        "last_modified": "2026-08-24T12:34:56Z",
        "is_redirect": redirect,
        "redirect_from_title": requested_page_title if redirect else None,
        "redirect_target_title": title if redirect else None,
        "redirect_target_anchor": anchor if redirect else None,
        "anchor": anchor,
    }


def not_found_response(requested_title: str) -> dict[str, Any]:
    return {
        "requested_title": requested_title,
        "requested_page_title": requested_title.partition("#")[0],
        "status": "not_found",
        "is_found": False,
        "title": None,
        "content": None,
        "last_modified": None,
        "is_redirect": False,
        "redirect_from_title": None,
        "redirect_target_title": None,
        "redirect_target_anchor": None,
        "anchor": None,
    }


class ScriptedModel:
    def __init__(
        self,
        tool_calls: Sequence[tuple[str, dict[str, Any]]],
        *,
        status: str = "answered",
        markdown: str = "基于本地 Wiki 的回答。",
        usages: Sequence[RequestUsage] | None = None,
    ):
        self._tool_calls = list(tool_calls)
        self._status = status
        self._markdown = markdown
        self._usages = list(usages or [])
        self.calls = 0
        self.infos: list[AgentInfo] = []
        self.model = OfflineFunctionModel(self._respond)

    async def _respond(self, messages: list[Any], info: AgentInfo) -> ModelResponse:
        del messages
        self.infos.append(info)
        index = self.calls
        self.calls += 1
        usage = (
            self._usages[index]
            if index < len(self._usages)
            else RequestUsage(input_tokens=20, output_tokens=5, cost=Decimal("0.000001"))
        )
        if index < len(self._tool_calls):
            name, args = self._tool_calls[index]
            return ModelResponse(parts=[ToolCallPart(name, args)], usage=usage)
        return ModelResponse(
            parts=[TextPart(self._markdown)],
            usage=usage,
        )
