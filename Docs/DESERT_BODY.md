# The player's body: blows that hurt, and limbs that stop at walls

2026-11-04. His notes: "when I kick or punch walls I have to be hurt, and repeated hurting leads to failure of the body
part"; "my legs and everything pass through walls; that is not possible in real life." In code on branch
`claude/gifted-lamport-ho6dv7` (not compiled or played: no Unity in the session that wrote it).

## What was wrong
| # | Bug | Cause |
|---|---|---|
| H1 | A punch or kick on a wall did nothing to the player | `MeleeStrike.HitWorld` only told the wall (a door, a loose object); a plain wall answered nothing and the blow counted as a miss |
| H2 | The fist, the rifle's stock and the kicking leg went into walls | The strike always played to full reach; the arms camera draws over the world, so the arms showed through |
| H3 | The legs went into walls when standing at one | `PlayerBody` draws the body ahead of the capsule (under the set-back eye, further in a kick) with nothing stopping it |
| H4 | The rifle's muzzle went into walls | Nothing drew the gun back when the player stood close to one |

## What it does now
* **Pain and injury** (`PlayerLimbs`, new). Every blow puts strain on the limb that threw it (the jab is the left hand,
  the other punches the right, the kick the right foot), by what it struck and by what struck it:

  | Surface | Hardness | | Striking with | Share |
  |---|---|---|---|---|
  | Metal | 1.1 | | Bare fist | 1.0 |
  | Concrete, stone, brick | 1.0 | | Boot (kick) | 0.45 |
  | Glass, ice | 0.7-0.8 | | Rifle stock | 0.12 |
  | Wood (a door) | 0.5 | | | |
  | A body (jaw, skull) | 0.3, halved | | | |
  | Sand, snow | 0.1-0.15 | | | |

  Strain builds through **Bruised** (a quarter), **Swollen** (over half) and **Fractured** (nine tenths): a bare fist on
  concrete is swollen by the third punch and broken by the fifth; a boot on concrete takes about nine kicks. Every blow
  costs a little health (6 for a bare fist on concrete, more as strain grows, 10 more with a broken limb), with a jolt
  of the view and the hurt vignette. A bruise eases if the limb is rested for 20 s; swelling and a break stay for the
  level, as the enemies' wounds do.
* **What an injury costs:** a bruised, swollen or broken limb strikes at 0.85, 0.6 or 0.3 of its force. A swollen hand
  reloads at 1.25x the time, a broken one at 1.6x (one-handed). A swollen foot limps (0.92 speed); a broken one limps
  badly (0.75) and the player cannot sprint.
* **Stopping at the wall:** a strike with no man in reach checks for a wall first. If one is closer than full reach, the
  blow lands when the fist, stock or boot gets there, and the strike then plays back the way it came: the arms and the
  kicking leg stop at the surface and rebound (`MeleeStrike.BlockedAt`, `StrikeProgress`).
* **The legs:** the drawn body is pushed back by however far its knees, toes or kicking foot would be inside whatever
  is in front (`PlayerBody.PlaceBody`).
* **The rifle:** up against a wall it is drawn back and its muzzle lifted, as a soldier does (`Weapon.UpdateWallTuck`);
  rounds still leave from the eye.

## Next
* A HUD hint when a limb gets worse (`PlayerLimbs.Worsened` is raised; nothing listens yet), and a grunt.
* The hand shaking with a broken hand (aim sway), a bandage at a med kit that eases a bruise.
* Glass: a punch cracks it and cuts the hand (`DESERT_GLASS.md`).
* A check (`VerifyBody`): five bare punches on a concrete wall break the hand; a kick on a door barely bruises the foot; a
  strike at a wall never puts the hand past the wall's plane; standing nose to a wall, no leg vertex is past it. It must
  fail with the rule deleted.
