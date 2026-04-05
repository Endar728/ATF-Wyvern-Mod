using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ATFWyvernMod
{
    /// <summary>
    /// Master safe/empty weapon slot: HUD strip when primary target is friendly; blocks release only when the weapon's target unit is friendly.
    /// Does not override <see cref="CombatHUD.ShowTargetInfo"/> (avoids desync with vanilla target label layout).
    /// </summary>
    public static class MasterSafeSlot
    {
        // Cache for target info to avoid repeated calculations
        private static readonly Dictionary<object, TargetInfo> targetInfoCache = new Dictionary<object, TargetInfo>();
        private static float cacheUpdateTime = 0f;
        private const float CACHE_UPDATE_INTERVAL = 0.1f; // Update cache every 100ms

        /// <summary>
        /// True when <paramref name="unit"/> is on the same <see cref="FactionHQ"/> as the local player's aircraft.
        /// Unknown or null HQ is not treated as friendly (avoids blocking weapons on neutrals).
        /// </summary>
        public static bool IsFriendlyToLocal(Unit unit)
        {
            if (unit == null) return false;
            Aircraft local;
            try
            {
                local = GameBindings.Player.Aircraft.GetAircraft(silent: true);
            }
            catch
            {
                return false;
            }

            if (local == null) return false;

            FactionHQ hqUnit;
            FactionHQ hqLocal;
            try
            {
                hqUnit = unit.NetworkHQ;
                hqLocal = local.NetworkHQ;
            }
            catch
            {
                return false;
            }

            if (hqUnit == null || hqLocal == null) return false;
            return hqUnit == hqLocal;
        }

        /// <summary>
        /// Friendly contact for master-safe HUD (same faction as local aircraft).
        /// </summary>
        public static bool IsSafeTarget(object target)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return false;
            return target is Unit u && IsFriendlyToLocal(u);
        }

        /// <summary>
        /// True when <paramref name="owner"/> is the aircraft controlled by this client (uses <see cref="Aircraft.Player"/> when available).
        /// </summary>
        public static bool IsLocalPlayersAircraft(Unit owner)
        {
            if (owner == null || !(owner is Aircraft ac)) return false;
            try
            {
                var p = ac.Player;
                if (p != null)
                    return p.IsLocalPlayer;
            }
            catch
            {
                // Fall through
            }

            try
            {
                var local = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                return local != null && ac == local;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// When true, local weapon release should be skipped because this shot is explicitly aimed at a same-faction unit.
        /// Does not use weapon list slot 0 alone — a friendly in slot 0 must not block guns/missiles aimed at a hostile or unguided shots.
        /// </summary>
        public static bool ShouldInhibitLocalWeaponRelease(Unit owner, Unit weaponTarget)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return false;
            if (owner == null || !IsLocalPlayersAircraft(owner)) return false;

            if (weaponTarget != null && IsFriendlyToLocal(weaponTarget))
                return true;

            return false;
        }

        /// <summary>
        /// Gets range, bearing, and speed information without requiring a lock.
        /// When <paramref name="observerBody"/> is set, bearing is horizontal relative angle (°) from observer forward, −180…180.
        /// </summary>
        public static TargetInfo GetTargetInfo(object target, Vector3 observerPosition, Transform observerBody = null)
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value) return null;
            if (target == null) return null;

            float currentTime = Time.timeSinceLevelLoad;
            // Bearing vs. ownship heading changes every frame — do not reuse cache for HUD-relative geometry
            bool canUseCache = observerBody == null &&
                               currentTime - cacheUpdateTime < CACHE_UPDATE_INTERVAL &&
                               targetInfoCache.ContainsKey(target);
            if (canUseCache)
                return targetInfoCache[target];

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

                    // Get velocity (Unit.rb is often a field, not a property — match game + TTI helpers)
                    Vector3 velocity = Vector3.zero;
                    if (target is Unit unitRef && unitRef.rb != null)
                    {
                        velocity = unitRef.rb.velocity;
                    }
                    else
                    {
                        const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        Rigidbody rbComp = null;
                        foreach (var name in new[] { "rb", "rigidbody", "rigidBody", "Rb" })
                        {
                            var p = targetType.GetProperty(name, bf);
                            if (p != null && typeof(Rigidbody).IsAssignableFrom(p.PropertyType))
                            {
                                rbComp = p.GetValue(target) as Rigidbody;
                                if (rbComp != null) break;
                            }
                            var f = targetType.GetField(name, bf);
                            if (f != null && typeof(Rigidbody).IsAssignableFrom(f.FieldType))
                            {
                                rbComp = f.GetValue(target) as Rigidbody;
                                if (rbComp != null) break;
                            }
                        }
                        if (rbComp != null)
                            velocity = rbComp.velocity;
                    }

                    Vector3 toTarget = targetPos - observerPosition;
                    float range = toTarget.magnitude;
                    float bearing;
                    if (observerBody != null)
                    {
                        Vector3 flatFwd = Vector3.ProjectOnPlane(observerBody.forward, Vector3.up);
                        Vector3 flatTo = Vector3.ProjectOnPlane(toTarget, Vector3.up);
                        if (flatFwd.sqrMagnitude < 1e-8f)
                            flatFwd = Vector3.forward;
                        else
                            flatFwd.Normalize();
                        if (flatTo.sqrMagnitude < 1e-8f)
                            bearing = 0f;
                        else
                            bearing = Vector3.SignedAngle(flatFwd, flatTo.normalized, Vector3.up);
                    }
                    else
                    {
                        bearing = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                    }

                    float speed = velocity.magnitude;

                    var info = new TargetInfo
                    {
                        Range = range,
                        Bearing = bearing,
                        Speed = speed,
                        Position = targetPos
                    };

                    if (observerBody == null)
                    {
                        targetInfoCache[target] = info;
                        cacheUpdateTime = currentTime;
                    }

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
            // Intentionally empty: previous version only logged IsSafeTarget every UpdateInfo tick (BepInEx spam).
            // Safe-target behavior is handled in weapon/lock patches elsewhere in this file.
        }
    }

    /// <summary>
    /// Patch weapon lock checks to allow info gathering on safe targets
    /// Note: This patch is optional - if the target method can't be found, the mod will still work
    /// </summary>
    [HarmonyPatch]
    static class WeaponLockCheckPatch
    {
        static bool Prepare()
        {
            // Only try to apply this patch if Master Safe Slot is enabled
            // This prevents errors during patch discovery if the feature is disabled
            // Always return false to skip this optional patch - it's not critical for functionality
            // The Master Safe Slot feature works through MasterSafeSlotHelper and other patches
            return false; // Disable this patch entirely as it's optional and causing issues
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            // Wrap everything in a top-level try-catch to ensure we never throw
            try
            {
                try
                {
                    var assembly = typeof(Unit).Assembly;
                    if (assembly == null)
                    {
                        return null;
                    }
                    
                    // Handle ReflectionTypeLoadException that can occur with GetTypes()
                    System.Type[] allTypes = null;
                    try
                    {
                        allTypes = assembly.GetTypes();
                    }
                    catch (System.Reflection.ReflectionTypeLoadException ex)
                    {
                        // If some types fail to load, use the successfully loaded ones
                        if (ex.Types != null)
                        {
                            allTypes = ex.Types.Where(t => t != null).ToArray();
                        }
                    }
                    catch
                    {
                        // Any other exception getting types - just return null
                        return null;
                    }

                    if (allTypes == null || allTypes.Length == 0)
                    {
                        return null;
                    }

                    // Look for WeaponSystem, WeaponStation, or WeaponManager classes
                    foreach (var type in allTypes)
                    {
                        if (type == null) continue;
                        
                        try
                        {
                            if (type.Name.Contains("WeaponSystem") || type.Name.Contains("WeaponStation") || 
                                type.Name.Contains("WeaponManager") || type.Name.Contains("TargetingSystem"))
                            {
                                try
                                {
                                    var methods = type.GetMethods(BindingFlags.Public | 
                                                                  BindingFlags.Instance | 
                                                                  BindingFlags.NonPublic);
                                    
                                    if (methods == null) continue;
                                    
                                    foreach (var method in methods)
                                    {
                                        if (method == null) continue;
                                        
                                        try
                                        {
                                            if ((method.Name.Contains("CanLock") || method.Name.Contains("CanTarget") || 
                                                 method.Name.Contains("IsValidTarget") || method.Name.Contains("CheckLock")) &&
                                                method.ReturnType == typeof(bool))
                                            {
                                                Plugin.Log.LogInfo($"[MasterSafeSlot] Found potential weapon lock check method: {type.FullName}.{method.Name}");
                                                return method;
                                            }
                                        }
                                        catch
                                        {
                                            // Skip this method if there's any issue
                                            continue;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Skip this type if we can't get its methods
                                    continue;
                                }
                            }
                        }
                        catch
                        {
                            // Skip this type entirely
                            continue;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // Log but don't throw - just return null
                    Plugin.Log.LogWarning($"[MasterSafeSlot] Error finding weapon lock check method: {ex.Message}");
                }
            }
            catch
            {
                // Catch absolutely everything - even unexpected exceptions
                // This ensures we never throw from TargetMethod
            }

            // Return null to indicate this patch should be skipped
            // Harmony will gracefully skip patches when TargetMethod returns null
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
    /// Clean implementation that creates its own UI element (like TimeToImpact)
    /// </summary>
    public class MasterSafeSlotHelper : MonoBehaviour
    {
        private static readonly Color HudNeonLime = new Color(162f / 255f, 1f, 0f, 1f);

        private Canvas _canvas;
        private GameObject displayGO;
        private Text targetInfoText;
        private RectTransform displayRect;
        private float lastTargetResolveTime = 0f;
        private const float TARGET_RESOLVE_INTERVAL = 0.12f; // Re-check weapon target list; not every frame

        private bool _hudTypographyBound;
        private float _nextTypographyAttemptTime;

        // Smoothing for values to prevent jittery display
        private float smoothedRange = 0f;
        private float smoothedBearing = 0f;
        private float smoothedSpeed = 0f;
        private const float SMOOTHING_FACTOR = 0.18f;

        private Unit currentSafeTarget = null;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            CreateDisplay();
        }

        void OnEnable()
        {
            _hudTypographyBound = false;
            _nextTypographyAttemptTime = 0f;
        }

        void CreateDisplay()
        {
            try
            {
                if (_canvas == null)
                {
                    var canvasGO = new GameObject("[ATFWyvernMod_MasterSafeSlot_Canvas]");
                    canvasGO.transform.SetParent(transform, false);
                    _canvas = canvasGO.AddComponent<Canvas>();
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _canvas.sortingOrder = 3900;
                    var scaler = canvasGO.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                    canvasGO.AddComponent<GraphicRaycaster>();
                }

                // Create display GameObject
                displayGO = new GameObject("[MasterSafeSlot_Display]");
                displayGO.transform.SetParent(_canvas.transform, false);

                displayRect = displayGO.AddComponent<RectTransform>();
                // Compact strip below central HUD (avoids overlap with Target Grid Ref / top overlays)
                displayRect.anchorMin = new Vector2(0.28f, 0.56f);
                displayRect.anchorMax = new Vector2(0.72f, 0.62f);
                displayRect.anchoredPosition = Vector2.zero;
                displayRect.sizeDelta = Vector2.zero;

                targetInfoText = displayGO.AddComponent<Text>();
                targetInfoText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                targetInfoText.fontSize = 14;
                targetInfoText.color = HudNeonLime;
                targetInfoText.alignment = TextAnchor.MiddleCenter;
                targetInfoText.horizontalOverflow = HorizontalWrapMode.Overflow;
                targetInfoText.verticalOverflow = VerticalWrapMode.Overflow;
                targetInfoText.raycastTarget = false;
                targetInfoText.text = "";

                displayGO.SetActive(false);

                _hudTypographyBound = false;
                _nextTypographyAttemptTime = 0f;

                Plugin.Log.LogInfo("[MasterSafeSlot] Display UI created successfully");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Error creating display: {ex.Message}");
            }
        }

        void LateUpdate()
        {
            if (!Plugin.modEnabled || !Plugin.cfgMasterSafeSlot.Value)
            {
                if (displayGO != null) displayGO.SetActive(false);
                if (_canvas != null) _canvas.enabled = false;
                currentSafeTarget = null;
                return;
            }

            if (_canvas != null) _canvas.enabled = true;

            TryBindHudTypography();

            try
            {
                if (displayGO == null || targetInfoText == null)
                {
                    CreateDisplay();
                    return;
                }

                var aircraft = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                if (aircraft == null)
                {
                    if (displayGO.activeSelf) displayGO.SetActive(false);
                    currentSafeTarget = null;
                    return;
                }

                float now = Time.timeSinceLevelLoad;
                if (now - lastTargetResolveTime >= TARGET_RESOLVE_INTERVAL)
                {
                    lastTargetResolveTime = now;
                    Unit safeTarget = FindSafeTarget(aircraft);
                    if (safeTarget != null && safeTarget != currentSafeTarget)
                    {
                        currentSafeTarget = safeTarget;
                        smoothedRange = 0f;
                        smoothedBearing = 0f;
                        smoothedSpeed = 0f;
                    }
                    else if (safeTarget == null)
                    {
                        currentSafeTarget = null;
                    }
                }

                // Refresh geometry/text every frame while showing so the strip tracks the world smoothly
                if (currentSafeTarget != null)
                {
                    UpdateTargetDisplay(aircraft, currentSafeTarget);
                }
                else if (displayGO.activeSelf)
                {
                    displayGO.SetActive(false);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[MasterSafeSlot] Update error: {ex.Message}");
            }
        }

        void TryBindHudTypography()
        {
            if (targetInfoText == null || _hudTypographyBound) return;
            if (Time.timeSinceLevelLoad < _nextTypographyAttemptTime) return;
            _nextTypographyAttemptTime = Time.timeSinceLevelLoad + 0.35f;

            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                if (hud == null) return;

                Text refText = null;
                var hudType = typeof(CombatHUD);
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var fieldName in new[] { "targetInfo", "weaponInfo", "weaponStatus" })
                {
                    var fi = hudType.GetField(fieldName, bf);
                    if (fi?.GetValue(hud) is Text t && t.font != null)
                    {
                        refText = t;
                        break;
                    }
                }

                if (refText == null) return;

                targetInfoText.font = refText.font;
                if (refText.fontSize > 0)
                    targetInfoText.fontSize = Mathf.Clamp(refText.fontSize, 10, 16);
                targetInfoText.fontStyle = refText.fontStyle;

                var c = refText.color;
                if (c.a < 0.05f || NearlyWhite(c))
                    c = HudNeonLime;
                targetInfoText.color = c;

                _hudTypographyBound = true;
            }
            catch
            {
                // keep defaults
            }
        }

        static bool NearlyWhite(Color c)
        {
            return c.r > 0.92f && c.g > 0.92f && c.b > 0.92f;
        }

        Unit FindSafeTarget(Aircraft aircraft)
        {
            try
            {
                // Only primary weapon target list slot 0 (no FOV scan — avoids wrong contacts / munition context)
                var targetList = GameBindings.Player.TargetList.GetTargets(silent: true);
                if (targetList == null || targetList.Count == 0) return null;
                var target = targetList[0];
                if (target is Unit unit && MasterSafeSlot.IsSafeTarget(unit))
                    return unit;
                return null;
            }
            catch
            {
                return null;
            }
        }

        void UpdateTargetDisplay(Aircraft aircraft, Unit target)
        {
            if (targetInfoText == null || displayGO == null) return;

            try
            {
                var info = MasterSafeSlot.GetTargetInfo(target, aircraft.transform.position, aircraft.transform);
                if (info == null)
                {
                    if (displayGO.activeSelf) displayGO.SetActive(false);
                    return;
                }

                // Apply smoothing
                smoothedRange = Mathf.Lerp(smoothedRange, info.Range, SMOOTHING_FACTOR);
                smoothedBearing = Mathf.LerpAngle(smoothedBearing, info.Bearing, SMOOTHING_FACTOR);
                smoothedSpeed = Mathf.Lerp(smoothedSpeed, info.Speed, SMOOTHING_FACTOR);

                float brgShow = Mathf.DeltaAngle(0f, smoothedBearing);

                // Short line to stay inside HUD band (separate from vanilla targetInfo / grid-ref mods)
                string displayText = $"SAFE  {smoothedRange:F0}m  BRG {brgShow:F0}°  {smoothedSpeed:F0}m/s";
                
                // Update display
                displayGO.SetActive(true);
                targetInfoText.text = displayText;
            }
            catch
            {
                // Silently fail
            }
        }

        void OnDestroy()
        {
            MasterSafeSlot.ClearCache();
        }
    }

    /// <summary>
    /// Skip weapon release for the local player when the shot's explicit target unit is same-faction (not blanket block on target list slot 0).
    /// Discovers every <see cref="Weapon"/> subtype that implements the standard <c>Fire(Unit, Unit, Vector3, WeaponStation, GlobalPosition)</c> signature.
    /// Prefix priority runs before TTI registration postfixes on the same methods.
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    static class MasterSafeSlotBlockWeaponFirePatch
    {
        static readonly System.Type[] FireSig =
        {
            typeof(Unit), typeof(Unit), typeof(Vector3), typeof(WeaponStation), typeof(GlobalPosition)
        };

        static bool Prepare()
        {
            foreach (var _ in EnumerateWeaponFireMethods())
                return true;
            return false;
        }

        static IEnumerable<MethodBase> TargetMethods() => EnumerateWeaponFireMethods();

        static IEnumerable<MethodBase> EnumerateWeaponFireMethods()
        {
            var seen = new HashSet<RuntimeMethodHandle>();
            System.Type[] types;
            try
            {
                types = typeof(Weapon).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types?.Where(x => x != null).ToArray() ?? System.Array.Empty<System.Type>();
            }
            catch
            {
                yield break;
            }

            foreach (var t in types)
            {
                if (t == null || !typeof(Weapon).IsAssignableFrom(t) || t.IsAbstract)
                    continue;
                MethodBase m;
                try
                {
                    m = AccessTools.Method(t, "Fire", FireSig);
                }
                catch
                {
                    continue;
                }

                if (m == null || !seen.Add(m.MethodHandle))
                    continue;
                yield return m;
            }
        }

        // Harmony matches patch parameters by name to the original method; Gun::Fire uses "firingUnit" not "owner".
        // Use __0/__1 so every override with the same signature patches cleanly (fixes PatchAll abort → TTI missing).
        static bool Prefix(Unit __0, Unit __1, Vector3 __2, WeaponStation __3, GlobalPosition __4)
        {
            if (!MasterSafeSlot.ShouldInhibitLocalWeaponRelease(__0, __1)) return true;
            return false;
        }
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    static class MasterSafeSlotBlockCmdLaunchMissilePatch
    {
        static bool Prepare()
        {
            return AccessTools.Method(typeof(Aircraft), "CmdLaunchMissile",
                new[] { typeof(byte), typeof(Unit), typeof(GlobalPosition) }) != null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Aircraft), "CmdLaunchMissile",
                new[] { typeof(byte), typeof(Unit), typeof(GlobalPosition) });
        }

        static bool Prefix(Aircraft __instance, byte stationIndex, Unit target, GlobalPosition aimpoint)
        {
            if (!MasterSafeSlot.ShouldInhibitLocalWeaponRelease(__instance, target)) return true;
            return false;
        }
    }
}
