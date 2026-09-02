import pytest
from pydantic import ValidationError
from pydantic_ai.models.openai import OpenAIChatModel, OpenAIResponsesModel

from terraria_wiki_agent import (
    TerrariaWikiAgent,
    WikiAgentError,
    WikiAgentErrorCode,
    WikiAgentSettings,
    WikiToolConnection,
)


@pytest.mark.parametrize(
    "endpoint",
    [
        "https://127.0.0.1:1234/mcp",
        "http://localhost:1234/mcp",
        "http://127.0.0.2:1234/mcp",
        "http://[::1]:1234/mcp",
        "http://127.0.0.1/mcp",
        "http://127.0.0.1:0/mcp",
        "http://127.0.0.1:65536/mcp",
        "http://user@127.0.0.1:1234/mcp",
        "http://127.0.0.1:1234/mcp/",
        "http://127.0.0.1:1234/other",
        "http://127.0.0.1:1234/mcp?secret=value",
        "http://127.0.0.1:1234/mcp#fragment",
    ],
)
def test_connection_rejects_every_noncanonical_endpoint(endpoint: str) -> None:
    with pytest.raises(WikiAgentError) as error:
        WikiToolConnection(endpoint, "token")
    assert error.value.code == WikiAgentErrorCode.INVALID_CONNECTION


@pytest.mark.parametrize("token", ["", " ", "line\nbreak", "tab\tvalue"])
def test_connection_rejects_unsafe_bearer_tokens(token: str) -> None:
    with pytest.raises(WikiAgentError) as error:
        WikiToolConnection("http://127.0.0.1:1234/mcp", token)
    assert error.value.code == WikiAgentErrorCode.INVALID_CONNECTION


def test_connection_keeps_token_secret_in_public_formatting() -> None:
    token = "phase06-super-secret"
    connection = WikiToolConnection("http://127.0.0.1:1234/mcp", token)

    assert str(connection) == "http://127.0.0.1:1234/mcp"
    assert token not in repr(connection)
    assert token not in str(connection.bearer_token)
    assert connection.bearer_token.get_secret_value() == token


def test_default_settings_choose_no_provider_and_keep_runtime_limits_without_cost_budget() -> None:
    settings = WikiAgentSettings()
    model_settings = settings.model_settings()
    limits = settings.usage_limits()

    assert settings.base_url is None
    assert settings.model is None
    assert settings.api == "responses"
    assert settings.reasoning_effort is None
    assert "openai_reasoning_effort" not in model_settings
    assert "openai_store" not in model_settings
    assert "openai_truncation" not in model_settings
    assert "parallel_tool_calls" not in model_settings
    assert model_settings["max_tokens"] == 4096
    assert limits.request_limit == 8
    assert limits.tool_calls_limit == 8
    assert limits.output_tokens_limit == 4096
    assert limits.total_tokens_limit == 300_000
    assert limits.per_request_input_tokens_limit == 256_000
    assert limits.cost_limit is None
    assert limits.count_tokens_before_request is False


@pytest.mark.parametrize("api", ["responses", "chat_completions"])
def test_settings_accept_safe_openai_compatible_provider(api: str) -> None:
    settings = WikiAgentSettings.model_validate(
        {
            "base_url": "https://api.example.test/openai/v1",
            "model": "provider/model-name",
            "api": api,
            "reasoning_effort": "medium",
        }
    )

    assert settings.base_url == "https://api.example.test/openai/v1"
    assert settings.model == "provider/model-name"
    assert settings.model_settings()["openai_reasoning_effort"] == "medium"


@pytest.mark.parametrize(
    "base_url",
    [
        "http://127.0.0.1:8000/v1",
        "http://[::1]:8000/v1",
        "https://api.deepseek.com",
    ],
)
def test_settings_accept_https_or_literal_loopback_http(base_url: str) -> None:
    settings = WikiAgentSettings(base_url=base_url, model="model")
    assert settings.base_url == base_url


@pytest.mark.parametrize(
    "base_url",
    [
        "http://api.example.test/v1",
        "http://localhost:8000/v1",
        "ftp://api.example.test/v1",
        "https://user@api.example.test/v1",
        "https://api.example.test/v1?key=value",
        "https://api.example.test/v1#fragment",
        " https://api.example.test/v1",
    ],
)
def test_settings_reject_unsafe_provider_endpoint(base_url: str) -> None:
    with pytest.raises(ValidationError):
        WikiAgentSettings(base_url=base_url, model="model")


def test_settings_require_base_url_and_model_together() -> None:
    with pytest.raises(ValidationError):
        WikiAgentSettings(base_url="https://api.example.test/v1")
    with pytest.raises(ValidationError):
        WikiAgentSettings(model="model")


def test_agent_builds_both_wire_models_and_keeps_key_out_of_repr(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    secret = "provider-secret-for-test"
    monkeypatch.setenv("TERRARIA_AI_API_KEY", secret)
    responses_agent = TerrariaWikiAgent(
        WikiAgentSettings(
            base_url="https://api.example.test/v1",
            model="responses-model",
            api="responses",
        )
    )
    chat_agent = TerrariaWikiAgent(
        WikiAgentSettings(
            base_url="https://api.example.test/v1",
            model="chat-model",
            api="chat_completions",
        )
    )

    responses_model = responses_agent._build_provider_model()
    chat_model = chat_agent._build_provider_model()

    assert isinstance(responses_model, OpenAIResponsesModel)
    assert isinstance(chat_model, OpenAIChatModel)
    for model in (responses_model, chat_model):
        assert model.profile.get("openai_supports_strict_tool_definition") is False
        assert model.profile.get("openai_supports_tool_choice_required") is False
        assert model.profile.get("openai_chat_supports_multiple_system_messages") is False
        assert model.profile.get("openai_system_prompt_role") == "system"
    assert secret not in repr(responses_model)
    assert secret not in repr(chat_model)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("request_limit", 9),
        ("tool_calls_limit", 9),
        ("output_tokens_limit", 4097),
        ("total_tokens_limit", 300_001),
        ("per_request_input_tokens_limit", 256_001),
        ("timeout_seconds", 120.1),
    ],
)
def test_settings_cannot_raise_a_runtime_ceiling(field: str, value: object) -> None:
    with pytest.raises(ValidationError):
        WikiAgentSettings.model_validate({field: value})


def test_settings_reject_removed_cost_budget_override() -> None:
    with pytest.raises(ValidationError):
        WikiAgentSettings.model_validate({"cost_limit_usd": "0.10"})


def test_settings_reject_unknown_api_and_removed_storage_override() -> None:
    with pytest.raises(ValidationError):
        WikiAgentSettings.model_validate({"api": "other"})
    with pytest.raises(ValidationError):
        WikiAgentSettings.model_validate({"store": True})
