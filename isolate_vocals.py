import logging
import os
import subprocess


def run_isolate_vocals(
    input_file: str,
    output_dir: str = "vocals",
    model: str = "htdemucs",
    two_stems: bool = False,
) -> bool:
    """Isolate vocals from an audio file using the Demucs CLI.

    Creates ``<output_dir>/<video_id>/vocals.wav`` and skips if it already exists.
    """
    try:
        if not os.path.exists(input_file):
            logging.error(f"Input audio not found for vocal isolation: {input_file}")
            return False

        video_id = os.path.splitext(os.path.basename(input_file))[0]
        dest_dir = os.path.join(output_dir, video_id)
        os.makedirs(dest_dir, exist_ok=True)
        output_path = os.path.join(dest_dir, "vocals.wav")

        if os.path.exists(output_path):
            logging.info(f"{output_path} already exists, skipping isolation")
            return True

        cmd = ["demucs"]
        if model:
            cmd += ["-n", model]
        if two_stems:
            cmd += ["--two-stems", "vocals"]
        cmd += ["--out", output_dir, input_file]

        subprocess.run(cmd, check=True)
        logging.info(f"Vocal isolation complete: {output_path}")
        return True
    except Exception as e:
        logging.error(f"Vocal isolation failed for {input_file}: {e}")
        return False
