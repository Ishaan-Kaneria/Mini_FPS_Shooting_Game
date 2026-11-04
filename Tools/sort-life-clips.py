#!/usr/bin/env python3
"""Sorts the townspeople's Mixamo downloads (Tools/mixamo-download-life.js) into the project.

    python3 Tools/sort-life-clips.py [~/Downloads] [manifest name]

The browser saves each clip as "Y Bot@<Name>.fbx", adding (1) to a repeat, so only the download order says which is which: the
files newer than the previous manifest, oldest first, are the manifest's clips in order. CHOICE names each activity's clip by the
manifest's description, because the script's searches also found wrong ones ("Overhead Bashing Swing" for washing up). An activity
left out of CHOICE has no clip and the person simply stands. Copied to Assets/Animations/Mixamo/Life/<Group>.fbx, the name
FPSKitTownHouseholds.AddLifeStates looks for. Rebuild the desert afterwards so the animator gains the states.
"""
import json, os, shutil, sys

CHOICE = {
    "Life_SitChair": "Sitting With Breathing Idle",
    "Life_SitSofa": "Sitting And Talking To Another Person",
    "Life_SitFloor": "Sitting On The Floor",
    "Life_Sleep": "Sleeping Deeply",
    "Life_Cook": "Standing Idle Looking Behind Over Right Side",   # stand-in: no cooking clip found
    "Life_Wash": "Quickly Looking To The Right",                   # stand-in: no washing clip found
    "Life_Clean": "Standing Around Bored Idle",                    # stand-in: no cleaning clip found
    "Life_Phone": "Female Standing Talking On Phone",
    "Life_Talk": "Talking Finding Something Funny",
    "Life_Browse": "Picking Up An Object With One Hand",
    "Life_Pay": "Giving Thumbs Up While Standing",
    "Life_Keeper": "Standing Around Bored Idle",
    "Life_Cower": "Being Terrified While Standing",
    "Life_Window": "Leaning Square With Shoulders Against A Wall",
    "Life_SitBed": "Sitting With Breathing Idle",
    "Life_Drive": "Male Driving A Car",
    # Life_Pray left out: Mixamo's "Praying" is not salah (mocap it, after approval).
}

src = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/Downloads")
manifests = sorted((f for f in os.listdir(src) if f.startswith("mixamo-manifest") and f.endswith(".json")),
                   key=lambda f: os.path.getmtime(os.path.join(src, f)))
name = sys.argv[2] if len(sys.argv) > 2 else manifests[-1]
clips = json.load(open(os.path.join(src, name)))["clips"]
at = os.path.getmtime(os.path.join(src, name))
before = [os.path.getmtime(os.path.join(src, m)) for m in manifests if os.path.getmtime(os.path.join(src, m)) < at]
since = max(before) if before else 0
files = sorted((f for f in os.listdir(src) if f.lower().endswith(".fbx") and "@" in f and os.path.getmtime(os.path.join(src, f)) > since),
               key=lambda f: os.path.getmtime(os.path.join(src, f)))
if len(files) != len(clips):
    sys.exit(f"{len(clips)} clips in {name} but {len(files)} files since the manifest before it: pair them by hand")
for clip, f in zip(clips, files):
    if clip["name"] not in f:
        sys.exit(f'order broken: {clip["name"]} against {f}')
by = {c["description"]: f for c, f in zip(clips, files)}
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
dst = os.path.join(root, "Assets", "Animations", "Mixamo", "Life")
os.makedirs(dst, exist_ok=True)
for group, description in sorted(CHOICE.items()):
    shutil.copyfile(os.path.join(src, by[description]), os.path.join(dst, group + ".fbx"))
    print(f"{group}: {by[description]}")
print(f"{len(CHOICE)} clips in {dst}")
