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

To see your practice, add **KitsuneUI_Practice** (part of
[KitsuneUI](https://github.com/Kitsune-Den/KitsuneUI), optional): a
Practice tab with a bar for every attribute and crafting skill, and practice
bars instead of Buy buttons on the attribute screen. In multiplayer it goes
on the server too, with KitsuneUI.

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
| `points_after` | 0 (off) | A player level. Past it, skill points come only every `points_every` levels. |
| `points_every` | 2 | With `points_after` set: a skill point every this many levels past it (2 = every other level). |
| `buy_attributes` | `false` | `true`: skill points can buy attribute ranks too, at vanilla's price, as well as practice earning them (see Attributes). |

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

A server that wants both can set `buy_attributes="true"`: the Buy button works
again at vanilla's price, and practice keeps counting. Buying a rank keeps the
practice already put towards the next one (as points, so the dearer rank after
it shows a smaller percentage). The Forgetting Elixir still refunds no
attribute ranks, bought ones included. Clients need Shugyō 1.2.0 or later for
the button; an older one still blocks it.

Each attribute has a practice meter. Going from rank r to r+1 costs
r × `attribute` practice points, so higher ranks take longer. On SLOW (40),
rank 1 to 2 is 40 points and rank 9 to 10 is 360.

| Practice | Attribute | Points |
|---|---|---|
| a kill holding gear tagged for the attribute (every vanilla weapon and tool has one), weighted by the victim's XP: a walker is 1, a chicken 0.25, ferals and bears up to 2 | rifles, spears, salvage tools, explosives: Perception · clubs, sledges, axes, picks, shovels: Strength · fists, knuckles, machine guns: Fortitude · knives, machetes, handguns, bows: Agility · batons, robotic sledge, turret, drone: Intellect | 1 |
| a block hit holding that gear (not on blocks you placed this session) | same as above | 0.1 |
| upgrading a block | Strength | 0.5 |
| being hit by a zombie, animal or bandit (falls, fire and cactus don't count) | Fortitude | 0.5 |
| healing a sprain, a splinted or cast limb, or curing an infection | Fortitude | 10 |
| a sneak-attack hit | Agility | 2 |
| crouch-moving within 8 m of an enemy that hasn't noticed you (sleepers count) | Agility | 0.05 a second |
| a headshot kill on a zombie or hostile animal, any weapon, on top of the weapon's own attribute (so a rifle headshot kill counts double), weighted like kills | Perception | 1 |
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

Smaller brakes: hitting blocks you placed yourself this session teaches no
attribute (no place-and-break loops; upgrading them still counts), wiring
the same pair of blocks again in one session teaches nothing, and
`share="nearby"` keeps a party of parked alts from learning off one reader.

Fatigue is `$shugyo_fatigue_<attribute>` (1 or more = tired), for UI mods.

### Practice sources

Every meter also records where its practice came from:
`$shugyo_src_<meter>_<source>` totals what each source has added since 0.5.0
(levels for crafting skills, points for attributes). Crafting-skill sources
are the Settings.xml names (`weapon`, `tool`, `wreck`, `use`, `craft`,
`station`, `vehicle`, `wire`, `loot`) plus `magazine` and `shared`;
attribute sources are `kill`, `hit`, `upgrade`, `hurt`, `heal`, `sneak`,
`stealth`, `headshot`, `loot`, `craft`, `wire`, `vehicle`, `read` and
`trade`. KitsuneUI's Practice tab shows the top ones on hover.

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

## Admin command

`shugyo` in the F1 console (or the server console / telnet). Admin-only by
default; serveradmin.xml can open it up like any command.

| Command | What it does |
|---|---|
| `shugyo version` | This server's Kitsune Shugyō version, and the version each player's game reported |
| `shugyo status [player]` | Every attribute and crafting skill: rank or level, % to the next, fatigue, top practice sources. Without a player, yourself |
| `shugyo give <player> <meter> <amount>` | Add practice. Attributes (`attStrength`, ...) take points, where a walker kill is 1; crafting skills (`craftingClubs`, ...) take levels, so 0.5 is half a level. Ignores fatigue and isn't counted as a source |
| `shugyo rest <player>` | Clear that player's fatigue |

`<player>` is a name, entity id or platform id.

## Version check

When a player joins, the server tells their game which Kitsune Shugyō it
runs. If the two differ, the player sees "This server runs Kitsune Shugyō X
and you have Y" on screen, and the server logs a warning naming them.
Mismatched versions can make practice silently not count, because the
server's config may use parts of Shugyō the player's copy doesn't have. Games
older than 1.1.0 can't answer, so `shugyo version` lists them as "no reply".

A game that hasn't answered after 30 seconds isn't running Shugyō at all (not
installed, or switched off by one of the old mods below), and the server logs a
warning naming the player: their practice and shared reads won't count.

## Server log for sharing

Every read a player reports is in the server log: "X read a Y magazine,
shared with N party member(s)", or "not shared" with the reason (not in a
party, no other member online or in range, sharing off). A report the server
refuses is logged as a warning. So when a party says reading isn't shared, the
log shows whether the reads arrived at all.

## Upgrading

**From a Kitsune Shugyō test build (0.x):** install over it. Attribute ranks your
characters bought are kept, the points spent on them stay spent, and practice
starts from there toward the next rank.

**From Kitsune Learned Skills:** remove the `KitsuneLearnedSkills` folder and
install `KitsuneShugyo`. Levels and attribute ranks carry over as they are. On each character's
first login, progress toward the next level is moved onto the new meters, and
the old buffs and counters are cleared.

**From Kitsune Shared Magazines or Kitsune Shared Reading:** remove the
`KitsuneSharedMagazines` or `KitsuneSharedReading` folder. Sharing is built in.

If any of these old mods is still installed, Kitsune Shugyō logs an error naming it
and doesn't start. The player also sees it on screen and in chat a few seconds
after loading into a world, with which folder to remove.

## Layout

- `KitsuneShugyo/`: the deployable mod payload (extract into `Mods/`)
- `KitsuneShugyo/Settings.xml`: learning pace, attribute pace, magazine value, sharing
- `Shugyo_*.cs` + `KitsuneShugyo.csproj`: DLL source; builds straight into
  the payload folder. References the installed 7DTD client's `Managed` folder
  (override with `-p:GameDir=...`).

## History

Kitsune Shugyō starts from Kitsune Learned Skills 0.10.0 (this repo keeps its
history, so its old v0.7.0 to v0.10.0 tags are Learned Skills releases) and
folds in Kitsune Shared Magazines 0.2.3.

1.2.0: optional `buy_attributes`, so skill points can buy attribute ranks
as well as practice earning them. Off by default.

1.1.1: the server logs every read report, shared or not and why, and players
whose game doesn't answer the version check; players see on screen when Shugyō
is switched off by an old mod.

1.1.0: the `shugyo` admin command (version, status, give, rest) and a
version check when a player joins.

1.0.0: the first release, everything below. The 0.x versions were never
released on their own.

0.5.0: crafting-skill level-ups no longer multiply by vanilla's
PointsPerMagazine sandbox option (the XML sources now level through the DLL),
every meter records its practice sources, and an optional skill-point
slowdown past a chosen level (`points_after`, `points_every`).

0.4.0: headshot kills teach Perception and sneaking near unaware enemies
teaches Agility, the two sources the design had that 0.2.0 left out.

0.3.0 (Phase 3, anti-grind): practice fatigue, kills weighted by victim XP,
crafts by recipe craft XP, no practice from blocks you placed, wire pairs
once per session, `share="nearby"`.

0.2.0 (Phase 2): attributes from practice, skill points for perks only, and
the Forgetting Elixir resets perks only.

0.1.0 (Phase 1): one practice meter per crafting skill, magazines back as a
boost shared with the party, Learned Skills progress carried over.

## License

[CC BY-NC-SA 4.0](LICENSE), with an extra permission for servers that make money: see [NOTICE.md](NOTICE.md). In short: play it, change it and share it, with credit, not for sale, and with changed versions under the same license.
