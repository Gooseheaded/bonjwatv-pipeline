import threading
from typing import Callable, List, Tuple


LogFn = Callable[[str], None]
Step = Tuple[str, Callable[[], None]]


class PipelineController:
    def __init__(self):
        self._thread = None
        self._lock = threading.Lock()
        self._cancel = threading.Event()

    def is_running(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def cancel(self):
        self._cancel.set()

    def is_cancelled(self) -> bool:
        return self._cancel.is_set()

    def run(self, steps: List[Step], on_done: Callable[[Exception | None], None], on_step: Callable[[int, int, str], None] | None = None):
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
            except Exception as e:  # noqa: BLE001
                exc = e
            finally:
                on_done(exc)

        self._thread = threading.Thread(target=worker, daemon=True)
        self._thread.start()
        return True
