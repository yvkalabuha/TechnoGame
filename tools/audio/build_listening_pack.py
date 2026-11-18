#!/usr/bin/env python3
"""Builds the audio listening pack (TASK-0073) from the candidates in docs/audio/AUDIO_DIRECTION_r2.md.

Steps: fetch (verify the licence on the page, pin sha256 in tools/audio/listening_pack_manifest.json) ->
cut (onset detection) -> common EQ -> loudness by priority -> OGG -> bed mix -> music previews -> index.html + CREDITS.yml.
Needs ffmpeg and Python 3 (stdlib only). Originals stay in the work dir and are not committed.
Freesound serves originals only after login, so the public HQ preview (mp3) from the page is used: preview grade, good for listening only.
A source that refuses a download is recorded as not obtained; nothing is worked around.
Usage: python3 tools/audio/build_listening_pack.py [--work DIR]
"""
import argparse
import array
import hashlib
import html
import json
import math
import os
import random
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "docs", "audio", "listening-pack")
MANIFEST = os.path.join(HERE, "listening_pack_manifest.json")
ACCESS_DATE = "2026-11-18"

FS = "https://freesound.org/people/"
CM = "https://commons.wikimedia.org/wiki/File:"
# key -> page URL. Licence is read from the page and must be CC0 / CC BY 3.0 / CC BY 4.0.
SOURCES = {
    "stamp_fekry": FS + "I.fekry/sounds/470710/",
    "stamp_frigola": FS + "eduardfrigola/sounds/233240/",
    "relay_bass": FS + "bassmosphere/sounds/384698/",
    "breaker_echo": FS + "EchoCinematics/sounds/131599/",
    "switch_joe": FS + "joedeshon/sounds/139062/",
    "type_triumph": FS + "YleArkisto/sounds/271525/",
    "teletype": FS + "stratcat322/sounds/169259/",
    "morse": FS + "ERH/sounds/34418/",
    "bell_jake": FS + "jakebagger/sounds/499787/",
    "bell_dbache": FS + "dbache/sounds/696039/",
    "phone_hock": FS + "Hockinfinger/sounds/274436/",
    "whistle_kent": FS + "kentspublicdomain/sounds/477827/",
    "paper_moose": FS + "moose13088/sounds/182889/",
    "paper_bevibel": FS + "bevibeldesign/sounds/635156/",
    "floppy": FS + "MrAuralization/sounds/259292/",
    "hum_kyles": FS + "kyles/sounds/453444/",
    "static_qr": FS + "quantumriver/sounds/552160/",
    "clock_yuval": FS + "Yuval/sounds/211192/",
    "typing_pool": FS + "Joseph999/sounds/376934/",
    "bsb_bell": "https://bigsoundbank.com/typewriter-bell-s2845.html",
    "music_extrapolation": "https://www.scottbuckley.com.au/library/extrapolation/",
    "music_cobalt": "https://scottbuckley.com.au/library/cobalt",
    "music_starfire": "https://www.scottbuckley.com.au/library/starfire/",
    "music_aphelion": "https://www.scottbuckley.com.au/library/aphelion/",
    "music_hyperion": "https://www.scottbuckley.com.au/library/hyperion/",
    "music_vanguard": "https://www.scottbuckley.com.au/library/vanguard/",
    "music_mars": CM + "Holst-_mars.ogg",
    "music_1812": CM + "1812_Overture.ogg",
    "music_souvenir": CM + "Tchaikovsky_-_Souvenir_de_Florence,_mvt-3_-_US_Army_Band.oga",
    "music_humoresque": CM + "Humoresque_1955_performance.ogg",
    "music_dvorak9": CM + "Antonin_Dvorak_-_symphony_no._9_in_e_minor_'from_the_new_world',_op._95_-_i._adagio_-_allegro_molto.ogg",
    "music_beethoven5": CM + "Ludwig_van_Beethoven_-_symphony_no._5_in_c_minor,_op._67_-_i._allegro_con_brio.ogg",
    "music_grieg": CM + "Musopen_-_In_the_Hall_Of_The_Mountain_King.ogg",
}

LICENCES = {  # licence URL fragment -> (short name, needs attribution)
    "publicdomain/zero/1.0": ("CC0 1.0", False),
    "licenses/by/4.0": ("CC BY 4.0", True),
    "licenses/by/3.0": ("CC BY 3.0", True),
}
TARGET_LUFS = {"P3": -30.0, "P2": -24.0, "P1": -20.0, "P0": -18.0}  # mid of the r2 §4.2 ranges
MAXLEN = {"P3": 0.08, "P2": 0.25, "P1": 1.2, "P0": 1.5}


