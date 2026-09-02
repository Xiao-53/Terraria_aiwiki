"""Offline cross-language fixture: production protocol and MCP, deterministic model only."""

from __future__ import annotations

import sys
from pathlib import Path

_AGENT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_AGENT_ROOT / "src"))
sys.path.insert(0, str(_AGENT_ROOT / "tests"))

from fakes import ScriptedModel  # noqa: E402

from terraria_wiki_agent import TerrariaWikiAgent  # noqa: E402
from terraria_wiki_agent.sidecar import main  # noqa: E402


def _agent_factory(settings):
    scripted = ScriptedModel(
        [
            ("search_wiki", {"query": "天顶剑"}),
            ("read_wiki", {"title": "天顶剑"}),
        ],
        markdown="天顶剑由多把剑合成，并需要在秘银砧或山铜砧制作。",
    )
    return TerrariaWikiAgent(settings, model=scripted.model)


if __name__ == "__main__":
    raise SystemExit(main(agent_factory=_agent_factory))
