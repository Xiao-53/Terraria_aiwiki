"""Explicit paid A/B evaluation on an isolated frozen Wiki snapshot; not a pytest test.

Run with the variant's Python source on PYTHONPATH and matching C# TestHost DLL.
The baseline and optimized hosts share only this acceptance harness, not product code.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
import time
from contextlib import asynccontextmanager
from pathlib import Path

from fastmcp import Client
from fastmcp.client.transports import StreamableHttpTransport
from pydantic_ai import models

from terraria_wiki_agent import WikiAgentError, WikiConversationTurn, WikiToolConnection
from terraria_wiki_agent import agent as agent_module
from tests.manual_evidence_acceptance import MeasuredAgent, load_existing_configuration

SNAPSHOT_SHA256 = "831562c02127f322fc94ff708e19eac0e8cbee0b0f90f6856d4e97265eef2428"
QUESTIONS = {
    2: "木剑的材料数量和制作站是什么？",
    5: "列全天顶剑的直接合成材料名称及制作站，不展开材料的合成树。",
    6: "猪鲨怎么召唤？说明鱼饵、地点和时间限制。",
    10: "那制作三把它需要多少材料？请重新核对本地配方，说明制作站。",
    12: "本地 Wiki 是否记录“星海永动钻”的制作配方？没有证据就明确拒答。",
}


def history_for(index):
    return (
        [
            WikiConversationTurn(
                user="木剑需要什么材料和制作站？",
                assistant="木剑需要7个木材，在工作台制作。",
            )
        ]
        if index == 10
        else []
    )


@asynccontextmanager
async def snapshot_host(dll, snapshot):
    process = await asyncio.create_subprocess_exec(
        "dotnet",
        str(dll),
        "--snapshot",
        str(snapshot),
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        line = await asyncio.wait_for(process.stdout.readline(), timeout=120)
        payload = json.loads(line.decode("utf-8-sig"))
        yield WikiToolConnection(payload["endpoint"], payload["bearer_token"])
    finally:
        if process.returncode is None:
            process.stdin.write(b"stop\n")
            await process.stdin.drain()
            process.stdin.close()
            try:
                await asyncio.wait_for(process.wait(), timeout=30)
            except TimeoutError:
                process.kill()
                await process.wait()
        if process.returncode != 0:
            raise RuntimeError("Snapshot test host did not exit successfully")


async def preflight(connection, variant):
    transport = StreamableHttpTransport(
        connection.endpoint, headers=connection._authorization_headers()
    )
    async with Client(transport) as client:
        tools = await client.list_tools()
        assert {tool.name for tool in tools} == {"search_wiki", "read_wiki"}
        result = await client.call_tool("search_wiki", {"query": "药水"})
        parsed = agent_module._WikiSearchResponse.model_validate(result.structured_content)
        assert len(parsed.results) == (50 if variant == "baseline" else 5)
        result = await client.call_tool("search_wiki", {"query": "木剑 配方"})
        parsed = agent_module._WikiSearchResponse.model_validate(result.structured_content)
        item = parsed.results[0]
        assert item.title == "木剑"
        if variant == "optimized":
            assert item.canonical_title == "木剑"
            section = next(s for s in item.matched_sections if s.anchor)
            title = item.canonical_title + "#" + section.anchor
        else:
            title = "木剑#.E9.85.8D.E6.96.B9"
        result = await client.call_tool("read_wiki", {"title": title, "mode": "section"})
        read = agent_module._WikiReadResponse.model_validate(result.structured_content)
        assert read.scope and read.scope.anchor_found
        text = " ".join(block.text for block in read.blocks)
        assert "木材" in text and "7" in text and "工作台" in text
    print(json.dumps({"preflight": "passed", "variant": variant}), flush=True)


class RecordedAgent(MeasuredAgent):
    def __init__(self, settings):
        super().__init__(settings)
        self.calls = []
        self._toolset_factory = self._recorded_factory

    def _recorded_factory(self, connection, evidence, settings):
        toolset = agent_module._default_toolset_factory(connection, evidence, settings)
        original = toolset.process_tool_call

        async def record(ctx, call_tool, name, tool_args):
            entry = {"name": name, "args": tool_args}
            self.calls.append(entry)
            try:
                value = await original(ctx, call_tool, name, tool_args)
                entry["projected_result"] = value
                return value
            except Exception as error:
                entry["error_type"] = type(error).__name__
                raise

        toolset.process_tool_call = record
        return toolset


async def evaluate(args):
    with args.snapshot.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != SNAPSHOT_SHA256:
            raise RuntimeError("Snapshot fingerprint differs from frozen baseline")
    async with snapshot_host(args.host, args.snapshot) as connection:
        await preflight(connection, args.variant)
        if args.preflight:
            return
        settings, key = load_existing_configuration()
        report = {
            "variant": args.variant,
            "source_identity": args.source_identity,
            "snapshot_sha256": SNAPSHOT_SHA256,
            "settings": settings.model_dump(mode="json"),
            "turns": [],
        }
        previous = os.environ.get("TERRARIA_AI_API_KEY")
        os.environ["TERRARIA_AI_API_KEY"] = key
        args.output.parent.mkdir(parents=True, exist_ok=True)
        try:
            with models.override_allow_model_requests(True):
                for repetition in range(1, 2):
                    for index, question in QUESTIONS.items():
                        agent = RecordedAgent(settings)
                        deltas = []

                        async def emit(event, deltas=deltas):
                            if event.type == "text_delta":
                                deltas.append(event.delta)

                        record = {"id": f"Q{index:02}", "repetition": repetition}
                        print(
                            json.dumps({"event": "start", "variant": args.variant, **record}),
                            flush=True,
                        )
                        started = time.perf_counter()
                        try:
                            answer = await agent.answer(
                                question, connection, emit, history=history_for(index)
                            )
                            assert "".join(deltas) == answer.markdown
                            record["answer"] = answer.model_dump(mode="json")
                            record["status"] = answer.status
                        except WikiAgentError as error:
                            record.update(status="failed", error_code=error.code)
                        finally:
                            record.update(
                                seconds=round(time.perf_counter() - started, 3),
                                provider_requests=agent.requests,
                                calls=agent.calls,
                            )
                            report["turns"].append(record)
                            args.output.write_text(
                                json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
                            )
                        print(
                            json.dumps(
                                {
                                    "event": "done",
                                    "variant": args.variant,
                                    "id": record["id"],
                                    "repetition": repetition,
                                    "status": record["status"],
                                    "seconds": record["seconds"],
                                    "usage": record.get("answer", {}).get("usage"),
                                }
                            ),
                            flush=True,
                        )
                        if record["status"] == "failed":
                            raise RuntimeError(
                                "Paid acceptance stopped on failed turn; no automatic rerun"
                            )
        finally:
            if previous is None:
                os.environ.pop("TERRARIA_AI_API_KEY", None)
            else:
                os.environ["TERRARIA_AI_API_KEY"] = previous


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", required=True, type=Path)
    parser.add_argument("--snapshot", required=True, type=Path)
    parser.add_argument("--variant", required=True, choices=["baseline", "optimized"])
    parser.add_argument("--source-identity", required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--preflight", action="store_true")
    parser.add_argument("--approve-paid", action="store_true")
    args = parser.parse_args()
    args.host = args.host.resolve(strict=True)
    args.snapshot = args.snapshot.resolve(strict=True)
    if not args.preflight and (
        not args.approve_paid or args.output is None or args.output.exists()
    ):
        raise RuntimeError("Paid approval and a new output path are required")
    asyncio.run(evaluate(args))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"acceptance_error_type": type(error).__name__}), flush=True)
        raise SystemExit(1) from None
