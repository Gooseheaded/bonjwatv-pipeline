import os
import sys
import json
import subprocess
import tkinter as tk
from tkinter import filedialog, messagebox
from tkinter import ttk
from datetime import datetime
from typing import Dict

from .controller import PipelineController
from .settings import load_settings, save_settings
from run_paths import compute_run_paths


def timestamp() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


# ---- Pipeline step stubs (replace these with real pipeline calls) -----------
def run_download_audio(videos_path: str, run_dirs: dict, log):
    log(f"{timestamp()} downloading audio to {run_dirs['audio_dir']} ...")


def run_isolate_vocals(run_dirs: dict, log):
    log(f"{timestamp()} isolating vocals into {run_dirs['vocals_dir']} ...")


def run_transcribe(run_dirs: dict, log):
    log(f"{timestamp()} transcribing from vocals in {run_dirs['vocals_dir']} to SRT in {run_dirs['subtitles_dir']} ...")


def run_normalize(run_dirs: dict, log):
    log(f"{timestamp()} normalizing subtitles in {run_dirs['subtitles_dir']} ...")


def run_translate(run_dirs: dict, log):
    log(f"{timestamp()} translating subtitles in {run_dirs['subtitles_dir']} ...")


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Subtitle Pipeline")
        self.minsize(820, 560)

        self.controller = PipelineController()
        self.vars = self._make_vars()
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

        ttk.Label(inputs, text="List of videos").grid(row=0, column=0, sticky="w", pady=4, padx=(8, 8))
        videos_entry = ttk.Entry(inputs, textvariable=self.vars["videos_path"])
        videos_entry.grid(row=0, column=1, sticky="ew", pady=4)
        ttk.Button(inputs, text="Select file…", command=self.pick_videos_file).grid(row=0, column=2, padx=(8, 8), pady=4)

        ttk.Label(inputs, text="This will use a folder called my_videos (or create it if it doesn't exist).", wraplength=600) \
            .grid(row=1, column=1, columnspan=2, sticky="w", pady=(0, 8))

        ttk.Label(inputs, text="OpenAI API Key").grid(row=2, column=0, sticky="w", padx=(8, 8))
        self.api_entry = ttk.Entry(inputs, textvariable=self.vars["api_key"], show="•")
        self.api_entry.grid(row=2, column=1, sticky="ew")
        ttk.Button(inputs, text="show/hide", command=self.toggle_api).grid(row=2, column=2, padx=(8, 8))

        # Derived (calculated) folders preview
        derived = ttk.Labelframe(main, text="Calculated folders (read-only)")
        derived.grid(sticky="ew", pady=(0, 12))
        for i in range(2):
            derived.columnconfigure(i, weight=1 if i == 1 else 0)
        self.run_root_var = tk.StringVar(value="")
        self.audio_dir_var = tk.StringVar(value="")
        self.vocals_dir_var = tk.StringVar(value="")
        self.subs_dir_var = tk.StringVar(value="")
        self.cache_dir_var = tk.StringVar(value="")
        ttk.Label(derived, text="Run root").grid(row=0, column=0, sticky="w", padx=(8, 8))
        ttk.Label(derived, textvariable=self.run_root_var).grid(row=0, column=1, sticky="w")
        ttk.Label(derived, text="Audio").grid(row=1, column=0, sticky="w", padx=(8, 8))
        ttk.Label(derived, textvariable=self.audio_dir_var).grid(row=1, column=1, sticky="w")
        ttk.Label(derived, text="Vocals").grid(row=2, column=0, sticky="w", padx=(8, 8))
        ttk.Label(derived, textvariable=self.vocals_dir_var).grid(row=2, column=1, sticky="w")
        ttk.Label(derived, text="Subtitles").grid(row=3, column=0, sticky="w", padx=(8, 8))
        ttk.Label(derived, textvariable=self.subs_dir_var).grid(row=3, column=1, sticky="w")
        ttk.Label(derived, text="Cache").grid(row=4, column=0, sticky="w", padx=(8, 8))
        ttk.Label(derived, textvariable=self.cache_dir_var).grid(row=4, column=1, sticky="w")

        # Pipeline
        pipe = ttk.Labelframe(main, text="Pipeline steps")
        pipe.grid(sticky="ew", pady=(0, 12))
        pipe.columnconfigure(0, weight=1)

        # Step 1
        ttk.Checkbutton(pipe, text="Download audio", variable=self.vars["do_download"],
                        command=self.refresh_states).grid(row=0, column=0, sticky="w", padx=(8, 8), pady=(6, 2))

        # Step 2
        ttk.Checkbutton(pipe, text="Voice isolation", variable=self.vars["do_isolate"],
                        command=self.refresh_states).grid(row=1, column=0, sticky="w", padx=(8, 8))

        # Step 3
        step3_frame = ttk.Frame(pipe)
        step3_frame.grid(row=2, column=0, sticky="w")
        ttk.Checkbutton(step3_frame, text="Transcribe korean subtitles", variable=self.vars["do_transcribe"],
                        command=self.refresh_states).pack(side="left", padx=(8, 8))
        self.transcribe_provider_combo = ttk.Combobox(step3_frame, textvariable=self.vars["transcription_provider"],
                                                     values=["local", "openai"], width=10)
        self.transcribe_provider_combo.pack(side="left")

        # Step 4
        ttk.Checkbutton(pipe, text="Normalize subtitles", variable=self.vars["do_normalize"],
                        command=self.refresh_states).grid(row=3, column=0, sticky="w", padx=(8, 8))

        # Step 5
        ttk.Checkbutton(pipe, text="Translate subtitles", variable=self.vars["do_translate"],
                        command=self.refresh_states).grid(row=4, column=0, sticky="w", padx=(8, 8), pady=(0, 4))

        # Run + progress
        runrow = ttk.Frame(main)
        runrow.grid(sticky="ew", pady=(0, 8))
        runrow.columnconfigure(1, weight=1)
        self.run_btn = ttk.Button(runrow, text="RUN", command=self.on_run)
        self.run_btn.grid(row=0, column=0, padx=(0, 12))
        self.progress = ttk.Progressbar(runrow, mode="determinate")
        self.progress.grid(row=0, column=1, sticky="ew")

        # Log controls (copy/clear)
        logctl = ttk.Frame(main)
        logctl.grid(sticky="e", pady=(4, 4))
        ttk.Button(logctl, text="Copy log", command=self.copy_log).grid(row=0, column=0, padx=(0, 8))
        ttk.Button(logctl, text="Clear log", command=self.clear_log).grid(row=0, column=1)

        # Log
        self.log = tk.Text(main, height=10, wrap="word", state="disabled")
        self.log.grid(sticky="nsew")
        main.rowconfigure(4, weight=1)

        # Bind
        self.vars["videos_path"].trace_add("write", lambda *_: self.update_derived_paths())

        self.update_derived_paths()
        self.refresh_states()
        self.protocol("WM_DELETE_WINDOW", self.on_close)

    # ----- Vars / settings
    def _make_vars(self) -> Dict[str, tk.Variable]:
        v: Dict[str, tk.Variable] = {
            "videos_path": tk.StringVar(value="my_videos.txt"),
            "api_key": tk.StringVar(value=""),
            "do_download": tk.BooleanVar(value=True),
            "do_isolate": tk.BooleanVar(value=True),
            "do_transcribe": tk.BooleanVar(value=True),
            "transcription_provider": tk.StringVar(value="local"),
            "do_normalize": tk.BooleanVar(value=True),
            "do_translate": tk.BooleanVar(value=False),
        }
        return v

    def _load_settings(self):
        defaults = {k: v.get() for k, v in self.vars.items()}
        data = load_settings(defaults)
        for k, var in self.vars.items():
            var.set(data.get(k, var.get()))

    def _save_settings(self):
        values = {k: v.get() for k, v in self.vars.items()}
        save_settings(values)

    # ----- UI handlers
    def toggle_api(self):
        self.api_entry.configure(show="" if self.api_entry.cget("show") else "•")

    def pick_videos_file(self):
        path = filedialog.askopenfilename(
            title="Select list of videos",
            filetypes=[("Text files", "*.txt"), ("All files", "*.*")]
        )
        if path:
            self.vars["videos_path"].set(path)

    def update_derived_paths(self):
        path = self.vars["videos_path"].get()
        if not path:
            return
        try:
            run_dirs = compute_run_paths(path)
        except Exception:
            return
        self.run_root_var.set(run_dirs['run_root'])
        self.audio_dir_var.set(run_dirs['audio_dir'])
        self.vocals_dir_var.set(run_dirs['vocals_dir'])
        self.subs_dir_var.set(run_dirs['subtitles_dir'])
        self.cache_dir_var.set(run_dirs['cache_dir'])

    def log_line(self, text: str):
        self.log.configure(state="normal")
        self.log.insert("end", text + "\n")
        self.log.see("end")
        self.log.configure(state="disabled")

    def clear_log(self):
        self.log.configure(state="normal")
        self.log.delete("1.0", "end")
        self.log.configure(state="disabled")

    def copy_log(self):
        try:
            content = self.log.get("1.0", "end-1c")
            self.clipboard_clear()
            self.clipboard_append(content)
        except tk.TclError:
            pass

    def refresh_states(self):
        def set_state(widget, enabled: bool):
            widget.configure(state="normal" if enabled else "disabled")

        set_state(self.transcribe_provider_combo, self.vars["do_transcribe"].get())

    def on_run(self):
        run_dirs = compute_run_paths(self.vars["videos_path"].get())

        # Build orchestrator config under run_root
        def build_cfg() -> str:
            base_path = os.path.join(os.getcwd(), 'pipeline-config.json')
            base = {}
            try:
                with open(base_path, 'r', encoding='utf-8') as f:
                    base = json.load(f)
            except Exception:
                base = {}

            steps = ['read_youtube_urls', 'fetch_video_metadata', 'translate_title', 'build_videos_json']
            if self.vars["do_download"].get():
                steps.append('download_audio')
            if self.vars["do_isolate"].get():
                steps.append('isolate_vocals')
            if self.vars["do_transcribe"].get():
                steps.append('transcribe_audio')
            if self.vars["do_normalize"].get():
                steps.append('normalize_srt')
            if self.vars["do_translate"].get():
                steps.append('translate_subtitles')

            cfg = {
                'video_list_file': run_dirs['video_list_file'],
                'video_metadata_dir': base.get('video_metadata_dir', 'metadata'),
                'audio_dir': run_dirs['audio_dir'],
                'vocals_dir': run_dirs['vocals_dir'],
                'subtitles_dir': run_dirs['subtitles_dir'],
                'cache_dir': run_dirs['cache_dir'],
                'slang_file': base.get('slang_file', 'slang/KoreanSlang.txt'),
                'website_dir': base.get('website_dir', 'website'),
                'urls_file': self.vars['videos_path'].get(),
                'steps': steps,
                'transcription_provider': self.vars['transcription_provider'].get(),
                # TODO: Make the API model configurable in the GUI
                'transcription_api_model': 'whisper-1',
            }
            # carry optional Google keys to satisfy schema but GUI ignores them
            for k in ('service_account_file', 'spreadsheet', 'worksheet', 'sheet_column'):
                if k in base:
                    cfg[k] = base[k]
            os.makedirs(run_dirs['run_root'], exist_ok=True)
            cfg_path = os.path.join(run_dirs['run_root'], 'gui-config.json')
            with open(cfg_path, 'w', encoding='utf-8') as f:
                json.dump(cfg, f, ensure_ascii=False, indent=2)
            return cfg_path

        cfg_path = build_cfg()

        def run_orchestrator():
            env = os.environ.copy()
            api_key = self.vars['api_key'].get().strip()
            if api_key:
                env['OPENAI_API_KEY'] = api_key
            cmd = [sys.executable, 'pipeline_orchestrator.py', '--config', cfg_path]
            try:
                proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=env)
            except Exception as e:
                self.after(0, lambda: messagebox.showerror('Error', f'Failed to start orchestrator: {e}'))
                return
            try:
                assert proc.stdout is not None
                for line in proc.stdout:
                    if line:
                        self.after(0, lambda l=line.rstrip(): self.log_line(l))
                    if self.controller.is_cancelled():
                        try:
                            proc.terminate()
                        except Exception:
                            pass
                        break
                proc.wait()
            finally:
                # no additional cleanup
                pass

        steps = [("Run orchestrator", run_orchestrator)]

        # Lock UI (except Cancel)
        self.set_ui_running(True)
        self.run_btn.configure(text="Cancel", command=self.on_cancel)
        self.progress.configure(maximum=len(steps), value=0)

        def on_step(idx: int, total: int, name: str):
            self.after(0, lambda: self.log_line(f"{timestamp()} step {idx}/{total}: {name}"))
            self.after(0, lambda: self.progress.configure(value=idx - 1))

        def on_done(exc: Exception | None):
            if exc is None:
                self.after(0, lambda: self.log_line(f"{timestamp()} pipeline finished."))
            else:
                self.after(0, lambda: messagebox.showerror("Error", str(exc)))
            self.after(0, lambda: self.progress.configure(value=self.progress["maximum"]))
            self.after(0, lambda: self.run_btn.configure(text="RUN", command=self.on_run))
            self.after(0, lambda: self.set_ui_running(False))

        # Start background work
        self.controller.run(steps, on_step=on_step, on_done=on_done)

    def on_cancel(self):
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
        self._save_settings()
        self.destroy()


if __name__ == "__main__":
    App().mainloop()
