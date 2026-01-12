import json
import os
import subprocess
import sys
import tkinter as tk
import threading
from datetime import datetime
from tkinter import filedialog, messagebox, ttk
from typing import Callable

from gui.controller import PipelineController
from gui.settings import load_settings, save_settings
from run_paths import compute_run_paths
import yt_dlp


def timestamp() -> str:
    """Return a formatted timestamp string."""
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


# ---- Pipeline step stubs (replace these with real pipeline calls) -----------
def run_download_audio(videos_path: str, run_dirs: dict, log: Callable[[str], None]):
    """Stub for downloading audio."""
    log(f"{timestamp()} downloading audio to {run_dirs['audio_dir']} ...")


def run_isolate_vocals(run_dirs: dict, log: Callable[[str], None]):
    """Stub for isolating vocals."""
    log(f"{timestamp()} isolating vocals into {run_dirs['vocals_dir']} ...")


def run_transcribe(run_dirs: dict, log: Callable[[str], None]):
    """Stub for transcribing audio."""
    log(
        f"{timestamp()} transcribing from vocals in {run_dirs['vocals_dir']} to SRT in {run_dirs['subtitles_dir']} ..."
    )


def run_normalize(run_dirs: dict, log: Callable[[str], None]):
    """Stub for normalizing subtitles."""
    log(f"{timestamp()} normalizing subtitles in {run_dirs['subtitles_dir']} ...")


def run_translate(run_dirs: dict, log: Callable[[str], None]):
    """Stub for translating subtitles."""
    log(f"{timestamp()} translating subtitles in {run_dirs['subtitles_dir']} ...")