def p(src, mode="first", n=0.25, **kw):
    """One part of a candidate. mode: first | hit (n = rank by strength, len in 'dur') | at (start, dur) | full."""
    d = {"src": src, "mode": mode, "dur": n, "off": 0.0, "g": 0.0, "pitch": 0.0, "lp": None, "fadein": 0.0, "rank": 0, "start": 0.0}
    d.update(kw)
    return d


def first(src, dur, **kw):
    return p(src, "first", dur, **kw)


def hit(src, rank, dur, **kw):
    return p(src, "hit", dur, rank=rank, **kw)


def at(src, start, dur, **kw):
    return p(src, "at", dur, start=start, **kw)


FAR = {"lp": 2500, "g": 0.0}
# (id, title, pri, [(cand id, label, era, [parts], opts)]) opts: lp (final low-pass), rel (dB vs target)
EVENTS = [
    ("E01", "Приказ принят", "P2", [
        ("a", "резиновый штамп (I.fekry)", "1945", [first("stamp_fekry", 0.25)], {}),
    ]),
    ("E02", "Приказ отклонён", "P1", [
        ("a", "реле пинбола", "1945", [first("relay_bass", 0.19)], {}),
    ]),
    ("E03", "В очереди", "P3", [
        ("a", "шорох листа (moose13088)", "1945", [hit("paper_moose", 2, 0.08)], {}),
    ]),
    ("E04", "Команда исполнена", "P2", [
        ("a", "каретка Triumph (автонарезка)", "1945", [hit("type_triumph", 1, 0.25)], {}),
    ]),
    ("E05", "Сигнал: внимание", "P1", [
        ("a", "настольный звонок (jakebagger)", "1945", [first("bell_jake", 1.2)], {}),
    ]),
    ("E06", "Сигнал: тревога", "P0", [
        ("a", "телефон Stromberg Carlson, 1 звонок", "1960", [first("phone_hock", 1.5)], {}),
    ]),
    ("E07", "Сигнал: критично", "P0", [
        ("a", "звонок + щелчок реле", "1945", [first("bell_jake", 1.5), first("relay_bass", 0.19, g=-2)], {}),
    ]),
    ("E08", "Сигнал снят", "P1", [
        ("a", "звонок тише, питч -2 полутона (jakebagger)", "1945", [first("bell_jake", 1.2, pitch=-2)], {"rel": -3}),
    ]),
    ("E09", "Узел открыт", "P1", [
        ("c", "звонок пишущей машинки (BigSoundBank)", "1945", [first("bsb_bell", 1.2)], {}),
    ]),
    ("E10", "Ключевой прорыв", "P1", [
        ("a", "двойной звонок (BigSoundBank)", "1945", [first("bsb_bell", 0.7), first("bsb_bell", 0.7, off=0.45)], {}),
    ]),
    ("E11", "Линия введена", "P1", [
        ("a", "далёкий заводской свисток (LP 2.5 кГц, -8 дБ)", "1945", [first("whistle_kent", 2.0, lp=2500)], {"rel": -8}),
    ]),
    ("E12", "Идёт стройка", "P3", [
        ("a", "шорох бумаги/пера", "1945", [hit("paper_moose", 5, 0.08)], {}),
        ("b", "шаговый двигатель дисковода", "1980", [hit("floppy", 6, 0.08)], {"lp": 12000}),
    ]),
    ("E13", "Война объявлена", "P0", [
        ("a", "рубильник + серия морзе", "1945", [first("breaker_echo", 1.5), hit("morse", 0, 0.8, off=0.5)], {}),
    ]),
    ("E14", "Потеря территории", "P0", [
        ("a", "серия морзе + звонок", "1945", [at("morse", 0.0, 1.0), first("bell_jake", 0.6, off=0.9)], {}),
        ("b", "телетайп + звонок", "1945", [hit("teletype", 0, 1.0), first("bell_dbache", 0.6, off=0.9)], {}),
    ]),
    ("E15", "Мир / перемирие", "P1", [
        ("a", "два штампа, спокойно (I.fekry)", "1945", [first("stamp_fekry", 0.25), first("stamp_fekry", 0.25, off=0.6)], {}),
        ("b", "два штампа (eduardfrigola)", "1945", [hit("stamp_frigola", 0, 0.25), hit("stamp_frigola", 0, 0.25, off=0.6)], {}),
    ]),
    ("E16", "Запуск космический", "P1", [
        ("b", "свисток + реле", "1960", [first("whistle_kent", 0.9), first("relay_bass", 0.19, off=0.9)], {}),
    ]),
    ("E17", "Запуск ядерный", "P0", [
        ("a", "рубильник + нарастающий гул трансформатора", "1945", [first("breaker_echo", 0.5), at("hum_kyles", 5.0, 1.8, off=0.0, fadein=1.5, g=-4)], {}),
    ]),
    ("E18", "Смена темпа", "P2", [
        ("a", "тумблер (joedeshon)", "1945", [first("switch_joe", 0.25)], {}),
    ]),
    ("E19", "Пауза / снятие", "P2", [
        ("a", "вниз: тумблер, питч -2", "1945", [first("switch_joe", 0.25, pitch=-2)], {}),
        ("b", "вверх: тумблер, питч +2", "1945", [first("switch_joe", 0.25, pitch=2)], {}),
    ]),
    ("E20", "Наведение / выбор", "P3", [
        ("a", "сухой тик бумаги", "1945", [hit("paper_bevibel", 1, 0.08)], {"rel": -2}),
    ]),
]
NOT_FOUND = [
    ("E02/E10 1980", "зуммер / «terminal beep» для 1980+ (сигналы, снятие): в r2 не найден"),
    ("E15", "«опустить трубку»: в r2 не назван источник"),
]
# (key, title, composer, preview start s, composition licence). Classical entries: recording licence is read from the Commons file page.
SB = "Scott Buckley"
MUSIC = [
    ("music_extrapolation", "Extrapolation", SB, 0, "оригинал, CC BY 4.0"),
    ("music_cobalt", "Cobalt", SB, 0, "оригинал, CC BY 4.0"),
    ("music_starfire", "Starfire", SB, 30, "оригинал, CC BY 4.0"),
    ("music_aphelion", "Aphelion", SB, 30, "оригинал, CC BY 4.0"),
    ("music_hyperion", "Hyperion", SB, 30, "оригинал, CC BY 4.0"),
    ("music_vanguard", "Vanguard", SB, 30, "оригинал, CC BY 4.0"),
    ("music_mars", "Mars (The Planets), духовой оркестр", "Gustav Holst", 20, "композиция PD (Holst, ум. 1934)"),
    ("music_1812", "Увертюра «1812» (финал)", "P. I. Tchaikovsky", 840, "композиция PD (ум. 1893)"),
    ("music_souvenir", "Souvenir de Florence, ч. 3", "P. I. Tchaikovsky", 0, "композиция PD (ум. 1893)"),
    ("music_humoresque", "Humoresque op.10 №2", "P. I. Tchaikovsky", 0, "композиция PD (ум. 1893)"),
    ("music_dvorak9", "Симфония №9, ч. 1", "Antonin Dvorak", 0, "композиция PD (ум. 1904)"),
    ("music_beethoven5", "Симфония №5, ч. 1", "L. van Beethoven", 0, "композиция PD (ум. 1827)"),
    ("music_grieg", "In the Hall of the Mountain King", "Edvard Grieg", 60, "композиция PD (ум. 1907)"),
]
PREVIEW_SECONDS = 75
BED_SECONDS = 60


