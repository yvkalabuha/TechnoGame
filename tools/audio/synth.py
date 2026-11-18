#!/usr/bin/env python3
"""Offline synthesiser for the UI sounds of the client (AUDIO_DIRECTION §5).

Pure standard library, fully deterministic: all noise comes from a seeded generator of our own, so the same seed gives
byte-identical WAV files. Output: 48 kHz, 16-bit, mono WAV, normalised to the loudness target of the recipe
(BS.1770 K-weighted level over the whole file) with a sample-peak ceiling of -3 dBFS.

Usage: python3 tools/audio/synth.py [--out DIR] [--seed N] [--era 0..1]
era_tone 0 = relay and noise attack, 1 = clean sine/FM (saturation 30% -> 0%, noise attack 40% -> 5%).
"""
import argparse
import hashlib
import json
import math
import os
import struct
import wave

SR = 48000


class Rng:
    """xorshift64*: deterministic noise and variant choice, independent of the simulation RNG."""

    def __init__(self, seed):
        self.s = (seed * 0x9E3779B97F4A7C15 + 0x1234567) & 0xFFFFFFFFFFFFFFFF or 1

    def u(self):
        x = self.s
        x ^= x >> 12
        x ^= (x << 25) & 0xFFFFFFFFFFFFFFFF
        x ^= x >> 27
        self.s = x
        return (((x * 0x2545F4914F6CDD1D) & 0xFFFFFFFFFFFFFFFF) >> 11) / float(1 << 53)

    def bi(self):
        return self.u() * 2.0 - 1.0

    def spread(self, frac):
        """Multiplier in [1-frac, 1+frac]."""
        return 1.0 + self.bi() * frac


def ms(x):
    return int(round(x * SR / 1000.0))


# ---------- blocks ----------

def osc(kind, f, n, pwm=0.5):
    """kind: sine, tri, square, saw. f: Hz or callable(i) -> Hz."""
    out = [0.0] * n
    ph = 0.0
    for i in range(n):
        hz = f(i) if callable(f) else f
        ph += hz / SR
        ph -= math.floor(ph)
        if kind == "sine":
            v = math.sin(2 * math.pi * ph)
        elif kind == "tri":
            v = 4 * abs(ph - 0.5) - 1
        elif kind == "square":
            v = 1.0 if ph < pwm else -1.0
        else:
            v = 2 * ph - 1
        out[i] = v
    return out


def fm(f, ratio, index, n):
    """Two-operator FM; index is a number or callable(i)."""
    out = [0.0] * n
    pc = pm = 0.0
    for i in range(n):
        hz = f(i) if callable(f) else f
        idx = index(i) if callable(index) else index
        pm += hz * ratio / SR
        pc += hz / SR
        out[i] = math.sin(2 * math.pi * pc + idx * math.sin(2 * math.pi * pm))
    return out


def white(rng, n):
    return [rng.bi() for _ in range(n)]


def pink(rng, n):
    """Paul Kellet's economy pink filter."""
    b0 = b1 = b2 = 0.0
    out = [0.0] * n
    for i in range(n):
        w = rng.bi()
        b0 = 0.99765 * b0 + w * 0.0990460
        b1 = 0.96300 * b1 + w * 0.2965164
        b2 = 0.57000 * b2 + w * 1.0526913
        out[i] = (b0 + b1 + b2 + w * 0.1848) * 0.25
    return out


def env(n, attack_ms=1.0, tau_ms=None, release_ms=0.0):
    """Linear attack, exponential decay (time constant tau), linear release at the end."""
    a = max(1, ms(attack_ms))
    r = ms(release_ms)
    tau = ms(tau_ms) if tau_ms else 0
    out = [0.0] * n
    for i in range(n):
        v = min(1.0, i / a)
        if tau:
            v *= math.exp(-i / tau)
        if r and n - i < r:
            v *= (n - i) / r
        out[i] = v
    return out


def mul(x, e):
    return [a * b for a, b in zip(x, e)]


def scale(x, g):
    return [a * g for a in x]


def mix(*parts):
    """Each part is (start_sample, signal); the result is as long as the latest end."""
    n = max(s + len(x) for s, x in parts)
    out = [0.0] * n
    for s, x in parts:
        for i, v in enumerate(x):
            out[s + i] += v
    return out


