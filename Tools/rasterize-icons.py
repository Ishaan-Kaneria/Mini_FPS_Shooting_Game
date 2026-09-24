#!/usr/bin/env python3
"""Rasterises the UI icon set into Assets/UI/Icons/.

One set, one style: 24-unit line icons at a 2-unit stroke with round caps and joins.
icons/tabler/ is Tabler Icons (MIT, see icons/tabler/LICENSE), copied unmodified; icons/custom/ holds the
game-specific ones (grenade, magazine, medkit, rifle, skull) drawn on the same grid with
the same stroke, so they cannot be told apart from the rest.

The PNGs are white on transparent: the colour is the Image's, which keeps the theme in
charge of what colour an icon is -- the same rule the detail textures follow. Rendered at
64px so a 24-32px icon on a 1080p canvas is downsampled through mipmaps rather than
upscaled.

Needs librsvg through GObject introspection (python3-gi, gir1.2-rsvg-2.0),
which a stock Ubuntu desktop already has. Deterministic: rerunning writes identical bytes.
"""
import pathlib, re
import gi
gi.require_version("Rsvg", "2.0")
from gi.repository import Rsvg

SIZE = 64
HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE.parent / "Assets" / "UI" / "Icons"


def render(src: pathlib.Path, dst: pathlib.Path):
    svg = src.read_text()
    svg = re.sub(r"<!--.*?-->", "", svg, flags=re.S).replace("currentColor", "#FFFFFF")
    # Render at SIZE by restating the intrinsic size; the viewBox keeps the geometry.
    svg = re.sub(r'width="24"', f'width="{SIZE}"', svg, count=1)
    svg = re.sub(r'height="24"', f'height="{SIZE}"', svg, count=1)
    pixbuf = Rsvg.Handle.new_from_data(svg.encode()).get_pixbuf()
    pixbuf.savev(str(dst), "png", [], [])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    names = {}
    for folder in ("tabler", "custom"):  # custom last, so it overrides a same-named Tabler icon
        for src in sorted((HERE / "icons" / folder).glob("*.svg")):
            names[src.stem] = src
    for name, src in sorted(names.items()):
        render(src, OUT / f"{name}.png")
    print(f"{len(names)} icons -> {OUT}")


if __name__ == "__main__":
    main()
