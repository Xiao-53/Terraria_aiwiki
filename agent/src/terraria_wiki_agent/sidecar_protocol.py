from __future__ import annotations

import json
import re
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, TypeAdapter, ValidationError, field_validator

PROTOCOL_VERSION = 2
RUNTIME_VERSION = "2"
MAX_LINE_BYTES = 256 * 1024

_REQUEST_ID = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")


class _ProtocolModel(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class SidecarProviderSettings(_ProtocolModel):
    base_url: str = Field(alias="baseUrl")
    model: str
    api: Literal["responses", "chat_completions"]
    reasoning_effort: Literal["none", "low", "medium", "high", "xhigh", "max"] | None = Field(
        default=None, alias="reasoningEffort"
    )


class SidecarWikiToolConnection(_ProtocolModel):
    endpoint: str
    bearer_token: str = Field(alias="bearerToken")


class _Command(_ProtocolModel):
    version: Literal[2]
    request_id: str = Field(alias="requestId")

    @field_validator("request_id")
    @classmethod
    def validate_request_id(cls, value: str) -> str:
        if not _REQUEST_ID.fullmatch(value):
            raise ValueError("requestId is invalid")
        return value


class InitializeCommand(_Command):
    type: Literal["initialize"]
    provider: SidecarProviderSettings
    wiki_tools: SidecarWikiToolConnection = Field(alias="wikiTools")


class SidecarConversationTurn(_ProtocolModel):
    user: str = Field(min_length=1, max_length=32_768)
    assistant: str = Field(min_length=1, max_length=32_768)


class AnswerCommand(_Command):
    type: Literal["answer"]
    question: str = Field(min_length=1, max_length=32_768)
    history: list[SidecarConversationTurn] = Field(max_length=4)
    answer_style: Literal["detailed", "concise"] = Field(default="detailed", alias="answerStyle")


class CancelCommand(_Command):
    type: Literal["cancel"]
    target_request_id: str = Field(alias="targetRequestId")

    @field_validator("target_request_id")
    @classmethod
    def validate_target_request_id(cls, value: str) -> str:
        if not _REQUEST_ID.fullmatch(value):
            raise ValueError("targetRequestId is invalid")
        return value


class ShutdownCommand(_Command):
    type: Literal["shutdown"]


type SidecarCommand = Annotated[
    InitializeCommand | AnswerCommand | CancelCommand | ShutdownCommand,
    Field(discriminator="type"),
]

_COMMAND_ADAPTER: TypeAdapter[SidecarCommand] = TypeAdapter(SidecarCommand)


class SidecarProtocolError(ValueError):
    def __init__(self, code: str):
        self.code = code
        super().__init__(code)


def parse_command(line: bytes) -> SidecarCommand:
    if not line or len(line) > MAX_LINE_BYTES:
        raise SidecarProtocolError("line_too_long")
    if not line.endswith(b"\n"):
        raise SidecarProtocolError("incomplete_line")
    try:
        decoded = line.decode("utf-8")
        payload = json.loads(decoded, parse_constant=_reject_json_constant)
        if not isinstance(payload, dict):
            raise SidecarProtocolError("invalid_message")
        return _COMMAND_ADAPTER.validate_python(payload)
    except SidecarProtocolError:
        raise
    except (UnicodeError, json.JSONDecodeError, ValidationError, ValueError, TypeError):
        raise SidecarProtocolError("invalid_message") from None


def encode_message(payload: dict[str, object]) -> bytes:
    try:
        line = (
            json.dumps(
                payload,
                ensure_ascii=False,
                allow_nan=False,
                separators=(",", ":"),
            ).encode("utf-8")
            + b"\n"
        )
    except (TypeError, ValueError):
        raise SidecarProtocolError("invalid_outgoing_message") from None
    if len(line) > MAX_LINE_BYTES:
        raise SidecarProtocolError("line_too_long")
    return line


def _reject_json_constant(value: str) -> None:
    del value
    raise ValueError("non-finite JSON numbers are forbidden")