def biquad(x, kind, fc, q=0.7071):
    """RBJ biquad (lp, hp, bp); fc is Hz or callable(i) -> Hz, coefficients are refreshed every 32 samples."""
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    b0 = b1 = b2 = a1 = a2 = 0.0
    for i, v in enumerate(x):
        if i % 32 == 0:
            f = min(fc(i) if callable(fc) else fc, SR * 0.45)
            w = 2 * math.pi * f / SR
            cw, sw = math.cos(w), math.sin(w)
            al = sw / (2 * q)
            if kind == "lp":
                b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
            elif kind == "hp":
                b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
            else:
                b0, b1, b2 = al, 0.0, -al
            a0 = 1 + al
            a1, a2 = -2 * cw / a0, (1 - al) / a0
            b0, b1, b2 = b0 / a0, b1 / a0, b2 / a0
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y


def saturate(x, amount):
    """Tube-ish waveshaper; amount 0..1 is the wet share."""
    if amount <= 0:
        return x
    k = 3.0
    d = math.tanh(k)
    return [(1 - amount) * v + amount * math.tanh(k * v) / d for v in x]


def room(x, wet=0.1, rt60=0.2):
    """Four parallel feedback combs: a small room, RT60 ~0.1-0.3 s."""
    delays = [ms(29.7), ms(37.1), ms(41.1), ms(43.7)]
    n = len(x) + int(rt60 * SR)
    out = [0.0] * n
    src = x + [0.0] * (n - len(x))
    for d in delays:
        g = 10 ** (-3.0 * d / SR / rt60)
        buf = [0.0] * n
        for i in range(n):
            buf[i] = src[i] + (g * buf[i - d] if i >= d else 0.0)
        for i in range(n):
            out[i] += buf[i] * 0.25
    return [(1 - wet) * (src[i]) + wet * out[i] for i in range(n)]


def glide(a, b, n):
    return lambda i: a + (b - a) * min(1.0, i / max(1, n))


def silence(n):
    return [0.0] * n


# ---------- loudness ----------

def k_weight(x):
    def run(sig, b, a):
        y = [0.0] * len(sig)
        x1 = x2 = y1 = y2 = 0.0
        for i, v in enumerate(sig):
            o = b[0] * v + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
            x2, x1, y2, y1 = x1, v, y1, o
            y[i] = o
        return y
    s1 = run(x, (1.53512485958697, -2.69169618940638, 1.19839281085285), (1.0, -1.69065929318241, 0.73248077421585))
    return run(s1, (1.0, -2.0, 1.0), (1.0, -1.99004745483398, 0.99007225036621))


def lufs(x):
    """BS.1770 K-weighted mean level over the file (no gating: the sounds are shorter than a gating block)."""
    y = k_weight(x)
    ms_ = sum(v * v for v in y) / max(1, len(y))
    return -0.691 + 10 * math.log10(ms_) if ms_ > 0 else -120.0


def peak_db(x):
    p = max((abs(v) for v in x), default=0.0)
    return 20 * math.log10(p) if p > 0 else -120.0


def finish(x, target_lufs, ceiling_db=-3.0):
    """Trims the silent end, applies the loudness gain, then lowers it if the sample peak passes the ceiling."""
    end = len(x)
    while end > 1 and abs(x[end - 1]) < 1e-4:
        end -= 1
    x = x[:end + ms(5)]
    g = 10 ** ((target_lufs - lufs(x)) / 20)
    x = scale(x, g)
    over = peak_db(x) - ceiling_db
    if over > 0:
        x = scale(x, 10 ** (-over / 20))
    return x


# ---------- recipes (AUDIO_DIRECTION §5, 15 starting sounds + variants) ----------
# Each recipe takes (rng, era) and returns (signal, target LUFS). rng drives variants: pitch +-3%, filter +-5%.

def noise_attack(era):
    return 0.40 * (1 - era) + 0.05 * era


def sat_amount(base, era):
    return base * (1 - era)


def order_ok(r, era, var=True):
    p = r.spread(0.03) if var else 1.0
    q = r.spread(0.05) if var else 1.0
    n = ms(120)
    body = mul(osc("tri", glide(880 * p, 660 * p, ms(60)), n), env(n, 1, 70))
    click = mul(biquad(white(r, n), "hp", 3000 * q), env(n, 0.2, 3))
    x = mix((0, body), (0, scale(click, noise_attack(era))))
    return saturate(biquad(x, "lp", 4000 * q), sat_amount(0.3, era) * 0.5), -26


