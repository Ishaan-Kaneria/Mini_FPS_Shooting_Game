#!/usr/bin/env python3
"""Draws the button-prompt glyphs into Assets/UI/Icons/Glyphs/.

Gamepad buttons (Xbox, PlayStation and a generic positional set), mouse buttons and
touch gestures, drawn on the same 24-unit grid at the same 2-unit stroke as the rest of
the icon set (Tools/icons), so a prompt sits in a sentence beside an icon without looking
borrowed. Nothing here is a manufacturer's artwork: an Xbox "A" is a ring with the letter
in it, a PlayStation cross is a ring with two strokes in it, in the game's own line style.

Letters are set in Barlow Condensed SemiBold (Assets/UI/Fonts), the face the HUD's
numbers use. White on transparent: the colour is the text's or the Image's.

Needs librsvg through GObject introspection (python3-gi, gir1.2-rsvg-2.0) and Pillow.
Deterministic: rerunning writes identical bytes.
"""
import io
import pathlib
import re

import gi

gi.require_version("Rsvg", "2.0")
from gi.repository import Rsvg  # noqa: E402
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

SIZE = 64
UNIT = SIZE / 24.0
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets" / "UI" / "Icons" / "Glyphs"
FONT = ROOT / "Assets" / "UI" / "Fonts" / "BarlowCondensed-SemiBold.ttf"
TABLER = ROOT / "Tools" / "icons" / "tabler"

HEAD = ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 24 24" '
        'fill="none" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
        % (SIZE, SIZE))

RING = '<circle cx="12" cy="12" r="9.5"/>'
BUMPER = '<rect x="2.5" y="6.5" width="19" height="11" rx="3.5"/>'
TRIGGER = '<path d="M5 20.5v-8.5a7 7 0 0 1 7 -7a7 7 0 0 1 7 7v8.5z"/>'
PILL = '<rect x="3" y="7" width="18" height="10" rx="5"/>'
TICKS = ('<path d="M12 2.5v1.8"/><path d="M12 19.7v1.8"/>'
         '<path d="M2.5 12h1.8"/><path d="M19.7 12h1.8"/>')
MOUSE = ('<rect x="6.5" y="2.5" width="11" height="19" rx="5.5"/>'
         '<path d="M12 2.5v7.5"/><path d="M6.5 10h11"/>')


def svg(body: str) -> Image.Image:
    pixbuf = Rsvg.Handle.new_from_data((HEAD + body + "</svg>").encode()).get_pixbuf()
    ok, data = pixbuf.save_to_bufferv("png", [], [])
    return Image.open(io.BytesIO(data)).convert("RGBA")


def tabler(name: str) -> Image.Image:
    text = (TABLER / f"{name}.svg").read_text()
    text = re.sub(r"<!--.*?-->", "", text, flags=re.S).replace("currentColor", "#FFFFFF")
    text = re.sub(r'width="24"', f'width="{SIZE}"', text, count=1)
    text = re.sub(r'height="24"', f'height="{SIZE}"', text, count=1)
    pixbuf = Rsvg.Handle.new_from_data(text.encode()).get_pixbuf()
    ok, data = pixbuf.save_to_bufferv("png", [], [])
    return Image.open(io.BytesIO(data)).convert("RGBA")


def lettered(body: str, text: str, size_units: float, dy_units: float = 0.0) -> Image.Image:
    """A shape with a label set in its middle, optically centred on the cap height."""
    img = svg(body)
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(str(FONT), int(round(size_units * UNIT)))
    # Centre on the ink, not on the font's line box, so single letters sit dead centre.
    left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
    x = SIZE / 2 - (left + right) / 2
    y = SIZE / 2 - (top + bottom) / 2 + dy_units * UNIT
    draw.text((x, y), text, font=font, fill=(255, 255, 255, 255))
    return img


def generic_face(which: str) -> Image.Image:
    """Four dots in the face-button diamond, the pressed one filled: no letters to get wrong."""
    spots = {"north": (12, 5.2), "east": (18.8, 12), "south": (12, 18.8), "west": (5.2, 12)}
    body = ""
    for name, (x, y) in spots.items():
        fill = ' fill="#FFFFFF"' if name == which else ""
        body += f'<circle cx="{x}" cy="{y}" r="2.9"{fill}/>'
    return svg(body)


