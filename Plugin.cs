using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ATFWyvernMod
{
    [BepInPlugin("com.atf.wyvernmod", "ATF Wyvern Mod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static bool modEnabled = true;

        // Configuration entries
        internal static ConfigEntry<bool> cfgLaserDeconfliction;
        internal static ConfigEntry<bool> cfgTimeToImpact;
        internal static ConfigEntry<bool> cfgMasterSafeSlot;
        internal static ConfigEntry<bool> cfgLaserReticleVisibility;
        internal static ConfigEntry<KeyCode> cfgToggleKey;
        internal static ConfigEntry<bool> cfgDiscoverMethods;

        static TimeToImpactDisplay ttiDisplayInstance;
        static LaserDeconflictionHelper laserHelperInstance;
        static LaserReticleVisibilityHelper laserReticleHelperInstance;

        void Update()
        {
            // Toggle mod features with key
            if (Input.GetKeyDown(cfgToggleKey.Value))
            {
                modEnabled = !modEnabled;
                string status = modEnabled ? "ENABLED" : "DISABLED";
                Log.LogInfo($"[ATF Wyvern Mod] ===== Mod features: {status} =====");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Deconfliction: {(cfgLaserDeconfliction.Value && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Time To Impact: {(cfgTimeToImpact.Value && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Master Safe Slot: {(cfgMasterSafeSlot.Value && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Reticle Visibility: {(cfgLaserReticleVisibility.Value && modEnabled ? "ON" : "OFF")}");
            }
        }

        void Awake()
        {
            Log = Logger;

            // Configuration setup
            cfgLaserDeconfliction = Config.Bind("Features", "LaserDeconfliction", true,
                "Enable smart laser designator deconfliction (distributes targets among players)");
            cfgTimeToImpact = Config.Bind("Features", "TimeToImpact", true,
                "Show Time-To-Impact readout for bombs and missiles");
            cfgMasterSafeSlot = Config.Bind("Features", "MasterSafeSlot", true,
                "Enable master safe/empty weapon slot (no friendly lock required for range/bearing/speed)");
            cfgLaserReticleVisibility = Config.Bind("Features", "LaserReticleVisibility", true,
                "Make laser reticle more visible in night vision mode on target camera");
            cfgToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F9,
                "Key to toggle mod features");
            cfgDiscoverMethods = Config.Bind("Debug", "DiscoverMethods", false,
                "Enable method discovery logging (logs all relevant game methods on startup)");

            // Method discovery (if enabled)
            if (cfgDiscoverMethods.Value)
            {
                DiscoverGameMethods();
            }

            // Initialize helpers on scene load
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (ttiDisplayInstance == null)
                {
                    var go = new GameObject("[ATFWyvernMod_TTIDisplay]");
                    ttiDisplayInstance = go.AddComponent<TimeToImpactDisplay>();
                    Log.LogInfo($"[ATF Wyvern Mod] TTI Display created in scene: {scene.name}");
                }
                
                if (laserHelperInstance == null && cfgLaserDeconfliction.Value)
                {
                    var go = new GameObject("[ATFWyvernMod_LaserHelper]");
                    laserHelperInstance = go.AddComponent<LaserDeconflictionHelper>();
                    Log.LogInfo($"[ATF Wyvern Mod] Laser Helper created in scene: {scene.name}");
                }
                
                if (laserReticleHelperInstance == null && cfgLaserReticleVisibility.Value)
                {
                    var go = new GameObject("[ATFWyvernMod_LaserReticleHelper]");
                    laserReticleHelperInstance = go.AddComponent<LaserReticleVisibilityHelper>();
                    Log.LogInfo($"[ATF Wyvern Mod] Laser Reticle Visibility Helper created in scene: {scene.name}");
                }
                
                // Clear caches on scene load
                LaserDeconfliction.Clear();
                MasterSafeSlot.ClearCache();
                LaserReticleVisibility.ClearCache();
            };
            
            // Handle scene unload
            SceneManager.sceneUnloaded += (scene) =>
            {
                LaserDeconfliction.Clear();
                MasterSafeSlot.ClearCache();
                LaserReticleVisibility.ClearCache();
            };

            var harmony = new Harmony("com.atf.wyvernmod");
            try
            {
                harmony.PatchAll();
                Logger.LogInfo("ATF Wyvern Mod v1.0.0 loaded - All patches applied");
                Logger.LogInfo($"[ATF Wyvern Mod] Features enabled: LaserDeconfliction={cfgLaserDeconfliction.Value}, TimeToImpact={cfgTimeToImpact.Value}, MasterSafeSlot={cfgMasterSafeSlot.Value}, LaserReticleVisibility={cfgLaserReticleVisibility.Value}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error applying Harmony patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Discovers and logs relevant game methods for patching
        /// Enable this via config to find methods without using dnSpy
        /// </summary>
        void DiscoverGameMethods()
        {
            Log.LogInfo("[Method Discovery] Starting method discovery...");
            
            try
            {
                var assembly = typeof(Unit).Assembly;
                var allTypes = assembly.GetTypes();
                
                // Keywords to search for
                var laserKeywords = new[] { "Laser", "Designator", "Target" };
                var projectileKeywords = new[] { "Projectile", "Missile", "Bomb", "Rocket" };
                var weaponKeywords = new[] { "Weapon", "Targeting", "Lock" };
                var hudKeywords = new[] { "HUD", "Display", "TargetInfo" };
                
                Log.LogInfo("[Method Discovery] === LASER/DESIGNATOR METHODS ===");
                foreach (var type in allTypes)
                {
                    if (laserKeywords.Any(kw => type.Name.Contains(kw)))
                    {
                        Log.LogInfo($"[Method Discovery] Found type: {type.FullName}");
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                        {
                            if (!method.IsSpecialName) // Skip properties/events
                            {
                                var paramStr = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                Log.LogInfo($"[Method Discovery]   {method.ReturnType.Name} {method.Name}({paramStr})");
                            }
                        }
                    }
                }
                
                Log.LogInfo("[Method Discovery] === PROJECTILE METHODS ===");
                foreach (var type in allTypes)
                {
                    if (projectileKeywords.Any(kw => type.Name.Contains(kw)))
                    {
                        Log.LogInfo($"[Method Discovery] Found type: {type.FullName}");
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                        {
                            if ((method.Name.Contains("Fire") || method.Name.Contains("Launch") || method.Name.Contains("Spawn") || 
                                 method.Name == "Awake" || method.Name == "Start") && !method.IsSpecialName)
                            {
                                var paramStr = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                Log.LogInfo($"[Method Discovery]   {method.ReturnType.Name} {method.Name}({paramStr})");
                            }
                        }
                    }
                }
                
                Log.LogInfo("[Method Discovery] === WEAPON/TARGETING METHODS ===");
                foreach (var type in allTypes)
                {
                    if (weaponKeywords.Any(kw => type.Name.Contains(kw)))
                    {
                        Log.LogInfo($"[Method Discovery] Found type: {type.FullName}");
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                        {
                            if ((method.Name.Contains("CanLock") || method.Name.Contains("CanTarget") || method.Name.Contains("IsValidTarget") ||
                                 method.Name.Contains("Assign") || method.Name.Contains("SetTarget")) && !method.IsSpecialName)
                            {
                                var paramStr = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                Log.LogInfo($"[Method Discovery]   {method.ReturnType.Name} {method.Name}({paramStr})");
                            }
                        }
                    }
                }
                
                Log.LogInfo("[Method Discovery] === HUD/DISPLAY METHODS ===");
                foreach (var type in allTypes)
                {
                    if (hudKeywords.Any(kw => type.Name.Contains(kw)))
                    {
                        Log.LogInfo($"[Method Discovery] Found type: {type.FullName}");
                        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                        {
                            if ((method.Name.Contains("Update") || method.Name.Contains("Display") || method.Name.Contains("Show")) &&
                                method.Name.Contains("Target") && !method.IsSpecialName)
                            {
                                var paramStr = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                Log.LogInfo($"[Method Discovery]   {method.ReturnType.Name} {method.Name}({paramStr})");
                            }
                        }
                    }
                }
                
                Log.LogInfo("[Method Discovery] Method discovery complete! Check BepInEx logs for results.");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[Method Discovery] Error during discovery: {ex}");
            }
        }
    }
}