def run(cmd, **kw):
    return subprocess.run(cmd, check=True, capture_output=True, text=True, **kw)


def ff(*args):
    run(["ffmpeg", "-y", "-v", "error", *args])


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def http(url, binary=False):
    ua = "SingularityListeningPack/1.0 (personal game project; ffmpeg pack builder)" if "wikimedia.org" in url else "curl/8.0"
    req = urllib.request.Request(url, headers={"User-Agent": ua})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                data = r.read()
            break
        except urllib.error.HTTPError as e:  # 429 is a rate limit, not a refusal: wait and retry politely
            if e.code != 429 or attempt == 2:
                raise
            time.sleep(40 * (attempt + 1))
    if "wikimedia.org" in url:
        time.sleep(5)
    return data if binary else data.decode("utf-8", "replace")


def parse_commons(url):
    title = urllib.parse.unquote(url.split("/wiki/", 1)[1])
    api = "https://commons.wikimedia.org/w/api.php?action=query&prop=imageinfo&iiprop=url%7Cextmetadata&format=json&titles=" + urllib.parse.quote(title)
    ii = list(json.loads(http(api))["query"]["pages"].values())[0]["imageinfo"][0]
    meta = ii["extmetadata"]
    lic = meta.get("LicenseShortName", {}).get("value", "")
    if not re.search(r"public domain|cc0|^pd", lic, re.I):
        raise ValueError("Commons licence is not PD/CC0: %r" % lic)
    artist = re.sub(r"<[^>]+>", " ", meta.get("Artist", {}).get("value", ""))
    artist = html.unescape(re.sub(r"\s+", " ", artist)).strip()
    return {"title": title.rsplit(".", 1)[0], "author": artist[:160], "licence": lic, "attribution": False,
            "licence_url": url, "audio_url": ii["url"].split("?")[0], "grade": "Commons original"}


