---
name: verify
description: Run the right FPSKit batch check after a change to this Unity project, and read its result correctly. Use whenever gameplay, builder, level, store, control, touch, bomb, terrain or navmesh code has been edited and needs verifying; when a change "compiled but did nothing"; when picking which of the checks behind Tools/unity-batch.sh to run; or when a check has failed and the output needs interpreting. Also use before committing a change to Assets/Scripts.
---

# Verifying a change to this project

Every check is a full headless Unity launch, so picking the wrong one costs a
round trip and proves nothing. Route from what was edited.

## Before anything

**Close the Unity editor.** `Tools/unity-batch.sh` refuses to run while the editor
holds the project lock and says so on stderr. It matches on the project path, not
the binary, so a Hub-launched editor is caught too.

```
Tools/unity-batch.sh                      # bare = CompileCheck, the cheap first pass
Tools/unity-batch.sh FPSKitBatch.<Method>
```

The full log is `Logs/unity-batch.log`; the script already greps out
`[FPSKitBatch]`, `[FPSKit]`, `error CS`, `FAILED` and `Aborting`. Read the log
directly when a failure needs more than the tail.

## Route from what changed

| Edited | Run |
|---|---|
| Any script, as a first pass | `CompileCheck` |
| `FPSKitSceneBuilder` / a `FPSKit*` builder partial | `VerifyBuild`, then `VerifyReach` |
| `LevelManager`, `LevelSet`, `LevelResult`, `LevelProgress` | `VerifyLevels` |
| `EnemyAI`, `Health`, `Hitbox`, archetypes | `VerifyCombat` |
| `Weapon`, `WeaponData`, `PlayerProgression`, `PlayerLoadout` | `VerifyStore` |
| `BombThrower`, `BombProjectile`, `Explosion`, `BombData` | `VerifyBomb` |
| `ControlSettings`, `PlayerMotor` movement or look | `VerifyControls` |
| `TouchControls`, `MobileInput`, `TouchProfile`, aim assist | `VerifyTouch` |
| `FPSKitMenuBuilder`, `MainMenuController`, any panel or card | `VerifyFlow` |
| `FPSKitTerrain`, `FPSKitDesert`, dune or river geometry | `VerifyTerrain`, `VerifyZone` |
| NavMesh modifiers, `NoStanding` / `NoEntry` / seals | `VerifyReach` |
| A new `static` field on any class | `VerifyStatics`, then `VerifyReplay` |
| A `Minimap` change | `VerifyLevels` (it owns the projection regression) |

`VerifyBuild` builds Industrial Warehouse **twice** on purpose and inspects only
the second, because the second is the path where every generated asset already
exists.

## The trap that wastes the most time

**Retuning a number in `Configure` does not reach the asset the game reads.**
`FPSKitEnemyRoster`, `FPSKitLevels`, `FPSKitStore` and `FPSKitThemes` all generate
once and are then left alone. A price, a level clock, an arena size or an enemy
stat changed in code compiles, builds and ships doing nothing until the matching
reset runs:

```
Tools/unity-batch.sh FPSKitBatch.ResetEnemyArchetypes
Tools/unity-batch.sh FPSKitBatch.ResetLevelSets
Tools/unity-batch.sh FPSKitBatch.ResetStore
Tools/unity-batch.sh FPSKitBatch.ResetThemes        # then rebuild the scenes
```

If a change "compiled and nothing happened", check this before debugging anything
else. It has cost a rebuild four separate times.

## Seeing an arena instead of asserting about it

No check answers what a level *looks* like — `VerifyZone` passes happily on cliffs
that are inside out. Render it, and note this one needs a real graphics device:

```
UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViews \
    -fpskitTheme "Desert Outpost" -fpskitOut Build/Views
```

Then read the PNGs. A uniformly dark, unlit mass is inverted winding, not shadow.

## Writing a new play-mode check

Four rules, each of which has already been a bug here:

- `FPSKitPlayMode.SuspendStartScene()` before entering play mode, restored in
  `Detach` — otherwise the test is handed the dashboard and fails on its first
  assertion.
- `Time.captureDeltaTime = 1f/60f` for anything measuring speed; batch delta time
  is near zero, so per-frame accumulation measures the batch frame rate. Reset it
  to 0 in `Detach`.
- Put back every shared asset the test retuned. `LevelSet` is shared across all six
  arenas; a test that left level one with a four-minute clock broke the game to pass.
- Create a `NavMeshPath` on demand, never in a field initialiser — Unity refuses to
  build one in a MonoBehaviour constructor, leaves it null, and files the exception
  against the object's construction.
