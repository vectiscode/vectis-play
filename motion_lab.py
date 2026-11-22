#!/usr/bin/env python3
"""Offline motion-compensated frame interpolation prototype.

Usage: python motion_lab.py INPUT OUTPUT.mp4

Converts a local recording (for example 30 fps) to 60 fps with FFmpeg's
minterpolate filter. Motion vectors come from CPU pixel analysis between
frames (bidirectional motion estimation), not from engine data and not from a
GPU. It runs on files only and is not part of the live Vectis Play session.
"""

import shutil
import subprocess
import sys
from pathlib import Path


def fail(message):
    print("motion_lab.py: " + message, file=sys.stderr)
    return 1


def main(argv):
    if len(argv) != 3:
        return fail("usage: python motion_lab.py INPUT OUTPUT.mp4")
    source = Path(argv[1]).resolve()
    target = Path(argv[2]).resolve()
    if not source.is_file():
        return fail("input is not a file: " + str(source))
    if target.suffix.lower() != ".mp4":
        return fail("output must end in .mp4")
    if target.exists():
        return fail("output already exists: " + str(target))
    if not target.parent.is_dir():
        return fail("output folder does not exist: " + str(target.parent))
    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg is None:
        return fail("ffmpeg was not found on PATH")

    command = [
        ffmpeg, "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
        "-i", str(source),
        "-map", "0:v:0", "-map", "0:a?",
        "-vf", "minterpolate=fps=60:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1",
        "-c:v", "libx264", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-movflags", "+faststart",
        str(target),
    ]
    try:
        result = subprocess.run(command, check=False)
    except OSError as error:
        return fail("could not start ffmpeg: " + str(error))
    if result.returncode != 0:
        return fail("ffmpeg exited with code %d" % result.returncode)
    print("wrote " + str(target))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
