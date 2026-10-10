from __future__ import annotations

import asyncio
import importlib.metadata
import io
import os
import sys
import threading
from collections.abc import Callable
from typing import BinaryIO, Literal

from pydantic import ValidationError

from .agent import TerrariaWikiAgent
from .errors import WikiAgentError
from .models import (
    WikiAgentEvent,
    WikiAgentRunStarted,
    WikiAgentSettings,
    WikiAgentTextDelta,
    WikiAgentToolFinished,
    WikiAgentToolStarted,
    WikiConversationTurn,
    WikiToolConnection,
)
from .sidecar_protocol import (
    MAX_LINE_BYTES,
    PROTOCOL_VERSION,
    RUNTIME_VERSION,
    AnswerCommand,
    CancelCommand,
    InitializeCommand,
    ShutdownCommand,
    SidecarConversationTurn,
    SidecarProtocolError,
    encode_message,
    parse_command,
)

type _AgentFactory = Callable[[WikiAgentSettings], TerrariaWikiAgent]

_SAFE_RUNTIME_MESSAGES = {
    "dependency_mismatch": "The configured Python environment has incompatible dependencies.",
    "invalid_initialize": "The AI Runtime initialization is invalid.",
    "invalid_message": "The AI Runtime protocol message is invalid.",
    "invalid_order": "The AI Runtime protocol message is out of order.",
    "duplicate_request_id": "The AI Runtime request ID was already used.",
    "line_too_long": "The AI Runtime protocol line is too long.",
    "incomplete_line": "The AI Runtime protocol line is incomplete.",
    "unexpected_eof": "The AI Runtime input closed unexpectedly.",
    "unexpected_stdout": "The AI Runtime wrote unexpected standard output.",
    "runtime_crashed": "The AI Runtime stopped unexpectedly.",
    "runtime_busy": "The AI Runtime is already answering another question.",
    "request_not_active": "The requested AI run is not active.",
}


class _UnexpectedStdout(RuntimeError):
    pass


class _ForbiddenStdout(io.TextIOBase):
    def write(self, value: str) -> int:
        del value
        raise _UnexpectedStdout

    def flush(self) -> None:
        return None


class _ProtocolWriter:
    def __init__(self, output: BinaryIO):
        self._output = output
        self._lock = threading.Lock()

    def write(self, payload: dict[str, object]) -> None:
        line = encode_message(payload)
        with self._lock:
            self._output.write(line)
            self._output.flush()


class _InputPump:
    def __init__(self, source: BinaryIO, loop: asyncio.AbstractEventLoop):
        self.queue: asyncio.Queue[bytes | None] = asyncio.Queue()
        self._source = source
        self._loop = loop
        self._thread = threading.Thread(target=self._run, name="sidecar-stdin", daemon=True)

    def start(self) -> None:
        self._thread.start()

    def _run(self) -> None:
        try:
            while True:
                line = self._source.readline(MAX_LINE_BYTES + 2)
                if line == b"":
                    return
                self._loop.call_soon_threadsafe(self.queue.put_nowait, line)
                # The host keeps stdin open while waiting for shutdown. Stop the
                # reader here so interpreter teardown does not wait on readline.
                if b'"shutdown"' not in line:
                    continue
                try:
                    command = parse_command(line)
                except SidecarProtocolError:
                    continue
                if isinstance(command, ShutdownCommand):
                    return
        finally:
            self._loop.call_soon_threadsafe(self.queue.put_nowait, None)


