#!/usr/bin/env python3
"""Synthesised car sounds (no recordings used, nothing to license): an engine as a four-cylinder's firing pulses through an exhaust, at three revs that CarAudio crossfades and pitches;
tyres rolling and squealing, wind, doors, the starter, a gear change and two crashes. Each loop is built to join its own end seamlessly (whole periods, circular noise).
Mono 44.1 kHz WAV in Assets/Resources/Audio.     python3 Tools/generate-car-audio.py"""
import math, os, random, struct, wave
RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Audio")

def write(name, s, peak=0.85):
    m = max(1e-6, max(abs(x) for x in s))
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, x / m * peak)) * 32767)) for x in s))

def loop_noise(n, cutoff, seed, high=False):
    """Noise that joins its own end: a one-pole filter run twice round the buffer, so its tail is its head."""
    rnd = random.Random(seed)
    x = [rnd.uniform(-1, 1) for _ in range(n)]
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    y = 0.0; out = [0.0] * n
    for _ in range(2):
        for i in range(n):
            y += (x[i] - y) * a
            out[i] = (x[i] - y) if high else y
    return out

def engine(name, rpm, seed, rough, bright):
    """A four-stroke four: two firings a revolution. Harmonics of the firing rate, the odd half-orders from cylinders not quite alike, a little breath."""
    f = rpm / 60.0 * 2.0
    secs = 2.0
    cycles = round(f / 2.0 * secs)                 # whole engine cycles (a cycle is 2 firings) so it loops
    n = int(round(cycles * 2.0 / f * RATE))
    rnd = random.Random(seed)
    order = []
    for h in range(1, 26):
        amp = 1.0 / (h ** (1.35 - bright * 0.5))
        order.append((h, amp, rnd.random() * 6.28))
    half = [(h + 0.5, rough / (h + 1.0), rnd.random() * 6.28) for h in range(0, 12)]
    # Each firing a little different, repeating over the loop: what makes it sound like a machine and not a tone.
    wobble = [1 + rnd.uniform(-rough, rough) * 0.5 for _ in range(int(cycles * 2))]
    breath = loop_noise(n, 1800 + 2600 * bright, seed + 1)
    out = [0.0] * n
    w = 2 * math.pi * f / RATE
    for i in range(n):
        ph = w * i
        v = 0.0
        for h, a, p in order: v += a * math.sin(h * ph + p)
        for h, a, p in half: v += a * math.sin(h * ph + p)
        firing = int(ph / (2 * math.pi)) % len(wobble)
        pulse = 0.5 + 0.5 * math.cos(ph)
        v *= wobble[firing]
        v += breath[i] * (0.18 + 0.5 * bright) * (0.35 + 0.65 * pulse * pulse)
        out[i] = math.tanh(v * 0.8)
    write(name, out)

def tone_noise(name, secs, cutoff, seed, high=False, amp_mod=None):
    n = int(secs * RATE)
    s = loop_noise(n, cutoff, seed, high)
    if amp_mod:
        for i in range(n): s[i] *= 1 + 0.4 * math.sin(2 * math.pi * amp_mod * i / n)
    write(name, s, 0.7)

def skid():
    n = int(2.0 * RATE)
    base = loop_noise(n, 3200, 91, True)
    out = [0.0] * n
    rnd = random.Random(92)
    tones = [(rnd.randint(1500, 1900), rnd.uniform(0.3, 0.6), rnd.randint(3, 9)) for _ in range(7)]   # whole cycles of its wobble fit the loop
    for i in range(n):
        t = i / n
        v = base[i] * 0.3
        for hz, a, wob in tones:
            f = round(hz * 2.0)   # cycles in the loop
            v += a * math.sin(2 * math.pi * (f * t + 3.0 * math.sin(2 * math.pi * wob * t) / (2 * math.pi)))
        out[i] = math.tanh(v * 0.6)
    write("CarSkid", out, 0.7)

def thump(buf, at, freq, amp, decay, drop=0.0):
    start = int(at * RATE); ph = 0.0
    for i in range(len(buf) - start):
        t = i / RATE
        if t > decay * 7: break
        ph += 2 * math.pi * (freq * math.exp(-drop * t)) / RATE
        buf[start + i] += amp * math.exp(-t / decay) * math.sin(ph)

def burst(buf, at, dur, amp, decay, cutoff, seed):
    start = int(at * RATE); rnd = random.Random(seed); y = 0.0; a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    for i in range(min(int(dur * RATE), len(buf) - start)):
        y += (rnd.uniform(-1, 1) - y) * a
        buf[start + i] += amp * y * math.exp(-i / RATE / decay)

def door_close():
    b = [0.0] * int(0.6 * RATE)
    thump(b, 0.0, 95, 1.0, 0.07, 4); thump(b, 0.0, 210, 0.5, 0.03, 2); burst(b, 0.0, 0.08, 0.7, 0.02, 2500, 11); thump(b, 0.012, 1400, 0.1, 0.012); write("CarDoorClose", b)

def door_open():
    b = [0.0] * int(0.5 * RATE)
    burst(b, 0.0, 0.03, 0.8, 0.008, 5000, 12); thump(b, 0.008, 900, 0.3, 0.01); thump(b, 0.05, 160, 0.35, 0.05, 2); burst(b, 0.06, 0.3, 0.06, 0.12, 1200, 13); write("CarDoorOpen", b)

def starter():
    b = [0.0] * int(1.5 * RATE)
    rnd = random.Random(21)
    t = 0.0; period = 0.085
    while t < 0.62:
        thump(b, t, 75 + rnd.uniform(-5, 5), 0.55, 0.035, 3); burst(b, t, 0.03, 0.25, 0.012, 2200, int(t * 1000) + 30); t += period; period *= 0.975
    # It catches: a rising bark settling.
    start = int(0.6 * RATE); f = 40.0; ph = 0.0
    for i in range(len(b) - start):
        x = i / RATE
        f = 40 + 110 * (1 - math.exp(-x * 8)) * math.exp(-x * 1.4)
        ph += 2 * math.pi * f / RATE
        v = sum(math.sin(h * ph) / h for h in range(1, 9))
        b[start + i] += 0.5 * v * min(1, x * 20) * math.exp(-x * 2.2)
    write("CarStart", b)

def shift():
    b = [0.0] * int(0.18 * RATE)
    burst(b, 0.0, 0.03, 0.8, 0.01, 3500, 41); thump(b, 0.002, 420, 0.35, 0.02); write("CarShift", b)

def crash(name, secs, heavy, seed):
    rnd = random.Random(seed)
    b = [0.0] * int(secs * RATE)
    thump(b, 0.0, 70, 1.2 * heavy, 0.12, 2); burst(b, 0.0, secs, 1.0 * heavy, 0.09 * heavy, 4200, seed)
    for _ in range(int(14 * heavy)):
        at = rnd.uniform(0, secs * 0.5)
        thump(b, at, rnd.uniform(300, 2400), rnd.uniform(0.12, 0.4), rnd.uniform(0.02, 0.09))
    write(name, b)

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    engine("CarEngine_Idle", 900, 1, 0.5, 0.05)
    engine("CarEngine_Low", 2500, 2, 0.28, 0.4)
    engine("CarEngine_High", 5000, 3, 0.16, 0.8)
    tone_noise("CarRoad", 3.0, 420, 71, False, 2)
    tone_noise("CarWind", 3.0, 1100, 72, True, 3)
    skid(); door_close(); door_open(); starter(); shift()
    crash("CarCrash_0", 0.6, 0.6, 81); crash("CarCrash_1", 1.2, 1.0, 82)
    print("[cars] audio written")
