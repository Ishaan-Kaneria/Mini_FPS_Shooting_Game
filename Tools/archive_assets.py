#!/usr/bin/env python3
"""Move unused assets out of Assets/ into ../MiniFPS_AssetArchive/ (same folder structure), never deleting.

Reads Logs/AssetAudit/unused.txt (written by Tools > MiniFPS > Assets > Audit Unused). Skipped on purpose:
  - Assets/Fairground/ThirdParty (CC0 textures planned for the dressing step)
  - shader includes (.hlsl .cginc .compute ...): the dependency analysis cannot see #include
  - Assets/InputSystem_Actions.inputactions (the Input System's project-wide actions asset)
Also moved whole: Assets/Flooded_Grounds/PostProcessing (the old post-processing stack; the look is in Docs/FAIRGROUND_LOOK_RECIPE.md).
Each moved file takes its .meta with it, so GUIDs survive a restore. Run with --apply to move; default is a dry run.
Restore: MiniFPS_AssetArchive/restore.sh
"""
import os, shutil, sys

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
ARCHIVE = os.path.abspath(os.path.join(PROJECT, '..', 'MiniFPS_AssetArchive'))
SKIP_EXT = {'.hlsl', '.cginc', '.hlslinc', '.glslinc', '.compute', '.shadersubgraph', '.shadergraph'}
WHOLE_FOLDERS = ['Assets/Flooded_Grounds/PostProcessing']
SKIP_FILES = {'Assets/InputSystem_Actions.inputactions'}

def plan():
    moves = []
    for line in open(os.path.join(PROJECT, 'Logs/AssetAudit/unused.txt')):
        _, p = line.rstrip('\n').split('\t')
        if p.startswith('Assets/Fairground/ThirdParty/') or p in SKIP_FILES: continue
        if os.path.splitext(p)[1].lower() in SKIP_EXT: continue
        if any(p.startswith(w + '/') for w in WHOLE_FOLDERS): continue
        moves.append(p)
    for w in WHOLE_FOLDERS:
        for dp, dn, fn in os.walk(os.path.join(PROJECT, w)):
            for f in fn:
                if not f.endswith('.meta'):
                    moves.append(os.path.relpath(os.path.join(dp, f), PROJECT).replace(os.sep, '/'))
    return sorted(set(moves))

def move_one(rel):
    done = []
    for r in (rel, rel + '.meta'):
        src = os.path.join(PROJECT, r)
        if not os.path.exists(src): continue
        dst = os.path.join(ARCHIVE, r)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.move(src, dst)
        done.append(r)
    return done

def main():
    apply = '--apply' in sys.argv
    moves = plan()
    size = sum(os.path.getsize(os.path.join(PROJECT, p)) for p in moves if os.path.exists(os.path.join(PROJECT, p)))
    print(f"{'MOVING' if apply else 'DRY RUN:'} {len(moves)} files, {size / 1048576:.0f} MB -> {ARCHIVE}")
    if not apply: return
    moved = []
    for rel in moves: moved += move_one(rel)
    # folder .meta files of the whole-moved folders, then folders left empty (and their .meta)
    for w in WHOLE_FOLDERS:
        m = os.path.join(PROJECT, w + '.meta')
        if os.path.exists(m):
            os.makedirs(os.path.dirname(os.path.join(ARCHIVE, w)), exist_ok=True)
            shutil.move(m, os.path.join(ARCHIVE, w + '.meta')); moved.append(w + '.meta')
    removed = []
    for dp, dn, fn in sorted(os.walk(os.path.join(PROJECT, 'Assets'), topdown=False), key=lambda t: -len(t[0])):
        if dp == os.path.join(PROJECT, 'Assets'): continue
        left = [f for f in os.listdir(dp)]
        if not left:
            os.rmdir(dp)
            rel = os.path.relpath(dp, PROJECT).replace(os.sep, '/')
            removed.append(rel)
            meta = dp + '.meta'
            if os.path.exists(meta):                      # the empty folder's own meta: kept in the archive, not deleted
                dst = os.path.join(ARCHIVE, rel + '.meta'); os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.move(meta, dst); moved.append(rel + '.meta')
    with open(os.path.join(ARCHIVE, 'MOVED.txt'), 'w') as f:
        f.write('# files moved out of Assets/ (relative to the project root); folders removed because they became empty are listed last\n')
        for r in moved: f.write(r + '\n')
        for r in removed: f.write('# emptied folder: ' + r + '\n')
    with open(os.path.join(ARCHIVE, 'restore.sh'), 'w') as f:
        f.write('#!/usr/bin/env bash\n# Puts everything in MOVED.txt back into the project. Close Unity first, then run: bash restore.sh /path/to/Mini_FPS_Shooting_Game\n')
        f.write('set -e\nPROJECT="${1:?usage: restore.sh <project root>}"\nHERE="$(cd "$(dirname "$0")" && pwd)"\n')
        f.write('grep -v "^#" "$HERE/MOVED.txt" | while IFS= read -r rel; do\n  [ -e "$HERE/$rel" ] || continue\n  mkdir -p "$PROJECT/$(dirname "$rel")"\n  mv "$HERE/$rel" "$PROJECT/$rel"\ndone\n')
        f.write('grep "^# emptied folder: " "$HERE/MOVED.txt" | sed "s/^# emptied folder: //" | while IFS= read -r d; do mkdir -p "$PROJECT/$d"; done\n')
        f.write('echo "restored; reopen Unity so it re-imports"\n')
    os.chmod(os.path.join(ARCHIVE, 'restore.sh'), 0o755)
    print(f'moved {len(moved)} paths, removed {len(removed)} emptied folders; list: {ARCHIVE}/MOVED.txt, restore: {ARCHIVE}/restore.sh')

main()
