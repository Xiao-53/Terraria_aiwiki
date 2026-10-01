from __future__ import annotations

import os
import sys
import time
from pathlib import Path

runtime_root = Path(__file__).resolve().parent
source_root = runtime_root / "src"
if not source_root.is_dir():
    raise SystemExit(1)
sys.path.insert(0, str(source_root))

diagnostics_enabled = os.environ.get("TERRARIA_STARTUP_DIAGNOSTICS") == "1"


def _diagnostic(marker: str, duration_ms: int = 0) -> None:
    if diagnostics_enabled:
        try:
            sys.stderr.write(f"TERRARIA_STARTUP_DIAGNOSTIC|{marker}|{duration_ms}\n")
            sys.stderr.flush()
        except (OSError, ValueError):
            pass


_diagnostic("launcher-entered")
_diagnostic("sidecar-import-started")
import_started = time.perf_counter()
try:
    from terraria_wiki_agent.sidecar import main  # noqa: E402
finally:
    _diagnostic("sidecar-import-finished", int((time.perf_counter() - import_started) * 1000))

raise SystemExit(main())
