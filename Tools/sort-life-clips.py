#!/usr/bin/env python3
"""Sorts the townspeople's Mixamo downloads (Tools/mixamo-download-life.js) into the project.

    python3 Tools/sort-life-clips.py [~/Downloads]

Each download is named <Group>__<Clip>.fbx with Group the activity (Life_SitChair, Life_Cook, ...). The first file of each
group (or the one named in PICK below) is copied to Assets/Animations/Mixamo/Life/<Group>.fbx, which is the name the
civilians' animator looks for (FPSKitTownHouseholds.AddLifeStates). Unity imports it as Humanoid, looping, in place
(FPSKitMixamoImport: "Life_"). Rebuild the desert afterwards so the animator gains the states.
"""
import os, shutil, sys

PICK = {}   # e.g. {"Life_Cook": "Life_Cook__Stirring.fbx"} to choose another result than the first

src = os.path.expanduser(sys.argv[1] if len(sys.argv) > 1 else "~/Downloads")
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
dst = os.path.join(root, "Assets", "Animations", "Mixamo", "Life")
os.makedirs(dst, exist_ok=True)
groups = {}
for name in sorted(os.listdir(src)):
    if name.startswith("Life_") and "__" in name and name.lower().endswith(".fbx"):
        groups.setdefault(name.split("__", 1)[0], []).append(name)
if not groups:
    sys.exit(f"no Life_*__*.fbx files in {src}")
for group, files in sorted(groups.items()):
    pick = PICK.get(group, files[0])
    shutil.copyfile(os.path.join(src, pick), os.path.join(dst, group + ".fbx"))
    print(f"{group}: {pick}" + (f"  (also: {', '.join(f for f in files if f != pick)})" if len(files) > 1 else ""))
print(f"{len(groups)} clips in {dst}")