class App(tk.Tk):
    """Tkinter GUI for configuring and running the subtitle pipeline."""

    def __init__(self):
        super().__init__()
        self.title("BWKT Subtitle Pipeline")
        self.minsize(820, 560)

        self.controller = PipelineController()
        self.vars = self._make_vars()
        self.run_root_var = tk.StringVar(value="")
        self._load_settings()

        main = ttk.Frame(self, padding=16)
        main.grid(sticky="nsew")
        self.columnconfigure(0, weight=1)
        main.columnconfigure(0, weight=1)

        # Inputs
        inputs = ttk.Labelframe(main, text="Inputs")
        inputs.grid(sticky="ew", padx=0, pady=(0, 12))
        for i in range(3):
            inputs.columnconfigure(i, weight=1 if i == 1 else 0)

        ttk.Label(inputs, text="List of videos").grid(
            row=0, column=0, sticky="w", pady=4, padx=(8, 8)
        )
        videos_entry = ttk.Entry(inputs, textvariable=self.vars["videos_path"])
        videos_entry.grid(row=0, column=1, sticky="ew", pady=4)
        ttk.Button(inputs, text="Select file…", command=self.pick_videos_file).grid(
            row=0, column=2, padx=(8, 8), pady=4
        )

        ttk.Label(
            inputs,
            text="This will use a folder called my_videos (or create it if it doesn't exist).",
            wraplength=600,
        ).grid(row=1, column=1, columnspan=2, sticky="w", pady=(0, 8))

        ttk.Label(inputs, text="OpenAI API Key").grid(
            row=2, column=0, sticky="w", padx=(8, 8)
        )
        self.api_entry = ttk.Entry(inputs, textvariable=self.vars["api_key"], show="•")
        self.api_entry.grid(row=2, column=1, sticky="ew")
        ttk.Button(inputs, text="show/hide", command=self.toggle_api).grid(
            row=2, column=2, padx=(8, 8)
        )

        # yt-dlp cookies source (optional)
        ttk.Label(inputs, text="YouTube cookies (browser)").grid(
            row=4, column=1, sticky="w", padx=(8, 8)
        )
        self.cookies_browser_combo = ttk.Combobox(
            inputs,
            textvariable=self.vars["ytdlp_cookies_browser"],
            values=["", "chrome", "chromium", "brave", "edge", "firefox", "safari"],
            width=12,
            state="readonly",
        )
        self.cookies_browser_combo.grid(row=4, column=1, sticky="e", pady=4)
        ttk.Button(inputs, text="Test cookies", command=self.test_cookies).grid(
            row=4, column=2, padx=(8, 8), pady=4, sticky="w"
        )

        # Derived path hint
        self.derived_path_hint_var = tk.StringVar()
        derived_hint = ttk.Label(
            main,
            textvariable=self.derived_path_hint_var,
            wraplength=700,
            justify="left",
        )
        derived_hint.grid(sticky="ew", pady=(0, 12), padx=4)

        # Pipeline
        pipe = ttk.Labelframe(main, text="Pipeline steps")
        pipe.grid(sticky="ew", pady=(0, 12))
        pipe.columnconfigure(0, weight=1)

        # Step 1
        ttk.Checkbutton(
            pipe,
            text="Download audio",
            variable=self.vars["do_download"],
            command=self.refresh_states,
        ).grid(row=0, column=0, sticky="w", padx=(8, 8), pady=(6, 2))

        # Step 2
        ttk.Checkbutton(
            pipe,
            text="Voice isolation",
            variable=self.vars["do_isolate"],
            command=self.refresh_states,
        ).grid(row=1, column=0, sticky="w", padx=(8, 8))

        # Step 3
        step3_frame = ttk.Frame(pipe)
        step3_frame.grid(row=2, column=0, sticky="w")
        ttk.Checkbutton(
            step3_frame,
            text="Transcribe korean subtitles",
            variable=self.vars["do_transcribe"],
            command=self.refresh_states,
        ).pack(side="left", padx=(8, 8))
        self.transcribe_provider_combo = ttk.Combobox(
            step3_frame,
            textvariable=self.vars["transcription_provider"],
            values=["local", "openai"],
            width=10,
        )
        self.transcribe_provider_combo.pack(side="left")

        # Step 4
        ttk.Checkbutton(
            pipe,
            text="Normalize subtitles",
            variable=self.vars["do_normalize"],
            command=self.refresh_states,
        ).grid(row=3, column=0, sticky="w", padx=(8, 8))

        # Step 5
        ttk.Checkbutton(
            pipe,
            text="Translate subtitles",
            variable=self.vars["do_translate"],
            command=self.refresh_states,
        ).grid(row=4, column=0, sticky="w", padx=(8, 8), pady=(0, 4))

        # bonjwa.tv integration (optional) — placed after pipeline
        catalog = ttk.Labelframe(main, text="bonjwa.tv integration (optional)")
        catalog.grid(sticky="ew", pady=(0, 12))
        for i in range(3):
            catalog.columnconfigure(i, weight=1 if i == 1 else 0)

        ttk.Checkbutton(
            catalog,
            text="Submit results to bonjwa.tv",
            variable=self.vars["submit_to_catalog"],
            command=self.refresh_states,
        ).grid(row=0, column=0, columnspan=3, sticky="w", padx=(8, 8), pady=(6, 4))

        ttk.Label(catalog, text="bonjwa.tv URL").grid(row=1, column=0, sticky="w", padx=(8, 8))
        self.catalog_base_entry = ttk.Entry(catalog, textvariable=self.vars["catalog_base"])
        self.catalog_base_entry.grid(row=1, column=1, sticky="ew", pady=2)

        ttk.Label(catalog, text="Ingest token").grid(row=2, column=0, sticky="w", padx=(8, 8))
        self.catalog_token_entry = ttk.Entry(catalog, textvariable=self.vars["catalog_api_token"], show="•")
        self.catalog_token_entry.grid(row=2, column=1, sticky="ew", pady=2)
        self.catalog_token_btn = ttk.Button(catalog, text="show/hide", command=self.toggle_catalog_token)
        self.catalog_token_btn.grid(row=2, column=2, padx=(8, 8))

        # Run + progress
        runrow = ttk.Frame(main)
        runrow.grid(sticky="ew", pady=(0, 8))
        runrow.columnconfigure(2, weight=1)
        self.run_btn = ttk.Button(runrow, text="RUN", command=self.on_run)
        self.run_btn.grid(row=0, column=0, padx=(0, 12))
        # Elapsed time (stopwatch) between RUN and progress bar (HH:MM:SS)
        self.elapsed_var = tk.StringVar(value="00:00:00")
        self.elapsed_label = ttk.Label(runrow, textvariable=self.elapsed_var)
        self.elapsed_label.grid(row=0, column=1, padx=(0, 12))
        self.progress = ttk.Progressbar(runrow, mode="determinate")
        self.progress.grid(row=0, column=2, sticky="ew")

        # Log
        self.log = tk.Text(main, height=10, wrap="word", state="disabled")
        self.log.grid(sticky="nsew")
        main.rowconfigure(4, weight=1)

        # Log controls (copy/clear)
        logctl = ttk.Frame(main)
        logctl.grid(sticky="e", pady=(4, 4))
        ttk.Button(logctl, text="Copy log", command=self.copy_log).grid(
            row=0, column=0, padx=(0, 8)
        )
        ttk.Button(logctl, text="Clear log", command=self.clear_log).grid(
            row=0, column=1
        )

        # Attribution
        attr_label = ttk.Label(
            main,
            text="BWKT Subtitle Pipeline v251206 by Gooseheaded",
            foreground="gray",
        )
        attr_label.grid(sticky="se", padx=4, pady=(8, 0))

        # Bind
        self.vars["videos_path"].trace_add(
            "write", lambda *_: self.update_derived_paths()
        )

        self.update_derived_paths()
        self.refresh_states()
        self.protocol("WM_DELETE_WINDOW", self.on_close)
        # Stopwatch state
        self._timer_running = False
        self._run_started_at: datetime | None = None

    # ----- Vars / settings
    def _make_vars(self) -> dict[str, tk.Variable]:
        """Create ttk/tk variables that back the UI widgets."""
        v: dict[str, tk.Variable] = {
            "videos_path": tk.StringVar(value="my_videos.txt"),
            "api_key": tk.StringVar(value=""),
            "ytdlp_cookies_browser": tk.StringVar(value=""),
            "do_download": tk.BooleanVar(value=True),
            "do_isolate": tk.BooleanVar(value=True),
            "do_transcribe": tk.BooleanVar(value=True),
            "transcription_provider": tk.StringVar(value="local"),
            "do_normalize": tk.BooleanVar(value=True),
            "do_translate": tk.BooleanVar(value=False),
            # Catalog integration
            "submit_to_catalog": tk.BooleanVar(value=False),
            "catalog_base": tk.StringVar(value="https://bonjwa.tv"),
            "catalog_api_token": tk.StringVar(value=""),
        }
        return v

    def _load_settings(self):
        """Load persisted GUI settings into the current variables."""
        defaults = {k: v.get() for k, v in self.vars.items()}
        data = load_settings(defaults)
        for k, var in self.vars.items():
            var.set(data.get(k, var.get()))

    def _save_settings(self):
        """Persist current GUI settings to disk."""
        values = {k: v.get() for k, v in self.vars.items()}
        save_settings(values)

    # ----- UI handlers
    def toggle_api(self):
        """Toggle showing/hiding the API key field."""
        self.api_entry.configure(show="" if self.api_entry.cget("show") else "•")

    def toggle_catalog_token(self):
        """Toggle showing/hiding the Catalog token field."""
        self.catalog_token_entry.configure(
            show="" if self.catalog_token_entry.cget("show") else "•"
        )

    def pick_videos_file(self):
        """Open a file picker for selecting a URLs list file."""
        path = filedialog.askopenfilename(
            title="Select list of videos",
            filetypes=[("Text files", "*.txt"), ("All files", "*.*")],
        )
        if path:
            self.vars["videos_path"].set(path)

    def update_derived_paths(self):
        """Update derived run directories based on the selected videos file."""
        path = self.vars["videos_path"].get()
        if not path or not os.path.exists(os.path.dirname(path)):
            self.derived_path_hint_var.set("")
            return
        try:
            run_dirs = compute_run_paths(path)
            self.run_root_var.set(run_dirs["run_root"])
            self.derived_path_hint_var.set(
                f"All subtitles (and any data files) are located at: {run_dirs['run_root']}"
            )
        except Exception:
            self.derived_path_hint_var.set("")

    def log_line(self, text: str):
        """Append a line to the log view."""
        self.log.configure(state="normal")
        self.log.insert("end", text + "\n")
        self.log.see("end")
        self.log.configure(state="disabled")

    def clear_log(self):
        """Clear the log view contents."""
        self.log.configure(state="normal")
        self.log.delete("1.0", "end")
        self.log.configure(state="disabled")

    def copy_log(self):
        """Copy the current log contents to the clipboard."""
        try:
            content = self.log.get("1.0", "end-1c")
            self.clipboard_clear()
            self.clipboard_append(content)
        except tk.TclError:
            pass

    def refresh_states(self):
        """Refresh enabled/disabled state of controls based on selections."""
        def set_state(widget, enabled: bool):
            widget.configure(state="normal" if enabled else "disabled")

        set_state(self.transcribe_provider_combo, self.vars["do_transcribe"].get())
        # Enable/disable bonjwa.tv inputs based on checkbox
        submit_enabled = self.vars["submit_to_catalog"].get()
        if hasattr(self, "catalog_base_entry"):
            set_state(self.catalog_base_entry, submit_enabled)
        if hasattr(self, "catalog_token_entry"):
            set_state(self.catalog_token_entry, submit_enabled)
        if hasattr(self, "catalog_token_btn"):
            set_state(self.catalog_token_btn, submit_enabled)

    def test_cookies(self):
        """Quickly test if yt-dlp can load cookies from the selected browser."""
        browser = self.vars["ytdlp_cookies_browser"].get().strip()
        if not browser:
            messagebox.showinfo("Cookies test", "No browser selected. Pick a browser first.")
            return

        def worker():
            try:
                opts = {
                    "quiet": True,
                    "no_warnings": True,
                    "cookiesfrombrowser": (browser,),
                    "retries": 0,
                    "socket_timeout": 5,
                }
                ydl = yt_dlp.YoutubeDL(opts)
                # Use yt-dlp's small public test video; metadata-only
                ydl.extract_info("https://www.youtube.com/watch?v=BaW_jenozKc", download=False)
                self.after(0, lambda: messagebox.showinfo("Cookies test", f"Success: Loaded cookies from '{browser}'."))
            except Exception as ex:
                msg = str(ex)
                hint = "\n\nTips: Close the browser (to release locks) or try a different browser.)"
                self.after(0, lambda: messagebox.showwarning("Cookies test", f"Failed to load cookies from '{browser}'.\n{msg}{hint}"))

        threading.Thread(target=worker, daemon=True).start()

    def on_run(self):  # noqa: C901
        """Build a run config and launch the orchestrator subprocess."""
        videos_file = self.vars["videos_path"].get()
        if not os.path.exists(videos_file):
            messagebox.showerror("Error", f"Videos file not found: {videos_file}")
            return

        run_dirs = compute_run_paths(videos_file)

        # --- Calculate total operations for progress bar ---
        num_videos = 0
        with open(videos_file, encoding="utf-8") as f:
            num_videos = len(f.readlines())

        per_video_steps = []
        if self.vars["do_download"].get():
            per_video_steps.append("download_audio")
        if self.vars["do_isolate"].get():
            per_video_steps.append("isolate_vocals")
        if self.vars["do_transcribe"].get():
            per_video_steps.append("transcribe_audio")
        if self.vars["do_normalize"].get():
            per_video_steps.append("normalize_srt")
        if self.vars["do_translate"].get():
            per_video_steps.append("translate_subtitles")
        # These are also per-video, but run before the main processing
        per_video_steps.extend(["fetch_video_metadata", "translate_title"])
        total_ops = num_videos * len(per_video_steps)

        # --- Build orchestrator config ---
        def build_cfg() -> str:
            base_path = os.path.join(os.getcwd(), "pipeline-config.json")
            base = {}
            try:
                with open(base_path, encoding="utf-8") as f:
                    base = json.load(f)
            except Exception:
                base = {}

            # Note: build_videos_json is a global step, not counted in per-video ops
            steps = [
                "read_youtube_urls",
                "fetch_video_metadata",
                "translate_title",
                "build_videos_json",
            ]
            steps.extend(
                [
                    s
                    for s in [
                        "download_audio",
                        "isolate_vocals",
                        "transcribe_audio",
                        "normalize_srt",
                        "translate_subtitles",
                    ]
                    if s in per_video_steps
                ]
            )

            cfg = {
                "video_list_file": run_dirs["video_list_file"],
                "video_metadata_dir": base.get("video_metadata_dir", "metadata"),
                "audio_dir": run_dirs["audio_dir"],
                "vocals_dir": run_dirs["vocals_dir"],
                "subtitles_dir": run_dirs["subtitles_dir"],
                "cache_dir": run_dirs["cache_dir"],
                "slang_file": base.get("slang_file", "slang/KoreanSlang.txt"),
                "website_dir": base.get("website_dir", "website"),
                "urls_file": videos_file,
                "steps": steps,
                "transcription_provider": self.vars["transcription_provider"].get(),
                "transcription_api_model": "whisper-1",
            }
            for k in (
                "service_account_file",
                "spreadsheet",
                "worksheet",
                "sheet_column",
            ):
                if k in base:
                    cfg[k] = base[k]
            os.makedirs(run_dirs["run_root"], exist_ok=True)
            cfg_path = os.path.join(run_dirs["run_root"], "gui-config.json")
            with open(cfg_path, "w", encoding="utf-8") as f:
                json.dump(cfg, f, ensure_ascii=False, indent=2)
            return cfg_path

        cfg_path = build_cfg()

        def _start_timer():
            self._run_started_at = datetime.now()
            self._timer_running = True
            self.elapsed_var.set("00:00:00")
            def tick():
                if not self._timer_running or self._run_started_at is None:
                    return
                delta = datetime.now() - self._run_started_at
                total_sec = int(delta.total_seconds())
                hh = total_sec // 3600
                mm = (total_sec % 3600) // 60
                ss = total_sec % 60
                self.elapsed_var.set(f"{hh:02d}:{mm:02d}:{ss:02d}")
                self.after(500, tick)
            self.after(500, tick)

        def _stop_timer():
            self._timer_running = False

        def run_orchestrator():
            env = os.environ.copy()
            api_key = self.vars["api_key"].get().strip()
            if api_key:
                env["OPENAI_API_KEY"] = api_key
            # Pass cookies browser selection to yt-dlp (download step)
            cookies_browser = self.vars["ytdlp_cookies_browser"].get().strip()
            if cookies_browser:
                # Prefer GUI-selected browser cookies over any cookiefile
                env["YTDLP_COOKIES_BROWSER"] = cookies_browser
                env.pop("YTDLP_COOKIES", None)
            else:
                # Ensure browser cookie usage is disabled if not selected
                env.pop("YTDLP_COOKIES_BROWSER", None)

            # Determine the correct command to run pipeline_orchestrator.py
            if hasattr(sys, "_MEIPASS"):
                # Running from a PyInstaller bundle.
                # The Orchestrator binary may be included as:
                # - a standalone file: Orchestrator or Orchestrator.exe
                # - inside a folder: Orchestrator/Orchestrator(.exe)
                base = sys._MEIPASS
                bases = [base, os.path.join(base, "_internal")]
                candidates = []
                for b in bases:
                    candidates.extend([
                        # Preferred: packaged under bin/
                        os.path.join(b, "bin", "Orchestrator"),
                        os.path.join(b, "bin", "Orchestrator.exe"),
                        # Fallbacks: historical layouts
                        os.path.join(b, "Orchestrator"),
                        os.path.join(b, "Orchestrator.exe"),
                        os.path.join(b, "Orchestrator", "Orchestrator"),
                        os.path.join(b, "Orchestrator", "Orchestrator.exe"),
                    ])
                orchestrator_exe_path = next((p for p in candidates if os.path.isfile(p)), None)
                if orchestrator_exe_path is None:
                    # Fallback: look relative to the executable directory (one-dir bundles)
                    exe_dir = os.path.dirname(sys.executable)
                    for b in (exe_dir, os.path.join(exe_dir, "_internal")):
                        for rel in (
                            os.path.join("bin", "Orchestrator"),
                            os.path.join("bin", "Orchestrator.exe"),
                            "Orchestrator",
                            "Orchestrator.exe",
                            os.path.join("Orchestrator", "Orchestrator"),
                            os.path.join("Orchestrator", "Orchestrator.exe"),
                        ):
                            cand = os.path.join(b, rel)
                            if os.path.isfile(cand):
                                orchestrator_exe_path = cand
                                break
                        if orchestrator_exe_path:
                            break
                if orchestrator_exe_path is None:
                    raise RuntimeError("Bundled Orchestrator binary not found in package.")
                # Ensure executable bit on POSIX systems
                try:
                    if os.name == "posix":
                        mode = os.stat(orchestrator_exe_path).st_mode
                        if (mode & 0o111) == 0:
                            os.chmod(orchestrator_exe_path, mode | 0o755)
                except Exception:
                    pass
                cmd = [orchestrator_exe_path, "--config", cfg_path]
            else:
                # Running from source (during development)
                cmd = [sys.executable, "pipeline_orchestrator.py", "--config", cfg_path]

            try:
                proc = subprocess.Popen(
                    cmd,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    env=env,
                )
                # Start the stopwatch when the process launches
                self.after(0, _start_timer)
            except Exception as e:
                self.after(
                    0,
                    lambda exc=e: messagebox.showerror(
                        "Error", f"Failed to start orchestrator: {exc}"
                    ),
                )
                return
            try:
                assert proc.stdout is not None
                for raw_line in proc.stdout:
                    line = raw_line.rstrip()
                    if not line:
                        continue

                    if line.startswith("PROGRESS:"):
                        parts = line.split(":")[1].split("/")
                        if len(parts) == 2:
                            current = int(parts[0])
                            self.after(
                                0, lambda c=current: self.progress.configure(value=c)
                            )
                    else:
                        self.after(0, lambda msg=line: self.log_line(msg))

                    if self.controller.is_cancelled():
                        try:
                            proc.terminate()
                        except Exception:
                            pass
                        break
                proc.wait()
            finally:
                # Stop the stopwatch when the process ends
                self.after(0, _stop_timer)

        # --- Controller Execution ---
        self.set_ui_running(True)
        self.run_btn.configure(text="Cancel", command=self.on_cancel)
        self.progress.configure(maximum=total_ops, value=0)

        def on_done(exc: Exception | None):
            if exc is None:
                self.after(
                    0, lambda: self.log_line(f"{timestamp()} pipeline finished.")
                )
            else:
                self.after(0, lambda: messagebox.showerror("Error", str(exc)))
            self.after(
                0, lambda: self.progress.configure(value=self.progress["maximum"])
            )
            self.after(
                0, lambda: self.run_btn.configure(text="RUN", command=self.on_run)
            )
            self.after(0, lambda: self.set_ui_running(False))
            self.after(0, self.refresh_states)
            self.after(0, self.refresh_states)

        # Optional submit step
        steps = [("Run orchestrator", run_orchestrator)]
        if self.vars["submit_to_catalog"].get():
            def run_submit():
                try:
                    catalog_base = self.vars["catalog_base"].get().strip()
                    token = self.vars["catalog_api_token"].get().strip()
                    if not catalog_base or not token:
                        self.after(0, lambda: self.log_line("bonjwa.tv URL or token missing; skipping submit."))
                        return
                    # Prefer enriched videos JSON built in the run-root next to the derived videos.json
                    run_videos_dir = os.path.dirname(os.path.abspath(run_dirs["video_list_file"]))
                    enriched_videos = os.path.join(run_videos_dir, "videos_enriched.json")
                    videos_json = enriched_videos if os.path.exists(enriched_videos) else run_dirs["video_list_file"]
                    cmd = [
                        sys.executable,
                        os.path.join(os.getcwd(), "submit_to_catalog.py"),
                        "--catalog-base", catalog_base,
                        "--api-key", token,
                        "--videos-json", videos_json,
                        "--subtitles-dir", run_dirs["subtitles_dir"],
                    ]
                    proc = subprocess.run(cmd, capture_output=True, text=True)
                    if proc.stdout:
                        for line in proc.stdout.splitlines():
                            self.after(0, lambda msg=line: self.log_line(msg))
                    if proc.returncode != 0:
                        self.after(0, lambda: self.log_line(f"Submit script failed (code {proc.returncode})."))
                except Exception as e:
                    self.after(0, lambda err=e: self.log_line(f"Error running submit_to_catalog: {err}"))

            steps.append(("Submit to Catalog", run_submit))

        self.controller.run(steps, on_done=on_done)

    def on_cancel(self):
        """Request cooperative cancellation of the running pipeline."""
        # Cooperative cancel: current step finishes; pipeline stops before next step.
        self.controller.cancel()
        self.log_line(f"{timestamp()} cancelling...")

    def set_ui_running(self, running: bool):
        """Disable all controls except the run/cancel button while running."""

        def recurse(widget):
            for child in widget.winfo_children():
                # Keep RUN/CANCEL button interactive
                if child is self.run_btn:
                    continue
                try:
                    if running:
                        child.configure(state="disabled")
                    else:
                        # Keep log Text readonly
                        if isinstance(child, tk.Text):
                            child.configure(state="disabled")
                        else:
                            child.configure(state="normal")
                except tk.TclError:
                    pass
                recurse(child)

        recurse(self)

    def on_close(self):
        """Handle window close by saving settings and shutting down."""
        self._save_settings()
        self.destroy()


if __name__ == "__main__":
    App().mainloop()