def order_reject(r, era, var=False):
    n = ms(50)
    b = mul(osc("square", 140, n), env(n, 1, 40, 8))
    x = mix((0, b), (ms(90), b))
    return saturate(biquad(x, "lp", 800), 0.2), -20


def queue_tick(r, era, var=True):
    q = r.spread(0.05) if var else 1.0
    p = r.spread(0.03) if var else 1.0
    a = mul(pink(r, ms(15)), env(ms(15), 0.5, 5, 2))
    b = mul(osc("sine", 1200 * p, ms(10)), env(ms(10), 0.5, 4, 2))
    return biquad(mix((0, a), (0, b)), "bp", 1500 * q, 0.8), -30


def order_done(r, era, var=True):
    p = r.spread(0.03) if var else 1.0
    n1 = ms(120)
    a = mul(osc("sine", glide(523 * p, 784 * p, ms(80)), n1), env(n1, 5, 90))
    tail = mul(osc("sine", 784 * p, ms(200)), env(ms(200), 4, 80, 60))
    return mix((0, a), (ms(60), scale(tail, 0.35))), -24


def _two_tone(f1, f2, p, idx, note_ms=75, tail_ms=150):
    n = ms(note_ms)
    a = mul(fm(f1 * p, 1.0, idx, n), env(n, 4, 90, 6))
    nt = ms(note_ms + tail_ms)
    b = mul(fm(f2 * p, 1.0, idx, nt), env(nt, 4, 70, 40))
    return mix((0, a), (n, b))


def signal_attention(r, era, var=False):
    return _two_tone(660, 784, 1.0, 1.0), -20


def signal_clear(r, era, var=False):
    return scale(_two_tone(784, 660, 1.0, 1.0), 0.63), -24  # -4 dB relative, then normalised to -24


def signal_alert(r, era, var=False):
    def blip():
        n = ms(100)
        f = glide(880, 1046, ms(100))
        a = fm(f, 1.0, 2.0, n)
        h = osc("sine", lambda i: 3 * f(i), n)
        return mul(mix((0, a), (0, scale(h, 0.25))), env(n, 2, 120, 10))
    b = blip()
    return mix((0, b), (ms(140), b)), -17


def signal_critical(r, era, var=False):
    n = ms(80)
    pulse = mul(osc("square", 440, n, pwm=0.3), env(n, 1, 200, 6))
    x = mix((0, pulse), (ms(130), pulse), (ms(260), pulse))
    return biquad(x, "lp", 3000), -16


def node_discovered(r, era, var=False):
    n = ms(1000)
    bell = mul(fm(1047, 3.5, lambda i: 3.0 * math.exp(-i / ms(150)), n), env(n, 1, 280, 100))
    click = mul(biquad(white(r, ms(5)), "hp", 2500), env(ms(5), 0.1, 1.5))
    return mix((0, bell), (0, scale(click, noise_attack(era) * 2))), -20


def breakthrough(r, era, var=False):
    n = ms(1000)
    k = 2 ** (7 / 12)
    b1 = mul(fm(1047, 3.5, lambda i: 3.0 * math.exp(-i / ms(150)), n), env(n, 1, 280, 100))
    b2 = mul(fm(1047 * k, 3.5, lambda i: 3.0 * math.exp(-i / ms(150)), n), env(n, 1, 280, 100))
    ns = ms(300)
    whistle = mul(biquad(white(r, ns), "hp", 6000), env(ns, 30, 150, 80))
    click = mul(biquad(white(r, ms(5)), "hp", 2500), env(ms(5), 0.1, 1.5))
    return mix((0, b1), (ms(120), b2), (0, scale(click, noise_attack(era) * 2)), (ms(120), scale(whistle, 0.12))), -18


def line_commissioned(r, era, var=False):
    n = ms(750)
    rise = [min(1.0, (i / ms(350)) ** 2) for i in range(n)]
    tail = env(n, 1, None, 300)
    hum = mix((0, osc("sine", 110, n)), (0, scale(osc("sine", 220, n), 0.4)))
    hum = mul(mul(hum, rise), tail)
    click = mul(biquad(white(r, ms(8)), "bp", 1000, 2.0), env(ms(8), 0.2, 2))
    return mix((0, scale(click, 0.9)), (0, scale(hum, 0.8))), -22


