import pathlib


def pytest_ignore_collect(path, config):
    p = pathlib.Path(str(path))
    # Ignore duplicate/copy repos and virtualenvs
    for part in p.parts:
        if part.startswith("bonjwatv-pipeline"):
            return True
        if part in {".venv", "dist"}:
            return True
    return False

