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
#
# The whole block is wrapped in a save/restore of the noise stream. Every clip in this
# file draws from one seeded generator, so changing how much noise anything in here
# consumes would re-roll every sound authored below it -- rewriting identical-sounding
# files and burying a real change in a diff full of churn.
_enemy_stream = random.getstate()

def growl(t, f0, f1, rasp=0.5):
    base = mix(gain(tone(t, f0, f1, "saw"), 0.6), gain(tone(t, f0 * 1.5, f1 * 1.5), 0.2))
    return mix(gain(lowpass(base, 900), 1.0), gain(lowpass(n(secs(t)), 700), rasp * 0.5))

save(f"{A}/SFX/enemy_alert.wav",  env(growl(0.55, 150, 185), attack=0.03, power=2))
save(f"{A}/SFX/enemy_attack.wav", env(growl(0.28, 220, 130, 0.8), attack=0.006, power=3))


# ---- being shot -------------------------------------------------------------
# A hit is two things happening at once and the sound has to carry both: something
# striking a body, and the body reacting to it. The reaction alone -- which is all
# these used to be -- reads as a person making a noise for no reason, because the
# round that caused it is a separate clip playing from the gun across the arena.
#
# So each one is a wet impact transient with a grunt starting a few milliseconds
# behind it. The delay is the point: simultaneous, they fuse into one muddy sound;
# offset, the ear hears a cause and an effect.

def impact(t=0.09, cut=340, bright=0.18):
    """The round landing. Low, short and wet -- a body, not a wall."""
    return mix(gain(env(lowpass(n(secs(t)), cut), power=6), 0.95),
               gain(env(highpass(n(secs(0.03)), 2200), power=11), bright),
               gain(env(tone(0.07, 150, 70), power=7), 0.4))


def hit(grunt_at, grunt, punch=0.09, cut=340):
    """An impact with a reaction a few milliseconds behind it."""
    return mix(impact(punch, cut), cat(blank(grunt_at), grunt))


# Four rather than three, and each a different shape: a short bark, a lower groan, a
# double take, and one that is almost all impact with barely a voice behind it. Four
# is where a crowd under sustained fire stops sounding like one enemy.
save(f"{A}/SFX/enemy_pain_01.wav",
     hit(0.035, gain(env(vowel(0.26, 260, 170, AH), attack=0.005, power=2.6), 0.85)))

save(f"{A}/SFX/enemy_pain_02.wav",
     hit(0.030, gain(env(vowel(0.22, 200, 132, UH), attack=0.004, power=3.0), 0.8),
         punch=0.11, cut=280))

save(f"{A}/SFX/enemy_pain_03.wav",
     hit(0.028, cat(env(vowel(0.11, 300, 240, AH), attack=0.003, power=3.8),
                    gain(env(vowel(0.20, 190, 126, UH), attack=0.01, power=2.8), 0.7))))

save(f"{A}/SFX/enemy_pain_04.wav",
     hit(0.040, gain(env(vowel(0.15, 175, 120, UH), attack=0.006, power=3.4), 0.45),
         punch=0.13, cut=240))


# ---- dying ------------------------------------------------------------------
# Deliberately a different *shape* from a pain grunt, not a longer one. A hit is a
# short bark that cuts off; a death falls a long way in pitch, runs out of air
# rather than stopping, and lands with the body. In a crowd that difference is the
# only way a player can tell "I hurt it" from "I killed it" without looking, which
# is most of what makes shooting into a group readable.

_death_cry = mix(
    gain(env(vowel(0.62, 205, 78, AH), attack=0.008, power=1.5), 1.0),
    # A second voice a fifth below, drifting out of tune with the first as it falls.
    # Two voices that do not quite agree is what stops a long note sounding sung.
    gain(env(vowel(0.62, 138, 54, UH), attack=0.02, power=1.3), 0.45),
    gain(impact(0.10, 300, 0.1), 0.7))

# The last of the air, after the voice has gone.
_death_rattle = cat(blank(0.52),
                    gain(env(lowpass(highpass(n(secs(0.30)), 400), 1800), power=2.5), 0.22))