def parse_page(key, url, page):
    """Returns dict(title, author, licence, attribution, audio_url) or raises ValueError (refused licence / no audio)."""
    lic_urls = re.findall(r'href="(https?://creativecommons\.org/[^"]+)"', page)
    lic = None
    for u in lic_urls:
        for frag, val in LICENCES.items():
            if frag in u:
                lic = (val, u)
    if "bigsoundbank.com" in url:
        if "CC0" not in page:
            raise ValueError("BigSoundBank page does not state CC0")
        m = re.search(r'"contentUrl":"(https://bigsoundbank\.com/UPLOAD/bwf-en/[^"]+\.wav)"', page)
        title = re.search(r"<title>([^<—|]+)", page).group(1).strip()
        return {"title": title, "author": "Joseph Sardin", "licence": "CC0 1.0", "attribution": False,
                "licence_url": "https://bigsoundbank.com/licenses.html", "audio_url": m.group(1), "grade": "original WAV"}
    if lic is None:
        raise ValueError("licence is not CC0 / CC BY 3.0 / CC BY 4.0 (found: %s)" % lic_urls)
    if "scottbuckley" in url:
        m = re.search(r'href="(https?://[^"]+\.mp3)"', page)
        t = re.search(r"<title>([^<|–-]+)", page).group(1).strip()
        return {"title": t, "author": "Scott Buckley", "licence": lic[0][0], "attribution": lic[0][1],
                "licence_url": lic[1], "audio_url": m.group(1), "grade": "original MP3"}
    title = html.unescape(re.search(r'og:audio:title" content="([^"]+)"', page).group(1))
    author = html.unescape(re.search(r'og:audio:artist" content="([^"]+)"', page).group(1))
    m = re.search(r'https://cdn\.freesound\.org/previews/[^"\']+-hq\.mp3', page)
    if not m:
        raise ValueError("no public HQ preview on the page")
    return {"title": title, "author": author, "licence": lic[0][0], "attribution": lic[0][1],
            "licence_url": lic[1], "audio_url": m.group(0), "grade": "Freesound HQ preview (mp3)"}


def fetch_all(work):
    """Fetches every source; returns (sources dict key -> info with local path, not_obtained list)."""
    pinned = {}
    if os.path.exists(MANIFEST):
        with open(MANIFEST, encoding="utf-8") as f:
            pinned = json.load(f).get("sources", {})
    got, missing, wiki_limited = {}, [], False
    for key, url in SOURCES.items():
        try:
            if wiki_limited and "wikimedia.org" in url:
                raise ValueError("Wikimedia rate limit (HTTP 429) persisted; not retried")
            info = parse_commons(url) if "commons.wikimedia.org" in url else parse_page(key, url, http(url))
            ext = os.path.splitext(info["audio_url"])[1]
            raw = os.path.join(work, "raw", key + ext)
            os.makedirs(os.path.dirname(raw), exist_ok=True)
            if not os.path.exists(raw):  # the work dir doubles as a download cache
                with open(raw + ".part", "wb") as f:
                    f.write(http(info["audio_url"], binary=True))
                os.replace(raw + ".part", raw)
            info.update(page=url, sha256=sha256(raw), path=raw)
            old = pinned.get(key)
            if old and old.get("sha256") != info["sha256"]:
                print("WARNING: %s sha256 differs from the pinned manifest" % key, file=sys.stderr)
            got[key] = info
        except (urllib.error.URLError, ValueError, AttributeError) as e:
            wiki_limited = wiki_limited or ("wikimedia.org" in url and "429" in str(e))
            missing.append({"key": key, "page": url, "reason": str(e)})
            print("not obtained: %s (%s)" % (key, e), file=sys.stderr)
    return got, missing