class _SidecarRuntime:
    def __init__(self, writer: _ProtocolWriter, agent_factory: _AgentFactory):
        self._writer = writer
        self._agent_factory = agent_factory
        self._used_request_ids: set[str] = set()
        self._agent: TerrariaWikiAgent | None = None
        self._connection: WikiToolConnection | None = None
        self._active_request_id: str | None = None
        self._active_task: asyncio.Task[None] | None = None
        self._fatal = asyncio.Event()
        self.initialized = False
        self.shutdown_requested = False

    @property
    def fatal_event(self) -> asyncio.Event:
        return self._fatal

    async def handle(self, command: object) -> None:
        request_id = getattr(command, "request_id", None)
        if not isinstance(request_id, str):
            self.fatal("invalid_message", "runtime")
            return
        if request_id in self._used_request_ids:
            self.fatal("duplicate_request_id", request_id)
            return
        self._used_request_ids.add(request_id)

        if isinstance(command, InitializeCommand):
            await self._initialize(command)
        elif isinstance(command, AnswerCommand):
            await self._answer(command)
        elif isinstance(command, CancelCommand):
            await self._cancel(command)
        elif isinstance(command, ShutdownCommand):
            await self._shutdown()
        else:
            self.fatal("invalid_message", request_id)

    async def _initialize(self, command: InitializeCommand) -> None:
        if self.initialized:
            self.fatal("invalid_order", command.request_id)
            return
        try:
            dependency_version = _validate_runtime_dependencies()
            provider = command.provider
            settings = WikiAgentSettings(
                base_url=provider.base_url,
                model=provider.model,
                api=provider.api,
                reasoning_effort=provider.reasoning_effort,
            )
            connection = WikiToolConnection(
                command.wiki_tools.endpoint,
                command.wiki_tools.bearer_token,
            )
            agent = self._agent_factory(settings)
        except _UnexpectedStdout:
            self.fatal("unexpected_stdout", command.request_id)
            return
        except (ValidationError, WikiAgentError, ValueError):
            self.fatal("invalid_initialize", command.request_id)
            return
        except Exception:
            self.fatal("dependency_mismatch", command.request_id)
            return

        self._agent = agent
        self._connection = connection
        self.initialized = True
        self._writer.write(
            {
                "version": PROTOCOL_VERSION,
                "type": "ready",
                "requestId": command.request_id,
                "runtimeVersion": RUNTIME_VERSION,
                "pythonVersion": (
                    f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
                ),
                "pydanticAiVersion": dependency_version,
            }
        )

    async def _answer(self, command: AnswerCommand) -> None:
        if not self.initialized or self._agent is None or self._connection is None:
            self.fatal("invalid_order", command.request_id)
            return
        if self._active_task is not None and not self._active_task.done():
            self.failed(command.request_id, "runtime_busy")
            return

        self._active_request_id = command.request_id
        self._active_task = asyncio.create_task(
            self._run_answer(
                command.request_id, command.question, command.history, command.answer_style
            ),
            name=f"sidecar-answer-{command.request_id}",
        )

    async def _cancel(self, command: CancelCommand) -> None:
        task = self._active_task
        if task is None or task.done() or self._active_request_id != command.target_request_id:
            self.failed(command.request_id, "request_not_active")
            return
        task.cancel()

    async def _shutdown(self) -> None:
        self.shutdown_requested = True
        task = self._active_task
        if task is not None and not task.done():
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass

    async def _run_answer(
        self,
        request_id: str,
        question: str,
        history: list[SidecarConversationTurn],
        answer_style: Literal["detailed", "concise"],
    ) -> None:
        assert self._agent is not None
        assert self._connection is not None
        deltas: list[str] = []

        async def on_event(event: WikiAgentEvent) -> None:
            if isinstance(event, WikiAgentRunStarted):
                self._event(request_id, "run_started")
            elif isinstance(event, WikiAgentToolStarted):
                self._event(request_id, "tool_started", name=event.name)
            elif isinstance(event, WikiAgentToolFinished):
                self._event(
                    request_id,
                    "tool_finished",
                    name=event.name,
                    status=event.status,
                )
            elif isinstance(event, WikiAgentTextDelta):
                deltas.append(event.delta)
                self._event(request_id, "answer_delta", delta=event.delta)

        try:
            conversation = tuple(
                WikiConversationTurn(
                    user=turn.user,
                    assistant=turn.assistant,
                )
                for turn in history
            )
            answer = await self._agent.answer(
                question,
                self._connection,
                on_event,
                history=conversation,
                answer_style=answer_style,
            )
            if "".join(deltas) != answer.markdown:
                raise SidecarProtocolError("invalid_outgoing_message")
            self._writer.write(
                {
                    "version": PROTOCOL_VERSION,
                    "type": "completed",
                    "requestId": request_id,
                    "answer": answer.model_dump(mode="json"),
                }
            )
        except asyncio.CancelledError:
            self._event(request_id, "cancelled")
        except WikiAgentError as error:
            self._writer.write(
                {
                    "version": PROTOCOL_VERSION,
                    "type": "failed",
                    "requestId": request_id,
                    "error": {"code": error.code.value, "message": str(error)},
                }
            )
        except _UnexpectedStdout:
            self.fatal("unexpected_stdout", request_id)
        except Exception:
            self.fatal("runtime_crashed", request_id)
        finally:
            if self._active_request_id == request_id:
                self._active_request_id = None
                self._active_task = None

    def failed(self, request_id: str, code: str) -> None:
        self._writer.write(
            {
                "version": PROTOCOL_VERSION,
                "type": "failed",
                "requestId": request_id,
                "error": {"code": code, "message": _SAFE_RUNTIME_MESSAGES[code]},
            }
        )

    def fatal(self, code: str, request_id: str) -> None:
        if self._fatal.is_set():
            return
        self._writer.write(
            {
                "version": PROTOCOL_VERSION,
                "type": "fatal",
                "requestId": request_id,
                "error": {"code": code, "message": _SAFE_RUNTIME_MESSAGES[code]},
            }
        )
        self._fatal.set()

    def _event(self, request_id: str, event_type: str, **values: object) -> None:
        self._writer.write(
            {
                "version": PROTOCOL_VERSION,
                "type": event_type,
                "requestId": request_id,
                **values,
            }
        )


