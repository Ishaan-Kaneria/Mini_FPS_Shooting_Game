## What this changes

<!-- What it does for a player or a reader of the code, not which files moved. -->

## Why

<!-- The problem it solves. Link an issue if there is one. -->

## Checks

<!-- Tick what you ran. Not every change needs all of them; say which you skipped and why. -->

- [ ] `Tools/unity-batch.sh` — compiles
- [ ] `Tools/unity-batch.sh FPSKitBatch.VerifyBuild` — the builder wired everything up
- [ ] `Tools/unity-batch.sh FPSKitBatch.VerifyLevels` — a level is scored on both its endings
- [ ] `Tools/unity-batch.sh FPSKitBatch.VerifyFlow` — dashboard → level select → arena → back
- [ ] `Tools/unity-batch.sh FPSKitBatch.VerifyReplay` — no state leaks between runs
- [ ] `Tools/unity-batch.sh FPSKitBatch.VerifyStatics` — every static resets
- [ ] Played it, and it behaves the same on the **second** press of Play as the first

## Conventions

- [ ] Scene or build output changes went into the builder or the WebGL template, not into generated files
- [ ] New content is a new `LevelTheme` / `EnemyArchetype` / `WeaponData` asset rather than a new branch in the builder
- [ ] Any new static field has a `SubsystemRegistration` reset hook
- [ ] Any new non-serializable field is created on demand, not assigned in `Awake`
- [ ] `.meta` files are committed alongside their assets
