"""Synthesises the kit's placeholder audio with the stdlib only.

Everything is mono 44.1k 16-bit PCM, which is what the import policy expects to
stamp: SFX get Force To Mono anyway because the enemy source is 3D.
"""
import wave, struct, math, random, os

RATE = 44100
random.seed(7)

def n(count):            return [random.uniform(-1, 1) for _ in range(count)]
def secs(t):             return int(RATE * t)
def blank(t):            return [0.0] * secs(t)

def tone(t, f0, f1=None, kind="sine"):
    f1 = f0 if f1 is None else f1
    out, phase = [], 0.0
    total = secs(t)
    for i in range(total):
        f = f0 + (f1 - f0) * (i / total)
        phase += 2 * math.pi * f / RATE
        if kind == "sine":  out.append(math.sin(phase))
        elif kind == "saw": out.append(2 * ((phase / (2 * math.pi)) % 1.0) - 1)
        else:               out.append(1.0 if math.sin(phase) > 0 else -1.0)
    return out

def env(buf, attack=0.002, decay=None, power=2.0):
    """Fast attack, exponential decay -- the shape almost every impact sound has."""
    total = len(buf)
    a = max(1, secs(attack))
    out = []
    for i, v in enumerate(buf):
        if i < a:
            g = i / a
        else:
            x = (i - a) / max(1, total - a)
            g = (1 - x) ** power
        out.append(v * g)
    return out

def lowpass(buf, cutoff):
    """One-pole. Crude, but it is the difference between 'noise' and 'a thump'."""
    dt = 1.0 / RATE
    rc = 1.0 / (2 * math.pi * cutoff)
    a = dt / (rc + dt)
    out, prev = [], 0.0
    for v in buf:
        prev = prev + a * (v - prev)
        out.append(prev)
    return out

def highpass(buf, cutoff):
    dt = 1.0 / RATE
    rc = 1.0 / (2 * math.pi * cutoff)
    a = rc / (rc + dt)
    out, prev_in, prev_out = [], 0.0, 0.0
    for v in buf:
        prev_out = a * (prev_out + v - prev_in)
        prev_in = v
        out.append(prev_out)
    return out

def resonator(buf, freq, bw):
    """Two-pole resonator. One per formant is the whole difference between a buzz
    and a voice: the mouth is a set of resonances, and this is one of them."""
    r = math.exp(-math.pi * bw / RATE)
    theta = 2 * math.pi * freq / RATE
    a1, a2 = 2 * r * math.cos(theta), -r * r
    g = 1 - r * r
    out, y1, y2 = [], 0.0, 0.0
    for v in buf:
        y = g * v + a1 * y1 + a2 * y2
        y2, y1 = y1, y
        out.append(y)
    return out


def vowel(t, f0, f1, formants, breath=0.05):
    """A glottal buzz shaped by three formants. Falling pitch is what makes it read
    as an involuntary noise rather than a sung note."""
    src = mix(gain(tone(t, f0, f1, "saw"), 0.9), gain(n(secs(t)), breath))
    return mix(*[gain(resonator(src, freq, bw), amp) for freq, bw, amp in formants])


# Measured formant centres for the two vowels a hurt grunt actually uses.
AH = [(730, 80, 1.0), (1090, 90, 0.55), (2440, 130, 0.20)]
UH = [(520, 80, 1.0), (1180, 90, 0.45), (2400, 130, 0.16)]


def mix(*parts):
    size = max(len(p) for p in parts)
    out = [0.0] * size
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out

def gain(buf, g):        return [v * g for v in buf]
def cat(*parts):
    out = []
    for p in parts: out.extend(p)
    return out

def normalize(buf, peak=0.89):
    m = max(abs(v) for v in buf) or 1.0
    return [v * peak / m for v in buf]

def save(path, buf, loop=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    buf = normalize(buf)
    if loop:
        # Crossfade the tail over the head so the ambience bed loops seamlessly.
        x = secs(0.25)
        for i in range(x):
            g = i / x
            buf[i] = buf[i] * g + buf[len(buf) - x + i] * (1 - g)
        buf = buf[: len(buf) - x]
    with wave.open(path, "w") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in buf))
    print(f"  {os.path.getsize(path):>7} B  {path}")

A = "Assets/Audio"

# ---- weapon -----------------------------------------------------------------
# Crack (HF noise) + body (LF thump) + a short tail, which is what stops a
# gunshot sounding like a hiss.
crack = env(highpass(n(secs(0.16)), 1800), decay=None, power=7)
body  = env(lowpass(n(secs(0.22)), 260), power=4)
thump = env(tone(0.18, 140, 55), power=5)
save(f"{A}/SFX/weapon_fire.wav", mix(gain(crack, 0.85), gain(body, 0.8), gain(thump, 0.55)))

click1 = env(highpass(n(secs(0.03)), 2500), power=9)
save(f"{A}/SFX/weapon_empty.wav", mix(gain(click1, 0.9), gain(env(tone(0.03, 900, 500), power=9), 0.25)))

