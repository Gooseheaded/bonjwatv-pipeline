#!/usr/bin/env python3
import os
import json
import subprocess
import argparse
import logging
from run_paths import compute_run_paths


def setup_logging():
    os.makedirs('logs', exist_ok=True)
    handler = logging.FileHandler('logs/pipeline_orchestrator.log', encoding='utf-8')
    fmt = logging.Formatter('%(asctime)s %(levelname)s %(message)s')
    handler.setFormatter(fmt)
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)
    console = logging.StreamHandler()
    console.setFormatter(fmt)
    root.addHandler(console)
    return root


def run_step(cmd):
    logging.info('Running: %s', ' '.join(cmd))
    result = subprocess.run(cmd)
    if result.returncode != 0:
        logging.error('Step failed (%d): %s', result.returncode, ' '.join(cmd))
        return False
    return True


def main():
    log = setup_logging()
    p = argparse.ArgumentParser(description='Pipeline orchestrator')
    p.add_argument('--config', required=True, help='Path to pipeline-config.json')
    args = p.parse_args()

    # Load configuration
    config = json.load(open(args.config, encoding='utf-8'))
    required_keys = ['video_list_file', 'video_metadata_dir', 'audio_dir', 'vocals_dir',
                     'subtitles_dir', 'cache_dir', 'slang_file', 'website_dir']
    for k in required_keys:
        if k not in config:
            logging.error('Missing required config key: %s', k)
            exit(1)

    steps = config.get('steps')
    if not steps or not isinstance(steps, list):
        logging.error('Config must define an ordered list of steps under "steps"')
        exit(1)

    allowed_per_video = [
        'fetch_video_metadata',
        'download_audio',
        'isolate_vocals',
        'transcribe_audio',
        'normalize_srt',
        'translate_subtitles',
        'translate_title',
        'upload_subtitles',
    ]
    allowed_global = [
        'google_sheet_read',
        'google_sheet_write',
        'read_youtube_urls',
        'build_videos_json',
        'manifest_builder',
    ]
    allowed = set(allowed_per_video + allowed_global)

    # Validate steps
    seen = set()
    for s in steps:
        if s not in allowed:
            logging.error('Unknown step "%s". Allowed: %s', s, ', '.join(sorted(allowed)))
            exit(1)
        if s in seen:
            logging.error('Duplicate step "%s" in steps list', s)
            exit(1)
        seen.add(s)

    # Enforce single source of videos.json: either Google Sheet or URL list, not both
    if 'google_sheet_read' in steps and 'read_youtube_urls' in steps:
        logging.error('Use only one source step: either google_sheet_read or read_youtube_urls, not both')
        exit(1)

    if 'manifest_builder' in steps and steps[-1] != 'manifest_builder':
        logging.error('manifest_builder must be the last step in the list')
        exit(1)
    for gstep in ('google_sheet_read', 'read_youtube_urls'):
        if gstep in steps:
            g_index = steps.index(gstep)
            for s in steps:
                if s in allowed_per_video and steps.index(s) < g_index:
                    logging.error('%s must come before all per-video steps', gstep)
                    exit(1)

    video_list_file = config['video_list_file']
    video_metadata_dir = config['video_metadata_dir']
    audio_dir = config['audio_dir']
    vocals_dir = config['vocals_dir']
    subtitles_dir = config['subtitles_dir']
    cache_dir = config['cache_dir']
    slang_file = config['slang_file']
    website_dir = config['website_dir']

    # If using URL list workflow, derive per-run directories from URLs filename
    if 'read_youtube_urls' in steps:
        urls_file = config.get('urls_file', '')
        if not urls_file:
            logging.error('urls_file must be set in config when using read_youtube_urls')
            exit(1)
        paths = compute_run_paths(urls_file)
        video_list_file = paths['video_list_file']
        audio_dir = paths['audio_dir']
        vocals_dir = paths['vocals_dir']
        subtitles_dir = paths['subtitles_dir']
        cache_dir = paths['cache_dir']
        # Ensure directories exist
        for d in (os.path.dirname(video_list_file), audio_dir, vocals_dir, subtitles_dir, cache_dir):
            os.makedirs(d, exist_ok=True)

    # Execute global steps that come before per-video steps
    for s in steps:
        if s == 'google_sheet_read':
            if not run_step(['python', 'google_sheet_read.py',
                             '--spreadsheet', config.get('spreadsheet', ''),
                             '--worksheet', config.get('worksheet', ''),
                             '--output', video_list_file,
                             '--service-account-file', config.get('service_account_file', '')]):
                logging.error('google_sheet_read failed, aborting pipeline')
                exit(1)
        elif s == 'read_youtube_urls':
            if not run_step(['python', 'read_youtube_urls.py',
                             '--urls-file', config.get('urls_file', ''),
                             '--output', video_list_file]):
                logging.error('read_youtube_urls failed, aborting pipeline')
                exit(1)
        elif s in allowed_per_video:
            break

    # Load video list after potential google_sheet_read
    try:
        videos = json.load(open(video_list_file, encoding='utf-8'))
    except Exception as e:
        logging.error('Failed to load video list file %s: %s', video_list_file, e)
        exit(1)

    # Calculate total number of per-video operations for progress reporting
    per_video_steps_in_run = [s for s in steps if s in allowed_per_video]
    total_ops = len(videos) * len(per_video_steps_in_run)
    current_op = 0

    # Process per-video steps in the specified order
    for v in videos:
        vid = v['v']
        for s in steps:
            if s not in allowed_per_video:
                continue

            # This is a placeholder for the actual step execution logic
            step_executed = False

            if s == 'fetch_video_metadata':
                if not run_step(['python', 'fetch_video_metadata.py',
                                 '--video-id', vid,
                                 '--output-dir', video_metadata_dir]):
                    break
                step_executed = True
            elif s == 'download_audio':
                if not run_step(['python', 'download_audio.py',
                                 '--url', v.get('youtube_url', f'https://www.youtube.com/watch?v={vid}'),
                                 '--video-id', vid,
                                 '--output-dir', audio_dir]):
                    break
                step_executed = True
            elif s == 'isolate_vocals':
                audio_path = os.path.join(audio_dir, f'{vid}.mp3')
                if not run_step(['python', 'isolate_vocals.py',
                                 '--input-file', audio_path,
                                 '--output-dir', vocals_dir]):
                    break
                step_executed = True
            elif s == 'transcribe_audio':
                # Prefer isolated vocals if available; fall back to original audio
                vocals_path = os.path.join(vocals_dir, vid, 'vocals.wav')
                input_audio = vocals_path if os.path.exists(vocals_path) else os.path.join(audio_dir, f'{vid}.mp3')
                kr_srt = os.path.join(subtitles_dir, f'kr_{vid}.srt')

                provider = config.get('transcription_provider', 'local')
                cmd = ['python', 'transcribe_audio.py',
                       '--input-file', input_audio,
                       '--output-file', kr_srt,
                       '--provider', provider]
                if provider == 'openai':
                    api_model = config.get('transcription_api_model', 'whisper-1')
                    cmd.extend(['--api-model', api_model])
                else: # local
                    model_size = config.get('transcription_model_size', 'large')
                    cmd.extend(['--model-size', model_size])

                if not run_step(cmd):
                    break
                step_executed = True
            elif s == 'normalize_srt':
                kr_srt = os.path.join(subtitles_dir, f'kr_{vid}.srt')
                if not run_step(['python', 'normalize_srt.py',
                                 '--input-file', kr_srt,
                                 '--output-file', kr_srt]):
                    break
                step_executed = True
            elif s == 'translate_subtitles':
                kr_srt = os.path.join(subtitles_dir, f'kr_{vid}.srt')
                en_srt = os.path.join(subtitles_dir, f'en_{vid}.srt')
                if not run_step(['python', 'translate_subtitles.py',
                                 '--input-file', kr_srt,
                                 '--output-file', en_srt,
                                 '--slang-file', slang_file,
                                 '--cache-dir', cache_dir]):
                    break
                step_executed = True
            elif s == 'translate_title':
                if not run_step(['python', 'translate_title.py',
                                 '--video-id', vid,
                                 '--metadata-dir', video_metadata_dir,
                                 '--cache-dir', cache_dir]):
                    break
                step_executed = True
            elif s == 'upload_subtitles':
                en_srt = os.path.join(subtitles_dir, f'en_{vid}.srt')
                if not run_step(['python', 'upload_subtitles.py',
                                 '--input-file', en_srt,
                                 '--cache-dir', cache_dir]):
                    break
                step_executed = True
            
            if step_executed:
                current_op += 1
                # Use a print statement that is distinct from logging
                print(f'PROGRESS:{current_op}/{total_ops}')

    # Prepare enriched videos path
    enriched_videos = os.path.join(os.path.dirname(os.path.abspath(video_list_file)), 'videos_enriched.json')

    # Execute remaining global steps in order
    for s in steps:
        if s == 'google_sheet_write':
            if not run_step(['python', 'google_sheet_write.py',
                             '--video-list-file', video_list_file,
                             '--cache-dir', cache_dir,
                             '--spreadsheet', config.get('spreadsheet', ''),
                             '--worksheet', config.get('worksheet', ''),
                             '--column-name', config.get('sheet_column', ''),
                             '--service-account-file', config.get('service_account_file', '')]):
                logging.error('google_sheet_write failed')
        elif s == 'build_videos_json':
            if not run_step(['python', 'build_videos_json.py',
                             '--video-list-file', video_list_file,
                             '--metadata-dir', video_metadata_dir,
                             '--cache-dir', cache_dir,
                             '--output', enriched_videos]):
                logging.error('build_videos_json failed')
        elif s == 'manifest_builder':
            manifest_out = os.path.join(website_dir, 'subtitles.json')
            # Use enriched videos list if it has been built in this run
            videos_input = enriched_videos if 'build_videos_json' in steps else video_list_file
            run_step(['python', 'manifest_builder.py',
                      '--video-list-file', videos_input,
                      '--subtitles-dir', subtitles_dir,
                      '--output-file', manifest_out,
                      '--details-dir', video_metadata_dir])


if __name__ == '__main__':
    main()
