using BepInEx;
using Dungeonator;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ETGCoopMultipleItems
{
    [BepInPlugin("ETGCoopMultipleItems", "Averito Coop Multiple Items Mod", "1.0")]
    public class ETGCoopMultipleItems : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("[ETGCoopMultipleItems] Mod loaded!");

            var harmony = new Harmony("ETGCoopMultipleItems");
            harmony.PatchAll();
        }

        private static RewardPedestal lastPedestalPrefab;

        [HarmonyPatch(typeof(RewardPedestal), "Spawn", new Type[] { typeof(RewardPedestal), typeof(IntVector2), typeof(RoomHandler) })]
        public class BossPedestalSpawnPatch
        {
            public static void Postfix(RewardPedestal __result, RewardPedestal pedestalPrefab, IntVector2 basePosition, RoomHandler room)
            {
                lastPedestalPrefab = pedestalPrefab;
            }
        }

        private static HashSet<string> clonedContents = new HashSet<string>();

        [HarmonyPatch(typeof(GameManager), "DoGameOver")]
        public class GameManagerDoGameOverPatch
        {
            public static void Postfix(string gameOverSource)
            {
                Debug.Log($"[ETGCoopMultipleItems] Clear clonedContents");
                clonedContents = new HashSet<string>();
            }
        }

        [HarmonyPatch(typeof(RewardPedestal), "DetermineContents")]
        public class BossPedestalDetermineContentsPatch
        {
            public static void Postfix(RewardPedestal __instance)
            {
                if (__instance == null) return;
                __instance.StartCoroutine(ClonePedestalWhenReady(__instance));
            }

            private static IEnumerator ClonePedestalWhenReady(RewardPedestal __instance)
            {
                while (!__instance.specRigidbody.enabled)
                {
                    yield return null;
                }

                Debug.Log($"[ETGCoopMultipleItems] __instance = {__instance}; contents = {__instance.contents?.name ?? "NULL"}");
                if (__instance.contents == null) yield break;

                if (clonedContents.Contains(__instance.contents.DisplayName)) yield break;
                clonedContents.Add(__instance.contents.DisplayName);
                Debug.Log($"[ETGCoopMultipleItems] Помечен ключ {__instance.contents.DisplayName}");

                PlayerController[] players = GameManager.Instance.AllPlayers;
                Debug.Log($"[ETGCoopMultipleItems] Игроков = {players.Length}");
                if (players.Length < 2) yield break;

                IntVector2 basePosition = __instance.specRigidbody.UnitBottomLeft.ToIntVector2();
                IntVector2 offsetPosition = basePosition + new IntVector2(0, -2);
                Debug.Log($"[ETGCoopMultipleItems] Cloning pedestal to {offsetPosition}");

                var clonePedestal = RewardPedestal.Spawn(lastPedestalPrefab, offsetPosition);
                if (clonePedestal == null) yield break;

                clonePedestal.overrideMimicChance = 0;
                clonePedestal.contents = __instance.contents;

                Debug.Log($"[ETGCoopMultipleItems] Cloned pedestal contents: {clonePedestal.contents?.name ?? "NULL"} at position {offsetPosition}");
            }
        }

        [HarmonyPatch(typeof(Chest), "Open")]
        public class Chest_Open_Patch
        {
            public static void Postfix(Chest __instance)
            {
                Debug.Log($"[ETGCoopMultipleItems] Is Rainbow Chest: {(__instance.IsRainbowChest ? "true" : "false")}");
                if (__instance.IsRainbowChest) return;

                PlayerController[] players = GameManager.Instance.AllPlayers;
                bool coopActive = players.Length > 1;
                bool anyDead = false;

                foreach (var player in players)
                {
                    if (!player || !player.healthHaver || !player.healthHaver.IsAlive)
                    {
                        anyDead = true;
                        break;
                    }
                }
                Debug.Log($"[ETGCoopMultipleItems] anyDead = {anyDead}; coopActive = {coopActive}; players = {players.ToList().Count}");

                if (!coopActive || anyDead) return;

                PickupObject extraItem = null;

                if (__instance.ChestType == Chest.GeneralChestType.WEAPON)
                {
                    extraItem = GetRandomFromLootTable(__instance, go => go.GetComponent<Gun>() != null);
                    Debug.Log($"[ETGCoopMultipleItems] Generate weapon item");
                }
                else if (__instance.ChestType == Chest.GeneralChestType.ITEM)
                {
                    extraItem = GetRandomFromLootTable(__instance, go => go.GetComponent<Gun>() == null);
                    Debug.Log($"[ETGCoopMultipleItems] Generate item");
                }
                else
                {
                    extraItem = GetRandomFromLootTable(__instance);
                    Debug.Log($"[ETGCoopMultipleItems] Generate generic item");
                }

                if (extraItem != null)
                {
                    LootEngine.SpawnItem(extraItem.gameObject, __instance.transform.position, Vector2.zero, 1f, false, true);
                    Debug.Log($"[ETGCoopMultipleItems] Spawned extra item: {extraItem.name}");
                }
            }

            private static PickupObject GetRandomFromLootTable(Chest chest, Func<GameObject, bool> predicate = null)
            {
                if (chest.lootTable == null) return null;

                var loot = chest.lootTable.lootTable.defaultItemDrops.elements
                    .Where(item => item != null && item.gameObject != null && (predicate == null || predicate(item.gameObject)))
                    .ToList();

                if (loot.Count == 0) return null;

                var randomWGO = loot[UnityEngine.Random.Range(0, loot.Count)];
                return randomWGO.gameObject.GetComponent<PickupObject>();
            }
        }
    }
}
