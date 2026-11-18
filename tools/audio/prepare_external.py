#!/usr/bin/env python3
"""Fetches the temporary CC0 music and ambient bed and converts them for the client (OGG Vorbis q5, stereo, 48 kHz).

Needs ffmpeg. Loudness is set by a measured gain (BS.1770 integrated, ffmpeg ebur128): music -23 LUFS, bed -32 LUFS.
Sources and licences are recorded in src/Singularity.Client.Spike/LICENSES.md; the downloads stay in a temp dir, only the OGG is kept.
Usage: python3 tools/audio/prepare_external.py
"""
import os
import re
import subprocess
import tempfile
import urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Singularity.Client.Spike", "assets", "audio")
BASE = "https://opengameart.org/sites/default/files/"
# (url file, output path, target LUFS, trim seconds or 0)
ITEMS = [
    ("irradiated_skies.ogg", "music/irradiated_skies.ogg", -23.0, 0),
    ("led_twlight.ogg", "music/led_twilight.ogg", -23.0, 0),
    ("light_through_a_pale_window.mp3", "ambient/pale_window.ogg", -32.0, 120),
]


def measure(path):
    out = subprocess.run(["ffmpeg", "-nostats", "-i", path, "-af", "ebur128", "-f", "null", "-"], capture_output=True, text=True).stderr
    return float(re.findall(r"I:\s+(-?\d+\.\d+) LUFS", out)[-1])


def main():
    with tempfile.TemporaryDirectory() as tmp:
        for src, dst, target, trim in ITEMS:
            raw = os.path.join(tmp, os.path.basename(src))
            req = urllib.request.Request(BASE + src, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=120) as r, open(raw, "wb") as f:
                f.write(r.read())
            cut = ["-t", str(trim)] if trim else []
            wav = os.path.join(tmp, "x.wav")
            subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", raw, *cut, "-ar", "48000", "-ac", "2", wav], check=True)
            gain = target - measure(wav)
            out = os.path.join(ROOT, dst)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", wav, "-af", f"volume={gain:.2f}dB,alimiter=limit=0.89", "-c:a", "libvorbis", "-q:a", "5", out], check=True)
            print(f"{dst}: gain {gain:+.1f} dB -> {measure(out):.1f} LUFS")


if __name__ == "__main__":
    main()
