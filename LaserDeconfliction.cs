using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ATFWyvernMod
{
    /// <summary>
    /// Smart laser designator deconfliction system.
    /// If multiple players have the same targets selected, distributes unique targets to each player's lasers.
    /// </summary>
    public static class LaserDeconfliction
    {
        // Track selected targets per player
        private static Dictionary<int, HashSet<object>> playerSelectedTargets = new Dictionary<int, HashSet<object>>();
        
        // Track laser assignments per player
        private static Dictionary<int, List<object>> playerLaserAssignments = new Dictionary<int, List<object>>();

        /// <summary>
        /// Updates target selection for a player and redistributes laser assignments
        /// </summary>
        public static void UpdatePlayerTargets(int playerId, List<object> selectedTargets)
        {
            if (!Plugin.cfgLaserDeconfliction.Value) return;

            // Update player's selected targets
            if (!playerSelectedTargets.ContainsKey(playerId))
            {
                playerSelectedTargets[playerId] = new HashSet<object>();
            }
            playerSelectedTargets[playerId].Clear();
            foreach (var target in selectedTargets)
            {
                playerSelectedTargets[playerId].Add(target);
            }

            // Redistribute targets among all players
            RedistributeLaserTargets();
        }

        /// <summary>
        /// Redistributes targets so each player's lasers get unique targets
        /// </summary>
        private static void RedistributeLaserTargets()
        {
            // Get all unique targets across all players
            HashSet<object> allTargets = new HashSet<object>();
            foreach (var playerTargets in playerSelectedTargets.Values)
            {
                foreach (var target in playerTargets)
                {
                    allTargets.Add(target);
                }
            }

            // Clear previous assignments
            foreach (var playerId in playerLaserAssignments.Keys.ToList())
            {
                playerLaserAssignments[playerId].Clear();
            }

            // Distribute targets round-robin style
            var targetList = allTargets.ToList();
            var playerIds = playerSelectedTargets.Keys.ToList();
            
            for (int i = 0; i < targetList.Count; i++)
            {
                int playerIndex = i % playerIds.Count;
                int playerId = playerIds[playerIndex];
                
                if (!playerLaserAssignments.ContainsKey(playerId))
                {
                    playerLaserAssignments[playerId] = new List<object>();
                }
                
                // Only assign if this player has this target selected
                if (playerSelectedTargets[playerId].Contains(targetList[i]))
                {
                    playerLaserAssignments[playerId].Add(targetList[i]);
                }
            }

            Plugin.Log.LogInfo($"[LaserDeconfliction] Redistributed {targetList.Count} targets among {playerIds.Count} players");
        }

        /// <summary>
        /// Gets the laser targets assigned to a specific player
        /// </summary>
        public static List<object> GetPlayerLaserTargets(int playerId)
        {
            if (playerLaserAssignments.ContainsKey(playerId))
            {
                return new List<object>(playerLaserAssignments[playerId]);
            }
            return new List<object>();
        }

        /// <summary>
        /// Clears all tracking data (call on scene unload)
        /// </summary>
        public static void Clear()
        {
            playerSelectedTargets.Clear();
            playerLaserAssignments.Clear();
        }
    }

    // === Helper MonoBehaviour for polling selected targets ===
    
    /// <summary>
    /// Helper class that polls DynamicMap for selected icons and updates laser deconfliction
    /// </summary>
    public class LaserDeconflictionHelper : MonoBehaviour
    {
        private List<object> lastSelectedTargets = new List<object>();
        private float lastUpdateTime = 0f;
        private const float UPDATE_INTERVAL = 0.5f; // Update every 500ms

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (!Plugin.cfgLaserDeconfliction.Value) return;

            // Throttle updates
            if (Time.timeSinceLevelLoad - lastUpdateTime < UPDATE_INTERVAL) return;
            lastUpdateTime = Time.timeSinceLevelLoad;

            try
            {
                // Try to get DynamicMap instance
                var mapType = typeof(DynamicMap);
                var mapInstance = SceneSingleton<DynamicMap>.i;
                if (mapInstance == null) return;

                // Try to get selectedIcons property via reflection
                var selectedIconsProp = mapType.GetProperty("selectedIcons");
                if (selectedIconsProp == null) return;

                var selectedIcons = selectedIconsProp.GetValue(mapInstance);
                if (selectedIcons == null) return;

                // Check if it's a List<MapIcon>
                if (selectedIcons is System.Collections.IList iconList)
                {
                    List<object> currentTargets = new List<object>();
                    foreach (var icon in iconList)
                    {
                        if (icon != null)
                        {
                            var iconType = icon.GetType();
                            var unitProp = iconType.GetProperty("unit");
                            if (unitProp != null)
                            {
                                var unit = unitProp.GetValue(icon);
                                if (unit != null)
                                {
                                    currentTargets.Add(unit);
                                }
                            }
                        }
                    }

                    // Only update if selection changed
                    if (!ListsEqual(currentTargets, lastSelectedTargets))
                    {
                        lastSelectedTargets = new List<object>(currentTargets);
                        
                        // Get player ID (assuming local player is 0)
                        int playerId = 0;
                        
                        if (currentTargets.Count > 0)
                        {
                            LaserDeconfliction.UpdatePlayerTargets(playerId, currentTargets);
                            Plugin.Log.LogDebug($"[LaserDeconfliction] Tracked {currentTargets.Count} selected targets for player {playerId}");
                        }
                    }
                }
            }
            catch
            {
                // Silently fail - this is expected if DynamicMap isn't available yet
            }
        }

        private bool ListsEqual(List<object> list1, List<object> list2)
        {
            if (list1.Count != list2.Count) return false;
            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] != list2[i]) return false;
            }
            return true;
        }
    }

    // === Harmony Patches ===

    /// <summary>
    /// Patch LaserDesignator.LaseTargets() to apply deconflicted targets
    /// </summary>
    [HarmonyPatch(typeof(LaserDesignator), "LaseTargets")]
    static class LaserDesignatorLaseTargetsPatch
    {
        static void Prefix(LaserDesignator __instance)
        {
            if (!Plugin.cfgLaserDeconfliction.Value) return;

            try
            {
                // Get player ID (assuming local player is 0 for now)
                int playerId = 0;

                // Get deconflicted targets for this player
                var deconflictedTargets = LaserDeconfliction.GetPlayerLaserTargets(playerId);

                if (deconflictedTargets.Count > 0)
                {
                    // Try to set the target list on the laser designator - try both property and field
                    var designatorType = typeof(LaserDesignator);
                    var targetListProp = designatorType.GetProperty("targetList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ?? 
                                         designatorType.GetProperty("TargetList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    var targetListField = designatorType.GetField("targetList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ??
                                          designatorType.GetField("TargetList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    
                    // Convert our object list to List<Unit>
                    var unitList = new List<Unit>();
                    foreach (var target in deconflictedTargets)
                    {
                        if (target is Unit unit)
                        {
                            unitList.Add(unit);
                        }
                    }
                    
                    if (unitList.Count > 0)
                    {
                        if (targetListProp != null)
                        {
                            targetListProp.SetValue(__instance, unitList);
                            Plugin.Log.LogDebug($"[LaserDeconfliction] Applied {unitList.Count} deconflicted targets to LaserDesignator via property");
                        }
                        else if (targetListField != null)
                        {
                            targetListField.SetValue(__instance, unitList);
                            Plugin.Log.LogDebug($"[LaserDeconfliction] Applied {unitList.Count} deconflicted targets to LaserDesignator via field");
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"[LaserDeconfliction] Could not find targetList property or field on LaserDesignator");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserDeconfliction] Error in LaserDesignator patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Patch WeaponManager.SetTargetList() to track selected targets and apply deconflicted targets
    /// Note: ReadOnlySpan is not available in .NET Framework 4.7.2, so we use a postfix to modify after
    /// </summary>
    [HarmonyPatch(typeof(WeaponManager), "SetTargetList")]
    static class WeaponManagerSetTargetListPatch
    {
        static void Postfix(WeaponManager __instance)
        {
            if (!Plugin.cfgLaserDeconfliction.Value) return;

            try
            {
                // Get player ID (assuming local player is 0 for now)
                int playerId = 0;

                // Use GameBindings to get target list
                var currentTargetList = GameBindings.Player.TargetList.GetTargets(silent: true);
                if (currentTargetList != null && currentTargetList.Count > 0)
                {
                    // Update the deconfliction system with the current player's selected targets
                    var targetObjects = currentTargetList.Cast<object>().ToList();
                    LaserDeconfliction.UpdatePlayerTargets(playerId, targetObjects);
                    Plugin.Log.LogDebug($"[LaserDeconfliction] Tracked {currentTargetList.Count} selected targets for player {playerId}");
                }

                // Then, get deconflicted targets for this player and apply them
                var deconflictedTargets = LaserDeconfliction.GetPlayerLaserTargets(playerId);

                if (deconflictedTargets.Count > 0)
                {
                    // Convert our object list to List<Unit>
                    var unitList = new List<Unit>();
                    foreach (var target in deconflictedTargets)
                    {
                        if (target is Unit unit)
                        {
                            unitList.Add(unit);
                        }
                    }
                    
                    if (unitList.Count > 0)
                    {
                        // Apply deconflicted targets by setting the targetList field via reflection
                        var managerType = typeof(WeaponManager);
                        var targetListField = managerType.GetField("targetList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (targetListField != null)
                        {
                            targetListField.SetValue(__instance, unitList);
                            Plugin.Log.LogDebug($"[LaserDeconfliction] Applied {unitList.Count} deconflicted targets to WeaponManager");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserDeconfliction] Error in WeaponManager patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
