# Desert town: real people living in it

2026-11-04. His brief: "real people roaming and shopping and living in the houses and doing their private things in
the house." Part 1 is what the people system does today, examined end to end. Part 2 is the household system, now in
code (not compiled, built or played: no Unity in the session that wrote it). Part 3 is what is still needed.

## 1. What there is today

| Part | Where | Finding |
|---|---|---|
| Who | `FPSKitTownLife.CivilianPrefabs` | Four models, `Civilian_Man_01-04`, all men in similar dress. No women, children or old people. |
| How many | `CivilianDirector`, `LevelTheme.townCivilians` 60 | 60 on High, 36 on Medium, 21 on Low and phones, over a town of about 250,000 m² |
| Where they appear | `CivilianDirector.TrySpawn` | 30-150 m from the player, never closer; later ones only out of view; never within 25 m of an enemy |
| What they do | `Civilian` | Walk from a random street spot to another 6-90 m away (shopfronts, the souk, benches, the mosque plaza), stand 5-16 s, repeat. Nobody goes into a shop or a house. |
| In a fight | `Civilian.Alarm`, `CivilianDirector.Update` | Everyone who hears a shot runs for a door. Once there and out of view they are **removed**, and replaced 30-150 m away, out of view. A fight therefore empties the town. |
| Animations | `FPSKitTownLife.CivilianController` | A walk/run blend and three deaths. The project's Mixamo set (`Assets/Animations/Mixamo/`, 117 clips) is all combat: no sitting, eating, talking, cooking, sleeping or praying. |
| Houses | `FPSKitTownInterior` | The enterable houses are fully furnished by room kind: living (sofas, a TV), dining (a table and chairs), kitchen (a counter with burners and a sink, a table), bedroom (a bed), majlis (floor mattresses), old kitchen, bathroom. Every room has a mark ("Room kind fN" with a "Look" in its middle); every house has a "Door" mark outside its front door. The navmesh reaches 924 of 933 rooms (`0c296f6`). Nobody lives in any of them. |
| Doors | `Door`, `0c296f6` | Front doors are shut and bolted (they carve the navmesh) until kicked in. Residents cannot go out through a shut door. |
| Shops | `FPSKitTownShopfront`, "ShopRooms" marks | Lit shops have a mark with their inside's box; the insides are nearly empty, with no counter (`DESERT_TOWN_FIXES.md` section 3). |
| Fairness | `Civilian.OnDied` | Every death counts against the player, including civilians killed by enemies (review C1). |
| Cost | `FPSKitTownLife` | A body is 40-120k triangles, culled past about 100 m; people are created and destroyed in play (review C2: spikes). |
| Presence | | They do not look at the player, do not react to a gun pointed at them, carry nothing, and make no sound. |

## 2. Households, now in code

### What happens in play

* **Residents.** The enterable houses near the player (60 m on High, 48 on Medium, 36 on Low) are lived in by 1-4
  people each, chosen from the house's place, so the same house has the same family every time. They are put down at
  home, never in the player's sight. Each goes from one thing to the next about the house:

  | Room | What they do there |
  |---|---|
  | Living | Sit on the sofa (the television), talk, use the phone |
  | Dining | Eat or sit at the table |
  | Kitchen | Cook at the burners, wash up at the sink, eat at the table, sweep |
  | Bedroom | Sit on the bed, lie down for a nap, pray |
  | Majlis | Sit on the floor mattresses, pray, talk |
  | Old kitchen | Cook at the bench, sweep |

  Each activity lasts its own time (a nap 1-2.5 minutes, cooking 20-50 s, the sofa 30-90 s) and is picked by weight
  among what the house has. The bathroom has no spots: private means a door shut, not something shown.
* **Shopping.** About one choice in six, a resident walks out to a lit shop within 140 m (if the path is open: a bolted
  front door keeps them in). They look along two or three shelves, pay at the counter and walk home.
* **Shopkeepers.** Every lit shop near the player has someone minding it, standing at the back.
* **When the shooting starts** (a shot, a blast, a fight near by): those indoors cower where they are, those out on
  an errand run home first. They are never removed while the player is close. After about 25 s of quiet they get up
  and carry on.
* **Death.** A resident killed stays dead, and their house has one fewer for the rest of the level.
* **Budget.** Up to 40 residents and 14 shopkeepers on High, 24 and 8 on Medium, 10 and 4 on Low and phones. These are
  on top of the street's walkers (`CivilianDirector`), which are unchanged.

### Files

