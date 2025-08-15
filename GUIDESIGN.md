## Pipeline GUI Design

### Goals
- Provide a simple, utilitarian GUI (Win98-like) to configure and run the pipeline.
- Minimize dependencies and packaging complexity; keep the repo Python-only.
- Surface the key config, ordered steps, and run controls with basic logging.

### Tech Stack
- UI: Tkinter + ttk (standard library). Use `ttk.Style().theme_use('classic')` for a retro look.
- Optional validation: Pydantic for `pipeline-config.json` schema and clear error messages.
- Process control: `subprocess.Popen` in a worker thread; UI updates via `root.after`.
- Packaging: PyInstaller (prefer `onedir` for easier debugging; `onefile` optional).

### Primary Features
- Focus the URL workflow only (legacy Google settings are not surfaced in the UI).
- Inputs: pick the `.txt` file of YouTube URLs and the OpenAI API key (with show/hide toggle).
- Derived paths (read‑only): preview of run root and calculated folders (audio, vocals, subtitles, .cache) based on the `.txt` filename.
- Pipeline controls: simple step toggles only — no custom directory selection (use calculated directories).
  - Steps: Download audio, Voice isolation, Transcribe Korean subtitles, Normalize subtitles, Translate subtitles.
  - Transcription reads from the isolated vocals directory.
- Run controls: Run button with determinate progress and a log text area.
- Persistence: save `videos_path`, API key, and step toggles to `settings.json` on exit; load on start.

- Config:
  - URL workflow: file picker for `urls_file` and a computed preview of per‑run paths (read‑only). Note: global `website_dir`, `slang_file` remain configurable.
  - Legacy Google: `service_account_file`, `spreadsheet`, `worksheet`, `sheet_column` (visible but optional).
  - Advanced (optional): direct overrides for `video_list_file`, `audio_dir`, `vocals_dir`, `subtitles_dir`, `cache_dir` only if user disables URL auto‑derivation.
  - Buttons: Load, Save, Validate.
- Steps:
  - Listbox showing ordered `steps` with Up/Down, Add/Remove, Reset to defaults.
  - Inline validation: mutual exclusivity (source), ordering (source before per‑video; `manifest_builder` last).
- Run:
  - Buttons: Check Credentials, Dry‑run, Run, Stop.
  - Optional filter: process all or a specific `video_id`.
- Logs:
  - Readonly Text widget tailing `logs/pipeline_orchestrator.log`; Clear/Copy buttons.

### Integration Details
- The GUI will orchestrate the URL workflow by calling your pipeline entry points from background threads.
- Keep all background work off the main thread; only touch widgets via `after()`.
- Provide replaceable hooks for: download audio, transcribe, normalize, translate; wire these to your Python scripts or API functions.

### Project Structure (GUI)
- `gui/app.py` (single‑window app implementing the widget tree below)
- `gui/controller.py` (run/stop process, log tailing, threading helpers)
- `gui/settings.py` (load/save settings.json)

### Packaging
- PyInstaller spec for GUI entry (`gui/app.py`).
- Include example `pipeline-config.example.json` and short README note on `.env`.

### Widget tree (names → type)

```
root (Tk)
└─ Main (ttk.Frame)  padding=16
   ├─ InputsLabel (ttk.Label, text="Inputs")
   ├─ InputsFrame (ttk.Labelframe, text="Inputs")
   │  ├─ VideosLbl (ttk.Label, text="List of videos")
   │  ├─ VideosVar (ttk.Entry)  [strvar=videos_path]
   │  ├─ VideosBtn (ttk.Button, text="Select file…")
   │  ├─ HintLbl (ttk.Label, wraplength=600,
   │  │           text="This will use a folder called my_videos (or create it if it doesn't exist).")
   │  ├─ ApiLbl (ttk.Label, text="OpenAI API Key")
   │  ├─ ApiEntry (ttk.Entry, show="•") [strvar=api_key]
   │  └─ ApiToggle (ttk.Button, text="show/hide")
   ├─ PipelineLabel (ttk.Label, text="Pipeline steps")
   ├─ DerivedFrame (ttk.Labelframe, text="Calculated folders (read‑only)")
   │  ├─ RunRootLbl (ttk.Label, textvariable=run_root)
   │  ├─ AudioDirLbl (ttk.Label, textvariable=audio_dir)
   │  ├─ VocalsDirLbl (ttk.Label, textvariable=vocals_dir)
   │  ├─ SubsDirLbl (ttk.Label, textvariable=subtitles_dir)
   │  └─ CacheDirLbl (ttk.Label, textvariable=cache_dir)
   ├─ PipelineFrame (ttk.Labelframe, text="Pipeline steps")
   │  ├─ Step1Chk (ttk.Checkbutton, text="Download audio") [boolvar=do_download]
   │  ├─ Step2Chk (ttk.Checkbutton, text="Voice isolation") [boolvar=do_isolate]
   │  ├─ Step3Chk (ttk.Checkbutton, text="Transcribe korean subtitles") [boolvar=do_transcribe]
   │  ├─ Step4Chk (ttk.Checkbutton, text="Normalize subtitles") [boolvar=do_normalize]
   │  └─ Step5Chk (ttk.Checkbutton, text="Translate subtitles") [boolvar=do_translate]
   ├─ RunRow (ttk.Frame)
   │  ├─ RunBtn (ttk.Button, text="RUN")
   │  └─ Prog (ttk.Progressbar, mode="determinate")
   └─ Log (tk.Text, height=10, state="disabled", wrap="word")
```

### Layout & Behavior
- Two‑column grid for Inputs; DerivedFrame shows read‑only calculated paths.
- show/hide toggles API entry masking.
- Run executes selected steps on a background thread, logs progress, and re‑enables controls on completion.
- Order: Download → Voice isolation → Transcribe (reads vocals) → Normalize → Translate.

### Persistence & File dialogs
- settings.json with: `videos_path`, API key, step toggles, folder modes/paths.
- askopenfilename/askdirectory for selecting inputs and folders.

### Minimal runnable skeleton

See the designer’s provided stub-based example for a runnable Tk/ttk shell, ready to wire into pipeline hooks.