def decode(src, work):
    """48 kHz mono float WAV of a source plus onset list."""
    wav = os.path.join(work, "dec", src["key"] + ".wav")
    os.makedirs(os.path.dirname(wav), exist_ok=True)
    ff("-i", src["path"], "-ac", "1", "-ar", "48000", "-c:a", "pcm_f32le", wav)
    src["wav"] = wav
    src["dur"] = float(run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", wav]).stdout)
    low = wav + ".16k.wav"
    ff("-i", wav, "-ar", "16000", "-c:a", "pcm_s16le", low)
    with wave.open(low) as w:
        a = array.array("h")
        a.frombytes(w.readframes(w.getnframes()))
    os.remove(low)
    frame = 80  # 5 ms
    env = [math.sqrt(sum(x * x for x in a[i:i + frame]) / frame) for i in range(0, len(a) - frame, frame)]
    if not env:
        src["onsets"] = []
        return
    floor = sorted(env)[len(env) // 5] + 1.0
    thr = max(floor * 4, max(env) * 0.08)
    ons, last = [], -100
    for i, e in enumerate(env):
        prev = env[i - 1] if i else 0.0
        if e >= thr and prev < thr and i - last >= 24:  # 120 ms refractory
            j = i
            while j > 0 and i - j < 6 and env[j - 1] > floor * 1.5:
                j -= 1
            ons.append((max(0.0, j * 0.005 - 0.002), max(env[i:i + 12])))
            last = i
    src["onsets"] = ons


def render_part(part, src, out):
    mode = part["mode"]
    if mode == "at":
        start = part["start"]
    elif mode == "first":
        start = src["onsets"][0][0] if src["onsets"] else 0.0  # no clear onset: the sound starts at 0
    else:
        ranked = sorted(src["onsets"], key=lambda o: -o[1])
        if not ranked:
            raise ValueError("no onsets in " + src["key"])
        start = ranked[min(part["rank"], len(ranked) - 1)][0]
    dur = part["dur"]
    chain = ["atrim=start=%.4f:duration=%.4f" % (start, dur), "asetpts=PTS-STARTPTS"]
    if part["pitch"]:
        chain += ["asetrate=%d" % round(48000 * 2 ** (part["pitch"] / 12)), "aresample=48000"]
    if part["lp"]:
        chain.append("lowpass=f=%d" % part["lp"])
    if part["fadein"]:
        chain.append("afade=t=in:d=%.3f" % part["fadein"])
    ff("-i", src["wav"], "-af", ",".join(chain), "-c:a", "pcm_f32le", out)


def lufs_tp(path, pad=2.0):
    af = ("apad=whole_dur=%.2f," % pad if pad > 0 else "") + "ebur128=peak=true"
    err = run(["ffmpeg", "-nostats", "-i", path, "-af", af, "-f", "null", "-"]).stderr
    tail = err[err.rfind("Summary:"):]
    i = float(re.search(r"I:\s+(-?\d+\.\d+) LUFS", tail).group(1))
    tp = re.search(r"Peak:\s+(-?\d+\.\d+) dBFS", tail)
    return i, float(tp.group(1)) if tp else None


def render_event_clip(pri, parts, opts, srcs, work, out_ogg, tag):
    tmp = os.path.join(work, "tmp")
    os.makedirs(tmp, exist_ok=True)
    wavs, total = [], 0.0
    for k, part in enumerate(parts):
        w = os.path.join(tmp, "%s_%d.wav" % (tag, k))
        render_part(part, srcs[part["src"]], w)
        wavs.append(w)
        total = max(total, part["off"] + part["dur"])
    ins, filt, labels = [], [], []
    for k, (part, w) in enumerate(zip(parts, wavs)):
        ins += ["-i", w]
        filt.append("[%d:a]adelay=%d:all=1,volume=%.2fdB,apad=whole_dur=%.3f[p%d]" % (k, round(part["off"] * 1000), part["g"], total, k))
        labels.append("[p%d]" % k)
    n = len(parts)
    mix = "".join(labels) + ("amix=inputs=%d:duration=longest,volume=%d," % (n, n) if n > 1 else "") + "atrim=0:%.3f" % total
    lp = opts.get("lp", 9000 if opts.get("era", "1945") in ("1945", "1960") else 12000)
    eq = "highpass=f=120,equalizer=f=3000:t=h:width=2000:g=-3,lowpass=f=%d,afade=t=out:st=%.3f:d=0.02" % (lp, max(0.0, total - 0.02))
    pre = os.path.join(tmp, tag + "_pre.wav")
    ff(*ins, "-filter_complex", ";".join(filt) + ";" + mix + "," + eq + "[o]", "-map", "[o]", "-c:a", "pcm_f32le", pre)
    target = TARGET_LUFS[pri] + opts.get("rel", 0.0)
    gain = target - lufs_tp(pre)[0]
    ff("-i", pre, "-af", "volume=%.2fdB,alimiter=limit=0.58:attack=1:release=20:level=0" % gain,
       "-ar", "48000", "-ac", "1", "-c:a", "libvorbis", "-q:a", "5", out_ogg)
    return lufs_tp(out_ogg), total


def loop_to(path, seconds, work, tag, xfade=1.5):
    """Loops a WAV with crossfades to exactly `seconds`."""
    dur = float(run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path]).stdout)
    out = os.path.join(work, "tmp", tag + "_loop.wav")
    if dur >= seconds:
        ff("-i", path, "-t", str(seconds), "-c:a", "pcm_f32le", out)
        return out
    n = int(math.ceil((seconds - dur) / (dur - xfade))) + 1
    ins = []
    for _ in range(n):
        ins += ["-i", path]
    chain, last = [], "[0:a]"
    for k in range(1, n):
        chain.append("%s[%d:a]acrossfade=d=%.2f[x%d]" % (last, k, xfade, k))
        last = "[x%d]" % k
    ff(*ins, "-filter_complex", ";".join(chain), "-map", last, "-t", str(seconds), "-c:a", "pcm_f32le", out)
    return out