def war_declared(r, era, var=False):
    hit = mul(mix((0, osc("sine", 55, ms(300))), (0, scale(osc("sine", 110, ms(300)), 0.5))), env(ms(300), 3, 150, 40))
    buzz = biquad(mul(osc("square", 220, ms(600)), env(ms(600), 10, None, 150)), "lp", 1200)
    return saturate(mix((0, hit), (ms(300), scale(buzz, 0.7))), 0.1), -16


def launch_space(r, era, var=False):
    n = ms(1200)
    sw = glide(200, 2000, n)
    noise = biquad(pink(r, n), "bp", sw, 2.5)
    swell = [(i / n) ** 1.5 for i in range(n)]
    tone_n = ms(250)
    tone = mul(osc("sine", 1175, tone_n), env(tone_n, 5, 90, 60))
    return mix((0, mul(noise, swell)), (n, scale(tone, 0.5))), -20


def launch_nuclear(r, era, var=False):
    n = ms(1500)
    x = mix((0, osc("sine", 82, n)), (0, osc("sine", 83.5, n)), (0, scale(osc("sine", 164, n), 0.3)))
    return mul(x, env(n, 600, None, 500)), -16


def tempo(steps):
    def make(r, era, var=False):
        clicks = []
        for k in range(steps):
            n = ms(8)
            clicks.append((k * ms(45), mul(osc("square", 2000 * (1.08 ** k), n), env(n, 0.3, 2, 2))))
        return biquad(mix(*clicks), "lp", 3500), -26
    return make


def pause(r, era, var=False):
    n = ms(250)
    x = mul(osc("sine", 110, n), env(n, 8, 90, 60))
    return biquad(x, "lp", glide(900, 150, n)), -26


def unpause(r, era, var=False):
    n = ms(250)
    x = mul(osc("sine", 110, n), env(n, 30, 80, 40))
    return biquad(x, "lp", glide(150, 900, n)), -26


def ui_tick(r, era, var=True):
    p = r.spread(0.03) if var else 1.0
    n = ms(25)
    x = mul(osc("sine", 1800 * p, n), env(n, 0.5, 5, 5))
    return biquad(x, "bp", 1800, 0.9), -30


# name -> (recipe, number of variants)
SOUNDS = {
    "order_ok": (order_ok, 4), "order_reject": (order_reject, 1), "queue_tick": (queue_tick, 4),
    "order_done": (order_done, 4), "signal_attention": (signal_attention, 1), "signal_alert": (signal_alert, 1),
    "signal_critical": (signal_critical, 1), "signal_clear": (signal_clear, 1), "node_discovered": (node_discovered, 1),
    "breakthrough": (breakthrough, 1), "line_commissioned": (line_commissioned, 1), "war_declared": (war_declared, 1),
    "launch_space": (launch_space, 1), "launch_nuclear": (launch_nuclear, 1),
    "tempo_1": (tempo(1), 1), "tempo_2": (tempo(2), 1), "tempo_3": (tempo(3), 1),
    "pause": (pause, 1), "unpause": (unpause, 1), "ui_tick": (ui_tick, 4),
}


def write_wav(path, x):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", max(-32768, min(32767, int(round(v * 32767))))) for v in x))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "..", "src", "Singularity.Client.Spike", "assets", "audio", "ui"))
    ap.add_argument("--seed", type=int, default=1910)
    ap.add_argument("--era", type=float, default=0.0)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    manifest = {}
    for name, (recipe, variants) in SOUNDS.items():
        for v in range(variants):
            r = Rng(args.seed * 1000 + hash_name(name) + v)
            x, target = recipe(r, args.era)
            x = finish(x, target)
            fn = name if variants == 1 else f"{name}_{v}"
            path = os.path.join(args.out, fn + ".wav")
            write_wav(path, x)
            manifest[fn] = {"seconds": round(len(x) / SR, 3), "lufs": round(lufs(x), 1), "peak_db": round(peak_db(x), 1), "target_lufs": target}
            print(f"{fn:22s} {len(x) / SR:5.2f}s  {manifest[fn]['lufs']:6.1f} LUFS (target {target})  peak {manifest[fn]['peak_db']:5.1f} dB")
    with open(os.path.join(args.out, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=1, sort_keys=True)


def hash_name(name):
    return int.from_bytes(hashlib.sha256(name.encode()).digest()[:4], "little")


if __name__ == "__main__":
    main()
