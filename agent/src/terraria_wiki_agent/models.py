from __future__ import annotations

from datetime import datetime
from decimal import Decimal
from enum import StrEnum
from ipaddress import ip_address
from typing import Annotated, Literal, Self
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, SecretStr, model_validator
from pydantic_ai.models.openai import OpenAIChatModelSettings, OpenAIResponsesModelSettings
from pydantic_ai.usage import UsageLimits

from .errors import WikiAgentError, WikiAgentErrorCode


class WikiAnswerStatus(StrEnum):
    ANSWERED = "answered"
    INSUFFICIENT_EVIDENCE = "insufficient_evidence"


class WikiAgentToolCallStatus(StrEnum):
    SUCCEEDED = "succeeded"
    NOT_FOUND = "not_found"


class _PublicModel(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class WikiAgentSource(_PublicModel):
    requested_title: str
    resolved_title: str
    anchor: str | None = None
    last_modified: datetime | None = None


class WikiAgentToolCall(_PublicModel):
    name: Literal["search_wiki", "read_wiki"]
    status: WikiAgentToolCallStatus


class WikiAgentUsage(_PublicModel):
    requests: int = Field(ge=0)
    tool_calls: int = Field(ge=0)
    input_tokens: int = Field(ge=0)
    cached_input_tokens: int = Field(default=0, ge=0)
    output_tokens: int = Field(ge=0)
    total_tokens: int = Field(ge=0)
    cost_usd: Decimal | None = Field(default=None, ge=0)


class WikiAgentAnswer(_PublicModel):
    status: WikiAnswerStatus
    markdown: str = Field(min_length=1)
    sources: tuple[WikiAgentSource, ...] = ()
    usage: WikiAgentUsage
    tool_trace: tuple[WikiAgentToolCall, ...] = ()


class WikiConversationTurn(_PublicModel):
    user: str = Field(min_length=1, max_length=32_768)
    assistant: str = Field(min_length=1, max_length=32_768)


class WikiAgentRunStarted(_PublicModel):
    type: Literal["run_started"] = "run_started"


class WikiAgentToolStarted(_PublicModel):
    type: Literal["tool_started"] = "tool_started"
    name: Literal["search_wiki", "read_wiki"]


class WikiAgentToolFinished(_PublicModel):
    type: Literal["tool_finished"] = "tool_finished"
    name: Literal["search_wiki", "read_wiki"]
    status: Literal["succeeded", "not_found", "failed"]


class WikiAgentTextDelta(_PublicModel):
    type: Literal["text_delta"] = "text_delta"
    delta: str = Field(min_length=1)


type WikiAgentEvent = Annotated[
    WikiAgentRunStarted | WikiAgentToolStarted | WikiAgentToolFinished | WikiAgentTextDelta,
    Field(discriminator="type"),
]


class WikiAgentSettings(_PublicModel):
    """OpenAI-compatible provider settings plus downward-configurable runtime limits."""

    base_url: str | None = None
    model: str | None = None
    api: Literal["responses", "chat_completions"] = "responses"
    reasoning_effort: Literal["none", "low", "medium", "high", "xhigh", "max"] | None = None
    request_limit: int = Field(default=8, ge=1, le=8)
    tool_calls_limit: int = Field(default=8, ge=1, le=8)
    output_tokens_limit: int = Field(default=4096, ge=1, le=4096)
    total_tokens_limit: int = Field(default=300_000, ge=1, le=300_000)
    per_request_input_tokens_limit: int = Field(default=256_000, ge=1, le=256_000)
    timeout_seconds: float = Field(default=120.0, gt=0, le=120.0)

    @model_validator(mode="after")
    def validate_provider_configuration(self) -> Self:
        if (self.base_url is None) != (self.model is None):
            raise ValueError("base_url and model must be configured together")
        if self.base_url is None:
            return self

        if self.base_url != self.base_url.strip() or len(self.base_url) > 2048:
            raise ValueError("base_url must be canonical")
        try:
            parsed = urlsplit(self.base_url)
            port = parsed.port
        except ValueError:
            raise ValueError("base_url is invalid") from None
        if (
            parsed.scheme not in {"http", "https"}
            or parsed.hostname is None
            or parsed.username is not None
            or parsed.password is not None
            or parsed.query
            or parsed.fragment
        ):
            raise ValueError("base_url must be a safe HTTP endpoint")
        if parsed.scheme == "http":
            try:
                is_loopback = ip_address(parsed.hostname).is_loopback
            except ValueError:
                is_loopback = False
            if not is_loopback:
                raise ValueError("plain HTTP is allowed only for literal loopback endpoints")
        if port is not None and not 1 <= port <= 65535:
            raise ValueError("base_url port is invalid")

        assert self.model is not None
        if (
            self.model != self.model.strip()
            or not self.model
            or len(self.model) > 256
            or any(ord(character) < 0x20 for character in self.model)
        ):
            raise ValueError("model must be a safe non-empty identifier")
        return self

    def model_settings(self) -> OpenAIResponsesModelSettings | OpenAIChatModelSettings:
        if self.api == "responses":
            responses = OpenAIResponsesModelSettings(
                max_tokens=self.output_tokens_limit,
                timeout=self.timeout_seconds,
            )
            if self.reasoning_effort is not None:
                responses["openai_reasoning_effort"] = self.reasoning_effort
            return responses

        chat = OpenAIChatModelSettings(
            max_tokens=self.output_tokens_limit,
            timeout=self.timeout_seconds,
        )
        if self.reasoning_effort is not None:
            chat["openai_reasoning_effort"] = self.reasoning_effort
        return chat

    def usage_limits(self) -> UsageLimits:
        return UsageLimits(
            request_limit=self.request_limit,
            tool_calls_limit=self.tool_calls_limit,
            output_tokens_limit=self.output_tokens_limit,
            total_tokens_limit=self.total_tokens_limit,
            per_request_input_tokens_limit=self.per_request_input_tokens_limit,
            count_tokens_before_request=False,
        )


class WikiToolConnection:
    """A redacted, exact loopback MCP connection value."""

    __slots__ = ("_bearer_token", "endpoint")

    def __init__(self, endpoint: str, bearer_token: str):
        if not isinstance(endpoint, str) or not isinstance(bearer_token, str):
            raise WikiAgentError(WikiAgentErrorCode.INVALID_CONNECTION)
        try:
            parsed = urlsplit(endpoint)
            port = parsed.port
        except ValueError:
            raise WikiAgentError(WikiAgentErrorCode.INVALID_CONNECTION) from None

        if (
            parsed.scheme != "http"
            or parsed.hostname != "127.0.0.1"
            or port is None
            or port <= 0
            or endpoint != f"http://127.0.0.1:{port}/mcp"
            or parsed.username is not None
            or parsed.password is not None
            or parsed.query
            or parsed.fragment
        ):
            raise WikiAgentError(WikiAgentErrorCode.INVALID_CONNECTION)
        if (
            not bearer_token
            or len(bearer_token) > 4096
            or any(character.isspace() or ord(character) < 0x20 for character in bearer_token)
        ):
            raise WikiAgentError(WikiAgentErrorCode.INVALID_CONNECTION)

        self.endpoint = endpoint
        self._bearer_token = SecretStr(bearer_token)

    @property
    def bearer_token(self) -> SecretStr:
        return self._bearer_token

    def _authorization_headers(self) -> dict[str, str]:
        return {"Authorization": f"Bearer {self._bearer_token.get_secret_value()}"}

    def __str__(self) -> str:
        return self.endpoint

    def __repr__(self) -> str:
        return f"{type(self).__name__}(endpoint={self.endpoint!r})"
