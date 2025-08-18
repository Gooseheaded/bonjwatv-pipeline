import json
import os
import sys

sys.path.insert(0, os.getcwd())

from read_youtube_urls import extract_video_id, run_read_youtube_urls


def test_extract_video_id_variants():
    assert extract_video_id("https://www.youtube.com/watch?v=abcDEF123") == "abcDEF123"
    assert extract_video_id("https://youtu.be/abcDEF123") == "abcDEF123"
    assert extract_video_id("https://www.youtube.com/shorts/abcDEF123") == "abcDEF123"
    assert extract_video_id("notaurl") is None


def test_read_youtube_urls_builds_minimal_json(tmp_path):
    urls = tmp_path / "urls.txt"
    urls.write_text(
        "\n".join(
            [
                "https://www.youtube.com/watch?v=abcDEF123",
                "https://youtu.be/abcDEF123",  # duplicate id
                "https://www.youtube.com/shorts/XYZ_987",
                "invalid",
            ]
        ),
        encoding="utf-8",
    )
    out = tmp_path / "videos.json"
    assert run_read_youtube_urls(str(urls), str(out))
    data = json.loads(out.read_text(encoding="utf-8"))
    ids = [x["v"] for x in data]
    assert ids == ["abcDEF123", "XYZ_987"]
    assert data[0]["youtube_url"].startswith("https://")
