# Contributing

Thanks for looking. This is a small project with a few strong conventions — most
of them exist because breaking them produces bugs that compile cleanly and only
show up at run time. The short version is below; [`CLAUDE.md`](CLAUDE.md) has the
long version with the reasoning.

## Getting set up

You need **Unity 6000.6.0f1** exactly. Different patch releases rewrite
`ProjectSettings` and the generated scenes, which makes for unreviewable diffs.

```bash
git clone https://github.com/Ishaan-Kaneria/Mini_FPS_Shooting_Game.git
cd Mini_FPS_Shooting_Game
git lfs pull                 # 93 files are in LFS; without this the audio is text
```

Open the project, then **FPSKit → Build Scene → Industrial Warehouse** and press
Play. There is no scene to open by hand.

## Before you open a pull request

Run the checks. They need no editor open — the script refuses to start while one
holds the project lock.

```bash
Tools/unity-batch.sh                             # compiles
Tools/unity-batch.sh FPSKitBatch.VerifyBuild     # the builder wired everything up
Tools/unity-batch.sh FPSKitBatch.VerifyWaves     # an uncleanable wave still ends
Tools/unity-batch.sh FPSKitBatch.VerifyReplay    # state does not leak between runs
Tools/unity-batch.sh FPSKitBatch.VerifyStatics   # every static resets
```

`VerifyReplay` and `VerifyStatics` are the ones that catch the failure this
project is most prone to, so please do not skip them. See *Statics* below.

## The five rules

**1. Generated content is output, not source.** `Assets/FPSKit_Generated/` and
`Build/` are both rebuilt from scratch. Hand-editing a generated `.unity` file
fixes nothing past the next **FPSKit → Build Scene**, and hand-editing
`Build/WebGL/index.html` fixes nothing past the next build. Change
`FPSKitSceneBuilder.cs` or `Assets/WebGLTemplates/FPSKit/index.html` instead.

**2. New content is a new asset, not a new branch in the builder.** A new arena
is a `LevelTheme`; a new enemy is an `EnemyArchetype`; a new weapon is a
`WeaponData`. Duplicate one, retune the numbers, use it. If the knob you need
genuinely does not exist, add it to the ScriptableObject — not to the builder.

**3. Statics need a reset hook.** Domain reload is disabled, so no C# static is
ever cleared between play sessions. Any static field carrying state must be reset
in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
hook on its own class, or the game works the first time you press Play and is
dead the second. `VerifyStatics` finds them by reflection, so a new one is
audited whether you registered it or not.

**4. Anything Unity cannot serialize is created on demand.** Editing a script
during play reloads the domain without re-running `Awake`, and a
`MaterialPropertyBlock`, a `Coroutine` or any plain C# class comes back `null`
while the `Renderer[]` beside it survives. Use a private property with a null
check — `EnemyAI.Block` is the pattern — and keep it out of `Awake` entirely.

**5. Input is the legacy API.** Gameplay reads `UnityEngine.Input`. Do not port
it to the Input System; the one deliberate exception is
`PlayerMotor.ReadMouseCounts()`. Read keys through `ControlSettings` helpers
rather than naming a `KeyCode` literal, so rebinding keeps working.

## Style

- Scripts live in `Assets/Scripts/Runtime/` (gameplay, flat, global namespace) or
  `Assets/Scripts/Editor/` (`namespace FPSKit.EditorTools`, every file wrapped in
  `#if UNITY_EDITOR`). There are no `.asmdef` files, so the folder name `Editor`
  is the only thing keeping that code out of player builds — do not move it.
- Tunables are `[Header]`-grouped public fields with `[Tooltip]`s in plain prose.
- Comments explain **why** a knob or a guard exists, not what the line does. A
  comment that restates the code is worse than no comment.
- `.meta` files are committed and must stay in sync with their assets.

## Commits

Write the subject as what the change does for the player or the reader, in the
imperative, with no prefix or tag:

```
Stop a touchscreen laptop from being treated as a phone
Make republishing one command instead of a folder drag
```

not `fix: touch bug` or `Updated TouchControls.cs`. If the reasoning is not
obvious from the subject, put it in the body.

## Reporting a bug

Please say which theme you built, whether it reproduces on a fresh
**FPSKit → Build Scene**, and whether it happens on the first press of Play or
only the second — that last one narrows it to a static almost immediately.
