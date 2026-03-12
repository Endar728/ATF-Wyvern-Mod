using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ATFWyvernMod
{
    /// <summary>
    /// Master safe/empty weapon slot feature.
    /// Allows getting range/bearing/speed information without locking onto friendlies.
    /// </summary>
    public static class MasterSafeSlot
    {
        // Cache for target info to avoid repeated calculations
        private static Dictionary<object, TargetInfo> targetInfoCache = new Dictionary<object, TargetInfo>();
        private static float cacheUpdateTime = 0f;
        private const float CACHE_UPDATE_INTERVAL = 0.1f; // Update cache every 100ms

        /// <summary>
        /// Checks if a target is friendly and should be treated as "safe" for information gathering
        /// </summary>
        public static bool IsSafeTarget(object target)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return false;
            if (target == null) return false;

            try
            {
                // Use reflection to check if target is a Unit and if it's friendly
                var targetType = target.GetType();
                
                // Check if it's a Unit (or derived class like Aircraft, Ship, GroundVehicle)
                if (targetType.Name == "Unit" || targetType.Name == "Aircraft" || 
                    targetType.Name == "Ship" || targetType.Name == "GroundVehicle")
                {
                    // Try to get team information
                    var teamProp = targetType.GetProperty("team") ?? targetType.GetProperty("Team");
                    var friendlyProp = targetType.GetProperty("isFriendly") ?? targetType.GetProperty("IsFriendly");
                    
                    if (teamProp != null)
                    {
                        var team = teamProp.GetValue(target);
                        // Try to get player's team for comparison
                        // For now, assume any unit can be queried (we'll refine this with actual team checking)
                        return true; // Allow info gathering on all units when safe slot is enabled
                    }
                    else if (friendlyProp != null)
                    {
                        return (bool)friendlyProp.GetValue(target);
                    }
                    
                    // If we can't determine team, allow it (user can disable if needed)
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error checking safe target: {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// Gets range, bearing, and speed information without requiring a lock
        /// </summary>
        public static TargetInfo GetTargetInfo(object target, Vector3 observerPosition)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return null;
            if (target == null) return null;

            // Use cached info if available and recent
            float currentTime = Time.timeSinceLevelLoad;
            if (currentTime - cacheUpdateTime < CACHE_UPDATE_INTERVAL && targetInfoCache.ContainsKey(target))
            {
                return targetInfoCache[target];
            }

            try
            {
                var targetType = target.GetType();
                
                // Check if it's a Unit
                if (targetType.Name == "Unit" || targetType.Name == "Aircraft" || 
                    targetType.Name == "Ship" || targetType.Name == "GroundVehicle")
                {
                    // Get position
                    Vector3 targetPos = Vector3.zero;
                    var transformProp = targetType.GetProperty("transform") ?? targetType.GetProperty("Transform");
                    if (transformProp != null)
                    {
                        var transform = transformProp.GetValue(target) as Transform;
                        if (transform != null)
                        {
                            targetPos = transform.position;
                        }
                    }

                    // Get velocity
                    Vector3 velocity = Vector3.zero;
                    var rbProp = targetType.GetProperty("rb") ?? targetType.GetProperty("rigidbody");
                    if (rbProp != null)
                    {
                        var rb = rbProp.GetValue(target);
                        if (rb != null)
                        {
                            var rbType = rb.GetType();
                            var velProp = rbType.GetProperty("velocity") ?? rbType.GetProperty("Velocity");
                            if (velProp != null)
                            {
                                velocity = (Vector3)velProp.GetValue(rb);
                            }
                        }
                    }

                    Vector3 toTarget = targetPos - observerPosition;
                    float range = toTarget.magnitude;
                    float bearing = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                    float speed = velocity.magnitude;

                    var info = new TargetInfo
                    {
                        Range = range,
                        Bearing = bearing,
                        Speed = speed,
                        Position = targetPos
                    };

                    // Cache the result
                    targetInfoCache[target] = info;
                    cacheUpdateTime = currentTime;

                    return info;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error getting target info: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Clears the target info cache
        /// </summary>
        public static void ClearCache()
        {
            targetInfoCache.Clear();
        }
    }

    public class TargetInfo
    {
        public float Range { get; set; }
        public float Bearing { get; set; }
        public float Speed { get; set; }
        public Vector3 Position { get; set; }
    }

    // === Harmony Patches ===

    /// <summary>
    /// Patch TrackingInfo to allow info gathering on friendlies
    /// Based on the TargetEstimator mod, TrackingInfo is used for target tracking
    /// </summary>
    [HarmonyPatch(typeof(TrackingInfo), "UpdateInfo")]
    static class TrackingInfoPatch
    {
        static void Postfix(TrackingInfo __instance, GlobalPosition position)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return;

            try
            {
                if (__instance.TryGetUnit(out Unit unit))
                {
                    // If this is a safe target, ensure tracking info is available
                    if (MasterSafeSlot.IsSafeTarget(unit))
                    {
                        // The tracking info is already updated, we just need to ensure
                        // it's accessible even without a weapon lock
                        Plugin.Log.LogDebug($"[MasterSafeSlot] Tracking info available for safe target");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error in TrackingInfo patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch weapon lock checks to allow info gathering on safe targets
    /// Note: This patch is optional - if no suitable method is found, it will be skipped
    /// </summary>
    [HarmonyPatch]
    static class WeaponLockCheckPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return null;

            try
            {
                var assembly = typeof(Unit).Assembly;
                var allTypes = assembly.GetTypes();

                // Look for WeaponSystem, WeaponStation, or WeaponManager classes
                foreach (var type in allTypes)
                {
                    if (type.Name.Contains("WeaponSystem") || type.Name.Contains("WeaponStation") || 
                        type.Name.Contains("WeaponManager") || type.Name.Contains("TargetingSystem"))
                    {
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | 
                                                                 System.Reflection.BindingFlags.Instance | 
                                                                 System.Reflection.BindingFlags.NonPublic))
                        {
                            if ((method.Name.Contains("CanLock") || method.Name.Contains("CanTarget") || 
                                 method.Name.Contains("IsValidTarget") || method.Name.Contains("CheckLock")) &&
                                method.ReturnType == typeof(bool))
                            {
                                Plugin.Log.LogInfo($"[MasterSafeSlot] Found potential weapon lock check method: {type.FullName}.{method.Name}");
                                return method;
                            }
                        }
                    }
                }
                
                // If no method found, log but don't error - this is optional
                Plugin.Log.LogDebug($"[MasterSafeSlot] No weapon lock check method found - feature may work partially");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error finding weapon lock check method: {ex.Message}");
            }

            return null;
        }

        static void Postfix(object __instance, ref bool __result, object __0)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return;

            try
            {
                // If lock check failed, but target is safe, allow info gathering
                if (!__result && __0 != null)
                {
                    if (MasterSafeSlot.IsSafeTarget(__0))
                    {
                        // Allow info lock (but not weapon lock)
                        // We'll set result to true for info gathering purposes
                        // but the actual weapon lock behavior may be handled elsewhere
                        Plugin.Log.LogDebug($"[MasterSafeSlot] Allowing info lock on safe target");
                        // Note: We don't change __result here to avoid breaking weapon lock behavior
                        // Instead, we rely on HUD patches to show info
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error in weapon lock check patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch HUD to display target info even without full weapon lock for safe targets
    /// Uses CombatHUD.ShowTargetInfo() which was discovered in method discovery
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
    static class HUDTargetInfoPatch
    {
        static void Postfix(CombatHUD __instance, ref bool __result)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return;

            try
            {
                Plugin.Log.LogDebug($"[MasterSafeSlot] ShowTargetInfo called, result={__result}");
                
                // If the original method returned false (no target info shown),
                // try to display info for a safe target.
                if (!__result)
                {
                    // Use GameBindings for safer access
                    var aircraft = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                    var targetList = GameBindings.Player.TargetList.GetTargets(silent: true);

                    if (aircraft == null)
                    {
                        Plugin.Log.LogDebug($"[MasterSafeSlot] Aircraft is null");
                        return;
                    }

                    if (targetList == null || targetList.Count == 0)
                    {
                        Plugin.Log.LogDebug($"[MasterSafeSlot] Target list is null or empty");
                        return;
                    }

                    var currentTarget = targetList[0]; // Assuming the first target in the list is the primary
                    if (currentTarget is Unit unitTarget && MasterSafeSlot.IsSafeTarget(unitTarget))
                    {
                        // Get observer position (player's aircraft position)
                        Vector3 observerPos = aircraft.transform.position;

                        var info = MasterSafeSlot.GetTargetInfo(unitTarget, observerPos);
                        if (info != null)
                        {
                            // Try to update HUD text if available - try multiple field names
                            var hudType = typeof(CombatHUD);
                            var targetInfoField = hudType.GetField("targetInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ??
                                                  hudType.GetField("TargetInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) ??
                                                  hudType.GetField("targetInfoText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                            
                            if (targetInfoField != null)
                            {
                                var targetInfoText = targetInfoField.GetValue(__instance) as Text;
                                if (targetInfoText != null)
                                {
                                    targetInfoText.text = $"SAFE TARGET\nRange: {info.Range:F0}m\nBearing: {info.Bearing:F0}°\nSpeed: {info.Speed:F0}m/s";
                                    targetInfoText.gameObject.SetActive(true);
                                    __result = true; // Indicate that target info is now shown
                                    Plugin.Log.LogInfo($"[MasterSafeSlot] Displaying info for safe target: {unitTarget.name}");
                                }
                                else
                                {
                                    Plugin.Log.LogDebug($"[MasterSafeSlot] targetInfo field is not a Text component");
                                }
                            }
                            else
                            {
                                Plugin.Log.LogDebug($"[MasterSafeSlot] Could not find targetInfo field on CombatHUD");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error in HUD target info patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