# Magazine out, in, bolt -- three transients spaced like the real motion.
def clunk(t, f, bright):
    return mix(gain(env(lowpass(n(secs(t)), f), power=6), 0.8),
               gain(env(highpass(n(secs(t)), bright), power=10), 0.35))
save(f"{A}/SFX/weapon_reload.wav",
     cat(clunk(0.09, 700, 3000), blank(0.16), clunk(0.11, 500, 2200),
         blank(0.22), clunk(0.08, 900, 3500), blank(0.1)))

# ---- impacts ----------------------------------------------------------------
save(f"{A}/SFX/impact_concrete.wav",
     mix(gain(env(highpass(n(secs(0.12)), 1200), power=8), 0.9),
         gain(env(lowpass(n(secs(0.09)), 400), power=6), 0.4)))
save(f"{A}/SFX/impact_metal.wav",
     mix(gain(env(highpass(n(secs(0.08)), 2000), power=10), 0.7),
         gain(env(tone(0.45, 2600, 2400), power=3), 0.5),
         gain(env(tone(0.38, 1750, 1680), power=3), 0.35)))
save(f"{A}/SFX/impact_wood.wav",
     mix(gain(env(lowpass(n(secs(0.13)), 900), power=7), 0.9),
         gain(env(tone(0.1, 420, 220), power=6), 0.4)))
save(f"{A}/SFX/impact_flesh.wav",
     mix(gain(env(lowpass(n(secs(0.16)), 320), power=5), 0.95),
         gain(env(tone(0.12, 180, 90), power=6), 0.45)))

# ---- movement ---------------------------------------------------------------
for i, (cut, dur) in enumerate([(520, 0.10), (600, 0.09), (460, 0.11), (560, 0.10)]):
    save(f"{A}/SFX/footstep_0{i + 1}.wav",
         mix(gain(env(lowpass(n(secs(dur)), cut), power=6), 0.85),
             gain(env(highpass(n(secs(0.035)), 3000), power=10), 0.18)))
save(f"{A}/SFX/land.wav",
     mix(gain(env(lowpass(n(secs(0.2)), 300), power=4), 0.95),
         gain(env(tone(0.16, 120, 48), power=5), 0.5)))

# ---- enemy ------------------------------------------------------------------
def growl(t, f0, f1, rasp=0.5):
    base = mix(gain(tone(t, f0, f1, "saw"), 0.6), gain(tone(t, f0 * 1.5, f1 * 1.5), 0.2))
    return mix(gain(lowpass(base, 900), 1.0), gain(lowpass(n(secs(t)), 700), rasp * 0.5))

save(f"{A}/SFX/enemy_alert.wav",  env(growl(0.55, 150, 185), attack=0.03, power=2))
save(f"{A}/SFX/enemy_attack.wav", env(growl(0.28, 220, 130, 0.8), attack=0.006, power=3))
save(f"{A}/SFX/enemy_death.wav",  env(growl(0.85, 170, 60, 0.7), attack=0.01, power=1.6))

# The hit reaction. Three of them because one grunt retriggered on every bullet is
# the most obviously looped sound a game can make -- EnemyAI picks at random and
# rate-limits it. Short, falling in pitch, and cut off: this is a body reacting,
# not a performance.
#
# The noise stream is saved and put back around them. Every clip here draws from one
# seeded generator, so inserting a sound that uses n() would otherwise re-roll every
# sound authored below it -- rewriting identical-sounding files and burying a real
# change in a diff full of churn.
_stream = random.getstate()
save(f"{A}/SFX/enemy_pain_01.wav",
     env(vowel(0.30, 250, 165, AH), attack=0.006, power=2.4))
save(f"{A}/SFX/enemy_pain_02.wav",
     env(vowel(0.24, 205, 140, UH), attack=0.005, power=2.8))
save(f"{A}/SFX/enemy_pain_03.wav",
     cat(env(vowel(0.13, 290, 230, AH), attack=0.004, power=3.5),
         env(vowel(0.22, 195, 130, UH), attack=0.01, power=2.6)))
random.setstate(_stream)

# ---- pickup -----------------------------------------------------------------
save(f"{A}/SFX/pickup.wav",
     mix(gain(env(tone(0.1, 660, 660), power=3), 0.5),
         gain(cat(blank(0.07), env(tone(0.2, 990, 990), power=3)), 0.5)))

# ---- ambience ---------------------------------------------------------------
# Long, quiet and 2D: a rumble bed plus air, which the policy will stream.
rumble = lowpass(n(secs(8.0)), 70)
air    = gain(highpass(lowpass(n(secs(8.0)), 1400), 300), 0.10)
swell  = [0.55 + 0.45 * math.sin(2 * math.pi * 0.07 * i / RATE) for i in range(secs(8.0))]
bed    = [rumble[i] * swell[i] + air[i] for i in range(secs(8.0))]
save(f"{A}/Ambience/arena_bed.wav", bed, loop=True)
