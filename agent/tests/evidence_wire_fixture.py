"""In-memory OpenAI wire fixture; no sockets, credentials or provider requests."""

import json

import httpx

from .openai_fixture_server import _chunk, _FixtureHandler


class EvidenceWireFixture:
    def __init__(self, api, sequence, markdown):
        self.api = api
        self.sequence = sequence
        self.markdown = markdown
        self.requests = []

    def __call__(self, request):
        assert request.url.host == "127.0.0.1"
        assert request.url.path == (
            "/v1/responses" if self.api == "responses" else "/v1/chat/completions"
        )
        payload = json.loads(request.content)
        self.requests.append(payload)
        index = len(self.requests) - 1
        call = self.sequence[index] if index < len(self.sequence) else None
        if self.api == "chat_completions":
            chunks = (
                _FixtureHandler._tool_call(call[0], json.dumps(call[1]), f"call-{index}")
                if call
                else [
                    _chunk(delta={"role": "assistant", "content": self.markdown}),
                    _chunk(delta={}, finish_reason="stop"),
                ]
            )
            body = (
                "".join(f"data: {json.dumps(chunk)}\n\n" for chunk in chunks) + "data: [DONE]\n\n"
            )
        else:
            response = {
                "id": f"resp-{index}",
                "object": "response",
                "created_at": 1,
                "model": "offline-fixture",
                "status": "in_progress",
                "output": [],
                "parallel_tool_calls": True,
                "tool_choice": "auto",
                "tools": [],
            }
            events = [{"type": "response.created", "response": response.copy()}]
            if call:
                item = {
                    "type": "function_call",
                    "id": f"fc-{index}",
                    "call_id": f"call-{index}",
                    "name": call[0],
                    "arguments": json.dumps(call[1]),
                    "status": "completed",
                }
                events.append(
                    {"type": "response.output_item.added", "output_index": 0, "item": item}
                )
            else:
                item = {
                    "type": "message",
                    "id": f"msg-{index}",
                    "role": "assistant",
                    "status": "completed",
                    "content": [{"type": "output_text", "text": self.markdown, "annotations": []}],
                }
                events.extend(
                    [
                        {"type": "response.output_item.added", "output_index": 0, "item": item},
                        {
                            "type": "response.output_text.delta",
                            "item_id": item["id"],
                            "output_index": 0,
                            "content_index": 0,
                            "delta": self.markdown,
                            "logprobs": [],
                        },
                    ]
                )
            response.update(status="completed", output=[item])
            events.append({"type": "response.completed", "response": response})
            body = "".join(
                f"event: {event['type']}\ndata: {json.dumps(dict(event, sequence_number=i))}\n\n"
                for i, event in enumerate(events)
            )
        return httpx.Response(200, headers={"content-type": "text/event-stream"}, content=body)