| File | What it does |
|---|---|
| `Runtime/LifeActivity.cs` | The 17 activities (the number is the animator's "Activity" parameter: append, never reorder), which are done seated, how long each lasts, how often each is picked |
| `Runtime/TownHouseholds.cs` | The houses, shops and spots as data; who lives where; puts people down near the player and takes them away when far and out of view; reserves spots so two people never sit in one seat |
| `Runtime/CivilianLife.cs` | One person's routine: choose, walk, settle onto the seat or bed (off the navmesh for it), do it, get up; errands; cowering and recovering |
| `Editor/FPSKitTownHouseholds.cs` | `LifeSpot(...)`, called while the houses are furnished; `BuildHouseholds` gathers each house's spots (plus standing spots in each room's middle) and each lit shop's (keeper, counter, four shelves); `AddLifeStates` adds the activity states to the civilians' animator |
| `Editor/FPSKitTownInterior.cs`, `Editor/FPSKitTownFurniture.cs` | The spots at the sofa, the bed (lying and sitting), the burners, the sink, the table chairs, the majlis mattresses, the old kitchen's bench. Positions only: no draws from the house's random sequence, so the town does not change. |
| `Editor/FPSKitTownLife.cs` | Builds the households beside the civilian director; adds the animator states |
| `Editor/FPSKitMixamoImport.cs` | Clips named `Life_*` import looping and in place |
| `Tools/mixamo-download-life.js`, `Tools/sort-life-clips.py` | Fetch the 17 everyday clips from Mixamo and put them where the animator looks |

### To do on the laptop

1. **The clips.** Log in to Mixamo, run `Tools/mixamo-download-life.js` in the browser console (the instructions are at
   its top), then:
   ```
   python3 Tools/sort-life-clips.py ~/Downloads
   ```
   Look at each clip in Mixamo's preview first; where the first search result is wrong, change that line's search
   text or set `PICK` in the sort script. **Prayer:** Mixamo's "Praying" is not salah. For the town, record it with the
   mocap pipeline (`Tools/mocap/mocap.sh`, `Docs/MOCAP_RECORDING_GUIDE.md`), with his approval of the preview as for
   every captured move, or leave `Life_Pray` out (those spots then stand).
2. Compile, rebuild the desert. The log says `[FPSKit] households: N houses (M people), K lit shops, S spots`, and warns
   if no everyday clips were found (then everyone stands at their spots).
3. Play near an enterable house: walk in, find people at the sofa, the burners, the table; fire a shot; wait.
4. **Tune the poses.** Each clip has its own idea of where the seat is. If someone floats above a chair or sinks into a
   bed, change the spot's height and offset in the furniture hook (`LifeSpot` calls) rather than the clip. Lying clips
   may need a quarter turn: the bed spot faces along the bed (head to the headboard).
5. Checks: `VerifyStatics`, `VerifyTown`, frame time near a lived-in street (`FrameCostProbe`); a new `VerifyHouseholds`
   (section 3) before relying on it.

## 3. What is still needed

In order of what it adds:

1. **People of every kind:** women, children, old men and women, in local dress (Mixamo characters, a commercial-use
   pack, or the MPFB generator already tried in `e9688f3`), logged in `LICENSES.md`. Children never a target: their
   deaths are a casualty like any civilian's, and the AI never aims at them.
2. **Props in hand:** a phone, a broom, a spoon at the pot, a shopping bag on the way home, a tea glass at the majlis.
   Laid on the hand socket each frame as the weapons are (never parented into the Animator's hierarchy: CLAUDE.md).
3. **Families together:** two people at one table facing each other, a conversation pair at the Talk spots (they turn
   to each other), children playing in the yard or the room.
4. **Doors:** residents open their own front door to go out and shut it behind them (the `Door` component already
   swings; give it an "opened by a resident" path that does not count as kicked in).
5. **Reacting to the player:** a look as he passes, hands up and backing away when a gun is pointed at them, a shout
   when he walks into their house, the shopkeeper calling out.
6. **Sound:** a television through the wall, a pan on the burner, voices in the majlis, the call to prayer at times
   (the mosque is there), footsteps on the stairs.
7. **The day:** with a time of day (`Docs/DESERT_LIGHTING.md` part 2), more sleeping at night, cooking at midday, the
   souk busy in the late afternoon.
8. **Upstairs and roofs:** washing hung on the roof, sitting on the roof in the evening (with `DESERT_TOWN_FIXES.md`
   section 1).
9. **Shops filled** (`DESERT_TOWN_FIXES.md` section 3), with a real counter for the keeper and shelves for the browsers.
10. **Pooling** instead of creating and destroying people in play (review C2), and the fairness fix (review C1).
11. **`VerifyHouseholds`:** every house's spots are on the navmesh or within 0.6 m of it (seats excepted), every
    resident reaches every one of their spots from their door, no two people ever hold one spot, a resident who hears a
    shot is cowering within a second and back at a spot within a minute of quiet, nobody is put down in view. It must
    fail with the rule deleted.
