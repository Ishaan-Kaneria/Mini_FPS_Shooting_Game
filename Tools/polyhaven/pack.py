"""Pack downloaded Poly Haven maps into what URP Lit / Terrain Lit read, and stage them for the project.

URP's mask map (and Lit's metallic-gloss + occlusion, which read the same channels) wants:
    R = metallic, G = ambient occlusion, B = height (terrain height blend), A = smoothness.
Poly Haven ships ARM (R = AO, G = roughness, B = metal) plus a separate displacement map.
Plants ship a separate alpha; it goes into the albedo's alpha for alpha clipping.

Usage: python3 -I phpack.py <dl dir> <stage dir>
"""
import json, os, shutil, sys
from PIL import Image

DL, STAGE = sys.argv[1], sys.argv[2]
man = json.load(open(os.path.join(DL, "manifest.json")))


def L(path, size=None):
    im = Image.open(path)
    im = im.convert("L") if im.mode not in ("L", "I;16", "I") else im.convert("L")
    return im.resize(size, Image.BILINEAR) if size and im.size != size else im


def mask_from(arm=None, rough=None, ao=None, disp=None, size=None):
    if arm:
        a = Image.open(arm).convert("RGB")
        size = size or a.size
        a = a.resize(size, Image.BILINEAR) if a.size != size else a
        ao_c, rough_c, metal_c = a.split()
    else:
        ref = rough or ao
        size = size or Image.open(ref).size
        rough_c = L(rough, size) if rough else Image.new("L", size, 200)
        ao_c = L(ao, size) if ao else Image.new("L", size, 255)
        metal_c = Image.new("L", size, 0)
    height_c = L(disp, size) if disp else Image.new("L", size, 128)
    smooth_c = rough_c.point(lambda v: 255 - v)
    return Image.merge("RGBA", (metal_c, ao_c, height_c, smooth_c))


def normal_jpg(src, dst):
    """Poly Haven normals are 16-bit PNG; Unity re-encodes them to BC5 anyway, so an 8-bit q95 JPG loses nothing visible."""
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im = Image.open(src)
    if im.mode not in ("RGB", "RGBA"):
        im = im.convert("RGB") if im.mode != "I;16" else im.point(lambda v: v / 257).convert("RGB")
    im.convert("RGB").save(dst, quality=95, subsampling=0)


SKIP = {"sand_rocks_small_01", "searsia_burchellii", "othonna_cerarioides"}   # 26-57 MB photoscan meshes


def stage(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copyfile(src, dst)


out = {"models": {}, "textures": {}, "hdri": None}

# ---- textures (2k): diffuse, normal, packed mask ----
for tid, maps in man["textures"].items():
    d = os.path.join(STAGE, "Textures", tid)
    get = lambda k: os.path.join(DL, maps[k]) if k in maps and os.path.exists(os.path.join(DL, maps[k])) else None
    diff, nor = get("diff"), get("nor_gl")
    if not diff or not nor:
        print("skip texture (missing maps):", tid); continue
    stage(diff, os.path.join(d, f"{tid}_diff.jpg"))
    normal_jpg(nor, os.path.join(d, f"{tid}_nor.jpg"))
    size = Image.open(diff).size
    half = (max(512, size[0] // 2), max(512, size[1] // 2))
    mask_from(get("arm"), get("rough"), get("ao"), get("disp"), half).save(os.path.join(d, f"{tid}_mask.png"), optimize=True)
    out["textures"][tid] = True

# ---- models (1k): fbx + one albedo/normal/mask per texture set ----
for mid, sets in man["models"].items():
    if mid in SKIP: continue
    fbx = os.path.join(DL, "Models", mid, f"{mid}.fbx")
    if not os.path.exists(fbx):
        print("skip model (no fbx):", mid); continue
    d = os.path.join(STAGE, "Models", mid)
    stage(fbx, os.path.join(d, f"{mid}.fbx"))
    staged = {}
    for prefix, maps in sets.items():
        get = lambda k: os.path.join(DL, maps[k]) if k in maps and os.path.exists(os.path.join(DL, maps[k])) else None
        diff = get("diff")
        if not diff:
            print("  no diffuse for", mid, prefix); continue
        alpha = get("alpha")
        if alpha:
            rgb = Image.open(diff).convert("RGB")
            rgba = Image.merge("RGBA", (*rgb.split(), L(alpha, rgb.size)))
            rgba.save(os.path.join(d, f"{prefix}_diff.png"))
        else:
            stage(diff, os.path.join(d, f"{prefix}_diff.jpg"))
        if get("nor_gl"): normal_jpg(get("nor_gl"), os.path.join(d, f"{prefix}_nor.jpg"))
        if get("arm") or get("rough"):
            mask_from(get("arm"), get("rough"), None, None, Image.open(diff).size).save(os.path.join(d, f"{prefix}_mask.png"))
        staged[prefix] = {"alpha": bool(alpha)}
    out["models"][mid] = staged

# ---- sky ----
for f in os.listdir(os.path.join(DL, "HDRI")):
    if f.endswith(".hdr"):
        stage(os.path.join(DL, "HDRI", f), os.path.join(STAGE, "HDRI", f)); out["hdri"] = f

json.dump(out, open(os.path.join(STAGE, "manifest.json"), "w"), indent=1)
open(os.path.join(STAGE, "LICENSE.txt"), "w").write(
    "Every model, texture and HDRI under this folder is from Poly Haven (https://polyhaven.com) and is\n"
    "released under CC0 1.0 (public domain). No attribution is required; this note records the source.\n"
    "Maps were repacked for URP by Tools/polyhaven (mask = metal, AO, height, smoothness).\n")
print("textures", len(out["textures"]), "models", len(out["models"]), "hdri", out["hdri"])