async def run_sidecar(
    source: BinaryIO,
    output: BinaryIO,
    *,
    agent_factory: _AgentFactory = TerrariaWikiAgent,
) -> int:
    loop = asyncio.get_running_loop()
    pump = _InputPump(source, loop)
    writer = _ProtocolWriter(output)
    runtime = _SidecarRuntime(writer, agent_factory)
    pump.start()

    while not runtime.shutdown_requested and not runtime.fatal_event.is_set():
        command_task = asyncio.create_task(pump.queue.get())
        fatal_task = asyncio.create_task(runtime.fatal_event.wait())
        done, pending = await asyncio.wait(
            {command_task, fatal_task}, return_when=asyncio.FIRST_COMPLETED
        )
        for task in pending:
            task.cancel()
        if fatal_task in done and fatal_task.result():
            break
        line = command_task.result()
        if line is None:
            active = runtime._active_task
            if active is not None and not active.done():
                active.cancel()
            runtime.fatal("unexpected_eof", "runtime")
            break
        try:
            command = parse_command(line)
        except SidecarProtocolError as error:
            code = error.code if error.code in _SAFE_RUNTIME_MESSAGES else "invalid_message"
            runtime.fatal(code, "runtime")
            break
        await runtime.handle(command)

    active = runtime._active_task
    if active is not None and not active.done():
        active.cancel()
        try:
            await active
        except asyncio.CancelledError:
            pass
    return 0 if runtime.shutdown_requested and not runtime.fatal_event.is_set() else 1


def main(*, agent_factory: _AgentFactory = TerrariaWikiAgent) -> int:
    source = sys.stdin.buffer
    protocol_output = sys.stdout.buffer
    sys.stdout = _ForbiddenStdout()
    try:
        return asyncio.run(run_sidecar(source, protocol_output, agent_factory=agent_factory))
    except _UnexpectedStdout:
        try:
            _ProtocolWriter(protocol_output).write(
                {
                    "version": PROTOCOL_VERSION,
                    "type": "fatal",
                    "requestId": "runtime",
                    "error": {
                        "code": "unexpected_stdout",
                        "message": _SAFE_RUNTIME_MESSAGES["unexpected_stdout"],
                    },
                }
            )
        except Exception:
            pass
        return 1


def _validate_runtime_dependencies() -> str:
    if not (3, 12) <= sys.version_info[:2] <= (3, 14):
        raise RuntimeError("unsupported Python")
    version = importlib.metadata.version("pydantic-ai-slim")
    if version != "2.31.0":
        raise RuntimeError("unsupported PydanticAI")
    if not os.environ.get("TERRARIA_AI_API_KEY", "").strip():
        raise ValueError("missing API key")
    return version


if __name__ == "__main__":
    raise SystemExit(main())
