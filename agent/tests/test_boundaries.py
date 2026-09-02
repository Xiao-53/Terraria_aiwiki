from pathlib import Path


def test_agent_source_has_no_database_core_ui_platform_or_logging_dependency() -> None:
    source_root = Path(__file__).parents[1] / "src" / "terraria_wiki_agent"
    source = "\n".join(path.read_text(encoding="utf-8") for path in source_root.glob("*.py"))
    forbidden = (
        "sqlite",
        "terraria_wiki.core",
        "terraria_wiki.application",
        "avalonia",
        "maui",
        "webview",
        "win32",
        "windowsapi",
        "logging.basicconfig",
        "vector database",
        "embedding",
    )

    lowered = source.lower()
    for token in forbidden:
        assert token not in lowered
    assert "trust_env=False" in source
    assert "follow_redirects=False" in source
    assert "TERRARIA_AI_API_KEY" in source
    assert "api.deepseek.com" not in source


def test_project_pins_runtime_and_direct_development_dependencies() -> None:
    project = (Path(__file__).parents[1] / "pyproject.toml").read_text(encoding="utf-8")

    assert '"pydantic-ai-slim[mcp,openai]==2.31.0"' in project
    assert '"pytest==9.1.1"' in project
    assert '"ruff==0.16.4"' in project
    assert '"mypy==2.3.1"' in project
    assert 'requires-python = ">=3.12,<3.15"' in project


def test_lock_contains_the_same_fixed_direct_versions() -> None:
    lock = (Path(__file__).parents[1] / "uv.lock").read_text(encoding="utf-8")

    for package, version in (
        ("pydantic-ai-slim", "2.31.0"),
        ("pytest", "9.1.1"),
        ("ruff", "0.16.4"),
        ("mypy", "2.3.1"),
    ):
        assert f'name = "{package}"\nversion = "{version}"' in lock
