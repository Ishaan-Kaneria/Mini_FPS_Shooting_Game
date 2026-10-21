"""Download CC0 Poly Haven assets: models (fbx 1k + jpg/png maps), textures (2k), one HDRI.
Usage: python3 -I fetch.py <outdir>               everything below
       python3 -I fetch.py <outdir> <id> [<id>...] just those textures (no models, no sky); pack.py merges them in"""
import json, os, sys, urllib.request, concurrent.futures as cf
UA = {"User-Agent": "FPSKit-AssetFetch/1.0 (CC0 game assets)"}
def _open(u, t): return urllib.request.urlopen(urllib.request.Request(u, headers=UA), timeout=t)

OUT = sys.argv[1]
API = "https://api.polyhaven.com/files/"

MODELS = """wicker_basket_01 wicker_basket_02 wooden_crate_01 wooden_crate_02 plastic_crate_01 plastic_crate_02 plastic_crate_03
brass_pot_01 brass_pot_02 brass_vase_01 brass_vase_02 brass_vase_03 ceramic_vase_01 ceramic_vase_02 ceramic_vase_03 ceramic_vase_04
antique_ceramic_vase_01 ceramic_pot jug_01 pot_enamel_01 tea_set_01 wooden_bowl_01 carved_wooden_plate bananas food_pomegranate_01
food_lime_01 food_apple_01 yellow_onion sweet_potato brass_diya_lantern Lantern_01 vintage_oil_lamp wooden_display_shelves_01
painted_wooden_table folding_wooden_stool plastic_monobloc_chair_01 wooden_stool_01 wooden_stool_02 outdoor_table_chair_set_01
CashRegister_01 steel_frame_shelves_01 cardboard_box_01 brass_goblets wooden_barrels_01
exterior_aircon_unit modular_electricity_poles street_lamp_01 street_lamp_02 utility_box_01 utility_box_02 metal_trash_can trashbag
old_tyre rusted_wheel_rim_01 concrete_road_barrier concrete_road_barrier_02 water_manhole_cover security_light Barrel_01 barrel_03
plastic_jerrycan propane_tank small_lpg_tank portable_generator cement_bag hand_truck wooden_ladder rollershutter_door
rollershutter_window_01 rollershutter_window_02 covered_car modular_chainlink_fence modular_street_seating barrel_stove
plastic_bottle_gallon security_camera_01 painted_wooden_bench wooden_picnic_table
namaqualand_boulder_02 namaqualand_boulder_03 namaqualand_boulder_04 namaqualand_boulder_05 namaqualand_boulder_06
namaqualand_rocks_01 namaqualand_stones_01 namaqualand_cliff_01 namaqualand_cliff_02 dead_quiver_trunk dead_quiver_branch_01
searsia_lucida searsia_burchellii wild_rooibos_bush cheiridopsis_succulent didelta_spinosa dry_branches_medium_01 sand_rocks_small_01
dead_tree_trunk_02 othonna_cerarioides
ammo_box old_military_crate wooden_military_crate portable_searchlight
painted_wooden_cabinet painted_wooden_shelves WoodenChair_01 WoodenTable_01 old_bed_frame Television_01 ceiling_fan sofa_02
throw_pillows_01 wooden_bookshelf_worn vintage_cabinet_01 painted_wooden_chair_01 Shelf_01 steel_frame_shelves_02 vintage_suitcase""".split()

TEXTURES = """gravelly_sand dense_sand dry_ground_rocks mud_cracked_dry_03 rock_ground farm_soil
asphalt_02 worn_asphalt square_concrete_pavers concrete_pavers sandstone_blocks_08 pavement_02
white_plaster_rough_01 white_plaster_rough_02 white_stucco beige_wall_002 painted_plaster_wall yellow_plaster worn_plaster_wall
damaged_plaster plastered_wall_02 concrete_block_wall clay_plaster patterned_clay_plaster white_sandstone_bricks castle_wall_varriation
rubble rusty_corrugated_iron corrugated_iron_02 painted_metal_shutter rusted_shutter wood_shutter weathered_planks rough_pine_door
wooden_gate marble_mosaic_tiles patterned_terracotta_tiling old_mosaic_floor terrazzo_tiles concrete_wall_008
brick_wall_003 brown_brick_02 seaworn_sandstone_brick plaster_brick_pattern sandstone_blocks_05 concrete_brick_wall_001""".split()

