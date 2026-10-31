"""Put the models' separate metalness maps into their staged masks (R channel), for the models packed before pack.py read
them (2026-10-31: the brass pieces were packed as metal 0 and drew as glossy plastic).

For every staged model, its FBX's texture list from the Poly Haven API; each "<set>_metal_1k" map found is downloaded into
<download dir> and written, resized, into Assets/ThirdParty/PolyHaven/Models/<id>/<set>_mask.png as R. Masks of models
without one are left alone. Requests are spaced.

Usage: python3 -I Tools/polyhaven/metal.py <download dir> <staged PolyHaven dir>
"""
import json, os, sys, time, urllib.request
from PIL import Image

DL, STAGE = sys.argv[1], sys.argv[2]
UA = {"User-Agent": "FPSKit-AssetFetch/1.0 (CC0 game assets)"}
def fetch(url, timeout=60):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout) as r:
        return r.read()

man = json.load(open(os.path.join(STAGE, "manifest.json")))
done, checked = [], 0
for mid in sorted(man["models"]):
    checked += 1
    try:
        info = json.loads(fetch("https://api.polyhaven.com/files/" + mid))
    except Exception as e:
        print("api failed:", mid, e); continue
    include = info.get("fbx", {}).get("1k", {}).get("fbx", {}).get("include", {})
    for name in include:
        stem = os.path.basename(name).rsplit(".", 1)[0]
        if not stem.endswith("_metal_1k"):
            continue
        prefix = stem[: -len("_metal_1k")]
        mask_path = os.path.join(STAGE, "Models", mid, f"{prefix}_mask.png")
        if not os.path.exists(mask_path):
            print("no staged mask for", mid, prefix); continue
        local = os.path.join(DL, mid, stem + ".jpg")
        os.makedirs(os.path.dirname(local), exist_ok=True)
        if not os.path.exists(local):
            open(local, "wb").write(fetch(f"https://dl.polyhaven.org/file/ph-assets/Models/jpg/1k/{mid}/{stem}.jpg"))
            time.sleep(0.5)
        mask = Image.open(mask_path).convert("RGBA")
        metal = Image.open(local).convert("L").resize(mask.size, Image.BILINEAR)
        r, g, b, a = mask.split()
        Image.merge("RGBA", (metal, g, b, a)).save(mask_path, optimize=True)
        mean = sum(metal.getdata()) / (metal.size[0] * metal.size[1]) / 255.0
        done.append(f"{mid}/{prefix} (mean metal {mean:.2f})")
    time.sleep(0.3)
print(f"checked {checked} models; metal written into {len(done)} masks:")
for d in done: print("  " + d)
