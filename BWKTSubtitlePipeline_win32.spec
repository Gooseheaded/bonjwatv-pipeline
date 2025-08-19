# -*- mode: python ; coding: utf-8 -*-


a = Analysis(
    ['gui/app.py'],
    pathex=[],
    binaries=[],
    datas=[
        ('gui/settings.json', 'gui'),
        ('pipeline-config.example.json', '.'),
        ('slang/KoreanSlang.txt', 'slang'),
        ('common.py', '.'),
        ('run_paths.py', '.'),
        ('pipeline_orchestrator.py', '.'),
        ('read_youtube_urls.py', '.'),
        ('fetch_video_metadata.py', '.'),
        ('translate_title.py', '.'),
        ('build_videos_json.py', '.'),
        ('download_audio.py', '.'),
        ('isolate_vocals.py', '.'),
        ('transcribe_audio.py', '.'),
        ('normalize_srt.py', '.'),
        ('translate_subtitles.py', '.'),
        ('upload_subtitles.py', '.'),
        ('google_sheet_read.py', '.'),
        ('google_sheet_write.py', '.'),
        ('manifest_builder.py', '.'),
        # Place backend exe under bin/ to avoid confusing nested paths
        # Copy backend exe into bin/ (dest is a directory)
        ('dist/Orchestrator.exe', 'bin'),
    ],
    hiddenimports=['openai'],  # omit whisper if not installed on Windows build host
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='BWKTSubtitlePipeline',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='BWKTSubtitlePipeline',
)