def level(path_in, target, work, tag, filt, pad=0.0):
    out = os.path.join(work, "tmp", tag + "_lvl.wav")
    pre = os.path.join(work, "tmp", tag + "_f.wav")
    ff("-i", path_in, "-af", filt, "-ac", "1", "-c:a", "pcm_f32le", pre)
    gain = target - lufs_tp(pre, pad)[0]
    ff("-i", pre, "-af", "volume=%.2fdB" % gain, "-c:a", "pcm_f32le", out)
    return out


def build_bed(srcs, work, out_dir):
    """Five-layer design-bureau bed, 60 s, levels per r2 §2. Returns list of output file infos."""
    T = BED_SECONDS
    rng = random.Random(1910)
    tmp = os.path.join(work, "tmp")
    layers = []
    a = loop_to(srcs["hum_kyles"]["wav"], T, work, "bedA")
    layers.append(level(a, -42, work, "bedA", "highpass=f=40,lowpass=f=2000"))
    b = loop_to(srcs["static_qr"]["wav"], T, work, "bedB")
    drift = "volume='pow(10,3*sin(2*PI*t/40)/20)':eval=frame"
    layers.append(level(b, -46, work, "bedB", "highpass=f=300,lowpass=f=4000," + drift))
    c = loop_to(srcs["clock_yuval"]["wav"], T, work, "bedC")
    layers.append(level(c, -50, work, "bedC", "highpass=f=500"))
    dsrc = os.path.join(tmp, "bedD_src.wav")
    ff("-i", srcs["typing_pool"]["wav"], "-ss", "20", "-t", str(T), "-c:a", "pcm_f32le", dsrc)
    layers.append(level(dsrc, -44, work, "bedD", "lowpass=f=3000"))
    # E: a phone ring every 60-120 s would give <1 event in a 60 s preview, so the preview uses two phone rings and three paper sounds
    events = []
    for k, (key, dur) in enumerate([("phone_hock", 2.0), ("paper_moose", 0.7), ("paper_moose", 0.6), ("phone_hock", 2.0), ("paper_moose", 0.5)]):
        w = os.path.join(tmp, "bedE_%d.wav" % k)
        s = srcs[key]
        ons = sorted(s["onsets"], key=lambda o: -o[1])
        st = s["onsets"][0][0] if key == "phone_hock" else ons[min(k, len(ons) - 1)][0]
        ff("-i", s["wav"], "-af", "atrim=start=%.3f:duration=%.2f,asetpts=PTS-STARTPTS,lowpass=f=2500,afade=t=out:st=%.2f:d=0.05" % (st, dur, dur - 0.05), "-c:a", "pcm_f32le", w)
        events.append((w, dur))
    times = [9 + rng.random() * 3, 21 + rng.random() * 3, 33 + rng.random() * 3, 44 + rng.random() * 3, 53 + rng.random() * 3]
    ins, filt, lab = [], [], []
    for k, ((w, dur), t) in enumerate(zip(events, times)):
        ins += ["-i", w]
        filt.append("[%d:a]adelay=%d:all=1,apad=whole_dur=%d[e%d]" % (k, round(t * 1000), T, k))
        lab.append("[e%d]" % k)
    etrack = os.path.join(tmp, "bedE_track.wav")
    ff(*ins, "-filter_complex", ";".join(filt) + ";" + "".join(lab) + "amix=inputs=5:duration=longest,volume=5,atrim=0:%d[o]" % T, "-map", "[o]", "-c:a", "pcm_f32le", etrack)
    layers.append(level(etrack, -40, work, "bedE", "highpass=f=200,lowpass=f=4000"))
    ins = []
    for l in layers:
        ins += ["-i", l]
    mixf = "".join("[%d:a]" % k for k in range(5)) + "amix=inputs=5:duration=first,volume=5,atrim=0:%d,afade=t=in:d=2,afade=t=out:st=%d:d=3,aformat=channel_layouts=stereo[o]" % (T, T - 3)
    mixwav = os.path.join(tmp, "bed_mix.wav")
    ff(*ins, "-filter_complex", mixf, "-map", "[o]", "-c:a", "pcm_f32le", mixwav)
    res = []
    for name, extra, label in [("bed_kb_spec.ogg", "", "по уровням r2 §2 (около -38 LUFS: очень тихо)"),
                               ("bed_kb_audible.ogg", "loudnorm=I=-26:TP=-3:LRA=7,", "тот же микс, подтянут до -26 LUFS для прослушивания")]:
        out = os.path.join(out_dir, name)
        ff("-i", mixwav, "-af", extra + "aformat=sample_rates=48000", "-ar", "48000", "-c:a", "libvorbis", "-q:a", "5", out)
        i, tp = lufs_tp(out, 0)
        res.append({"file": "bed/" + name, "label": label, "lufs": i, "tp": tp})
    return res


