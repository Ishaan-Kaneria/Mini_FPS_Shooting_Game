# Third-party notices

The MIT licence in [`LICENSE`](LICENSE) covers the work in this repository that
was written for it: everything under `Assets/Scripts/`, `Assets/WebGLTemplates/`,
`Assets/Plugins/`, `Tools/`, `.github/`, the generated content under
`Assets/FPSKit_Generated/`, and the documentation.

It does **not** cover the third-party material listed below. Each of those
carries its own terms, and nothing here grants you rights the original licence
does not.

---

## Art — *RPG/FPS Game Assets for PC, Industrial Set*

`Assets/RPG_FPS_game_assets_industrial/`

Models, textures and materials from a Unity Asset Store package. Distributed
under the [Unity Asset Store End User License
Agreement](https://unity.com/legal/as-terms), not under this repository's MIT
licence.

**What that means in practice.** The EULA lets a licensee use these assets in a
game and distribute the finished game. It does not let anyone redistribute the
assets themselves, or extract them from this repository for use in another
project. If you fork this repository, obtain your own licence for the pack from
the Asset Store — it is free — rather than treating the copy here as yours.

The gameplay does not depend on it. `LevelTheme` drives arena construction from
primitives, and the art pack is dressing layered on top; a fork that deletes the
folder still builds and still plays.

## Engine and packages

Unity 6000.6.0f1 and the packages in `Packages/manifest.json` — Universal RP,
AI Navigation, Input System, TextMeshPro and their dependencies — are licensed
by Unity Technologies under the [Unity Companion
License](https://unity.com/legal/licenses/unity-companion-license) and the terms
shipped in each package. They are referenced by the project, not vendored into
it.

## Repository templates

| File | Source | Licence |
|---|---|---|
| `.gitignore` | [github/gitignore](https://github.com/github/gitignore) `Unity.gitignore` | CC0-1.0 |
| `.gitattributes` | [gitattributes/gitattributes](https://github.com/gitattributes/gitattributes) `Unity.gitattributes` | MIT |

## Audio

`Assets/Audio/` is synthesised from scratch by
[`Tools/generate-placeholder-audio.py`](Tools/generate-placeholder-audio.py)
using nothing but the Python standard library. It is placeholder material
written for this project and is covered by the MIT licence above — it is not
sampled from any recording.
