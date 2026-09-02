from __future__ import annotations

import sys
from pathlib import Path

runtime_root = Path(__file__).resolve().parent
source_root = runtime_root / "src"
if not source_root.is_dir():
    raise SystemExit(1)
sys.path.insert(0, str(source_root))

from terraria_wiki_agent.sidecar import main  # noqa: E402

raise SystemExit(main())