def build_music(srcs, out_dir, work):
    res = []
    for key, title, author, start, comp in MUSIC:
        if key not in srcs:
            continue
        pre = os.path.join(work, "tmp", key + "_pv.wav")
        ff("-ss", str(start), "-i", srcs[key]["path"], "-t", str(PREVIEW_SECONDS), "-ar", "48000", "-ac", "2",
           "-af", "afade=t=in:d=0.5,afade=t=out:st=%d:d=3" % (PREVIEW_SECONDS - 3), "-c:a", "pcm_f32le", pre)
        gain = -23.0 - lufs_tp(pre, 0)[0]
        out = os.path.join(out_dir, key.replace("music_", "") + "_pv.ogg")
        ff("-i", pre, "-af", "volume=%.2fdB,alimiter=limit=0.66:level=0" % gain, "-c:a", "libvorbis", "-q:a", "4", out)
        i, tp = lufs_tp(out, 0)
        res.append({"key": key, "file": "music/" + os.path.basename(out), "title": title, "author": author, "comp": comp, "start": start, "lufs": i, "tp": tp})
    return res


def write_index(events_out, bed, music, srcs, missing):
    def src_of(key):
        s = srcs[key]
        return '%s - %s (<a href="%s">%s</a>, %s)' % (html.escape(s["title"]), html.escape(s["author"]), s["page"], html.escape(s["licence"]), html.escape(s["grade"]))

    def audio(f):
        return '<audio controls preload="none" src="%s"></audio>' % f

    rows = []
    for ev in events_out:
        rows.append('<h3>%s %s <small>(%s)</small></h3>' % (ev["id"], html.escape(ev["title"]), ev["pri"]))
        if not ev["cands"]:
            rows.append("<p><em>not found / not obtained</em></p>")
        for c in ev["cands"]:
            used = "<br>".join(src_of(k) for k in c["srcs"])
            rows.append('<div class="c">%s <b>%s</b> <span class="era">эра %s</span> <small>%.1f LUFS, TP %s dBFS</small><br>%s<br><small>%s</small></div>' % (
                audio(c["file"]), html.escape(c["label"]), c["era"], c["lufs"], c["tp"], "", used))
    bed_html = "".join('<div class="c">%s <b>%s</b> <small>%.1f LUFS</small></div>' % (audio(b["file"]), html.escape(b["label"]), b["lufs"]) for b in bed)
    bed_src = "<small>Слои: " + "; ".join("%s: %s" % (l, src_of(k)) for l, k in [("A", "hum_kyles"), ("B", "static_qr"), ("C", "clock_yuval"), ("D", "typing_pool"), ("E", "phone_hock")]) + "; бумага: " + src_of("paper_moose") + "</small>"
    mus_html = "".join('<div class="c">%s <b>%s</b> - %s <small>%.1f LUFS</small><br><small>композиция: %s; фрагмент с %d с<br>запись: %s</small></div>' % (audio(m["file"]), html.escape(m["title"]), html.escape(m["author"]), m["lufs"], html.escape(m["comp"]), m["start"], src_of(m["key"])) for m in music)
    nf = "".join("<li>%s: %s</li>" % (html.escape(a), html.escape(b)) for a, b in NOT_FOUND)
    mi = "".join("<li>%s: %s</li>" % (m["key"], html.escape(m["reason"])) for m in missing) or "<li>нет</li>"
    page = """<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><title>PROJECT SINGULARITY: listening pack (r2)</title>
<style>body{font-family:sans-serif;max-width:60em;margin:1em auto;padding:0 1em}.c{margin:.4em 0;padding:.4em;border-left:3px solid #999}.era{color:#555}small{color:#444}audio{vertical-align:middle;height:32px}</style></head><body>
<h1>Listening pack: AUDIO_DIRECTION r2</h1>
<p>Кандидаты на прослушивание. В клиент ничего не входит до решения владельца (#309). Все звуки обработаны единым EQ (HP 120 Гц, -3 дБ на 3 кГц, LP 9/12 кГц) и нормированы по приоритету; нарезка автоматическая по началу удара и на слух не проверялась. Freesound-источники - публичные HQ-превью (оригиналы требуют логина).</p>
<h2>События (карта r2 §1)</h2>
%s
<h2>Не найдено / замечания</h2><ul>%s</ul>
<h2>Постель «КБ / штаб», 60 с</h2>%s<br>%s
<h2>Музыка: классика (PD) и Scott Buckley (CC BY 4.0), превью 75 с, -23 LUFS</h2>%s
<h2>Не получено</h2><ul>%s</ul>
<p><small>Атрибуции: CREDITS.yml. Воспроизводимость: tools/audio/build_listening_pack.py, manifest tools/audio/listening_pack_manifest.json.</small></p>
</body></html>
""" % ("\n".join(rows), nf, bed_html, bed_src, mus_html, mi)
    with open(os.path.join(OUT, "index.html"), "w", encoding="utf-8") as f:
        f.write(page)


