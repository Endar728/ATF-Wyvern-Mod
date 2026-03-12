using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ATFWyvernMod
{
    /// <summary>
    /// Makes the laser reticle on the target camera more visible in night vision mode.
    /// </summary>
    public static class LaserReticleVisibility
    {
        // Enhanced color for night vision mode (bright cyan/white)
        private static readonly Color NightVisionReticleColor = new Color(0.5f, 1.0f, 1.0f, 1.0f); // Bright cyan
        private static readonly Color NormalReticleColor = new Color(1.0f, 0.0f, 0.0f, 1.0f); // Red (default)

        private static readonly TraverseCache<TargetCam, bool> usingIRCache = new("usingIR");

        /// <summary>
        /// Checks if night vision mode is active
        /// </summary>
        public static bool IsNightVisionActive()
        {
            try
            {
                var targetCam = GameBindings.Player.Aircraft.GetTargetCam(silent: true);
                if (targetCam == null) return false;

                // Try method first
                var usingIRMethod = typeof(TargetCam).GetMethod("UsingIR", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (usingIRMethod != null)
                {
                    return (bool)usingIRMethod.Invoke(targetCam, null);
                }

                // Try cached field access
                try
                {
                    var usingIR = usingIRCache.GetValue(targetCam, silent: true);
                    // Traverse.GetValue<bool>() returns bool, not bool?, so just use it directly
                    if (usingIR is bool value)
                    {
                        return value;
                    }
                }
                catch { }

                // Fallback: try direct field access
                var irField = typeof(TargetCam).GetField("usingIR", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) ??
                              typeof(TargetCam).GetField("UsingIR", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (irField != null)
                {
                    return (bool)irField.GetValue(targetCam);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogDebug($"[LaserReticleVisibility] Error checking night vision: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets the appropriate reticle color based on night vision state
        /// </summary>
        public static Color GetReticleColor(bool isNightVision)
        {
            if (isNightVision)
            {
                return NightVisionReticleColor;
            }
            return NormalReticleColor;
        }

        /// <summary>
        /// Clears cached reticle components
        /// </summary>
        public static void ClearCache()
        {
            // Clear any cached components if needed
        }
    }

    /// <summary>
    /// Helper MonoBehaviour to manage laser reticle visibility updates
    /// </summary>
    public class LaserReticleVisibilityHelper : MonoBehaviour
    {
        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Plugin.Log.LogInfo("[LaserReticleVisibilityHelper] Helper initialized");
        }

        void Update()
        {
            if (!Plugin.cfgLaserReticleVisibility.Value) return;

            // The Harmony patches handle the actual reticle color updates
            // This helper can be used for additional runtime checks if needed
        }
    }

    // === Harmony Patches ===

    /// <summary>
    /// Patch HUDLaserGuidedState to enhance reticle visibility in night vision
    /// This class likely handles the laser reticle display on the HUD/target camera
    /// </summary>
    [HarmonyPatch]
    static class HUDLaserGuidedStatePatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            try
            {
                var assembly = typeof(Unit).Assembly;
                var hudLaserType = assembly.GetType("HUDLaserGuidedState");
                if (hudLaserType != null)
                {
                    // Try UpdateWeaponDisplay first (from logs, this is the actual method name)
                    var updateWeaponDisplayMethod = hudLaserType.GetMethod("UpdateWeaponDisplay", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (updateWeaponDisplayMethod != null)
                    {
                        Plugin.Log.LogInfo($"[LaserReticleVisibility] Found UpdateWeaponDisplay method on HUDLaserGuidedState");
                        return updateWeaponDisplayMethod;
                    }

                    // Fallback: Look for methods that update/display the reticle
                    foreach (var method in hudLaserType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        if ((method.Name.Contains("Update") || method.Name.Contains("Display") || method.Name.Contains("Show")) &&
                            !method.IsSpecialName)
                        {
                            Plugin.Log.LogInfo($"[LaserReticleVisibility] Found potential method: {hudLaserType.FullName}.{method.Name}");
                            return method;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserReticleVisibility] Error finding HUDLaserGuidedState method: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        static void Postfix(object __instance, Aircraft aircraft, System.Collections.Generic.List<Unit> targetList)
        {
            if (!Plugin.cfgLaserReticleVisibility.Value) return;

            try
            {
                bool isNightVision = LaserReticleVisibility.IsNightVisionActive();
                if (!isNightVision) return;

                // Check if there's a lased target
                bool hasLasedTarget = false;
                if (targetList != null && targetList.Count > 0)
                {
                    var laserDesignatorType = typeof(LaserDesignator);
                    var laserDesignators = Object.FindObjectsOfType(laserDesignatorType);
                    
                    foreach (var designator in laserDesignators)
                    {
                        if (designator == null) continue;
                        
                        var lasedTargetsMethod = laserDesignatorType.GetMethod("GetLasedTargets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (lasedTargetsMethod != null)
                        {
                            var targets = lasedTargetsMethod.Invoke(designator, null) as System.Collections.IList;
                            if (targets != null && targets.Count > 0)
                            {
                                hasLasedTarget = true;
                                break;
                            }
                        }
                    }
                }

                if (!hasLasedTarget) return;

                var instanceType = __instance.GetType();
                var instanceGO = (__instance as MonoBehaviour)?.gameObject;
                if (instanceGO == null) return;
                
                // Search for reticle UI elements in the GameObject hierarchy
                var allImages = instanceGO.GetComponentsInChildren<Image>(true);
                var allSprites = instanceGO.GetComponentsInChildren<SpriteRenderer>(true);
                
                bool foundReticle = false;
                foreach (var image in allImages)
                {
                    if (image == null) continue;
                    string name = image.name.ToLower();
                    if (name.Contains("reticle") || name.Contains("laser") || name.Contains("designator") || name.Contains("crosshair"))
                    {
                        image.color = LaserReticleVisibility.GetReticleColor(true);
                        foundReticle = true;
                        Plugin.Log.LogDebug($"[LaserReticleVisibility] Enhanced reticle Image: {image.name}");
                    }
                }
                
                foreach (var sprite in allSprites)
                {
                    if (sprite == null) continue;
                    string name = sprite.name.ToLower();
                    if (name.Contains("reticle") || name.Contains("laser") || name.Contains("crosshair"))
                    {
                        sprite.color = LaserReticleVisibility.GetReticleColor(true);
                        foundReticle = true;
                        Plugin.Log.LogDebug($"[LaserReticleVisibility] Enhanced reticle SpriteRenderer: {sprite.name}");
                    }
                }
                
                // Also check fields/properties
                var fields = instanceType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    var value = field.GetValue(__instance);
                    if (value == null) continue;

                    if (value is Image image)
                    {
                        string fieldName = field.Name.ToLower();
                        if (fieldName.Contains("reticle") || fieldName.Contains("laser") || fieldName.Contains("designator") ||
                            (image.name != null && (image.name.ToLower().Contains("reticle") || image.name.ToLower().Contains("laser"))))
                        {
                            image.color = LaserReticleVisibility.GetReticleColor(true);
                            foundReticle = true;
                            Plugin.Log.LogDebug($"[LaserReticleVisibility] Enhanced reticle field: {field.Name}");
                        }
                    }
                    else if (value is SpriteRenderer spriteRenderer)
                    {
                        string fieldName = field.Name.ToLower();
                        if (fieldName.Contains("reticle") || fieldName.Contains("laser") ||
                            (spriteRenderer.name != null && spriteRenderer.name.ToLower().Contains("reticle")))
                        {
                            spriteRenderer.color = LaserReticleVisibility.GetReticleColor(true);
                            foundReticle = true;
                            Plugin.Log.LogDebug($"[LaserReticleVisibility] Enhanced reticle field: {field.Name}");
                        }
                    }
                }
                
                if (!foundReticle)
                {
                    Plugin.Log.LogDebug($"[LaserReticleVisibility] No reticle components found in HUDLaserGuidedState (this is normal if not lasing)");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserReticleVisibility] Error in HUDLaserGuidedState patch: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Patch TargetCam to enhance reticle visibility in night vision
    /// TargetCam likely handles the target camera display including the reticle
    /// </summary>
    [HarmonyPatch(typeof(TargetCam), "Update")]
    static class TargetCamUpdatePatch
    {
        static void Postfix(TargetCam __instance)
        {
            if (!Plugin.cfgLaserReticleVisibility.Value) return;

            try
            {
                bool isNightVision = LaserReticleVisibility.IsNightVisionActive();
                if (!isNightVision) return;

                // Check if there's a lased target
                var laserDesignatorType = typeof(LaserDesignator);
                var laserDesignators = Object.FindObjectsOfType(laserDesignatorType);
                
                bool hasLasedTarget = false;
                foreach (var designator in laserDesignators)
                {
                    if (designator == null) continue;
                    
                    var lasedTargetsMethod = laserDesignatorType.GetMethod("GetLasedTargets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (lasedTargetsMethod != null)
                    {
                        var targets = lasedTargetsMethod.Invoke(designator, null) as System.Collections.IList;
                        if (targets != null && targets.Count > 0)
                        {
                            hasLasedTarget = true;
                            break;
                        }
                    }
                }

                if (!hasLasedTarget) return;

                // Try to find reticle UI elements in TargetCam
                var camType = typeof(TargetCam);
                var fields = camType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                
                foreach (var field in fields)
                {
                    var value = field.GetValue(__instance);
                    if (value == null) continue;

                    if (value is Image image)
                    {
                        if (field.Name.ToLower().Contains("reticle") || 
                            field.Name.ToLower().Contains("laser") ||
                            field.Name.ToLower().Contains("crosshair") ||
                            (image.name != null && (image.name.ToLower().Contains("reticle") || image.name.ToLower().Contains("laser"))))
                        {
                            image.color = LaserReticleVisibility.GetReticleColor(true);
                            // Also try to increase brightness by adjusting alpha or using a brighter color
                            var enhancedColor = LaserReticleVisibility.GetReticleColor(true);
                            enhancedColor.a = 1.0f; // Full opacity
                            image.color = enhancedColor;
                        }
                    }
                    else if (value is SpriteRenderer spriteRenderer)
                    {
                        if (field.Name.ToLower().Contains("reticle") || 
                            field.Name.ToLower().Contains("laser") ||
                            (spriteRenderer.name != null && spriteRenderer.name.ToLower().Contains("reticle")))
                        {
                            var enhancedColor = LaserReticleVisibility.GetReticleColor(true);
                            enhancedColor.a = 1.0f;
                            spriteRenderer.color = enhancedColor;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserReticleVisibility] Error in TargetCam patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to enhance any UI element that displays laser reticle
    /// This is a more general approach that patches UI update methods
    /// </summary>
    [HarmonyPatch]
    static class LaserReticleUIPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            try
            {
                var assembly = typeof(Unit).Assembly;
                var allTypes = assembly.GetTypes();

                // Look for classes that might handle laser reticle display
                // Exclude HUDLaserGuidedState since it's already handled by HUDLaserGuidedStatePatch
                foreach (var type in allTypes)
                {
                    if (type.Name == "HUDLaserGuidedState") continue; // Skip, already patched
                    
                    if (type.Name.Contains("Laser") && (type.Name.Contains("HUD") || type.Name.Contains("Display") || type.Name.Contains("State")))
                    {
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            if ((method.Name.Contains("Update") || method.Name.Contains("Display") || method.Name.Contains("Show")) &&
                                !method.IsSpecialName)
                            {
                                Plugin.Log.LogInfo($"[LaserReticleVisibility] Found potential UI method: {type.FullName}.{method.Name}");
                                return method;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserReticleVisibility] Error finding laser reticle UI method: {ex.Message}\n{ex.StackTrace}");
            }

            return null;
        }

        static void Postfix(object __instance)
        {
            if (!Plugin.cfgLaserReticleVisibility.Value) return;

            try
            {
                bool isNightVision = LaserReticleVisibility.IsNightVisionActive();
                if (!isNightVision) return;

                // Search for reticle-related UI elements
                var instanceType = __instance.GetType();
                var fields = instanceType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                
                foreach (var field in fields)
                {
                    var value = field.GetValue(__instance);
                    if (value == null) continue;

                    if (value is Image image)
                    {
                        string fieldName = field.Name.ToLower();
                        if (fieldName.Contains("reticle") || fieldName.Contains("laser") || fieldName.Contains("designator"))
                        {
                            image.color = LaserReticleVisibility.GetReticleColor(true);
                            Plugin.Log.LogDebug($"[LaserReticleVisibility] Enhanced UI reticle: {field.Name}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[LaserReticleVisibility] Error in laser reticle UI patch: {ex.Message}");
            }
        }
    }
}
