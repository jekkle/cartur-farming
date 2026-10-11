# Cartur's Farming

More to the Farming skill you already have. The better a farmer you are, the faster your
crops grow and the more each one gives.

Inspired by **Smoothbrain's [Farming](https://thunderstore.io/c/valheim/p/Smoothbrain/Farming/)**,
which was deprecated on Thunderstore in 2026 - credit to Smoothbrain for the idea and years of
farming mods. This is a separate mod, written from scratch for Valheim 1.0; it shares no code
with theirs.

## What it does

Everything builds on the game's own Farming skill, which you level by planting and harvesting.
There is no second skill to grind.

| Farming level | You get |
| --- | --- |
| any | Crops grow faster - twice as fast at 100. Speed comes from the level of whoever planted it. |
| any | Bigger harvests - double at 100, on top of the game's own bonus chance. |
| any | Planting costs 1% less stamina per level (the game gives 0.5%). Free at 100. |
| 20 | Plant a 3x3 grid in one go, and picking a crop picks the same crop around it. Toggle with **Left Shift**. |
| 30 | Look at a planted crop to see how far grown it is. |
| 50 | Crops you plant grow in any biome. Ashlands heat and northern cold still need a shield, as in the game. |

**Left Ctrl** (cultivator out) toggles snapping new crops to the grid of the nearest one.

Group planting pays for every crop: seeds, stamina and wards all apply, and it stops when you
run out.

## Settings

`BepInEx/config/com.jekkle.valheim.carturfarming.cfg`

| Setting | Default | What it does |
| --- | --- | --- |
| `GrowthBonus` | 1 | How much faster crops grow at level 100. 1 = twice as fast, 0 = vanilla. |
| `YieldBonus` | 1 | Extra harvest at level 100, as a share of the normal amount. 1 = double. |
| `StaminaPerLevel` | 1 | Percent less planting stamina per level. |
| `GroupLevel` | 20 | Level for group planting and harvesting. |
| `ProgressLevel` | 30 | Level for seeing growth progress. |
| `AnyBiomeLevel` | 50 | Level for planting in any biome. |
| `EnforceVersion` | true | On a server, refuse clients on a different version. |
| `GroupKey` / `SnapKey` | Left Shift / Left Ctrl | Toggles, with the cultivator out. |

Install it on the server too and its numbers apply to everyone. Key bindings stay your own.

## Install

r2modman or Thunderstore Mod Manager, or drop `CarturFarming.dll` into `BepInEx/plugins`.
Needs BepInEx.
