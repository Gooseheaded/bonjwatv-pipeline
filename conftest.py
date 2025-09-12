import pathlib


def pytest_ignore_collect(collection_path, config):
    # Accept both py.path.local (pytest<9) and pathlib.Path (pytest>=9)
    p = pathlib.Path(str(collection_path))
    # Ignore duplicate/copy repos and virtualenvs
    for part in p.parts:
        if part.startswith("bonjwatv-pipeline"):
            return True
        if part in {".venv", "dist"}:
            return True
    return False
