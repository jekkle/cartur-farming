using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturFarming
{
    /// <summary>
    /// Planting a whole field in one go: a grid of any size from the crop you place, previewed cell by
    /// cell (red where a crop cannot go), snapping onto an existing field, and an optional random turn
    /// per crop so a field does not look stamped.
    ///
    /// The placed crop is the grid's corner: columns run to the ghost's right, rows away from you.
    /// Each extra crop is paid for and placed the way the game places the first - Player.UpdatePlacement
    /// does TryPlacePiece, then ConsumeResources and UseStamina - so seeds, stamina and wards apply
    /// per crop. PlantEasily does all of this too; when it is loaded this whole file stands down,
    /// because with both every crop would go in twice.
    /// </summary>
    internal static class Field
    {
        internal static int Rows => Plugin.GridRows.Value;
        internal static int Columns => Plugin.GridColumns.Value;
        internal const int MaxSide = 10;

        internal static readonly AccessTools.FieldRef<Player, GameObject> GhostRef =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, bool> NoCost =
            AccessTools.FieldRefAccess<Player, bool>("m_noPlacementCost");
        private static readonly Func<Player, float> BuildStamina =
            AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));

        // Plant.HaveGrowSpace: nothing on these layers inside m_growRadius. The game's ghosts, and our
        // previews, are on "ghost" and so never count.
        private static readonly int SpaceMask =
            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static readonly int GhostLayer = LayerMask.NameToLayer("ghost");

        // HaveGrowSpace tests the neighbour's collider, not its centre, so cells sit a bit further
        // apart than m_growRadius.
        internal static float Spacing(Plant plant) => plant.m_growRadius * 1.25f + 0.1f;

        internal static bool Active(Player p) =>
            !Farming.PlantEasily && Farming.GroupOn && p == Player.m_localPlayer &&
            Farming.Level(p) >= Plugin.GroupLevel.Value && Rows * Columns > 1;

        /// Every cell but the corner the player placed, in placement order.
        internal static IEnumerable<Vector3> Cells(Vector3 corner, Quaternion rot, float step)
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                {
                    if (r == 0 && c == 0) continue;
                    Vector3 pos = corner + rot * new Vector3(c * step, 0f, r * step);
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                    yield return pos;
                }
        }

        /// Why a cell cannot take a crop, or null if it can.
        internal static string Blocked(Player player, Plant plant, Piece piece, Vector3 pos)
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
            foreach (Collider c in Physics.OverlapSphere(pos, plant.m_growRadius, SpaceMask))
                return "occupied by " + c.transform.root.name;
            return null;
        }

        /// How many of this piece the bag can pay for. Player.UpdatePlacement charges for the placed
        /// crop only after TryPlacePiece returns, so while the field is planted that one is still
        /// owed and is subtracted here - or 5 seeds would plant 5 extras and a free corner.
        internal static int Affordable(Player player, Piece piece)
        {
            if (NoCost(player) || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()))
                return int.MaxValue;
            int n = int.MaxValue;
            foreach (Piece.Requirement r in piece.m_resources)
                if (r.m_resItem != null && r.m_amount > 0)
                    n = Math.Min(n, player.GetInventory().CountItems(r.m_resItem.m_itemData.m_shared.m_name) / r.m_amount);
            return n;
        }

        internal static Quaternion Turn(Quaternion rot) =>
            Plugin.RandomRotation.Value ? Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) : rot;

        internal static bool IsGhost(Collider c) => c.gameObject.layer == GhostLayer;

        // ---- planting --------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Plant_Field
        {
            private static void Postfix(Player __instance, Piece piece, bool __result)
            {
                if (!__result || !Active(__instance))
                    return;
                Plant plant = piece != null ? piece.GetComponent<Plant>() : null;
                GameObject ghost = GhostRef(__instance);
                if (plant == null || ghost == null)
                    return;

                // Read before the loop: ConsumeResources changes the inventory, and the game rebuilds
                // its ghost when that happens.
                Vector3 corner = ghost.transform.position;
                Quaternion rot = ghost.transform.rotation;
                bool free = NoCost(__instance) || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
                int left = Affordable(__instance, piece) - 1;     // the corner is still owed

                foreach (Vector3 pos in Cells(corner, rot, Spacing(plant)))
                {
                    if (left <= 0)
                        return;
                    if (Blocked(__instance, plant, piece, pos) != null)
                        continue;
                    float stamina = BuildStamina(__instance);
                    if (!__instance.HaveStamina(stamina))
                        return;

                    __instance.PlacePiece(piece, pos, Turn(rot), doAttack: false);
                    if (!free)
                    {
                        __instance.ConsumeResources(piece.m_resources, 0);
                        left--;
                    }
                    __instance.UseStamina(stamina);
                    __instance.RaiseSkill(Skills.SkillType.Farming);
                }
            }
        }

        // ---- ghost: any biome, snapping, preview -------------------------------------------------

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class Ghost
        {
            // Player.UpdatePlacementGhost checks Piece.m_onlyInBiome on the ghost instance, so clearing
            // it there lets a high-level farmer place it; StampPlanter lets the crop grow there.
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

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;
                GameObject ghost = GhostRef(__instance);
                Plant plant = ghost != null && ghost.activeSelf ? ghost.GetComponent<Plant>() : null;
                if (plant == null || Farming.PlantEasily)
                {
                    Preview.Hide();
                    return;
                }
                if (Farming.SnapOn)
                    Snap(ghost, plant);
                Preview.Show(__instance, ghost, plant);
            }

            // Onto the grid of the nearest crop, at that crop's spacing, in the ghost's own axes.
            private static void Snap(GameObject ghost, Plant plant)
            {
                float step = Spacing(plant);
                Plant nearest = null;
                float best = step * 1.8f;
                foreach (Collider c in Physics.OverlapSphere(ghost.transform.position, best))
                {
                    if (IsGhost(c)) continue;
                    Plant p = c.GetComponentInParent<Plant>();
                    if (p == null) continue;
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

        /// Copies of the game's ghost, one per extra cell, tinted with the game's own invalid-placement
        /// highlight (Piece.SetInvalidPlacementHeightlight) where a crop could not go or the seeds run
        /// out. The ghost is already stripped by Player.SetupPlacementGhost - no network object, colliders
        /// on the "ghost" layer - so a copy made with ZNetView.m_forceDisableInit does nothing but show.
        internal static class Preview
        {
            private static readonly List<GameObject> Pool = new List<GameObject>();
            private static GameObject s_source;

            internal static int Shown;
            internal static int Invalid;

            internal static void Show(Player player, GameObject ghost, Plant plant)
            {
                if (!Active(player))
                {
                    Hide();
                    return;
                }
                if (s_source != ghost)
                {
                    Clear();
                    s_source = ghost;
                }

                Piece piece = ghost.GetComponent<Piece>();
                int left = Affordable(player, piece) - 1;
                int i = 0;
                Invalid = 0;
                foreach (Vector3 pos in Cells(ghost.transform.position, ghost.transform.rotation, Spacing(plant)))
                {
                    GameObject cell = i < Pool.Count ? Pool[i] : Add(ghost);
                    cell.transform.SetPositionAndRotation(pos, ghost.transform.rotation);
                    bool ok = left > 0 && Blocked(player, plant, piece, pos) == null;
                    if (ok) left--;
                    else Invalid++;
                    if (!cell.activeSelf) cell.SetActive(true);
                    cell.GetComponent<Piece>()?.SetInvalidPlacementHeightlight(!ok);
                    i++;
                }
                Shown = i;
                for (; i < Pool.Count; i++)
                    if (Pool[i].activeSelf) Pool[i].SetActive(false);
            }

            private static GameObject Add(GameObject ghost)
            {
                ZNetView.m_forceDisableInit = true;
                GameObject copy;
                try { copy = UnityEngine.Object.Instantiate(ghost); }
                finally { ZNetView.m_forceDisableInit = false; }
                copy.name = ghost.name + "_cartur_preview";
                foreach (Collider c in copy.GetComponentsInChildren<Collider>(true))
                    c.enabled = false;
                Pool.Add(copy);
                return copy;
            }

            internal static void Hide()
            {
                Shown = 0;
                foreach (GameObject g in Pool)
                    if (g != null && g.activeSelf) g.SetActive(false);
            }

            private static void Clear()
            {
                foreach (GameObject g in Pool)
                    if (g != null) UnityEngine.Object.Destroy(g);
                Pool.Clear();
            }
        }
    }
}