# And the body arriving. Low, dull and late, so the clip finishes on the floor.
_body_fall = cat(blank(0.66),
                 mix(gain(env(lowpass(n(secs(0.34)), 190), power=3.5), 0.8),
                     gain(env(tone(0.22, 95, 38), power=4), 0.55)))

save(f"{A}/SFX/enemy_death.wav", mix(_death_cry, _death_rattle, _body_fall))

random.setstate(_enemy_stream)

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
# Appended last, like everything else that draws from the seeded noise stream, and
# wrapped in a save/restore of it so that retuning the blast -- which is the clip here
# most likely to be tuned again -- cannot rewrite the store sounds underneath it.
_blast_stream = random.getstate()

# The throw: cloth and a grunt of effort, over in a tenth of a second.
save(f"{A}/SFX/bomb_throw.wav",
     mix(gain(env(highpass(lowpass(n(secs(0.16)), 2600), 600), power=5), 0.55),
         gain(env(tone(0.10, 320, 180), power=6), 0.25)))

# The blast.
#
# Five layers over two and a half seconds, because the size of an explosion is almost
# entirely in how long it takes to stop. A short one is a door slamming however loud it
# is; what makes one read as *big* is the tail -- the rumble still going when the crack
# is long finished, and debris landing after that.
#
#   crack   the leading edge, high and gone in a tenth of a second
#   body    the detonation itself, mid-band, with a real decay
#   rumble  low noise that outlasts everything above it
#   drop    a sub-bass sweep, which is what a subwoofer reproduces and a laptop implies
#   debris  scattered late transients, so the blast has a floor to land on
_crack  = env(highpass(n(secs(0.40)), 1600), power=7)
_body   = env(lowpass(highpass(n(secs(1.60)), 180), 900), power=1.8)
_rumble = env(lowpass(n(secs(2.50)), 110), power=1.0)
_drop   = env(tone(1.10, 120, 22), power=1.5)

# A second, slower sweep under the first. Two sweeps an octave apart is what turns a
# tone falling in pitch into something collapsing.
_drop2  = env(tone(1.60, 60, 16), power=1.2)

# Rubble coming down, spread over the second half so the tail has detail in it rather
# than being a fade. Deterministic offsets, because a seeded stream is the whole reason
# rebuilding this file does not rewrite every clip in it.
_debris = blank(2.50)
for _i, (_at, _len, _cut) in enumerate([(0.55, 0.09, 900), (0.72, 0.07, 1400),
                                        (0.94, 0.11, 600), (1.18, 0.08, 1100),
                                        (1.45, 0.10, 500), (1.79, 0.07, 800)]):
    _piece = cat(blank(_at), gain(env(lowpass(n(secs(_len)), _cut), power=7), 0.42))
    _debris = mix(_debris, _piece)

save(f"{A}/SFX/explosion.wav",
     mix(gain(_crack, 0.8), gain(_body, 1.0), gain(_rumble, 0.95),
         gain(_drop, 0.85), gain(_drop2, 0.6), gain(_debris, 0.5)))

# The drink: a can cracking open, three swallows, and a rising tone as the rush lands.
def swallow(at, t):
    return cat(blank(at), gain(env(lowpass(n(secs(t)), 380), attack=0.01, power=4), 0.5))

save(f"{A}/SFX/drink.wav",
     mix(gain(env(highpass(n(secs(0.05)), 3000), power=9), 0.6),        # the tab
         swallow(0.10, 0.10), swallow(0.26, 0.10), swallow(0.42, 0.12),
         gain(cat(blank(0.30), blip(0.55, 440, 880, 1.6, attack=0.08)), 0.45)))

random.setstate(_blast_stream)

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


# ---- arming a bomb -----------------------------------------------------------
# Drawn from its own saved stream for the reason the blast is: this is the newest
# clip in the file, so it is the one most likely to be retuned again, and an
# un-wrapped draw here would re-roll every noise clip written above it.
_pin_stream = random.getstate()

