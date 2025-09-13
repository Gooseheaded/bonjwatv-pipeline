import argparse
import json
import logging
import os
import sys
import time

import build_videos_json
import download_audio
import fetch_video_metadata
import google_sheet_read
import google_sheet_write
import isolate_vocals
import manifest_builder
import normalize_srt
import read_youtube_urls
import transcribe_audio
import translate_subtitles
import translate_title
import upload_subtitles
from run_paths import compute_run_paths


def setup_logging():
    """Configure root logging to file and console for the orchestrator."""
    os.makedirs("logs", exist_ok=True)
    handler = logging.FileHandler("logs/pipeline_orchestrator.log", encoding="utf-8")
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
    handler.setFormatter(fmt)
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)
    console = logging.StreamHandler()
    console.setFormatter(fmt)
    root.addHandler(console)
    return root


def main():  # noqa: C901
    """Run the pipeline orchestrator according to the provided config file."""
    setup_logging()
    p = argparse.ArgumentParser(description="Pipeline orchestrator")
    p.add_argument("--config", required=True, help="Path to pipeline-config.json")
    args = p.parse_args()

    # Load configuration
    config = json.load(open(args.config, encoding="utf-8"))
    required_keys = [
        "video_list_file",
        "video_metadata_dir",
        "audio_dir",
        "vocals_dir",
        "subtitles_dir",
        "cache_dir",
        "slang_file",
        "website_dir",
    ]
    for k in required_keys:
        if k not in config:
            logging.error("Missing required config key: %s", k)
            sys.exit(1)

    steps = config.get("steps")
    if not steps or not isinstance(steps, list):
        logging.error('Config must define an ordered list of steps under "steps"')
        sys.exit(1)

    allowed_per_video = [
        "fetch_video_metadata",
        "download_audio",
        "isolate_vocals",
        "transcribe_audio",
        "normalize_srt",
        "translate_subtitles",
        "translate_title",
        "upload_subtitles",
    ]
    allowed_global = [
        "google_sheet_read",
        "google_sheet_write",
        "read_youtube_urls",
        "build_videos_json",
        "manifest_builder",
    ]
    allowed = set(allowed_per_video + allowed_global)

    # Validate steps
    seen = set()
    for s in steps:
        if s not in allowed:
            logging.error(
                'Unknown step "%s". Allowed: %s', s, ", ".join(sorted(allowed))
            )
            sys.exit(1)
        if s in seen:
            logging.error('Duplicate step "%s" in steps list', s)
            sys.exit(1)
        seen.add(s)

    # Enforce single source of videos.json: either Google Sheet or URL list, not both
    if "google_sheet_read" in steps and "read_youtube_urls" in steps:
        logging.error(
            "Use only one source step: either google_sheet_read or read_youtube_urls, not both"
        )
        sys.exit(1)

    if "manifest_builder" in steps and steps[-1] != "manifest_builder":
        logging.error("manifest_builder must be the last step in the list")
        sys.exit(1)
    for gstep in ("google_sheet_read", "read_youtube_urls"):
        if gstep in steps:
            g_index = steps.index(gstep)
            for s in steps:
                if s in allowed_per_video and steps.index(s) < g_index:
                    logging.error("%s must come before all per-video steps", gstep)
                    sys.exit(1)

    video_list_file = config["video_list_file"]
    video_metadata_dir = config["video_metadata_dir"]
    audio_dir = config["audio_dir"]
    vocals_dir = config["vocals_dir"]
    subtitles_dir = config["subtitles_dir"]
    cache_dir = config["cache_dir"]
    slang_file = config["slang_file"]
    website_dir = config["website_dir"]

    # If using URL list workflow, derive per-run directories from URLs filename
    if "read_youtube_urls" in steps:
        urls_file = config.get("urls_file", "")
        if not urls_file:
            logging.error(
                "urls_file must be set in config when using read_youtube_urls"
            )
            sys.exit(1)
        paths = compute_run_paths(urls_file)
        video_list_file = paths["video_list_file"]
        audio_dir = paths["audio_dir"]
        vocals_dir = paths["vocals_dir"]
        subtitles_dir = paths["subtitles_dir"]
        cache_dir = paths["cache_dir"]
        # Ensure directories exist
        for d in (
            os.path.dirname(video_list_file),
            audio_dir,
            vocals_dir,
            subtitles_dir,
            cache_dir,
        ):
            os.makedirs(d, exist_ok=True)

    logging.info("RUN START")

    # Execute global steps that come before per-video steps
    for s in steps:
        if s == "google_sheet_read":
            logging.info("START google_sheet_read")
            _t0 = time.monotonic()
            if not google_sheet_read.run_google_sheet_read(
                spreadsheet=config.get("spreadsheet", ""),
                worksheet=config.get("worksheet", ""),
                output=video_list_file,
                service_account_file=config.get("service_account_file", ""),
            ):
                logging.error("google_sheet_read failed, aborting pipeline")
                logging.info("END google_sheet_read FAIL (%.1fs)", time.monotonic() - _t0)
                sys.exit(1)
            logging.info("END google_sheet_read OK (%.1fs)", time.monotonic() - _t0)
        elif s == "read_youtube_urls":
            logging.info("START read_youtube_urls")
            _t0 = time.monotonic()
            if not read_youtube_urls.run_read_youtube_urls(
                urls_file=config.get("urls_file", ""), output=video_list_file
            ):
                logging.error("read_youtube_urls failed, aborting pipeline")
                logging.info("END read_youtube_urls FAIL (%.1fs)", time.monotonic() - _t0)
                sys.exit(1)
            logging.info("END read_youtube_urls OK (%.1fs)", time.monotonic() - _t0)
        elif s in allowed_per_video:
            break

    # Load video list after potential google_sheet_read
    try:
        videos = json.load(open(video_list_file, encoding="utf-8"))
    except Exception as e:
        logging.error("Failed to load video list file %s: %s", video_list_file, e)
        sys.exit(1)

    # Calculate total number of per-video operations for progress reporting
    per_video_steps_in_run = [s for s in steps if s in allowed_per_video]
    total_ops = len(videos) * len(per_video_steps_in_run)
    current_op = 0

    # Process per-video steps in the specified order
    for v in videos:
        vid = v["v"]
        for s in steps:
            if s not in allowed_per_video:
                continue

            # This is a placeholder for the actual step execution logic
            step_executed = False

            if s == "fetch_video_metadata":
                logging.info("START fetch_video_metadata video=%s", vid)
                _t0 = time.monotonic()
                if not fetch_video_metadata.run_fetch_video_metadata(
                    video_id=vid, output_dir=video_metadata_dir
                ):
                    logging.error("END fetch_video_metadata video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END fetch_video_metadata video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "download_audio":
                logging.info("START download_audio video=%s", vid)
                _t0 = time.monotonic()
                if not download_audio.run_download_audio(
                    url=v.get("youtube_url", f"https://www.youtube.com/watch?v={vid}"),
                    video_id=vid,
                    output_dir=audio_dir,
                ):
                    logging.error("END download_audio video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END download_audio video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "isolate_vocals":
                logging.info("START isolate_vocals video=%s", vid)
                _t0 = time.monotonic()
                audio_path = os.path.join(audio_dir, f"{vid}.mp3")
                if not isolate_vocals.run_isolate_vocals(
                    input_file=audio_path, output_dir=vocals_dir
                ):
                    logging.error("END isolate_vocals video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END isolate_vocals video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "transcribe_audio":
                logging.info("START transcribe_audio video=%s", vid)
                _t0 = time.monotonic()
                # Prefer isolated vocals if available; fall back to original audio
                vocals_path = os.path.join(vocals_dir, vid, "vocals.wav")
                input_audio = (
                    vocals_path
                    if os.path.exists(vocals_path)
                    else os.path.join(audio_dir, f"{vid}.mp3")
                )
                kr_srt = os.path.join(subtitles_dir, f"kr_{vid}.srt")

                provider = config.get("transcription_provider", "local")

                if not transcribe_audio.run_transcribe_audio(
                    audio_path=input_audio,
                    output_subtitle=kr_srt,
                    provider=provider,
                    model_size=(
                        config.get("transcription_model_size", "large")
                        if provider == "local"
                        else None
                    ),
                    api_model=(
                        config.get("transcription_api_model", "whisper-1")
                        if provider == "openai"
                        else None
                    ),
                ):
                    logging.error("END transcribe_audio video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END transcribe_audio video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "normalize_srt":
                logging.info("START normalize_srt video=%s", vid)
                _t0 = time.monotonic()
                kr_srt = os.path.join(subtitles_dir, f"kr_{vid}.srt")
                if not normalize_srt.run_normalize_srt(
                    input_file=kr_srt, output_file=kr_srt
                ):
                    logging.error("END normalize_srt video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END normalize_srt video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "translate_subtitles":
                logging.info("START translate_subtitles video=%s", vid)
                _t0 = time.monotonic()
                kr_srt = os.path.join(subtitles_dir, f"kr_{vid}.srt")
                en_srt = os.path.join(subtitles_dir, f"en_{vid}.srt")
                if not translate_subtitles.run_translate_subtitles(
                    input_file=kr_srt,
                    output_file=en_srt,
                    slang_file=slang_file,
                    cache_dir=cache_dir,
                ):
                    logging.error("END translate_subtitles video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END translate_subtitles video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "translate_title":
                logging.info("START translate_title video=%s", vid)
                _t0 = time.monotonic()
                if not translate_title.run_translate_title(
                    video_id=vid, metadata_dir=video_metadata_dir, cache_dir=cache_dir
                ):
                    logging.error("END translate_title video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END translate_title video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True
            elif s == "upload_subtitles":
                logging.info("START upload_subtitles video=%s", vid)
                _t0 = time.monotonic()
                en_srt = os.path.join(subtitles_dir, f"en_{vid}.srt")
                if not upload_subtitles.run_upload_subtitles(
                    input_file=en_srt, cache_dir=cache_dir
                ):
                    logging.error("END upload_subtitles video=%s FAIL (%.1fs)", vid, time.monotonic() - _t0)
                    break
                logging.info("END upload_subtitles video=%s OK (%.1fs)", vid, time.monotonic() - _t0)
                step_executed = True

            if step_executed:
                current_op += 1
                # Use a print statement that is distinct from logging
                print(f"PROGRESS:{current_op}/{total_ops}")  # noqa: T201

    # Prepare enriched videos path
    enriched_videos = os.path.join(
        os.path.dirname(os.path.abspath(video_list_file)), "videos_enriched.json"
    )

    # Execute remaining global steps in order
    for s in steps:
        if s == "google_sheet_write":
            logging.info("START google_sheet_write")
            _t0 = time.monotonic()
            if not google_sheet_write.run_google_sheet_write(
                video_list_file=video_list_file,
                cache_dir=cache_dir,
                spreadsheet=config.get("spreadsheet", ""),
                worksheet=config.get("worksheet", ""),
                column_name=config.get("sheet_column", ""),
                service_account_file=config.get("service_account_file", ""),
            ):
                logging.error("google_sheet_write failed")
            logging.info("END google_sheet_write OK (%.1fs)", time.monotonic() - _t0)
        elif s == "build_videos_json":
            logging.info("START build_videos_json")
            _t0 = time.monotonic()
            if not build_videos_json.run_build_videos_json(
                video_list_file=video_list_file,
                metadata_dir=video_metadata_dir,
                cache_dir=cache_dir,
                output=enriched_videos,
            ):
                logging.error("build_videos_json failed")
            logging.info("END build_videos_json OK (%.1fs)", time.monotonic() - _t0)
        elif s == "manifest_builder":
            logging.info("START manifest_builder")
            _t0 = time.monotonic()
            manifest_out = os.path.join(website_dir, "subtitles.json")
            # Use enriched videos list if it has been built in this run
            videos_input = (
                enriched_videos if "build_videos_json" in steps else video_list_file
            )
            if not manifest_builder.run_manifest_builder(
                video_list_file=videos_input,
                subtitles_dir=subtitles_dir,
                output_file=manifest_out,
                details_dir=video_metadata_dir,
            ):
                logging.error("manifest_builder failed")
            logging.info("END manifest_builder OK (%.1fs)", time.monotonic() - _t0)

    logging.info("RUN END")


if __name__ == "__main__":
    main()
