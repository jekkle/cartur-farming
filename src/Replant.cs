using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturFarming
{
    /// <summary>
    /// Replant on harvest: a picked crop is replanted where it stood, from the seeds in your bag.
    ///
    /// The seedling is found from the game's own data - each sapling's Plant.m_grownPrefabs lists the
    /// crop it becomes - so modded crops work too. It runs half a second after the pick, because
    /// RPC_Pick removes the crop on its owner and until then the spot is not free. Placed through
    /// Player.PlacePiece like any crop, so the planter's Farming level is recorded on it.
    /// </summary>
    internal static class Replant
    {
        private static Dictionary<string, Piece> s_saplingFor;

        internal static void After(Player player, Pickable crop)
        {
            Piece sapling = SaplingFor(Utils.GetPrefabName(crop.gameObject));
            if (sapling == null || Plugin.Instance == null)
                return;
            Plugin.Instance.StartCoroutine(Later(player, sapling, crop.transform.position, crop.transform.rotation));
        }

        private static IEnumerator Later(Player player, Piece sapling, Vector3 pos, Quaternion rot)
        {
            yield return new WaitForSeconds(0.5f);
            if (player == null || player != Player.m_localPlayer)
                yield break;
            Plant plant = sapling.GetComponent<Plant>();
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            if (Field.Affordable(player, sapling) < 1 || Field.Blocked(player, plant, sapling, pos) != null)
                yield break;

            player.PlacePiece(sapling, pos, rot, doAttack: false);
            if (Field.Affordable(player, sapling) != int.MaxValue)
                player.ConsumeResources(sapling.m_resources, 0);
            player.RaiseSkill(Skills.SkillType.Farming);
        }

        internal static Piece SaplingFor(string cropPrefab)
        {
            if (s_saplingFor == null)
            {
                if (ZNetScene.instance == null)
                    return null;
                s_saplingFor = new Dictionary<string, Piece>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                    Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                    if (plant == null || piece == null)
                        continue;
                    foreach (GameObject grown in plant.m_grownPrefabs)
                        if (grown != null && grown.GetComponent<Pickable>() != null && !s_saplingFor.ContainsKey(grown.name))
                            s_saplingFor[grown.name] = piece;
                }
            }
            return s_saplingFor.TryGetValue(cropPrefab, out Piece p) ? p : null;
        }
    }

    // Group harvest for beehives too: taking honey from one takes it from the hives beside it.
    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
    internal static class BeehiveGroup
    {
        private static bool s_inGroup;

        private static void Postfix(Beehive __instance, Humanoid character, bool repeat)
        {
            if (s_inGroup || repeat || !(character is Player p) || p != Player.m_localPlayer ||
                !Farming.IsGroupHarvest || Farming.Level(p) < Plugin.GroupLevel.Value)
                return;
            s_inGroup = true;
            try
            {
                var seen = new HashSet<Beehive> { __instance };
                foreach (Collider c in Physics.OverlapSphere(__instance.transform.position, 4f))
                {
                    Beehive other = c.GetComponentInParent<Beehive>();
                    if (other != null && seen.Add(other))
                        other.Interact(p, false, false);
                }
            }
            finally { s_inGroup = false; }
        }
    }
}