def write_credits(srcs, used):
    lines = ["# Draft attribution entries for the listening pack (assets/audio/CREDITS.yml format, #288). Nothing here is in the client yet.\n"]
    for key in sorted(used):
        s = srcs[key]
        lines.append("- name: %s\n  author: %s\n  url: %s\n  licence: %s\n  licence_url: %s\n  accessed: %s\n  sha256_source: %s\n  source_grade: %s\n  modifications: %s\n  attribution_required: %s\n" % (
            json.dumps(s["title"], ensure_ascii=False), json.dumps(s["author"], ensure_ascii=False), s["page"], s["licence"], s["licence_url"], ACCESS_DATE,
            s["sha256"], json.dumps(s["grade"]), "trimmed, filtered, loudness-normalised", "true" if s["attribution"] else "false"))
    with open(os.path.join(OUT, "CREDITS.yml"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--work", default=os.path.join(tempfile.gettempdir(), "singularity_listening_pack"))
    args = ap.parse_args()
    work = args.work
    os.makedirs(os.path.join(work, "tmp"), exist_ok=True)
    got, missing = fetch_all(work)
    for key, info in got.items():
        info["key"] = key
        if not key.startswith("music_"):  # music is only cut by time, no onset analysis
            decode(info, work)
    for d in ("events", "bed", "music"):
        os.makedirs(os.path.join(OUT, d), exist_ok=True)

    events_out, used, outputs = [], set(), []
    for eid, title, pri, cands in EVENTS:
        ev = {"id": eid, "title": title, "pri": pri, "cands": []}
        for cid, label, era, parts, opts in cands:
            need = {x["src"] for x in parts}
            if not need <= set(got):
                print("skip %s%s: source not obtained" % (eid, cid), file=sys.stderr)
                continue
            if any(x["mode"] == "hit" and not got[x["src"]]["onsets"] for x in parts):
                print("skip %s%s: no onset found" % (eid, cid), file=sys.stderr)
                continue
            rel = "events/%s_%s.ogg" % (eid, cid)
            o = dict(opts, era=era)
            (i, tp), total = render_event_clip(pri, parts, o, got, work, os.path.join(OUT, rel), eid + cid)
            used |= need
            ev["cands"].append({"file": rel, "label": label, "era": era, "lufs": i, "tp": tp, "srcs": sorted(need)})
            outputs.append({"file": rel, "lufs": round(i, 1), "tp": tp, "len_s": round(total, 3)})
        events_out.append(ev)

    bed, music = [], []
    if all(k in got for k in ("hum_kyles", "static_qr", "clock_yuval", "typing_pool", "phone_hock", "paper_moose")):
        bed = build_bed(got, work, os.path.join(OUT, "bed"))
        used |= {"hum_kyles", "static_qr", "clock_yuval", "typing_pool", "phone_hock", "paper_moose"}
    music = build_music(got, os.path.join(OUT, "music"), work)
    used |= {m["key"] for m in music}

    write_index(events_out, bed, music, got, missing)
    write_credits(got, used)
    man = {"access_date": ACCESS_DATE,
           "sources": {k: {f: v[f] for f in ("page", "audio_url", "sha256", "title", "author", "licence", "licence_url", "grade")} for k, v in sorted(got.items())},
           "not_obtained": missing, "outputs": outputs + [{"file": b["file"], "lufs": round(b["lufs"], 1)} for b in bed] + [{"file": m["file"], "lufs": round(m["lufs"], 1)} for m in music]}
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(man, f, ensure_ascii=False, indent=1, sort_keys=True)
    print("done: %d event files, bed %d, music %d, not obtained %d" % (len(outputs), len(bed), len(music), len(missing)))


if __name__ == "__main__":
    main()
