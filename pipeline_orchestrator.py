#!/usr/bin/env python3
import os
import json
import subprocess
import argparse
import logging


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
    metadata_file = config['metadata_file']
    audio_dir = config['audio_dir']
    vocals_dir = config['vocals_dir']
    subtitles_dir = config['subtitles_dir']
    cache_dir = config['cache_dir']
    slang_file = config['slang_file']
    website_dir = config['website_dir']
    skip_steps = set(config.get('skip_steps', []))

    # Load video list
    videos = json.load(open(metadata_file, encoding='utf-8'))

    for v in videos:
        vid = v['v']
        # Download audio
        if 'download_audio' in skip_steps:
            logging.info('Skipping download_audio for %s', vid)
        else:
            if not run_step(['python', 'download_audio.py',
                             '--url', v.get('youtube_url', ''),
                             '--video-id', vid,
                             '--output-dir', audio_dir]):
                continue

        # Vocal isolation
        audio_path = os.path.join(audio_dir, f'{vid}.mp3')
        if 'isolate_vocals' in skip_steps:
            logging.info('Skipping isolate_vocals for %s', vid)
        else:
            if not run_step(['python', 'isolate_vocals.py',
                             '--input-file', audio_path,
                             '--output-dir', vocals_dir]):
                continue

        # Transcription
        kr_srt = os.path.join(subtitles_dir, f'kr_{vid}.srt')
        if 'transcribe_audio' in skip_steps:
            logging.info('Skipping transcribe_audio for %s', vid)
        else:
            if not run_step(['python', 'transcribe_audio.py',
                             '--input-file', audio_path,
                             '--output-file', kr_srt]):
                continue

        # Post-process
        if 'whisper_postprocess' in skip_steps:
            logging.info('Skipping whisper_postprocess for %s', vid)
        else:
            if not run_step(['python', 'whisper_postprocess.py',
                             '--input-file', kr_srt,
                             '--output-file', kr_srt]):
                continue

        # Translation
        en_srt = os.path.join(subtitles_dir, f'en_{vid}.srt')
        if 'translate_subtitles' in skip_steps:
            logging.info('Skipping translate_subtitles for %s', vid)
        else:
            if not run_step(['python', 'translate_subtitles.py',
                             '--input-file', kr_srt,
                             '--output-file', en_srt,
                             '--slang-file', slang_file,
                             '--cache-dir', cache_dir]):
                continue

        # Upload to Pastebin
        if 'upload_subtitles' in skip_steps:
            logging.info('Skipping upload_subtitles for %s', vid)
        else:
            if not run_step(['python', 'upload_subtitles.py',
                             '--input-file', en_srt,
                             '--cache-dir', cache_dir]):
                continue

    # Build manifest after all videos processed
    if 'manifest_builder' in skip_steps:
        logging.info('Skipping manifest_builder')
    else:
        manifest_out = os.path.join(website_dir, 'subtitles.json')
        run_step(['python', 'manifest_builder.py',
                  '--metadata-file', metadata_file,
                  '--subtitles-dir', subtitles_dir,
                  '--output-file', manifest_out])


if __name__ == '__main__':
    main()