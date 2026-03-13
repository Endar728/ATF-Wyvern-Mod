using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private static readonly Dictionary<object, TargetInfo> targetInfoCache = new Dictionary<object, TargetInfo>();
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
                    targetType.Name == "Ship" || targetType.Name == "GroundVehicle" ||
                    typeof(Unit).IsAssignableFrom(targetType))
                {
                    // Try to get team information
                    var teamProp = targetType.GetProperty("team", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? 
                                   targetType.GetProperty("Team", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var friendlyProp = targetType.GetProperty("isFriendly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? 
                                       targetType.GetProperty("IsFriendly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    
                    if (friendlyProp != null)
                    {
                        var friendlyValue = friendlyProp.GetValue(target);
                        if (friendlyValue is bool isFriendly)
                        {
                            return isFriendly;
                        }
                    }
                    
                    // If we can't determine team, allow it (user can disable if needed)
                    // This allows info gathering on all units when safe slot is enabled
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
                    targetType.Name == "Ship" || targetType.Name == "GroundVehicle" ||
                    typeof(Unit).IsAssignableFrom(targetType))
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
    /// </summary>
    [HarmonyPatch(typeof(TrackingInfo), "UpdateInfo")]
    static class TrackingInfoPatch
    {
        static void Postfix(TrackingInfo __instance, GlobalPosition position)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return;
            if (__instance == null) return;

            try
            {
                if (__instance.TryGetUnit(out Unit unit))
                {
                    // If this is a safe target, ensure tracking info is available
                    if (MasterSafeSlot.IsSafeTarget(unit))
                    {
                        Plugin.Log.LogDebug("[MasterSafeSlot] Tracking info available for safe target");
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
                        foreach (var method in type.GetMethods(BindingFlags.Public | 
                                                                 BindingFlags.Instance | 
                                                                 BindingFlags.NonPublic))
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
                
                Plugin.Log.LogDebug("[MasterSafeSlot] No weapon lock check method found - feature may work partially");
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
                        Plugin.Log.LogDebug("[MasterSafeSlot] Allowing info lock on safe target");
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
    /// Helper MonoBehaviour to continuously update HUD display for safe targets
    /// </summary>
    public class MasterSafeSlotHelper : MonoBehaviour
    {
        private CombatHUD combatHUD;
        private Text targetInfoText;
        private FieldInfo targetInfoField;
        private PropertyInfo targetInfoProp;
        private float lastUpdateTime = 0f;
        private const float UPDATE_INTERVAL = 0.1f; // Update every 100ms

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            FindHUDComponents();
        }

        void FindHUDComponents()
        {
            try
            {
                combatHUD = GameBindings.UI.GetCombatHUD(silent: true);
                if (combatHUD == null)
                {
                    Plugin.Log.LogDebug("[MasterSafeSlot] CombatHUD not found yet");
                    return;
                }

                var hudType = combatHUD.GetType();
                
                // Try to find targetInfo field or property
                targetInfoField = hudType.GetField("targetInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                                 hudType.GetField("TargetInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                                 hudType.GetField("targetInfoText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                                 hudType.GetField("targetText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                
                if (targetInfoField == null)
                {
                    targetInfoProp = hudType.GetProperty("targetInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                                     hudType.GetProperty("TargetInfo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                                     hudType.GetProperty("targetInfoText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                if (targetInfoField != null)
                {
                    var textObj = targetInfoField.GetValue(combatHUD);
                    if (textObj is Text text)
                    {
                        targetInfoText = text;
                        Plugin.Log.LogInfo("[MasterSafeSlot] Found targetInfo Text field");
                    }
                    else
                    {
                        Plugin.Log.LogDebug($"[MasterSafeSlot] targetInfo field is not a Text component, type: {textObj?.GetType().Name ?? "null"}");
                    }
                }
                else if (targetInfoProp != null)
                {
                    var textObj = targetInfoProp.GetValue(combatHUD);
                    if (textObj is Text text)
                    {
                        targetInfoText = text;
                        Plugin.Log.LogInfo("[MasterSafeSlot] Found targetInfo Text property");
                    }
                }
                else
                {
                    // Try to find Text components in children
                    var textComponents = combatHUD.GetComponentsInChildren<Text>(true);
                    foreach (var text in textComponents)
                    {
                        if (text.name.Contains("Target") || text.name.Contains("Info"))
                        {
                            targetInfoText = text;
                            Plugin.Log.LogInfo($"[MasterSafeSlot] Found Text component in children: {text.name}");
                            break;
                        }
                    }
                }

                if (targetInfoText == null)
                {
                    Plugin.Log.LogWarning("[MasterSafeSlot] Could not find targetInfo Text component - feature may not display correctly");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error finding HUD components: {ex.Message}");
            }
        }

        void Update()
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value)
            {
                if (targetInfoText != null && targetInfoText.text.Contains("SAFE TARGET"))
                {
                    targetInfoText.text = "";
                }
                return;
            }

            // Update at intervals to avoid excessive calculations
            if (Time.timeSinceLevelLoad - lastUpdateTime < UPDATE_INTERVAL)
            {
                return;
            }
            lastUpdateTime = Time.timeSinceLevelLoad;

            try
            {
                // Re-find HUD if needed
                if (combatHUD == null || targetInfoText == null)
                {
                    FindHUDComponents();
                }

                if (combatHUD == null || targetInfoText == null)
                {
                    return;
                }

                // Get current target
                var aircraft = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                var targetList = GameBindings.Player.TargetList.GetTargets(silent: true);

                if (aircraft == null || targetList == null || targetList.Count == 0)
                {
                    // Clear display if no target
                    if (targetInfoText.text.Contains("SAFE TARGET"))
                    {
                        targetInfoText.text = "";
                    }
                    return;
                }

                var currentTarget = targetList[0];
                if (currentTarget is Unit unitTarget && MasterSafeSlot.IsSafeTarget(unitTarget))
                {
                    // Get observer position
                    Vector3 observerPos = aircraft.transform.position;

                    var info = MasterSafeSlot.GetTargetInfo(unitTarget, observerPos);
                    if (info != null)
                    {
                        // Update HUD text
                        string displayText = $"SAFE TARGET\nRange: {info.Range:F0}m\nBearing: {info.Bearing:F0}°\nSpeed: {info.Speed:F0}m/s";
                        
                        if (targetInfoText.text != displayText)
                        {
                            targetInfoText.text = displayText;
                            targetInfoText.gameObject.SetActive(true);
                            Plugin.Log.LogDebug($"[MasterSafeSlot] Updated HUD display for safe target: {unitTarget.name}");
                        }
                    }
                }
                else
                {
                    // Clear display if target is not safe
                    if (targetInfoText.text.Contains("SAFE TARGET"))
                    {
                        targetInfoText.text = "";
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error in Update: {ex.Message}\n{ex.StackTrace}");
            }
        }

        void OnDestroy()
        {
            MasterSafeSlot.ClearCache();
        }
    }

    /// <summary>
    /// Patch HUD to display target info even without full weapon lock for safe targets
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
    static class CombatHUDShowTargetInfoPatch
    {
        static void Postfix(CombatHUD __instance, ref bool __result)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return;
            if (__instance == null) return;

            try
            {
                // If the original method returned false (no target info shown),
                // try to display info for a safe target.
                if (!__result)
                {
                    // Use GameBindings for safer access
                    var aircraft = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                    var targetList = GameBindings.Player.TargetList.GetTargets(silent: true);

                    if (aircraft == null || targetList == null || targetList.Count == 0)
                    {
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
                            // The helper MonoBehaviour will handle the display update
                            __result = true; // Indicate that target info is now shown
                            Plugin.Log.LogDebug($"[MasterSafeSlot] Safe target detected: {unitTarget.name}, helper will update display");
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