# The pin.
#
# Holding the bomb key was the only control in the game that made no sound at all,
# which reads as a key that did nothing -- the ring is on the floor, and a player
# looking down the sights never sees it. A pin is two metal events a hair apart:
# the lever letting go, and the ring coming off. Short and dry, because it plays
# under whatever else is happening and has to be heard, not listened to.
save(f"{A}/SFX/bomb_pin.wav",
     mix(gain(env(highpass(n(secs(0.05)), 3400), power=9), 0.55),          # the catch
         gain(cat(blank(0.035), blip(0.14, 2093, 1976, 5.0)), 0.5),        # the ring
         gain(cat(blank(0.035), env(lowpass(n(secs(0.12)), 700), power=6)), 0.3)))

random.setstate(_pin_stream)


# ---- the punch ---------------------------------------------------------------
# Its own saved stream, for the reason the pin has one: appended last, so a retune
# here re-rolls nothing written above it.
_punch_stream = random.getstate()

# The swing: air moving past a sleeve. Band-limited noise that rises and falls in the
# time the fist takes to go out, so a miss still sounds like something was thrown.
def whoosh(t, lo, hi):
    body = highpass(lowpass(n(secs(t)), hi), lo)
    total = len(body)
    return [v * math.sin(math.pi * i / total) ** 1.5 for i, v in enumerate(body)]

save(f"{A}/SFX/punch_swing.wav", gain(whoosh(0.20, 500, 2600), 0.7))

# The landing: a dull, heavy knock -- a glove on a body, lower and drier than a round
# hitting one, so the two never read as the same event.
save(f"{A}/SFX/punch_hit.wav",
     mix(gain(env(lowpass(n(secs(0.12)), 260), power=5), 1.0),
         gain(env(tone(0.10, 110, 55), power=5), 0.7),
         gain(env(highpass(n(secs(0.02)), 1800), power=10), 0.25)))

random.setstate(_punch_stream)


# ---- voices -------------------------------------------------------------------
# Its own saved stream, like the pin and the punch: appended last, so nothing here
# re-rolls a clip written above it.
_voice_stream = random.getstate()
random.seed(1907)

# Two throats. A soldier and a creature, picked per archetype (EnemyArchetype.voice)
# and routed by EnemyVoice through the VoiceBank asset the scene builder writes.
#
# The old pain grunts were a sawtooth through three resonators, which is why they
# sounded like a synthesiser saying "uh". What a throat does that a sawtooth does not:
#
# - the source is a *pulse* (the folds opening and snapping shut), not a ramp, and its
#   spectrum falls away steeply -- that is most of the difference between a voice and
#   a buzz;
# - no two periods are the same length or the same loudness (jitter and shimmer), and
#   under strain the folds start skipping every other cycle (the subharmonic that makes
#   a scream rough and a growl a growl);
# - breath runs through the same mouth as the voice, so it has the vowel's colour;
# - the mouth moves: the vowel slides while the note is held.
#
# Every one of those is below. None of it is a real recording, and a real recording
# will always beat it -- the VoiceBank takes any clips, so replacing these is a drag
# and drop.

def glottal(t, f0, jitter=0.012, shimmer=0.07, sub=0.0, open_q=0.62):
    """The folds: one pulse per period, each a little different. f0 is a function of
    0..1 across the clip. `sub` drops every other pulse by that much -- period doubling,
    the rasp of a throat pushed past what it can hold steady."""
    total = secs(t)
    raw = [0.0] * total
    phase, period_f, amp, odd = 0.0, f0(0.0), 1.0, False
    rise = open_q * 0.72
    fall = open_q - rise
    for i in range(total):
        phase += period_f / RATE
        if phase >= 1.0:
            phase -= 1.0
            x = i / total
            period_f = max(25.0, f0(x) * (1.0 + random.gauss(0, jitter)))
            amp = max(0.1, 1.0 + random.gauss(0, shimmer))
            odd = not odd
        p = phase
        if p < rise:
            g = 0.5 * (1 - math.cos(math.pi * p / rise))
        elif p < open_q:
            g = math.cos(0.5 * math.pi * (p - rise) / fall)
        else:
            g = 0.0
        raw[i] = g * amp * ((1.0 - sub) if odd else 1.0)
    # What leaves the mouth is the derivative of the airflow, not the airflow.
    return [raw[i] - raw[i - 1] if i else 0.0 for i in range(total)]

