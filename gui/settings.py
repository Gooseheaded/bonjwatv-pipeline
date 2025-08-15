import json
from pathlib import Path
from typing import Dict, Any


SETTINGS_FILE = Path(__file__).resolve().parent / "settings.json"


def load_settings(defaults: Dict[str, Any]) -> Dict[str, Any]:
    try:
        data = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return dict(defaults)
    except Exception:
        return dict(defaults)
    merged = dict(defaults)
    merged.update({k: v for k, v in data.items() if k in defaults})
    return merged


def save_settings(values: Dict[str, Any]) -> None:
    SETTINGS_FILE.write_text(json.dumps(values, indent=2), encoding="utf-8")

