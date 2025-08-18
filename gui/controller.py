import threading
from typing import Callable

LogFn = Callable[[str], None]
Step = tuple[str, Callable[[], None]]


class PipelineController:
    """Lightweight controller to run background steps with cancellation."""

    def __init__(self):
        self._thread = None
        self._lock = threading.Lock()
        self._cancel = threading.Event()

    def is_running(self) -> bool:
        """Return True if a background worker thread is active."""
        return self._thread is not None and self._thread.is_alive()

    def cancel(self):
        """Request cooperative cancellation of the running worker."""
        self._cancel.set()

    def is_cancelled(self) -> bool:
        """Return True if cancellation has been requested."""
        return self._cancel.is_set()

    def run(
        self,
        steps: list[Step],
        on_done: Callable[[Exception | None], None],
        on_step: Callable[[int, int, str], None] | None = None,
    ):
        """Start a background thread to run the provided steps in order."""
        if self.is_running():
            return False
        self._cancel.clear()

        def worker():
            exc: Exception | None = None
            try:
                total = len(steps)
                for idx, (name, fn) in enumerate(steps, start=1):
                    if self._cancel.is_set():
                        exc = RuntimeError("Cancelled")
                        break
                    if on_step:
                        on_step(idx, total, name)
                    fn()
            except Exception as e:
                exc = e
            finally:
                on_done(exc)

        self._thread = threading.Thread(target=worker, daemon=True)
        self._thread.start()
        return True
