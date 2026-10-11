using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturFarming
{
    /// <summary>
    /// More to the game's own Farming skill. Inspired by Smoothbrain's Farming (deprecated on
    /// Thunderstore in 2026); written from scratch against the game's code, none of theirs.
    ///
    /// Everything here hangs off Skills.SkillType.Farming, which Valheim 1.0 already levels when you
    /// plant and harvest - so there is no new skill to register and nothing to double-count.
    /// Read off assembly_valheim (1.0): the vanilla skill only scales the scythe's harvest radius
    /// (Attack/Piece m_harvestRadius..m_harvestRadiusMaxLevel), the cultivator's stamina through
    /// Player.GetBuildStamina (up to -50%), and a crop's bonus-yield chance in Pickable.Interact
    /// (skillFactor * m_maxLevelBonusChance, 0.25). Growth speed, yield size, progress, biome and
    /// grid planting are what this adds.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Farming.PlantEasilyGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturfarming";
        public const string PluginName = "Cartur's Farming";
        public const string PluginVersion = "1.0.0";
        internal const string Name = PluginName;
        internal const string Version = PluginVersion;

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigFile Cfg;

        internal static ConfigEntry<float> GrowthBonus;
        internal static ConfigEntry<float> YieldBonus;
        internal static ConfigEntry<float> StaminaPerLevel;
        internal static ConfigEntry<int> GroupLevel;
        internal static ConfigEntry<int> ProgressLevel;
        internal static ConfigEntry<int> AnyBiomeLevel;
        internal static ConfigEntry<bool> EnforceVersion;
        internal static ConfigEntry<KeyboardShortcut> GroupKey;
        internal static ConfigEntry<KeyboardShortcut> SnapKey;

        internal const string ServerSection = "1 - Server";

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;

            GrowthBonus = Config.Bind(ServerSection, "GrowthBonus", 1f,
                new ConfigDescription("How much faster crops grow at Farming 100, scaled by the planter's level when they planted it. 1 = twice as fast, 0 = vanilla.",
                    new AcceptableValueRange<float>(0f, 4f)));
            YieldBonus = Config.Bind(ServerSection, "YieldBonus", 1f,
                new ConfigDescription("Extra crops per harvest at Farming 100, as a share of the normal amount. 1 = double, 0 = vanilla. Comes on top of the game's own bonus chance.",
                    new AcceptableValueRange<float>(0f, 4f)));
            StaminaPerLevel = Config.Bind(ServerSection, "StaminaPerLevel", 1f,
                new ConfigDescription("Percent less stamina to plant, per Farming level. The game already gives 0.5; this replaces it.",
                    new AcceptableValueRange<float>(0f, 1f)));
            GroupLevel = Config.Bind(ServerSection, "GroupLevel", 20,
                new ConfigDescription("Farming level at which you can plant a 3x3 grid and harvest neighbouring crops in one go (toggle with GroupKey).",
                    new AcceptableValueRange<int>(0, 100)));
            ProgressLevel = Config.Bind(ServerSection, "ProgressLevel", 30,
                new ConfigDescription("Farming level at which a planted crop's growth shows as a percentage when you look at it.",
                    new AcceptableValueRange<int>(0, 100)));
            AnyBiomeLevel = Config.Bind(ServerSection, "AnyBiomeLevel", 50,
                new ConfigDescription("Farming level at which crops you plant grow in any biome. Heat and cold still apply - Ashlands and the north need a shield, as in vanilla.",
                    new AcceptableValueRange<int>(0, 101)));
            EnforceVersion = Config.Bind(ServerSection, "EnforceVersion", true,
                "On a server: refuse clients running a different version of this mod.");
            GroupKey = Config.Bind("2 - Keys", "GroupKey", new KeyboardShortcut(KeyCode.LeftShift),
                "With the cultivator out, toggles planting and harvesting in groups.");
            SnapKey = Config.Bind("2 - Keys", "SnapKey", new KeyboardShortcut(KeyCode.LeftControl),
                "With the cultivator out, toggles snapping new crops to the grid of the nearest one.");

            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
                if (Farming.PlantEasily)
                    Log.LogInfo("PlantEasily is installed - it does grid planting and snapping, so ours stand down; group harvest and the rest stay.");
                Log.LogInfo($"{PluginName} {PluginVersion} loaded - growth, yield, progress, any-biome, grid planting and stamina on the Farming skill.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"{PluginName} failed to patch, game continues without it: {e}");
            }
        }

        private void Update() => Farming.Keys();

        // ---- helpers for Sync ----------------------------------------------------------------

        internal static IEnumerable<ConfigEntryBase> ServerOwnedEntries(HashSet<string> sections) =>
            Cfg.Keys.Where(k => sections.Contains(k.Section)).Select(k => Cfg[k]);

        internal static ConfigEntryBase FindEntry(string section, string key) =>
            Cfg.Keys.Where(k => k.Section == section && k.Key == key).Select(k => Cfg[k]).FirstOrDefault();

        internal static void WithoutSaving(Action change)
        {
            bool was = Cfg.SaveOnConfigSet;
            Cfg.SaveOnConfigSet = false;
            try { change(); }
            finally { Cfg.SaveOnConfigSet = was; }
        }
    }

    internal static class Farming
    {
        // The planter's Farming level when the crop went in, on the crop's ZDO. Growth and the
        // any-biome rule read it, so a crop grows the way its planter could make it grow, whoever
        // owns the zone later.
        internal static readonly int PlanterLevelKey = "cartur_farming_level".GetStableHashCode();

        internal const string PlantEasilyGuid = "advize.PlantEasily";

        // PlantEasily (1.2M downloads) already plants in grids and snaps crops. Both at once would
        // place every crop twice - its grid and ours - so ours stand down when it is loaded.
        internal static readonly bool PlantEasily = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(PlantEasilyGuid);

        internal static bool GroupOn = true;
        internal static bool SnapOn;

        internal static float Level(Player p) => p != null ? p.GetSkillLevel(Skills.SkillType.Farming) : 0f;

        private static readonly Func<Humanoid, ItemDrop.ItemData> RightItem =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(AccessTools.Method(typeof(Humanoid), "GetRightItem"));

        internal static bool HoldsCultivator(Player p)
        {
            ItemDrop.ItemData right = p != null ? RightItem(p) : null;
            return right != null && right.m_shared.m_buildPieces != null &&
                   right.m_shared.m_buildPieces.m_skill == Skills.SkillType.Farming;
        }

        internal static void Keys()
        {
            Player p = Player.m_localPlayer;
            if (p == null || !HoldsCultivator(p) || Chat.instance != null && Chat.instance.HasFocus())
                return;
            if (Plugin.GroupKey.Value.IsDown())
            {
                GroupOn = !GroupOn;
                p.Message(MessageHud.MessageType.TopLeft, "Group planting and harvesting " + (GroupOn ? "on" : "off"));
            }
            if (!PlantEasily && Plugin.SnapKey.Value.IsDown())
            {
                SnapOn = !SnapOn;
                p.Message(MessageHud.MessageType.TopLeft, "Crop snapping " + (SnapOn ? "on" : "off"));
            }
        }

        internal static int PlanterLevel(ZNetView nview) =>
            nview != null && nview.IsValid() ? nview.GetZDO().GetInt(PlanterLevelKey, 0) : 0;
    }

    // ---- recording who planted it -----------------------------------------------------------

    // Plant.Awake stamps s_plantTime only when the ZDO has none, i.e. at placement, and it runs
    // inside the Object.Instantiate in Player.PlacePiece. So PlacePiece brackets the call and
    // Awake writes the planter's level beside the plant time.
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PlacingLevel
    {
        internal static int Current = -1;

        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer)
                Current = Mathf.RoundToInt(Farming.Level(__instance));
        }

        private static void Finalizer() => Current = -1;
    }

    [HarmonyPatch(typeof(Plant), "Awake")]
    internal static class StampPlanter
    {
        private static void Postfix(Plant __instance, ZNetView ___m_nview)
        {
            if (___m_nview == null || ___m_nview.GetZDO() == null)
                return;
            if (PlacingLevel.Current >= 0 && ___m_nview.IsOwner() && ___m_nview.GetZDO().GetInt(Farming.PlanterLevelKey, -1) < 0)
                ___m_nview.GetZDO().Set(Farming.PlanterLevelKey, Mathf.Max(1, PlacingLevel.Current));

            // Any biome: Plant.UpdateHealth tests (biome & m_biome) == 0. m_biome is this instance's
            // copy, so widening it touches no other plant. Heat and cold are separate tests and stay.
            if (Farming.PlanterLevel(___m_nview) >= Plugin.AnyBiomeLevel.Value)
                __instance.m_biome = Heightmap.Biome.All;
        }
    }

    // ---- growth ----------------------------------------------------------------------------

    // Plant.SUpdate grows the plant once TimeSincePlanted passes GetGrowTime, and also uses it for
    // the half-grown model. Shortening the returned time is the whole change.
    [HarmonyPatch(typeof(Plant), "GetGrowTime")]
    internal static class FasterGrowth
    {
        private static void Postfix(ZNetView ___m_nview, ref float __result)
        {
            float bonus = Farming.PlanterLevel(___m_nview) / 100f * Plugin.GrowthBonus.Value;
            if (bonus > 0f)
                __result /= 1f + bonus;
        }
    }

    // ---- progress (level 30) -----------------------------------------------------------------

    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
    internal static class ShowProgress
    {
        private static readonly AccessTools.FieldRef<Plant, ZNetView> NView = AccessTools.FieldRefAccess<Plant, ZNetView>("m_nview");
        private static readonly Func<Plant, double> SincePlanted =
            AccessTools.MethodDelegate<Func<Plant, double>>(AccessTools.Method(typeof(Plant), "TimeSincePlanted"));
        private static readonly Func<Plant, float> GrowTime =
            AccessTools.MethodDelegate<Func<Plant, float>>(AccessTools.Method(typeof(Plant), "GetGrowTime"));

        private static void Postfix(Plant __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || Farming.Level(Player.m_localPlayer) < Plugin.ProgressLevel.Value)
                return;
            ZNetView nview = NView(__instance);
            if (nview == null || !nview.IsValid())
                return;
            double pct = Math.Min(100.0, SincePlanted(__instance) / Math.Max(1f, GrowTime(__instance)) * 100.0);
            __result += $"\n{pct:0}% grown";
        }
    }

    // ---- yield and group harvest -------------------------------------------------------------

    // Pickable.Interact rolls Random.value < skillFactor * m_maxLevelBonusChance and, on success,
    // hands m_bonusYieldAmount to RPC_Pick, which drops it on top of m_amount and shows "+N". The
    // extra yield rides that same path: this instance's two fields are set for the one call so the
    // roll always succeeds with our amount plus whatever vanilla's own roll would have given.
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class Harvest
    {
        private static bool s_inGroup;

        private struct Saved { public bool Set; public int Bonus; public float Chance; }

        private static void Prefix(Pickable __instance, Humanoid character, bool ___m_picked, out Saved __state)
        {
            __state = default;
            if (!(character is Player p) || p != Player.m_localPlayer || ___m_picked ||
                __instance.m_pickRaiseSkill != Skills.SkillType.Farming)
                return;

            float factor = p.GetSkillFactor(Skills.SkillType.Farming);
            float extra = __instance.m_amount * factor * Plugin.YieldBonus.Value;
            int whole = Mathf.FloorToInt(extra) + (UnityEngine.Random.value < extra - Mathf.Floor(extra) ? 1 : 0);
            if (UnityEngine.Random.value < factor * __instance.m_maxLevelBonusChance)
                whole += __instance.m_bonusYieldAmount;    // vanilla's roll, made here instead
            if (whole <= 0)
            {
                __state = new Saved { Set = true, Bonus = __instance.m_bonusYieldAmount, Chance = __instance.m_maxLevelBonusChance };
                __instance.m_maxLevelBonusChance = 0f;     // already rolled above, and lost
                return;
            }

            __state = new Saved { Set = true, Bonus = __instance.m_bonusYieldAmount, Chance = __instance.m_maxLevelBonusChance };
            __instance.m_bonusYieldAmount = whole;
            __instance.m_maxLevelBonusChance = 2f / Mathf.Max(factor, 0.0001f);   // Random.value < 2 always
        }

        private static void Postfix(Pickable __instance, Humanoid character, bool repeat)
        {
            if (s_inGroup || repeat || !(character is Player p) || p != Player.m_localPlayer ||
                __instance.m_pickRaiseSkill != Skills.SkillType.Farming || !Farming.GroupOn ||
                Farming.Level(p) < Plugin.GroupLevel.Value)
                return;

            // Same crop, within a few metres - one more row each side of a normally spaced field.
            s_inGroup = true;
            try
            {
                foreach (Collider c in Physics.OverlapSphere(__instance.transform.position, 3f))
                {
                    Pickable other = c.GetComponentInParent<Pickable>();
                    if (other == null || other == __instance || other.m_itemPrefab != __instance.m_itemPrefab || !other.CanBePicked())
                        continue;
                    other.Interact(p, false, false);
                }
            }
            finally { s_inGroup = false; }
        }

        private static void Finalizer(Pickable __instance, Saved __state)
        {
            if (!__state.Set)
                return;
            __instance.m_bonusYieldAmount = __state.Bonus;
            __instance.m_maxLevelBonusChance = __state.Chance;
        }
    }

    // ---- stamina -----------------------------------------------------------------------------

    // GetBuildStamina already takes off attackStamina * 0.5 * skillFactor when the tool has a skill.
    // For the cultivator that becomes StaminaPerLevel percent per level instead.
    [HarmonyPatch(typeof(Player), "GetBuildStamina")]
    internal static class PlantingStamina
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (!Farming.HoldsCultivator(__instance) || __result <= 0f)
                return;
            float factor = __instance.GetSkillFactor(Skills.SkillType.Farming);
            float vanilla = 1f - 0.5f * factor;
            float ours = Mathf.Clamp01(1f - Plugin.StaminaPerLevel.Value * Farming.Level(__instance) / 100f);
            __result = vanilla > 0f ? __result / vanilla * ours : 0f;
        }
    }

    // ---- placement: any biome, snapping, grid ------------------------------------------------

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class Ghost
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> GhostRef =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        // Player.UpdatePlacementGhost checks Piece.m_onlyInBiome on the ghost (the instance, not the
        // prefab), so clearing it on the ghost lets a high-level farmer place it; StampPlanter then
        // lets the planted crop grow there.
        private static void Prefix(Player __instance)
        {
            GameObject ghost = GhostRef(__instance);
            if (ghost == null || __instance != Player.m_localPlayer || ghost.GetComponent<Plant>() == null)
                return;
            if (Farming.Level(__instance) >= Plugin.AnyBiomeLevel.Value)
            {
                Piece piece = ghost.GetComponent<Piece>();
                if (piece != null) piece.m_onlyInBiome = Heightmap.Biome.None;
            }
        }

        // Snap: move the ghost onto the grid of the nearest crop of any kind, at that crop's spacing.
        private static void Postfix(Player __instance)
        {
            GameObject ghost = GhostRef(__instance);
            if (Farming.PlantEasily || !Farming.SnapOn || ghost == null || !ghost.activeSelf || __instance != Player.m_localPlayer)
                return;
            Plant plant = ghost.GetComponent<Plant>();
            if (plant == null)
                return;

            float step = Grid.Spacing(plant);
            Plant nearest = null;
            float best = step * 1.8f;
            foreach (Collider c in Physics.OverlapSphere(ghost.transform.position, best))
            {
                Plant p = c.GetComponentInParent<Plant>();
                if (p == null || p.gameObject == ghost) continue;
                float d = Vector3.Distance(p.transform.position, ghost.transform.position);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest == null)
                return;

            Quaternion rot = ghost.transform.rotation;
            Vector3 local = Quaternion.Inverse(rot) * (ghost.transform.position - nearest.transform.position);
            Vector3 snapped = nearest.transform.position + rot * new Vector3(Mathf.Round(local.x / step) * step, 0f, Mathf.Round(local.z / step) * step);
            snapped.y = ZoneSystem.instance.GetGroundHeight(snapped);
            ghost.transform.position = snapped;
        }
    }

    // Group planting: after a crop is placed, fill the other eight cells of a 3x3 grid around it
    // wherever a crop could legally go. Each one is paid for and placed the way the game places
    // the first - Player.UpdatePlacement does TryPlacePiece, ConsumeResources, UseStamina,
    // RaiseSkill - so seeds, stamina and wards all apply per crop.
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class Grid
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> GhostRef =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, bool> NoCost =
            AccessTools.FieldRefAccess<Player, bool>("m_noPlacementCost");
        private static readonly Func<Player, float> BuildStamina =
            AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));

        // Plant.HaveGrowSpace fails on any other healthy plant within m_growRadius of the centre, and
        // that test hits the neighbour's collider, not its centre, so cells sit a bit further apart.
        private static readonly int SpaceMask =
            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");   // Plant.HaveGrowSpace

        internal static float Spacing(Plant plant) => plant.m_growRadius * 1.25f + 0.1f;

        private static void Postfix(Player __instance, Piece piece, bool __result)
        {
            if (Farming.PlantEasily || !__result || __instance != Player.m_localPlayer || !Farming.GroupOn ||
                Farming.Level(__instance) < Plugin.GroupLevel.Value)
                return;
            Plant plant = piece != null ? piece.GetComponent<Plant>() : null;
            GameObject ghost = GhostRef(__instance);
            if (plant == null || ghost == null)
                return;

            Vector3 centre = ghost.transform.position;
            Quaternion rot = ghost.transform.rotation;
            float step = Spacing(plant);

            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && z == 0) continue;
                    Vector3 pos = centre + rot * new Vector3(x * step, 0f, z * step);
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                    if (Blocked(__instance, plant, piece, pos) != null)
                        continue;

                    bool free = NoCost(__instance) || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
                    if (!free && !CanAffordWithCentre(__instance, piece))
                        return;    // out of seeds - stop here
                    float stamina = BuildStamina(__instance);
                    if (!__instance.HaveStamina(stamina))
                        return;

                    __instance.PlacePiece(piece, pos, rot, doAttack: false);
                    if (!free)
                        __instance.ConsumeResources(piece.m_resources, 0);
                    __instance.UseStamina(stamina);
                    __instance.RaiseSkill(Skills.SkillType.Farming);
                }
        }

        // Player.UpdatePlacement charges for the centre crop only after TryPlacePiece returns, so it is
        // still owed while these run. Each extra needs its own cost plus the centre's still in the bag,
        // or 5 seeds would plant 5 extras and the centre for free.
        private static bool CanAffordWithCentre(Player player, Piece piece)
        {
            foreach (Piece.Requirement r in piece.m_resources)
                if (r.m_resItem != null && r.m_amount > 0 &&
                    player.GetInventory().CountItems(r.m_resItem.m_itemData.m_shared.m_name) < r.m_amount * 2)
                    return false;
            return true;
        }

        // Why a cell cannot take a crop, or null if it can.
        private static string Blocked(Player player, Plant plant, Piece piece, Vector3 pos)
        {
            Heightmap map = Heightmap.FindHeightmap(pos);
            if (map == null)
                return "no heightmap";
            if (plant.m_needCultivatedGround && !map.IsCultivated(pos))
                return "not cultivated";
            if (piece.m_onlyInBiome != Heightmap.Biome.None && (map.GetBiome(pos) & piece.m_onlyInBiome) == 0 &&
                Farming.Level(player) < Plugin.AnyBiomeLevel.Value)
                return "biome";
            if (!PrivateArea.CheckAccess(pos, 0f, false))
                return "ward";
            // The game's own room test, Plant.HaveGrowSpace: nothing on these layers inside m_growRadius
            // except another plant, and here any plant counts since it will grow. A wider sphere wrongly
            // refused the side cells; checking only plants let a crop go in on top of a branch or rock.
            foreach (Collider c in Physics.OverlapSphere(pos, plant.m_growRadius, SpaceMask))
                return "occupied by " + c.transform.root.name;
            return null;
        }
    }
}
