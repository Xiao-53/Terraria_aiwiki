"""Offline multi-anchor verification against the frozen Wiki Tool snapshot.

This script starts the isolated Windows TestHost, calls only search_wiki/read_wiki,
and never loads the Python Agent model configuration or sends a provider request.
"""

from __future__ import annotations

import asyncio
import hashlib
import json
from pathlib import Path
from typing import Any

from fastmcp import Client
from fastmcp.client.transports import StreamableHttpTransport

from terraria_wiki_agent import WikiToolConnection

REPOSITORY = Path(__file__).parents[2]
HOST_DLL = (
    REPOSITORY
    / "tests"
    / "Terraria_Wiki.WikiTools.TestHost"
    / "bin"
    / "Release"
    / "net10.0"
    / "Terraria_Wiki.WikiTools.TestHost.dll"
)
SNAPSHOT = REPOSITORY / "artifacts" / "ai-search-evidence-evaluation" / "2026-09-16" / "data.db"
OUTPUT = (
    REPOSITORY
    / "artifacts"
    / "ai-search-evidence-evaluation"
    / "2026-09-16"
    / "multi-anchor-read-verification.json"
)
SNAPSHOT_SHA256 = "831562c02127f322fc94ff708e19eac0e8cbee0b0f90f6856d4e97265eef2428"

SEARCHES = [
    "Wooden Sword crafting recipe",
    "天顶剑 Zenith",
    "猪鲨 召唤 鱼饵 地点 时间",
    "木剑 配方 木材 工作台",
]

READS = [
    ("Q02", "木剑#制作", "木剑 材料数量 制作站"),
    ("Q05", "天顶剑#制作", "天顶剑的直接合成材料与制作站"),
    ("Q06a", "猪龙鱼公爵#召唤", "猪龙鱼公爵召唤方法 鱼饵 地点 时间限制"),
    ("Q06b", "Guide:猪龙鱼公爵攻略#生成条件", "猪龙鱼公爵生成条件 鱼饵 地点 时间"),
    ("Q10a", "木剑#制作", "木剑 配方 制作站 需要多少木材"),
    ("Q10b", "木剑#配方", "木剑 配方 材料 木材 7 制作站 工作台"),
]


def _snapshot_fingerprint() -> str:
    with SNAPSHOT.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _require_body(result: Any) -> dict[str, Any]:
    if result.is_error or not isinstance(result.structured_content, dict):
        raise RuntimeError(f"Wiki Tool call failed: {result}")
    return result.structured_content


async def _run() -> dict[str, Any]:
    if not HOST_DLL.is_file() or not SNAPSHOT.is_file():
        raise RuntimeError("Release TestHost or frozen snapshot is unavailable")
    fingerprint = _snapshot_fingerprint()
    if fingerprint != SNAPSHOT_SHA256:
        raise RuntimeError(f"Snapshot fingerprint differs: {fingerprint}")

    process = await asyncio.create_subprocess_exec(
        "dotnet",
        str(HOST_DLL),
        "--snapshot",
        str(SNAPSHOT),
        cwd=REPOSITORY,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        assert process.stdout is not None
        line = await asyncio.wait_for(process.stdout.readline(), timeout=120)
        payload = json.loads(line.decode("utf-8-sig"))
        connection = WikiToolConnection(payload["endpoint"], payload["bearer_token"])
        transport = StreamableHttpTransport(
            connection.endpoint,
            headers=connection._authorization_headers(),
        )
        report: dict[str, Any] = {
            "snapshot_sha256": fingerprint,
            "searches": [],
            "reads": [],
        }
        async with Client(transport) as client:
            search_results: list[dict[str, Any]] = []
            for query in SEARCHES:
                result = await client.call_tool("search_wiki", {"query": query})
                body = _require_body(result)
                results = body.get("results")
                if not isinstance(results, list):
                    raise RuntimeError(f"Search response has no results: {query}")
                search_results.extend(item for item in results if isinstance(item, dict))
                report["searches"].append(
                    {
                        "query": query,
                        "results": [
                            {
                                "canonical_title": item.get("canonical_title"),
                                "matched_sections": item.get("matched_sections", []),
                            }
                            for item in results
                            if isinstance(item, dict)
                        ],
                    }
                )

            controls: dict[tuple[str, str], list[str]] = {}
            for item in search_results:
                title = item.get("canonical_title")
                for section in item.get("matched_sections", []):
                    if not isinstance(section, dict) or not section.get("anchor"):
                        continue
                    key = (title, section.get("section_path"))
                    controls.setdefault(key, []).append(section["anchor"])

            read_bodies: dict[str, dict[str, Any]] = {}
            for identifier, title, focus in READS:
                arguments = {"title": title, "mode": "section", "focus": focus}
                body = _require_body(await client.call_tool("read_wiki", arguments))
                scope = body.get("scope")
                blocks = body.get("blocks")
                if (
                    not isinstance(scope, dict)
                    or scope.get("anchor_found") is not True
                    or not isinstance(scope.get("selected_section"), str)
                    or not isinstance(blocks, list)
                    or not blocks
                ):
                    raise AssertionError(f"Anchor read did not select evidence: {identifier}")

                page_title, requested_anchor = title.split("#", 1)
                control_anchors = controls.get((body.get("title"), scope["selected_section"]), [])
                control_anchor = next(
                    (anchor for anchor in control_anchors if anchor != requested_anchor),
                    None,
                )
                comparison: dict[str, Any] | None = None
                if control_anchor is not None:
                    control_title = f"{page_title}#{control_anchor}"
                    control_body = _require_body(
                        await client.call_tool(
                            "read_wiki",
                            {"title": control_title, "mode": "section", "focus": focus},
                        )
                    )
                    if control_body.get("blocks") != blocks:
                        raise AssertionError(
                            f"Anchor aliases selected different evidence: {identifier}"
                        )
                    comparison = {
                        "anchor": control_anchor,
                        "selected_section": control_body["scope"]["selected_section"],
                        "block_count": len(control_body["blocks"]),
                    }

                record = {
                    "id": identifier,
                    "requested_title": body.get("requested_title"),
                    "resolved_title": body.get("title"),
                    "requested_anchor": requested_anchor,
                    "selected_section": scope["selected_section"],
                    "anchor_found": scope["anchor_found"],
                    "block_count": len(blocks),
                    "first_blocks": [block.get("text") for block in blocks[:4]],
                    "comparison": comparison,
                }
                report["reads"].append(record)
                read_bodies[identifier] = body

            if [block.get("text") for block in read_bodies["Q02"]["blocks"]] != [
                block.get("text") for block in read_bodies["Q10a"]["blocks"]
            ]:
                raise AssertionError("Repeated 木剑#制作 reads changed their evidence range")

        return report
    finally:
        if process.returncode is None:
            assert process.stdin is not None
            process.stdin.write(b"stop\n")
            await process.stdin.drain()
            process.stdin.close()
            try:
                await asyncio.wait_for(process.wait(), timeout=60)
            except TimeoutError:
                process.kill()
                await process.wait()
        if process.returncode != 0:
            assert process.stderr is not None
            error = (await process.stderr.read()).decode("utf-8", errors="replace")
            raise RuntimeError(f"TestHost exited {process.returncode}: {error[-1000:]}")


def main() -> None:
    report = asyncio.run(_run())
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"output": str(OUTPUT), "reads": len(report["reads"])}, ensure_ascii=False))


if __name__ == "__main__":
    main()
