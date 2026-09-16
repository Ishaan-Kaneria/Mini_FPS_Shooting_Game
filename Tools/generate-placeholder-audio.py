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

# ---- interface --------------------------------------------------------------
# Appended after everything else on purpose: these draw from the same seeded noise
# stream, and adding them anywhere earlier would re-roll every sound authored below
# them into an identical-sounding but byte-different file. See enemy_pain above.

def blip(t, f0, f1, power=3.0, attack=0.004):
    """A soft two-partial tone. The octave above keeps it from sounding like a test
    signal without making it a chime."""
    body = mix(gain(tone(t, f0, f1), 0.85), gain(tone(t, f0 * 2, f1 * 2), 0.16))
    return env(body, attack=attack, power=power)

save(f"{A}/UI/ui_hover.wav", gain(blip(0.06, 1180, 1420, 4.5), 0.42))

save(f"{A}/UI/ui_click.wav",
     mix(gain(blip(0.12, 720, 960, 3.0), 0.9),
         gain(env(highpass(n(secs(0.025)), 3200), power=9), 0.13)))

save(f"{A}/UI/ui_back.wav", gain(blip(0.14, 640, 430, 3.0), 0.8))

# Two notes up: the sound of something starting rather than something being pressed.
save(f"{A}/UI/ui_launch.wav",
     cat(gain(blip(0.10, 660, 660, 3.5), 0.7),
         gain(blip(0.26, 988, 1318, 2.4), 0.85)))


# ---- menu music -------------------------------------------------------------
# Four bars of a slow minor progression, quiet enough to sit under a conversation.
# Sine partials only: the kit has no instrument samples, and a soft stack of sines is
# the one synthetic sound that reads as intentional rather than as a placeholder.

def voice(t, freq, level, attack=0.5, fade=0.55):
    stack = mix(gain(tone(t, freq, freq), 1.0),
                gain(tone(t, freq * 2, freq * 2), 0.26),
                gain(tone(t, freq * 3, freq * 3), 0.08))
    total = len(stack)
    rise = max(1, secs(attack))
    out = []
    for i, v in enumerate(stack):
        g = min(1.0, i / rise) * (1.0 - (i / total) ** 2 * fade)
        out.append(v * g)
    return gain(out, level)


def chord(t, notes, level=0.16):
    return mix(*[voice(t, f, level) for f in notes])


def pluck(at, t, freq, level=0.1):
    return cat(blank(at), gain(env(mix(gain(tone(t, freq, freq), 1.0),
                                       gain(tone(t, freq * 2, freq * 2), 0.2)),
                                  attack=0.008, power=4.5), level))


BAR = 4.0
PROGRESSION = [
    (220.00, [220.00, 261.63, 329.63]),   # Am
    (174.61, [174.61, 220.00, 261.63]),   # F
    (130.81, [261.63, 329.63, 392.00]),   # C
    (196.00, [196.00, 246.94, 293.66]),   # G
]

bars = []
for root, notes in PROGRESSION:
    layers = [chord(BAR, notes), voice(BAR, root / 2.0, 0.13, attack=0.8)]

    # A sparse arpeggio over the top, on the off-beats so it drifts rather than marches.
    for step, note in enumerate(notes + [notes[1]]):
        layers.append(pluck(0.5 + step * 0.85, 0.7, note * 2.0, 0.055))

    bar = mix(*layers)
    bars.append(bar[: secs(BAR)])

save(f"{A}/Music/menu_loop.wav", gain(cat(*bars), 0.75), loop=True)


# ---- level results -----------------------------------------------------------
# The sounds the star rating is made of. Appended last for the same reason the rest
# of the interface set is: they draw from the same seeded noise stream, and inserting
# them anywhere earlier would re-roll every sound authored below them into an
# identical-sounding but byte-different file.
#
# These matter more than their length suggests. A star awarded silently is a number;
# a star that lands with a note is the thing people replay a level for. LevelResultsUI
# plays star.wav once per star, pitched up a fifth each time, so the clip is
# deliberately plain -- the tune is in the sequence, not in the sample.

save(f"{A}/UI/star.wav",
     mix(gain(blip(0.30, 1318, 1318, 2.2, attack=0.002), 0.85),
         gain(cat(blank(0.01), blip(0.26, 1976, 1976, 2.6)), 0.25)))