def glyphs():
    g = {}
    # Xbox and anything calling itself one.
    for letter in "ABXY":
        g[f"xbox_{letter.lower()}"] = lettered(RING, letter, 12)
    g["xbox_lb"] = lettered(BUMPER, "LB", 8.5)
    g["xbox_rb"] = lettered(BUMPER, "RB", 8.5)
    g["xbox_lt"] = lettered(TRIGGER, "LT", 8, 1.6)
    g["xbox_rt"] = lettered(TRIGGER, "RT", 8, 1.6)
    g["xbox_ls"] = lettered(RING, "LS", 8.5)
    g["xbox_rs"] = lettered(RING, "RS", 8.5)
    g["xbox_menu"] = svg(RING + '<path d="M8 9h8"/><path d="M8 12h8"/><path d="M8 15h8"/>')
    g["xbox_view"] = svg(RING + '<rect x="7" y="7.5" width="6.5" height="5.5" rx="1"/>'
                                '<path d="M10.5 16h5.5a1 1 0 0 0 1 -1v-4"/>')

    # PlayStation: the four shapes, drawn in the same line as everything else.
    g["ps_cross"] = svg(RING + '<path d="M8.6 8.6l6.8 6.8"/><path d="M15.4 8.6l-6.8 6.8"/>')
    g["ps_circle"] = svg(RING + '<circle cx="12" cy="12" r="4.2"/>')
    g["ps_square"] = svg(RING + '<rect x="8.2" y="8.2" width="7.6" height="7.6"/>')
    g["ps_triangle"] = svg(RING + '<path d="M12 7.6l4.6 7.9h-9.2z"/>')
    g["ps_l1"] = lettered(BUMPER, "L1", 8.5)
    g["ps_r1"] = lettered(BUMPER, "R1", 8.5)
    g["ps_l2"] = lettered(TRIGGER, "L2", 8, 1.6)
    g["ps_r2"] = lettered(TRIGGER, "R2", 8, 1.6)
    g["ps_l3"] = lettered(RING, "L3", 8.5)
    g["ps_r3"] = lettered(RING, "R3", 8.5)
    g["ps_options"] = svg(PILL + '<path d="M8.5 10.5h7"/><path d="M8.5 13.5h7"/>')

    # A pad that is neither: face buttons by position, shoulders by side.
    for side in ("north", "east", "south", "west"):
        g[f"pad_{side}"] = generic_face(side)
    g["pad_l1"] = lettered(BUMPER, "L", 9.5)
    g["pad_r1"] = lettered(BUMPER, "R", 9.5)
    g["pad_l2"] = lettered(TRIGGER, "L2", 8, 1.6)
    g["pad_r2"] = lettered(TRIGGER, "R2", 8, 1.6)
    g["pad_start"] = svg(PILL + '<path d="M8.5 10.5h7"/><path d="M8.5 13.5h7"/>')

    # Shared by every pad.
    g["stick_l"] = lettered(RING + TICKS, "L", 10)
    g["stick_r"] = lettered(RING + TICKS, "R", 10)
    g["dpad"] = svg('<path d="M9.5 3h5v6.5h6.5v5h-6.5v6.5h-5v-6.5h-6.5v-5h6.5z"/>')

    # Mouse.
    g["mouse_left"] = svg(MOUSE + '<path d="M12 2.5v7.5h-5.5v-2a5.5 5.5 0 0 1 5.5 -5.5z" fill="#FFFFFF"/>')
    g["mouse_right"] = svg(MOUSE + '<path d="M12 2.5v7.5h5.5v-2a5.5 5.5 0 0 0 -5.5 -5.5z" fill="#FFFFFF"/>')
    g["mouse_middle"] = svg('<rect x="6.5" y="2.5" width="11" height="19" rx="5.5"/>'
                            '<path d="M12 5.5v4" stroke-width="3"/>')
    g["mouse_move"] = svg('<rect x="8" y="5" width="8" height="14" rx="4"/>'
                          '<path d="M4.5 12h-2.5l1.5 -1.5"/><path d="M2 12l1.5 1.5"/>'
                          '<path d="M19.5 12h2.5l-1.5 -1.5"/><path d="M22 12l-1.5 1.5"/>')

    # Touch.
    g["touch_tap"] = tabler("hand-finger")
    g["touch_drag"] = tabler("hand-move")
    return g


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    made = glyphs()
    for name, img in sorted(made.items()):
        img.save(OUT / f"{name}.png", optimize=False)
    print(f"{len(made)} glyphs -> {OUT}")


if __name__ == "__main__":
    main()
