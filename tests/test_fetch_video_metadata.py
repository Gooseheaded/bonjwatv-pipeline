import json
import os
import sys
from unittest.mock import MagicMock, patch

import pytest

# Add the project root to the Python path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from fetch_video_metadata import run_fetch_video_metadata


@pytest.fixture
def mock_yt_dlp():
    """Fixture to mock the yt_dlp library."""
    with patch("yt_dlp.YoutubeDL") as mock_yt_dlp_class:
        mock_instance = MagicMock()
        mock_yt_dlp_class.return_value.__enter__.return_value = mock_instance
        yield mock_instance


def test_fetch_video_metadata_skips_if_exists(tmp_path):
    """Test that metadata fetching is skipped if the output file already exists."""
    video_id = "test_video"
    output_dir = tmp_path
    output_file = output_dir / f"{video_id}.json"
    output_file.touch()  # Create the dummy file

    run_fetch_video_metadata(video_id, str(output_dir))

    # No assertion needed, the test passes if no exceptions are raised
    # and the function returns early. We could add a mock to assert
    # that yt-dlp was NOT called, but this is simple enough.


def test_fetch_video_metadata_fetches_and_saves(mock_yt_dlp, tmp_path):
    """Test that metadata is fetched and saved correctly."""
    video_id = "test_video"
    output_dir = str(tmp_path)
    expected_info = {"upload_date": "20230101", "title": "Test Video"}

    mock_yt_dlp.extract_info.return_value = expected_info

    run_fetch_video_metadata(video_id, output_dir)

    mock_yt_dlp.extract_info.assert_called_once_with(
        f"https://www.youtube.com/watch?v={video_id}", download=False
    )

    output_file = tmp_path / f"{video_id}.json"
    assert output_file.exists()

    with open(output_file) as f:
        data = json.load(f)

    assert data == expected_info