HDRI = "qwantani_late_afternoon_puresky"

def get(url, path):
    last = None
    for attempt in range(6):
        try:
            return _get(url, path)
        except Exception as e:
            last = e
            if os.path.exists(path + ".part"): os.remove(path + ".part")
    raise last

def _get(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0: return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    with _open(url, 25) as r, open(tmp, "wb") as f:
        while True:
            b = r.read(1 << 16)
            if not b: break
            f.write(b)
    os.replace(tmp, path)
    return path

def api(id):
    with _open(API + id, 60) as r:
        return json.load(r)

jobs = []
manifest = {"models": {}, "textures": {}, "hdri": HDRI}

def plan_model(id):
    d = api(id)
    fbx = d["fbx"]["1k"]["fbx"]
    files = [(fbx["url"], f"{OUT}/Models/{id}/{id}.fbx")]
    sets = {}
    for name in fbx.get("include", {}):
        base = os.path.basename(name)            # e.g. x_diff_1k.exr
        stem = base.rsplit(".", 1)[0]
        for kind in ("diff", "nor_gl", "arm", "rough", "alpha", "metal"):
            tag = "_" + kind + "_1k"
            if stem.endswith(tag):
                prefix = stem[: -len(tag)]
                ext = "png" if kind in ("nor_gl", "alpha") else "jpg"
                url = f"https://dl.polyhaven.org/file/ph-assets/Models/{ext}/1k/{id}/{stem}.{ext}"
                local = f"{OUT}/Models/{id}/textures/{stem}.{ext}"
                files.append((url, local))
                sets.setdefault(prefix, {})[kind] = os.path.relpath(local, OUT)
    return id, files, sets

def plan_texture(id):
    d = api(id)
    files = []; maps = {}
    for key, kind, ext in (("Diffuse", "diff", "jpg"), ("nor_gl", "nor_gl", "png"), ("arm", "arm", "jpg"),
                           ("Displacement", "disp", "jpg"), ("Rough", "rough", "jpg"), ("AO", "ao", "jpg")):
        if key in d and "2k" in d[key] and ext in d[key]["2k"]:
            local = f"{OUT}/Textures/{id}/{id}_{kind}_2k.{ext}"
            files.append((d[key]["2k"][ext]["url"], local)); maps[kind] = os.path.relpath(local, OUT)
    return id, files, maps

ONLY = sys.argv[2:]
if ONLY:
    MODELS, TEXTURES = [], ONLY
    manifest["hdri"] = None
with cf.ThreadPoolExecutor(8) as ex:
    for id, files, sets in ex.map(plan_model, MODELS):
        manifest["models"][id] = sets; jobs += files
    for id, files, maps in ex.map(plan_texture, TEXTURES):
        manifest["textures"][id] = maps; jobs += files
if not ONLY:
    h = api(HDRI)["hdri"]["4k"]["hdr"]["url"]
    jobs.append((h, f"{OUT}/HDRI/{HDRI}_4k.hdr"))

json.dump(manifest, open(f"{OUT}/manifest.json", "w"), indent=1)
print("files to fetch:", len(jobs))
fails = []
with cf.ThreadPoolExecutor(8) as ex:
    futs = {ex.submit(get, u, p): (u, p) for u, p in jobs}
    for f in cf.as_completed(futs):
        try: f.result()
        except Exception as e: fails.append((futs[f], str(e)))
print("failed:", len(fails))
for x in fails[:20]: print(x)