def curve(*points):
    """A pitch contour through (x, hz) points, linear between them."""
    pts = sorted(points)
    def f(x):
        if x <= pts[0][0]: return pts[0][1]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            if x <= x1:
                return y0 + (y1 - y0) * (x - x0) / max(1e-6, x1 - x0)
        return pts[-1][1]
    return f

def through(src, formants, scale=1.0, widen=1.0):
    return mix(*[gain(resonator(src, f * scale, bw * widen), a) for f, bw, a in formants])

def morph(a, b, shape=lambda x: x):
    total = min(len(a), len(b))
    return [a[i] * (1 - shape(i / total)) + b[i] * shape(i / total) for i in range(total)]

def drive(buf, k):
    """Soft clipping: a voice at full effort distorts in the throat before it ever
    reaches a microphone."""
    m = max(abs(v) for v in buf) or 1.0
    d = math.tanh(k)
    return [math.tanh(k * v / m) / d for v in buf]

def tremolo(buf, rate, depth):
    return [v * (1 - depth * 0.5 * (1 + math.sin(2 * math.pi * rate * i / RATE))) for i, v in enumerate(buf)]

def shape_env(buf, attack=0.01, release=0.08, hold_power=1.0):
    total = len(buf)
    a, r = max(1, secs(attack)), max(1, secs(release))
    out = []
    for i, v in enumerate(buf):
        g = 1.0
        if i < a: g = i / a
        if i > total - r: g = min(g, (total - i) / r)
        out.append(v * g ** hold_power)
    return out

# Five-formant vowels for an adult male (Peterson & Barney, rounded), with bandwidths.
V = {
    "AH": [(730, 90, 1.0), (1090, 110, 0.55), (2440, 160, 0.25), (3400, 250, 0.12), (4200, 300, 0.06)],
    "AE": [(660, 90, 1.0), (1720, 120, 0.5), (2410, 160, 0.26), (3400, 250, 0.12), (4200, 300, 0.06)],
    "EH": [(530, 80, 1.0), (1840, 120, 0.5), (2480, 160, 0.25), (3450, 250, 0.1), (4200, 300, 0.05)],
    "EE": [(270, 60, 1.0), (2290, 120, 0.35), (3010, 180, 0.2), (3500, 250, 0.1), (4300, 300, 0.05)],
    "OH": [(570, 80, 1.0), (840, 90, 0.7), (2410, 160, 0.15), (3300, 250, 0.07), (4100, 300, 0.04)],
    "UH": [(520, 80, 1.0), (1190, 110, 0.45), (2390, 160, 0.16), (3300, 250, 0.08), (4100, 300, 0.04)],
    "OO": [(300, 60, 1.0), (870, 90, 0.4), (2240, 160, 0.1), (3300, 250, 0.05), (4100, 300, 0.03)],
    "ER": [(490, 80, 1.0), (1350, 110, 0.5), (1690, 140, 0.3), (3300, 250, 0.08), (4100, 300, 0.04)],
    "MM": [(250, 60, 1.0), (1000, 200, 0.08), (2200, 250, 0.05), (3300, 300, 0.02), (4000, 300, 0.01)],
}

def voice(t, f0, vowel, to=None, breath=0.1, jitter=0.012, shimmer=0.07, sub=0.0,
          scale=1.0, widen=1.0, tilt=3200):
    """A voiced sound: pulses, their breath, the mouth, and optionally the mouth moving."""
    src = glottal(t, f0, jitter, shimmer, sub)
    src = lowpass(src, tilt)
    noise = highpass(n(len(src)), 500)
    # Breath is loudest while the folds are open; gating the noise by the pulse is
    # what makes it the same breath as the voice rather than hiss laid over it.
    gate = [abs(v) for v in lowpass(src, 60)]
    gm = max(gate) or 1.0
    air = [noise[i] * (0.35 + 0.65 * gate[i] / gm) * breath for i in range(len(src))]
    excite = mix(src, air)
    a = through(excite, V[vowel], scale, widen)
    if to is None:
        return a
    b = through(excite, V[to], scale, widen)
    return morph(a, b, lambda x: min(1.0, x * 1.4))

