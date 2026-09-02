"""Minimal local Chat Completions stream used only by Windows publish smoke tests."""

from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any


def _chunk(*, delta: dict[str, Any], finish_reason: str | None = None) -> dict[str, Any]:
    return {
        "id": "chatcmpl-phase07-fixture",
        "object": "chat.completion.chunk",
        "created": 1,
        "model": "offline-fixture",
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish_reason}],
    }


class _FixtureHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_POST(self) -> None:
        if self.path != "/v1/chat/completions":
            self.send_error(404)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > 512 * 1024:
                raise ValueError
            payload = json.loads(self.rfile.read(length))
            messages = payload["messages"]
            if not isinstance(messages, list):
                raise ValueError
        except (KeyError, TypeError, ValueError, json.JSONDecodeError):
            self.send_error(400)
            return

        tool_results = sum(
            1 for message in messages if isinstance(message, dict) and message.get("role") == "tool"
        )
        if tool_results == 0:
            chunks = self._tool_call("search_wiki", '{"query":"Smoke Target"}', "call-search")
        elif tool_results == 1:
            chunks = self._tool_call("read_wiki", '{"title":"Smoke Target"}', "call-read")
        else:
            chunks = [
                _chunk(delta={"role": "assistant", "content": "Smoke Target 是"}),
                _chunk(delta={"content": "本地测试页面。"}),
                _chunk(delta={}, finish_reason="stop"),
            ]
        chunks.append(
            {
                "id": "chatcmpl-phase07-fixture",
                "object": "chat.completion.chunk",
                "created": 1,
                "model": "offline-fixture",
                "choices": [],
                "usage": {
                    "prompt_tokens": 20,
                    "completion_tokens": 5,
                    "total_tokens": 25,
                },
            }
        )

        body = (
            "".join(
                f"data: {json.dumps(chunk, ensure_ascii=False, separators=(',', ':'))}\n\n"
                for chunk in chunks
            )
            + "data: [DONE]\n\n"
        )
        encoded = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(encoded)

    @staticmethod
    def _tool_call(name: str, arguments: str, call_id: str) -> list[dict[str, Any]]:
        return [
            _chunk(
                delta={
                    "role": "assistant",
                    "tool_calls": [
                        {
                            "index": 0,
                            "id": call_id,
                            "type": "function",
                            "function": {"name": name, "arguments": arguments},
                        }
                    ],
                }
            ),
            _chunk(delta={}, finish_reason="tool_calls"),
        ]

    def log_message(self, format: str, *args: object) -> None:
        del format, args


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18765)
    arguments = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", arguments.port), _FixtureHandler)
    print(json.dumps({"baseUrl": f"http://127.0.0.1:{server.server_port}/v1"}), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