def note(at, t, freq, level=0.5, power=2.2):
    """One voice of a fanfare: a tone with an octave and a fifth over it, placed at a
    time offset. The partials are what stop four sine notes reading as a test tone."""
    body = mix(gain(tone(t, freq, freq), 1.0),
               gain(tone(t, freq * 2, freq * 2), 0.30),
               gain(tone(t, freq * 3, freq * 3), 0.12))
    return cat(blank(at), gain(env(body, attack=0.004, power=power), level))


# A major arpeggio walking up to the octave, then the triad held under it. Rising and
# major, because this is the sound of having beaten something.
save(f"{A}/UI/level_cleared.wav",
     mix(note(0.00, 0.30, 523.25, 0.55, 3.0),    # C5
         note(0.11, 0.30, 659.25, 0.55, 3.0),    # E5
         note(0.22, 0.34, 783.99, 0.60, 3.0),    # G5
         note(0.33, 0.95, 1046.50, 0.70, 1.6),   # C6, held
         note(0.33, 0.95, 783.99, 0.30, 1.6),
         note(0.33, 0.95, 523.25, 0.26, 1.6)))

# Two notes down onto a minor third, over a soft thud. Short: a failure screen that
# announces itself at length is one the player resents before they have read it.
save(f"{A}/UI/level_failed.wav",
     mix(note(0.00, 0.26, 349.23, 0.55, 3.2),    # F4
         note(0.14, 0.70, 277.18, 0.60, 1.9),    # C#4
         note(0.14, 0.70, 233.08, 0.32, 1.9),    # A#3
         gain(env(lowpass(n(secs(0.35)), 220), power=4), 0.30)))


# ---- explosives and supplies -------------------------------------------------
# Appended last, like everything else that draws from the seeded noise stream.

# The throw: cloth and a grunt of effort, over in a tenth of a second.
save(f"{A}/SFX/bomb_throw.wav",
     mix(gain(env(highpass(lowpass(n(secs(0.16)), 2600), 600), power=5), 0.55),
         gain(env(tone(0.10, 320, 180), power=6), 0.25)))

# The blast. Three layers, because an explosion that is only noise is a hiss and one
# that is only a thump is a door closing: a crack off the front, a body of filtered
# noise with a long tail, and a sub-bass drop underneath that is most of what makes it
# feel large on a speaker that cannot reproduce it.
_crack = env(highpass(n(secs(0.35)), 1400), power=6)
_body  = env(lowpass(n(secs(1.10)), 420), power=2.2)
_rumble = env(lowpass(n(secs(1.40)), 120), power=1.4)
_drop  = env(tone(0.70, 90, 28), power=2.0)

save(f"{A}/SFX/explosion.wav",
     mix(gain(_crack, 0.75), gain(_body, 0.95), gain(_rumble, 0.85), gain(_drop, 0.7)))

# The drink: a can cracking open, three swallows, and a rising tone as the rush lands.
def swallow(at, t):
    return cat(blank(at), gain(env(lowpass(n(secs(t)), 380), attack=0.01, power=4), 0.5))

save(f"{A}/SFX/drink.wav",
     mix(gain(env(highpass(n(secs(0.05)), 3000), power=9), 0.6),        # the tab
         swallow(0.10, 0.10), swallow(0.26, 0.10), swallow(0.42, 0.12),
         gain(cat(blank(0.30), blip(0.55, 440, 880, 1.6, attack=0.08)), 0.45)))

# ---- store -------------------------------------------------------------------
# A coin is a short pair of high partials a fifth apart -- the interval is what makes
# two notes read as money rather than as a beep.
save(f"{A}/UI/coin.wav",
     mix(gain(blip(0.09, 1568, 1568, 4.0), 0.7),
         gain(cat(blank(0.045), blip(0.22, 2349, 2349, 3.2)), 0.55)))

# A purchase: the coin, then a low confirming thud, so spending feels like a weight
# changing hands rather than like another click.
save(f"{A}/UI/purchase.wav",
     mix(gain(blip(0.10, 784, 1046, 3.0), 0.75),
         gain(cat(blank(0.08), blip(0.34, 1318, 1568, 2.0)), 0.7),
         gain(env(lowpass(n(secs(0.18)), 300), power=5), 0.3)))