def breathe(t, vowel, rising=True, level=1.0, scale=1.0):
    """Air alone, coloured by a mouth shape: a breath in, or out."""
    total = secs(t)
    air = through(highpass(n(total), 300), V[vowel], scale, 2.5)
    out = []
    for i, v in enumerate(air):
        x = i / total
        g = math.sin(math.pi * x) ** (0.7 if rising else 1.3)
        out.append(v * g * level)
    return out

def aspirate(t, level=1.0):
    """An 'h': a short puff of unvoiced air before a vowel."""
    return gain(env(highpass(lowpass(n(secs(t)), 3500), 800), attack=0.004, power=1.5), level)

def gurgle(t, rate=22, level=1.0, low=110):
    """Liquid in the throat: low noise chopped into bubbles, with a rough low voice under it."""
    total = secs(t)
    bubbles = lowpass(n(total), 700)
    chop = [bubbles[i] * max(0.0, math.sin(2 * math.pi * rate * i / RATE + random.uniform(-0.3, 0.3))) ** 2
            for i in range(total)]
    throat = through(glottal(t, curve((0, low), (1, low * 0.6)), 0.05, 0.3, 0.5), V["UH"])
    return gain(mix(gain(chop, 1.2), gain(throat, 0.5)), level)

VO = f"{A}/SFX/Voice"

def say(name, buf, attack=0.006, release=0.06, power=1.0):
    save(f"{VO}/{name}.wav", shape_env(buf, attack, release, power))

# ---- the soldier ----------------------------------------------------------------
# Adult male, around 120Hz speaking. Under pain the pitch jumps an octave and falls.

say("human_pain_01", voice(0.24, curve((0, 190), (0.25, 175), (1, 118)), "UH", breath=0.14))
say("human_pain_02", drive(voice(0.28, curve((0, 230), (0.2, 215), (1, 150)), "AH", breath=0.12, sub=0.1), 1.6))
say("human_pain_03", cat(voice(0.26, curve((0, 175), (1, 132)), "MM", breath=0.05, jitter=0.02),
                         gain(breathe(0.16, "UH", rising=False), 0.25)))
say("human_pain_04", cat(voice(0.12, curve((0, 210), (1, 180)), "UH", breath=0.1),
                         blank(0.05),
                         gain(voice(0.2, curve((0, 190), (1, 120)), "UH", breath=0.14), 0.8)))
say("human_pain_05", cat(aspirate(0.04, 0.5), drive(voice(0.16, curve((0, 250), (1, 190)), "AH", breath=0.1), 1.4)))
say("human_pain_06", voice(0.4, curve((0, 150), (0.3, 140), (1, 98)), "OH", breath=0.16, jitter=0.02, shimmer=0.1),
    release=0.15)

# Screams: an octave and more above speech, straining (subharmonics, clipping), the
# mouth opening wider as it goes.
say("human_hurt_01", drive(voice(0.95, curve((0, 300), (0.15, 430), (0.6, 400), (1, 250)), "AH", to="AE",
                                 breath=0.18, jitter=0.03, shimmer=0.12, sub=0.25), 2.2), release=0.25)
say("human_hurt_02", drive(voice(0.7, curve((0, 380), (0.3, 360), (1, 230)), "AE", breath=0.2,
                                 jitter=0.03, shimmer=0.12, sub=0.2), 2.0), release=0.2)
say("human_hurt_03", drive(cat(aspirate(0.05, 0.6),
                               voice(0.75, curve((0, 210), (0.3, 250), (1, 170)), "AH", to="ER",
                                     breath=0.16, jitter=0.035, shimmer=0.14, sub=0.45)), 3.0), release=0.2)

# Deaths: a long fall in pitch that runs out of air rather than stopping.
say("human_death_01", mix(voice(1.1, curve((0, 230), (0.2, 210), (1, 68)), "AH", to="UH",
                                breath=0.18, jitter=0.03, shimmer=0.12, sub=0.2),
                          cat(blank(0.85), gain(breathe(0.45, "UH", rising=False), 0.3))), release=0.3)
