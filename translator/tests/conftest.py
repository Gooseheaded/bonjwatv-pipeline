import os
import sys
from pathlib import Path


# Ensure the translator/ directory is importable when running pytest from repo root
REPO_ROOT = Path(__file__).resolve().parents[2]
TRANSLATOR_DIR = REPO_ROOT / "translator"
if str(TRANSLATOR_DIR) not in sys.path:
    sys.path.insert(0, str(TRANSLATOR_DIR))

