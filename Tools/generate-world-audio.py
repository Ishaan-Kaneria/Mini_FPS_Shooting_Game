#!/usr/bin/env python3
"""Synthesised sounds for the world (no recordings used, so nothing to license): a car horn, glass cracking, shattering and
tinkling, thuds of kicked props. Modal synthesis: each struck piece is a handful of inharmonic decaying partials over a
noise burst, which is how such things really sound. Mono 44.1 kHz WAV in Assets/Resources/Audio; run again to replace.
    python3 Tools/generate-world-audio.py"""
import math, os, random, struct, wave
RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Audio")

def write(name, s, peak=0.9):
    m = max(1e-6, max(abs(x) for x in s))
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, x / m * peak)) * 32767)) for x in s))

def blank(sec): return [0.0] * int(sec * RATE)

def ping(buf, at, freq, amp, decay):
    """One struck mode added into buf at `at` seconds."""
    start = int(at * RATE); w = 2 * math.pi * freq / RATE; k = math.exp(-1.0 / (decay * RATE)); a = amp; ph = random.random() * 6.28
    n = min(len(buf) - start, int(decay * 7 * RATE))
    for i in range(max(0, n)):
        buf[start + i] += a * math.sin(ph + w * i); a *= k

def noise(buf, at, dur, amp, decay, hp=0.0):
    """A burst of noise, optionally brightened by a one-pole high-pass."""
    start = int(at * RATE); last = 0.0; lp = 0.0
    for i in range(min(int(dur * RATE), len(buf) - start)):
        x = random.uniform(-1, 1) * amp * math.exp(-i / (decay * RATE))
        lp += (x - lp) * (1 - hp); buf[start + i] += x - lp if hp else x

def piece(buf, at, size):
    """A piece of glass striking: bright partials, higher and shorter the smaller it is."""
    base = random.uniform(2600, 5200) / max(0.35, size)
    for ratio in (1.0, 1.58, 2.21, 2.97):
        ping(buf, at, min(16000, base * ratio * random.uniform(0.97, 1.03)), random.uniform(0.12, 0.3) * size, random.uniform(0.018, 0.06))

def shatter(sec, count, seed, heavy=1.0):
    random.seed(seed); b = blank(sec)
    noise(b, 0.0, 0.25, 1.1 * heavy, 0.035, 0.9)                      # the break itself
    for f in (820, 1340, 2150): ping(b, 0, f * random.uniform(.95, 1.05), 0.5 * heavy, 0.05)
    for _ in range(count):
        t = random.random() ** 1.6 * (sec - 0.1)
        piece(b, t, random.uniform(0.35, 1.1) * (1 - 0.45 * t / sec))
    return b

def crack(seed):
    random.seed(seed); b = blank(0.22)
    noise(b, 0, 0.05, 1.0, 0.006, 0.92)
    for i in range(6): noise(b, random.uniform(0.002, 0.09), 0.012, random.uniform(.25, .7), 0.003, 0.95)
    for f in (1900, 3100, 4700): ping(b, 0.001, f * random.uniform(.96, 1.04), 0.25, 0.018)
    return b

def tinkle(seed):
    random.seed(seed); b = blank(0.8)
    for _ in range(14): piece(b, random.random() ** 1.3 * 0.7, random.uniform(0.25, 0.6))
    return b

def horn(seed, sec=0.62):
    """A car horn: two reed tones a third apart, rich in harmonics, with a soft attack and a road-side roll-off."""
    random.seed(seed); n = int(sec * RATE); f1, f2 = random.uniform(405, 430), random.uniform(500, 530); s = []; lp = 0.0
    for i in range(n):
        t = i / RATE; env = min(1, t / 0.012) * min(1, (sec - t) / 0.03); v = 0.0
        for f in (f1, f2):
            for h in range(1, 13): v += math.sin(2 * math.pi * f * h * t + h) / (h ** 0.95)
        v = math.tanh(v * 0.55) * env; lp += (v - lp) * 0.42; s.append(lp)
    return s

def thud(seed, freqs, decay, click=0.5, sec=0.5):
    random.seed(seed); b = blank(sec); noise(b, 0, 0.05, click, 0.008)
    for i, f in enumerate(freqs): ping(b, 0, f * random.uniform(.97, 1.03), 1.0 / (1 + i * 0.6), decay * (1 - i * 0.08))
    return b

os.makedirs(OUT, exist_ok=True)
write("CarHorn_A", horn(1)); write("CarHorn_B", horn(2, 0.45))
for i in range(3): write(f"GlassCrack_{i}", crack(10 + i), 0.75)
for i in range(3): write(f"GlassShatter_{i}", shatter(1.4, 70, 20 + i))
write("GlassShatterBig", shatter(2.1, 140, 30, 1.3))
write("GlassCubes", shatter(1.1, 180, 31, 0.7))
for i in range(2): write(f"GlassTinkle_{i}", tinkle(40 + i), 0.5)
write("PropHit_Metal_0", thud(50, [186, 311, 478, 742, 1104, 1630], 0.22, 0.4, 0.9))
write("PropHit_Metal_1", thud(51, [146, 262, 401, 655, 980], 0.28, 0.5, 1.0))
write("PropHit_Wood_0", thud(52, [172, 340, 590], 0.05, 0.9, 0.3))
write("PropHit_Plastic_0", thud(53, [310, 620, 1050], 0.035, 0.7, 0.25))