say("human_death_02", cat(drive(voice(0.35, curve((0, 320), (1, 260)), "AE", breath=0.15, sub=0.2), 1.8),
                          gain(gurgle(0.45, rate=18), 0.55)), release=0.2)
say("human_death_03", voice(1.0, curve((0, 160), (0.5, 120), (0.85, 60), (1, 42)), "OH", to="UH",
                            breath=0.2, jitter=0.06, shimmer=0.2, sub=0.35), release=0.35)

# A headshot leaves no time for a cry: a choke, or only the air going out.
say("human_headshot_01", cat(gain(env(highpass(n(secs(0.02)), 1500), power=6), 0.4),
                             gurgle(0.26, rate=24, level=0.8, low=95)), release=0.1)
say("human_headshot_02", gain(breathe(0.4, "UH", rising=False), 0.8), release=0.15)

# Finding you: a shout, not a word -- a word from a synthesiser sounds like one.
say("human_alert_01", drive(cat(aspirate(0.06, 0.7),
                                voice(0.34, curve((0, 175), (0.35, 205), (1, 160)), "EH", to="EE",
                                      breath=0.12, sub=0.1)), 1.8), release=0.08)
say("human_alert_02", drive(cat(aspirate(0.05, 0.6),
                                voice(0.3, curve((0, 200), (1, 175)), "AH", breath=0.12, sub=0.15)), 2.0))
say("human_alert_03", drive(voice(0.42, curve((0, 160), (0.2, 190), (1, 150)), "EH", to="ER",
                                  breath=0.14, sub=0.1), 1.7), release=0.1)

# Hunting: breathing hard, and the odd bark of effort.
say("human_hunt_01", cat(gain(breathe(0.45, "AH", True), 0.5), gain(breathe(0.55, "UH", False), 0.7),
                         blank(0.08), gain(breathe(0.4, "AH", True), 0.45), gain(breathe(0.5, "UH", False), 0.65)),
    release=0.1)
say("human_hunt_02", cat(voice(0.14, curve((0, 170), (1, 150)), "UH", breath=0.2), blank(0.12),
                         voice(0.14, curve((0, 175), (1, 150)), "UH", breath=0.2)))
say("human_hunt_03", cat(gain(breathe(0.35, "EE", True), 0.4),
                         voice(0.3, curve((0, 130), (1, 105)), "MM", breath=0.1, jitter=0.03)), release=0.12)
say("human_hunt_04", drive(cat(aspirate(0.05, 0.5), voice(0.26, curve((0, 185), (1, 165)), "UH", to="AH",
                                                          breath=0.14)), 1.5))

# The effort of a swing.
say("human_attack_01", drive(cat(aspirate(0.04, 0.8), voice(0.3, curve((0, 210), (0.3, 230), (1, 160)), "AH",
                                                            breath=0.18, sub=0.3)), 2.6))
say("human_attack_02", drive(voice(0.22, curve((0, 240), (1, 190)), "AH", breath=0.15, sub=0.2), 2.2))
say("human_attack_03", drive(voice(0.26, curve((0, 200), (1, 170)), "MM", to="AH", breath=0.1, sub=0.25), 2.4))

# On the floor, dragging a wrecked leg.
say("human_crawl_01", voice(1.2, curve((0, 128), (0.3, 138), (0.7, 118), (1, 100)), "OH", to="UH",
                            breath=0.22, jitter=0.03, shimmer=0.16), attack=0.08, release=0.3)
say("human_crawl_02", cat(voice(0.3, curve((0, 190), (1, 160)), "MM", breath=0.1, jitter=0.025), blank(0.2),
                          voice(0.35, curve((0, 200), (1, 150)), "MM", breath=0.1, jitter=0.03), blank(0.15),
                          gain(breathe(0.4, "UH", False), 0.5)), release=0.15)

# ---- the creature ---------------------------------------------------------------
# A longer throat: every formant lower and wider (scale ~0.7), the pitch down in the
# range where the ear stops hearing notes and hears a rumble, and the folds skipping
# constantly. Tremolo in the 20-35Hz band is the flutter of a growl.

C = dict(scale=0.7, widen=1.6)

