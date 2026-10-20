# Kitsune Shugyō

*Shugyō (修行): training through practice.*

A 7 Days to Die progression mod built on one rule: **you get better at what
you do.** Attributes level from practice, and skill points buy perks only.
Crafting skills level from using tools and weapons, crafting,
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
<KitsuneShugyo preset="SLOW" magazine="0.25" share="party" fatigue="60" rest="6" />
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
| `attribute` practice points per attribute rank step (see Attributes) | 40 | 25 | 10 |

Each number is actions per level (higher = slower). Any source can be
overridden on top of a preset, e.g. `<KitsuneShugyo preset="MEDIUM" weapon="24" />`.

| Setting | Default | Meaning |
|---|---|---|
| `magazine` | 0.25 | Levels one magazine read adds to its skill. 1 = vanilla. |
| `share` | `party` | `party`: magazines and perk books you read also teach your party, wherever they are. `nearby`: only party members within `range`. `off`: no sharing. |
| `range` | 100 | Metres, for `share="nearby"`. |
| `fatigue` | 60 | Practice points before an attribute tires (see Anti-grind). 0 turns fatigue off. |
| `rest` | 6 | In-game hours for a tired attribute to recover fully. |

- **Multiplayer:** only the server's `Settings.xml` counts. The DLL bakes it
  into the `buffs.xml` the server sends to every client, so players can't
  change their own pace. Restart the server after editing.
- **Single player:** takes effect the next time you load a world.

## Attributes

Perception, Strength, Fortitude, Agility and Intellect are **earned by
practice, not bought**. The Buy button on the attribute screen does nothing,
and skill points go to perks only. Perks still need their attribute rank, so
you can't buy Dead Eye 3 until you've shot your way to Perception 5. Player
level, XP and skill points per level are unchanged.

Each attribute has a practice meter. Going from rank r to r+1 costs
r × `attribute` practice points, so higher ranks take longer. On SLOW (40),
rank 1 to 2 is 40 points and rank 9 to 10 is 360.

| Practice | Attribute | Points |
|---|---|---|
| a kill holding gear tagged for the attribute (every vanilla weapon and tool has one), weighted by the victim's XP: a walker is 1, a chicken 0.25, ferals and bears up to 2 | rifles, spears, salvage tools, explosives: Perception · clubs, sledges, axes, picks, shovels: Strength · fists, knuckles, machine guns: Fortitude · knives, machetes, handguns, bows: Agility · batons, robotic sledge, turret, drone: Intellect | 1 |
| a block hit holding that gear | same as above | 0.1 |
| upgrading a block | Strength | 0.5 |
| being hit by a zombie, animal or bandit (falls, fire and cactus don't count) | Fortitude | 0.5 |
| healing a sprain, a splinted or cast limb, or curing an infection | Fortitude | 10 |
| a sneak-attack hit | Agility | 2 |
| opening a fresh loot container | Perception | 1 |
| a completed craft (backpack or workstation), weighted by the recipe's craft XP: a stone axe is about 0.6, a steel tool 2 (at most 5 per batch) | Intellect | 0.1–2 |
| a wire connection (each pair once per session) | Intellect | 1 |
| repairing a vehicle | Intellect | 2 |
| reading a magazine or perk book yourself (shared reads don't count) | Intellect | 2 |
| buying or selling at a trader or vending machine | Intellect | 1 |

A rank-up shows a line like "Your Perception sharpens: rank 5."

### Anti-grind

Practice tires an attribute. Every point adds to that attribute's fatigue,
and after `fatigue` points (60 by default) it's tired: practice counts at a
quarter until it rests. It recovers fully in `rest` in-game hours (6 by
default), measured in world time, so logging off on a running server counts
as rest too. A long session still pays; a zombie farm or a place-and-break
loop pays much less. You get one quiet line per attribute per session when
it tires. Crafting skills don't tire.

Two smaller brakes: wiring the same pair of blocks again in one session
teaches nothing, and `share="nearby"` keeps a party of parked alts from
learning off one reader.

Fatigue is `$shugyo_fatigue_<attribute>` (1 or more = tired), for UI mods.

**Grandpa's Forgetting Elixir resets perks only.** It refunds the points you
spent on perks and leaves attributes and their practice alone. (Vanilla
refunds every attribute rank above 1 as if it had been bought, which would
hand out points for ranks earned by practice.) This applies to every
progression reset, including admin and Twitch resets.

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

**From Kitsune Shugyō 0.1.x:** install over it. Attribute ranks your
characters bought are kept, the points spent on them stay spent, and practice
starts from there toward the next rank.

**From Kitsune Learned Skills:** remove the `KitsuneLearnedSkills` folder and
install `KitsuneShugyo`. Levels and attribute ranks carry over as they are. On each character's
first login, progress toward the next level is moved onto the new meters, and
the old buffs and counters are cleared.

**From Kitsune Shared Magazines or Kitsune Shared Reading:** remove the
`KitsuneSharedMagazines` or `KitsuneSharedReading` folder. Sharing is built in.

If any of these old mods is still installed, Kitsune Shugyō logs an error naming it
and doesn't start.

## Layout

- `KitsuneShugyo/`: the deployable mod payload (extract into `Mods/`)
- `KitsuneShugyo/Settings.xml`: learning pace, attribute pace, magazine value, sharing
- `Shugyo_*.cs` + `KitsuneShugyo.csproj`: DLL source; builds straight into
  the payload folder. References the installed 7DTD client's `Managed` folder
  (override with `-p:GameDir=...`).

## History

Kitsune Shugyō starts from Kitsune Learned Skills 0.10.0 (this repo keeps its
history) and folds in Kitsune Shared Magazines 0.2.3.

0.2.0 (Phase 2): attributes from practice, skill points for perks only, and
the Forgetting Elixir resets perks only.

0.3.0 (Phase 3, anti-grind): practice fatigue, kills weighted by victim XP,
crafts by recipe craft XP, wire pairs once per session, `share="nearby"`.

## License

[CC BY-NC-SA 4.0](LICENSE), with an extra permission for servers that make money: see [NOTICE.md](NOTICE.md). In short: play it, change it and share it, with credit, not for sale, and with changed versions under the same license.
