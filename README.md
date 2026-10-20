# Kitsune Shugyō

*Shugyō (修行): training through practice.*

A 7 Days to Die progression mod built on one rule: **you get better at what
you do.** Crafting skills level from using tools and weapons, crafting,
cooking, repairing and salvaging vehicles, wiring and looting. Magazines are
back in loot, but they speed you up instead of gating you, and whatever you
read is shared with your party. Perk books are untouched, apart from being
shared too.

Kitsune Shugyō replaces two mods: **Kitsune Learned Skills** and **Kitsune
Shared Magazines**. Remove both before installing it (see Upgrading).

Built for 7DTD 3.3. Requires EAC off (ships a DLL). Install on the server
**and every client**.

## The practice meter

Every crafting skill has one bar. Each thing you do fills part of it, and the
skill levels when the bar is full. Every source fills the same bar, so on
SLOW, 40 salvage hits plus two Salvage Tools magazines are one level
(40/80 + 2 × 0.25).

## Settings.xml

```xml
<KitsuneShugyo preset="SLOW" magazine="0.25" share="party" />
```

| Source | SLOW (default) | MEDIUM | FAST |
|---|---|---|---|
| `weapon` kills with that weapon type | 16 | 12 | 4 |
| `tool` block hits / repairs with that tool | 80 | 60 | 20 |
| `use` food / medical items consumed | 8 | 6 | 2 |
| `craft` items crafted (per skill) | 8 | 6 | 2 |
| `station` workstation queue completions | 16 | 8 | 4 |
| `vehicle` vehicle repairs | 4 | 2 | 1 |
| `wire` wire connections | 4 | 2 | 1 |
| `loot` fresh loot containers opened | 30 | 20 | 8 |
| `wreck` salvage-tool hits on vehicle wrecks | 80 | 60 | 20 |

Each number is actions per level (higher = slower). Any source can be
overridden on top of a preset, e.g. `<KitsuneShugyo preset="MEDIUM" weapon="24" />`.

| Setting | Default | Meaning |
|---|---|---|
| `magazine` | 0.25 | Levels one magazine read adds to its skill. 1 = vanilla. |
| `share` | `party` | `party`: magazines and perk books you read also teach your party, wherever they are. `off`: no sharing. |

- **Multiplayer:** only the server's `Settings.xml` counts. The DLL bakes it
  into the `buffs.xml` the server sends to every client, so players can't
  change their own pace. Restart the server after editing.
- **Single player:** takes effect the next time you load a world.

## How skills level

### Learn by using (Config/buffs.xml)

| Skill group | Levels from | SLOW |
|---|---|---|
| Weapon crafting (clubs, blades, spears, sledges, knuckles, bows, handguns, shotguns, rifles, machine guns, explosives) | kills while holding that weapon type | 16 kills per level |
| Tool crafting (harvesting, salvage, repair) | block hits / repairs with that tool type | 80 hits per level |
| Vehicles | salvaging a car, truck or bus wreck with a wrench, ratchet or impact driver | 80 hits per level |
| Food + medical crafting | consuming food / medical items | 8 uses per level |

### Learn by crafting and doing (KitsuneShugyo.dll)

Every completed craft reads the crafted item's (or block's) `UnlockedBy`
property and adds practice to that skill: 8 crafted items = +1 level on SLOW.
Both the backpack queue and workstation queues count.

These skill lines have no craftable below their first unlock, so the DLL
gives them "doing" hooks that work from level 1:

| Skill | Levels from | SLOW | Bootstrap |
|---|---|---|---|
| Workstations | any workstation queue completion (campfire, forge, workbench, cement mixer, chem station) | 16 completions per level | cook on a campfire, forge unlocks at 4 |
| Vehicles | repairing a vehicle with a repair kit; salvaging wrecks (above) | 4 repairs per level | repair a looted bicycle or salvage wrecks, parts unlock at 5 |
| Electrician | completing a wire connection with the wire tool | 4 connections per level | loot or buy a wire tool, own gear unlocks at 25 |
| Traps | crafting spikes / barbed fence (always unlocked) | 8 crafts per level | dart traps and turrets unlock at 25 |
| Seeds | crafting farm plots (always unlocked) | 8 crafts per level | seed recipes unlock from 2 |
| Backpacks | opening a fresh loot container (first open only; your own storage never counts) | 30 containers per level | small backpack unlocks at 5 |

### Magazines and perk books

All 24 crafting magazines are back in loot and on traders at vanilla rates.
Reading one adds `magazine` levels (a quarter by default) to its skill and
shows how close you are to the next level. With `share="party"`, everyone in
your party gets the same practice and a note saying who read it. Perk books
work as in vanilla and are shared the same way; each member earns a series'
completion bonus themselves when the shared volume finishes their set.

Under the hood, a read rides three vanilla console packages (reader to server
to party), so a player or server without the mod just sees "unknown command"
and nobody is dropped.

## Upgrading

**From Kitsune Learned Skills:** remove the `KitsuneLearnedSkills` folder and
install `KitsuneShugyo`. Levels carry over as they are. On each character's
first login, progress toward the next level is moved onto the new meters, and
the old buffs and counters are cleared.

**From Kitsune Shared Magazines or Kitsune Shared Reading:** remove the
`KitsuneSharedMagazines` or `KitsuneSharedReading` folder. Sharing is built in.

If any of these old mods is still installed, Kitsune Shugyō logs an error naming it
and doesn't start.

## Layout

- `KitsuneShugyo/`: the deployable mod payload (extract into `Mods/`)
- `KitsuneShugyo/Settings.xml`: learning pace, magazine value, sharing
- `Shugyo_*.cs` + `KitsuneShugyo.csproj`: DLL source; builds straight into
  the payload folder. References the installed 7DTD client's `Managed` folder
  (override with `-p:GameDir=...`).

## History

Kitsune Shugyō starts from Kitsune Learned Skills 0.10.0 (this repo keeps its
history) and folds in Kitsune Shared Magazines 0.2.3.

## License

[CC BY-NC-SA 4.0](LICENSE), with an extra permission for servers that make money: see [NOTICE.md](NOTICE.md). In short: play it, change it and share it, with credit, not for sale, and with changed versions under the same license.
