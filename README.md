# Cartur's Farming

More to the Farming skill you already have, and a better way to plant whole fields. The better a
farmer you are, the faster your crops grow and the more each one gives.

Inspired by **Smoothbrain's [Farming](https://thunderstore.io/c/valheim/p/Smoothbrain/Farming/)**
(deprecated in 2026) and **Advize's [PlantEasily](https://thunderstore.io/c/valheim/p/Advize/PlantEasily/)** -
credit to both for the ideas. This is a separate mod, written from scratch for Valheim 1.0; it
shares no code with either.

## The Farming skill

Everything builds on the game's own Farming skill, which you level by planting and harvesting.
There is no second skill to grind.

| Farming level | You get |
| --- | --- |
| any | Crops grow faster - twice as fast at 100. Speed comes from the level of whoever planted it. |
| any | Bigger harvests - double at 100, on top of the game's own bonus chance. |
| any | Planting costs 1% less stamina per level (the game gives 0.5%). Free at 100. |
| 20 | Plant a whole field in one go, and harvest the same crop around you in one go - beehives too. |
| 30 | Look at a planted crop to see how far grown it is. |
| 50 | Crops you plant grow in any biome. Ashlands heat and northern cold still need a shield, as in the game. |

## Planting fields (level 20)

With the cultivator out, the crop you place is the corner of a field. Columns run to your right,
rows away from you.

- **Right Ctrl + arrow keys** - more or fewer rows and columns, up to 10 x 10.
- Every cell shows a preview first: **red** where a crop can't grow - no room, not hoed, a ward,
  or you've run out of seeds.
- Each crop is paid for: seeds, stamina and wards all apply, and it stops when you run out.
- Extra crops get a random turn so the field doesn't look stamped (setting).
- **F10** - snap new crops onto the grid of the nearest crop, to extend a field.
- **F6** - replant crops as you harvest them, from the seeds in your bag.
- **F8** - turn field planting and group harvesting off and on.

## Settings

`BepInEx/config/com.jekkle.valheim.carturfarming.cfg`

| Setting | Default | What it does |
| --- | --- | --- |
| `GrowthBonus` | 1 | How much faster crops grow at level 100. 1 = twice as fast, 0 = vanilla. |
| `YieldBonus` | 1 | Extra harvest at level 100, as a share of the normal amount. 1 = double. |
| `StaminaPerLevel` | 1 | Percent less planting stamina per level. |
| `GroupLevel` | 20 | Level for fields and group harvest. |
| `ProgressLevel` | 30 | Level for seeing growth progress. |
| `AnyBiomeLevel` | 50 | Level for planting in any biome. |
| `EnforceVersion` | true | On a server, refuse clients on a different version. |
| `Rows` / `Columns` | 3 / 3 | Field size, also changed live with the keys. |
| `RandomRotation` | true | A random turn for each extra crop. |
| `ReplantOnHarvest` | false | Replant as you harvest (F6). |

Install it on the server too and its Farming numbers apply to everyone. Keys and field settings
stay your own.

## Compatibility

Don't run it alongside **PlantEasily**: both plant fields, and every crop would go in twice. If
PlantEasily is installed this mod leaves fields, snapping, replanting and group harvest to it and
keeps the skill side (growth, yield, progress, any biome, stamina).

## Install

r2modman or Thunderstore Mod Manager, or drop `CarturFarming.dll` into `BepInEx/plugins`.
Needs BepInEx.
