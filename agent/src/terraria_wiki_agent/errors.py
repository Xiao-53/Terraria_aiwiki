from __future__ import annotations

from enum import StrEnum


class WikiAgentErrorCode(StrEnum):
    """Stable public failure categories for a single Agent run."""

    INVALID_INPUT = "invalid_input"
    INVALID_CONNECTION = "invalid_connection"
    WIKI_TOOL_UNAVAILABLE = "wiki_tool_unavailable"
    MODEL_AUTHENTICATION = "model_authentication"
    MODEL_UNAVAILABLE = "model_unavailable"
    CONTEXT_LIMIT = "context_limit"
    BUDGET_EXCEEDED = "budget_exceeded"
    TIMEOUT = "timeout"
    UNKNOWN = "unknown"


_SAFE_MESSAGES: dict[WikiAgentErrorCode, str] = {
    WikiAgentErrorCode.INVALID_INPUT: "The question is invalid.",
    WikiAgentErrorCode.INVALID_CONNECTION: "The Wiki Tool connection is invalid.",
    WikiAgentErrorCode.WIKI_TOOL_UNAVAILABLE: "The local Wiki tools are unavailable.",
    WikiAgentErrorCode.MODEL_AUTHENTICATION: "Model authentication failed.",
    WikiAgentErrorCode.MODEL_UNAVAILABLE: "The model service is unavailable.",
    WikiAgentErrorCode.CONTEXT_LIMIT: "The Wiki evidence exceeds the model context limit.",
    WikiAgentErrorCode.BUDGET_EXCEEDED: "The Agent run exceeded its safety budget.",
    WikiAgentErrorCode.TIMEOUT: "The Agent run timed out.",
    WikiAgentErrorCode.UNKNOWN: "The Agent run failed.",
}


class WikiAgentError(RuntimeError):
    """A redacted public error that never embeds an underlying exception."""

    def __init__(self, code: WikiAgentErrorCode):
        self.code = code
        super().__init__(_SAFE_MESSAGES[code])

    def __repr__(self) -> str:
        return f"{type(self).__name__}(code={self.code.value!r})"
