import json
import os
import sys
from pathlib import Path
from typing import Any


def _user_config_dir() -> Path:
    """Return an OS-appropriate, user-writable config directory.

    - Windows: %APPDATA%/BWKTSubtitlePipeline
    - macOS: ~/Library/Application Support/BWKTSubtitlePipeline
    - Linux/Unix: $XDG_CONFIG_HOME/BWKTSubtitlePipeline or ~/.config/BWKTSubtitlePipeline
    """
    app_name = "BWKTSubtitlePipeline"
    if sys.platform.startswith("win"):
        base = os.environ.get("APPDATA", str(Path.home()))
        return Path(base) / app_name
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / app_name
    # Linux/Unix
    base = os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))
    return Path(base) / app_name


SETTINGS_FILE = _user_config_dir() / "settings.json"


def load_settings(defaults: dict[str, Any]) -> dict[str, Any]:
    """Load persisted settings and merge them over defaults."""
    try:
        data = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return dict(defaults)
    except Exception:
        return dict(defaults)
    merged = dict(defaults)
    merged.update({k: v for k, v in data.items() if k in defaults})
    return merged


def save_settings(values: dict[str, Any]) -> None:
    """Persist settings to disk as JSON."""
    SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS_FILE.write_text(json.dumps(values, indent=2), encoding="utf-8")
