using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ATFWyvernMod
{
    /// <summary>
    /// Time-To-Impact (TTI) readout system for bombs and missiles.
    /// Displays estimated time until impact on the HUD.
    /// </summary>
    public class TimeToImpactDisplay : MonoBehaviour
    {
        private GameObject ttiDisplayGO;
        private Text ttiText;
        private RectTransform ttiRect;

        // Track active projectiles for TTI calculation
        private static readonly Dictionary<object, ProjectileData> activeProjectiles = new Dictionary<object, ProjectileData>();
        private static readonly object projectilesLock = new object();

        public class ProjectileData
        {
            public Vector3 position;
            public Vector3 velocity;
            public Vector3 targetPosition;
            public bool isBallistic;
            public bool isGuided;
            public float spawnTime;
            public string weaponType;
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            CreateDisplay();
        }

        void CreateDisplay()
        {
            // Find or create canvas
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("[TTI_Canvas]");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // Create TTI display
            ttiDisplayGO = new GameObject("[TTI_Display]");
            ttiDisplayGO.transform.SetParent(canvas.transform, false);

            ttiRect = ttiDisplayGO.AddComponent<RectTransform>();
            ttiRect.anchorMin = new Vector2(0.02f, 0.85f);
            ttiRect.anchorMax = new Vector2(0.3f, 0.95f);
            ttiRect.anchoredPosition = Vector2.zero;
            ttiRect.sizeDelta = Vector2.zero;

            ttiText = ttiDisplayGO.AddComponent<Text>();
            ttiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ttiText.fontSize = 24;
            ttiText.color = Color.yellow;
            ttiText.alignment = TextAnchor.UpperLeft;
            ttiText.text = "";

            ttiDisplayGO.SetActive(false);
        }

        void Update()
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value)
            {
                if (ttiDisplayGO != null) ttiDisplayGO.SetActive(false);
                return;
            }

            // Get current weapon/projectile and calculate TTI
            float tti = CalculateTimeToImpact();
            
            if (tti > 0f && tti < 300f) // Show if between 0 and 5 minutes
            {
                if (ttiDisplayGO != null)
                {
                    ttiDisplayGO.SetActive(true);
                    if (ttiText != null)
                    {
                        string weaponType = GetWeaponType();
                        ttiText.text = $"TTI: {tti:F1}s ({weaponType})";
                    }
                }
            }
            else
            {
                if (ttiDisplayGO != null) ttiDisplayGO.SetActive(false);
            }
        }

        /// <summary>
        /// Calculates time to impact for current weapon/projectile
        /// </summary>
        private float CalculateTimeToImpact()
        {
            // Find the most recent projectile
            ProjectileData bestProjectile = null;
            float bestTTI = float.MaxValue;

            lock (projectilesLock)
            {
                var toRemove = new List<object>();
                
                foreach (var kvp in activeProjectiles)
                {
                    var proj = kvp.Value;
                    
                    // Update position based on velocity
                    float dt = Time.timeSinceLevelLoad - proj.spawnTime;
                    Vector3 currentPos = proj.position + proj.velocity * dt;
                    
                    // Calculate TTI
                    float tti = -1f;
                    Vector3 toTarget = proj.targetPosition - currentPos;
                    
                    if (proj.isBallistic)
                    {
                        // Ballistic trajectory calculation
                        float g = Physics.gravity.magnitude;
                        float vy = proj.velocity.y;
                        float dy = toTarget.y;
                        
                        // Solve: y = vy*t - 0.5*g*t^2
                        // Rearranged: 0.5*g*t^2 - vy*t + dy = 0
                        float a = 0.5f * g;
                        float b = -vy;
                        float c = dy;
                        
                        float discriminant = b * b - 4 * a * c;
                        if (discriminant >= 0)
                        {
                            float t1 = (-b + Mathf.Sqrt(discriminant)) / (2 * a);
                            float t2 = (-b - Mathf.Sqrt(discriminant)) / (2 * a);
                            tti = Mathf.Max(t1, t2);
                            
                            // Also check horizontal distance
                            Vector3 horizontalToTarget = new Vector3(toTarget.x, 0, toTarget.z);
                            Vector3 horizontalVel = new Vector3(proj.velocity.x, 0, proj.velocity.z);
                            if (horizontalVel.magnitude > 0.1f)
                            {
                                float horizontalTTI = horizontalToTarget.magnitude / horizontalVel.magnitude;
                                tti = Mathf.Max(tti, horizontalTTI);
                            }
                        }
                    }
                    else if (proj.isGuided)
                    {
                        // Guided missile - simple distance/speed calculation
                        float distance = toTarget.magnitude;
                        float speed = proj.velocity.magnitude;
                        if (speed > 0.1f)
                        {
                            tti = distance / speed;
                        }
                    }
                    else
                    {
                        // Generic projectile
                        float distance = toTarget.magnitude;
                        float speed = proj.velocity.magnitude;
                        if (speed > 0.1f)
                        {
                            tti = distance / speed;
                        }
                    }
                    
                    // Remove old projectiles (older than 60 seconds or already hit)
                    if (dt > 60f || (tti > 0 && tti < 0.1f))
                    {
                        toRemove.Add(kvp.Key);
                        continue;
                    }
                    
                    if (tti > 0 && tti < bestTTI)
                    {
                        bestTTI = tti;
                        bestProjectile = proj;
                    }
                }
                
                // Clean up old projectiles
                foreach (var key in toRemove)
                {
                    activeProjectiles.Remove(key);
                }
            }
            
            return bestTTI < float.MaxValue ? bestTTI : -1f;
        }

        private string GetWeaponType()
        {
            lock (projectilesLock)
            {
                if (activeProjectiles.Count > 0)
                {
                    // Get the most recent projectile's weapon type
                    ProjectileData latest = null;
                    float latestTime = -1f;
                    foreach (var proj in activeProjectiles.Values)
                    {
                        if (proj.spawnTime > latestTime)
                        {
                            latestTime = proj.spawnTime;
                            latest = proj;
                        }
                    }
                    return latest?.weaponType ?? "Weapon";
                }
            }
            return "Weapon";
        }

        /// <summary>
        /// Register a projectile for TTI tracking
        /// </summary>
        public static void RegisterProjectile(object projectile, Vector3 position, Vector3 velocity, Vector3 targetPos, bool ballistic, bool guided, string weaponType)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (projectile == null) return;
            
            lock (projectilesLock)
            {
                activeProjectiles[projectile] = new ProjectileData
                {
                    position = position,
                    velocity = velocity,
                    targetPosition = targetPos,
                    isBallistic = ballistic,
                    isGuided = guided,
                    spawnTime = Time.timeSinceLevelLoad,
                    weaponType = weaponType
                };
            }
        }

        /// <summary>
        /// Unregister a projectile (when it hits or is destroyed)
        /// </summary>
        public static void UnregisterProjectile(object projectile)
        {
            if (projectile == null) return;
            
            lock (projectilesLock)
            {
                activeProjectiles.Remove(projectile);
            }
        }
    }

    // === Harmony Patches ===

    /// <summary>
    /// Patch Missile.Awake() to register missiles for TTI tracking
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Awake")]
    static class MissileAwakePatch
    {
        static void Postfix(Missile __instance)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;
            ProjectileRegistrationHelper.RegisterProjectileForTTI(__instance, "Missile");
        }
    }

    /// <summary>
    /// Patch MountedMissile.Fire() to register missiles when fired
    /// </summary>
    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    static class MountedMissileFirePatch
    {
        static void Postfix(MountedMissile __instance, Unit owner, Unit target, Vector3 inheritedVelocity, WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;
            ProjectileRegistrationHelper.RegisterProjectileForTTI(__instance, "Missile", target, aimpoint);
        }
    }

    /// <summary>
    /// Patch Laser.Fire() to register laser-guided weapons
    /// </summary>
    [HarmonyPatch(typeof(Laser), "Fire")]
    static class LaserFirePatch
    {
        static void Postfix(Laser __instance, Unit owner, Unit target, Vector3 inheritedVelocity, WeaponStation weaponStation, GlobalPosition aimpoint)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;
            ProjectileRegistrationHelper.RegisterProjectileForTTI(__instance, "Laser", target, aimpoint);
        }
    }

    /// <summary>
    /// Helper class for projectile registration
    /// </summary>
    static class ProjectileRegistrationHelper
    {
        /// <summary>
        /// Helper method to register projectiles for TTI tracking
        /// </summary>
        public static void RegisterProjectileForTTI(object projectile, string weaponType, Unit target = null, GlobalPosition? targetPos = null)
        {
            try
            {
                var projType = projectile.GetType();

                // Get position
                Vector3 position = Vector3.zero;
                var transformProp = projType.GetProperty("transform") ?? projType.GetProperty("Transform");
                if (transformProp != null)
                {
                    var transform = transformProp.GetValue(projectile) as Transform;
                    if (transform != null)
                    {
                        position = transform.position;
                    }
                }

                // Get velocity (read-only, never set to avoid kinematic body warnings)
                Vector3 velocity = Vector3.zero;
                try
                {
                    var rbProp = projType.GetProperty("rb") ?? projType.GetProperty("rigidbody") ?? 
                                 projType.GetProperty("rigidBody");
                    if (rbProp != null)
                    {
                        var rb = rbProp.GetValue(projectile);
                        if (rb != null)
                        {
                            var rbType = rb.GetType();
                            var velProp = rbType.GetProperty("velocity") ?? rbType.GetProperty("Velocity");
                            if (velProp != null)
                            {
                                // Only read velocity, never set it (to avoid kinematic body warnings)
                                velocity = (Vector3)velProp.GetValue(rb);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogDebug($"[TimeToImpact] Could not read velocity from rigidbody: {ex.Message}");
                }

                // Try to get velocity directly from projectile
                if (velocity.magnitude < 0.1f)
                {
                    var velProp = projType.GetProperty("velocity") ?? projType.GetProperty("Velocity");
                    if (velProp != null)
                    {
                        velocity = (Vector3)velProp.GetValue(projectile);
                    }
                }

                // Get target position
                Vector3 finalTargetPos = position;
                bool isGuided = false;
                bool isBallistic = weaponType.Contains("Bomb");

                if (targetPos.HasValue)
                {
                    // GlobalPosition has AsVector3() method
                    var gp = targetPos.Value;
                    var asVector3Method = typeof(GlobalPosition).GetMethod("AsVector3");
                    if (asVector3Method != null)
                    {
                        finalTargetPos = (Vector3)asVector3Method.Invoke(gp, null);
                    }
                    else
                    {
                        // Fallback: try to access properties directly
                        var xProp = typeof(GlobalPosition).GetProperty("x");
                        var yProp = typeof(GlobalPosition).GetProperty("y");
                        var zProp = typeof(GlobalPosition).GetProperty("z");
                        if (xProp != null && yProp != null && zProp != null)
                        {
                            finalTargetPos = new Vector3((float)xProp.GetValue(gp), (float)yProp.GetValue(gp), (float)zProp.GetValue(gp));
                        }
                    }
                    isGuided = true;
                }
                else if (target != null)
                {
                    var targetTransformProp = typeof(Unit).GetProperty("transform") ?? typeof(Unit).GetProperty("Transform");
                    if (targetTransformProp != null)
                    {
                        var targetTransform = targetTransformProp.GetValue(target) as Transform;
                        if (targetTransform != null)
                        {
                            finalTargetPos = targetTransform.position;
                            isGuided = true;
                        }
                    }
                }

                // Register the projectile
                TimeToImpactDisplay.RegisterProjectile(projectile, position, velocity, finalTargetPos, 
                                                       isBallistic, isGuided, weaponType);
                
                Plugin.Log.LogDebug($"[TimeToImpact] Registered {weaponType} at position {position}, velocity {velocity.magnitude:F1} m/s");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeToImpact] Error registering projectile: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Legacy patch using dynamic discovery (fallback)
    /// </summary>
    [HarmonyPatch]
    static class ProjectileSpawnPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return null;

            try
            {
                var assembly = typeof(Unit).Assembly;
                var allTypes = assembly.GetTypes();

                // Look for Bomb classes
                foreach (var type in allTypes)
                {
                    if (type.Name.Contains("Bomb") && !type.Name.Contains("Missile"))
                    {
                        // Look for Awake or Fire methods
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | 
                                                                 System.Reflection.BindingFlags.Instance | 
                                                                 System.Reflection.BindingFlags.NonPublic))
                        {
                            if ((method.Name == "Awake" || method.Name.Contains("Fire")) && 
                                !method.IsAbstract && !method.IsVirtual)
                            {
                                Plugin.Log.LogInfo($"[TimeToImpact] Found potential bomb spawn method: {type.FullName}.{method.Name}");
                                return method;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeToImpact] Error finding projectile spawn method: {ex.Message}");
            }

            return null;
        }

        static void Postfix(object __instance)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;

            try
            {
                var projType = __instance.GetType();
                if (!projType.Name.Contains("Projectile") && !projType.Name.Contains("Missile") && 
                    !projType.Name.Contains("Bomb") && !projType.Name.Contains("Rocket"))
                {
                    return;
                }

                // Get weapon type name
                string weaponType = projType.Name;
                if (weaponType.Contains("Bomb")) weaponType = "Bomb";
                else if (weaponType.Contains("Missile")) weaponType = "Missile";
                else if (weaponType.Contains("Rocket")) weaponType = "Rocket";
                else weaponType = "Projectile";

                // Get target if available
                Unit target = null;
                var targetProp = projType.GetProperty("target") ?? projType.GetProperty("Target");
                if (targetProp != null)
                {
                    var targetObj = targetProp.GetValue(__instance);
                    if (targetObj is Unit unit)
                    {
                        target = unit;
                    }
                }

                // Use helper method to register
                ProjectileRegistrationHelper.RegisterProjectileForTTI(__instance, weaponType, target);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeToImpact] Error registering projectile: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to unregister projectiles when they are destroyed
    /// </summary>
    [HarmonyPatch(typeof(MonoBehaviour), "OnDestroy")]
    static class ProjectileDestroyPatch
    {
        static void Postfix(MonoBehaviour __instance)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;

            var projType = __instance.GetType();
            if (projType.Name.Contains("Projectile") || projType.Name.Contains("Missile") || 
                projType.Name.Contains("Bomb") || projType.Name.Contains("Rocket"))
            {
                TimeToImpactDisplay.UnregisterProjectile(__instance);
            }
        }
    }
}