def growl(t, f0, vowel="OH", to=None, sub=0.55, flutter=28, rough=0.4, k=2.6, breath=0.3):
    body = voice(t, f0, vowel, to=to, breath=breath, jitter=0.05, shimmer=0.25, sub=sub, tilt=2200, **C)
    return drive(tremolo(body, flutter, rough), k)

for i, (t, lo, hi, fl, v) in enumerate([(1.5, 62, 78, 26, "OH"), (1.7, 55, 70, 31, "UH"),
                                         (1.3, 70, 90, 24, "OH"), (1.8, 58, 74, 34, "ER")]):
    wobble = curve((0, lo), (0.3, hi), (0.55, lo * 1.05), (0.8, hi * 0.95), (1, lo))
    say(f"creature_hunt_0{i + 1}", growl(t, wobble, v, flutter=fl), attack=0.12, release=0.3)

say("creature_alert_01", growl(1.3, curve((0, 75), (0.3, 135), (0.7, 120), (1, 70)), "AH", to="OH",
                               sub=0.45, rough=0.3, k=3.4, breath=0.4), attack=0.05, release=0.35)
say("creature_alert_02", cat(growl(0.35, curve((0, 110), (1, 150)), "EH", sub=0.4, k=3.0, breath=0.5),
                             growl(0.6, curve((0, 140), (1, 80)), "AH", sub=0.5, k=3.2)), release=0.2)

for i, (t, a, b, v) in enumerate([(0.26, 160, 95, "EH"), (0.22, 180, 110, "AE"), (0.3, 140, 85, "AH"),
                                  (0.2, 200, 130, "EH"), (0.32, 125, 75, "UH")]):
    say(f"creature_pain_0{i + 1}", growl(t, curve((0, a), (1, b)), v, sub=0.4, flutter=32, rough=0.25, k=3.0,
                                         breath=0.45))

say("creature_hurt_01", growl(0.95, curve((0, 120), (0.25, 210), (0.6, 190), (1, 90)), "AH", to="OH",
                              sub=0.35, rough=0.2, k=3.4, breath=0.4), release=0.3)
say("creature_hurt_02", growl(0.75, curve((0, 170), (1, 95)), "AE", to="UH", sub=0.45, k=3.2, breath=0.5),
    release=0.25)

say("creature_death_01", mix(growl(1.3, curve((0, 130), (0.3, 110), (1, 38)), "AH", to="UH", sub=0.6,
                                   flutter=22, k=3.0),
                             cat(blank(0.9), gurgle(0.6, rate=14, level=0.6, low=70))), release=0.4)
say("creature_death_02", cat(growl(0.4, curve((0, 180), (1, 120)), "AE", sub=0.4, k=3.4),
                             gurgle(0.8, rate=12, level=0.9, low=65)), release=0.35)
say("creature_death_03", growl(1.2, curve((0, 90), (0.6, 60), (1, 30)), "OH", to="UH", sub=0.7,
                               flutter=18, rough=0.5, k=2.4, breath=0.5), release=0.45)

say("creature_headshot_01", cat(growl(0.12, curve((0, 150), (1, 110)), "EH", k=3.0),
                                gurgle(0.3, rate=20, level=0.8, low=70)), release=0.1)
say("creature_headshot_02", gurgle(0.45, rate=16, level=1.0, low=60), release=0.15)

for i, (t, a, b) in enumerate([(0.4, 110, 170), (0.35, 130, 95), (0.45, 95, 150)]):
    say(f"creature_attack_0{i + 1}", growl(t, curve((0, a), (0.5, max(a, b) * 1.1), (1, b)), "AH",
                                           sub=0.45, rough=0.25, k=3.6, breath=0.45), release=0.12)

say("creature_crawl_01", cat(gain(breathe(0.4, "OH", True, scale=0.7), 0.8),
                             growl(0.7, curve((0, 70), (1, 58)), "UH", sub=0.6, k=2.2)), release=0.2)
say("creature_crawl_02", mix(growl(1.1, curve((0, 65), (0.5, 80), (1, 55)), "OH", sub=0.65, flutter=20, k=2.0),
                             gain(gurgle(1.1, rate=9, level=0.5, low=55), 0.6)), attack=0.1, release=0.3)

random.setstate(_voice_stream)
