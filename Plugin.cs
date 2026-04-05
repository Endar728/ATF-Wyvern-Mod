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
        static MasterSafeSlotHelper masterSafeSlotHelperInstance;

        void Update()
        {
            // Toggle mod features with key
            // Safety check: ensure config is loaded before accessing
            if (cfgToggleKey != null && Input.GetKeyDown(cfgToggleKey.Value))
            {
                modEnabled = !modEnabled;
                string status = modEnabled ? "ENABLED" : "DISABLED";
                Log.LogInfo($"[ATF Wyvern Mod] ===== Mod features: {status} =====");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Deconfliction: {(cfgLaserDeconfliction?.Value == true && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Time To Impact: {(cfgTimeToImpact?.Value == true && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Master Safe Slot: {(cfgMasterSafeSlot?.Value == true && modEnabled ? "ON" : "OFF")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Reticle Visibility: {(cfgLaserReticleVisibility?.Value == true && modEnabled ? "ON" : "OFF")}");
            }
        }

        void Awake()
        {
            // Wrap everything in try-catch to ensure we always log something
            try
            {
                Log = Logger;
                Log.LogInfo("[ATF Wyvern Mod] v1.0.0 - Starting initialization...");

                // Configuration setup
                try
                {
                    cfgLaserDeconfliction = Config.Bind("Features", "LaserDeconfliction", true,
                        "Enable smart laser designator deconfliction (distributes targets among players)");
                    cfgTimeToImpact = Config.Bind("Features", "TimeToImpact", true,
                        "Show Time-To-Impact readout for bombs and missiles");
                    cfgMasterSafeSlot = Config.Bind("Features", "MasterSafeSlot", true,
                        "Show range/bearing/speed for same-faction contacts and block weapon release while they are primary target or engaged unit");
                    cfgLaserReticleVisibility = Config.Bind("Features", "LaserReticleVisibility", true,
                        "Make laser reticle more visible in night vision mode on target camera");
                    cfgToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F9,
                        "Key to toggle mod features");
                    cfgDiscoverMethods = Config.Bind("Debug", "DiscoverMethods", false,
                        "Enable method discovery logging (logs all relevant game methods on startup)");
                    
                    Log.LogInfo("[ATF Wyvern Mod] Configuration loaded successfully");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[ATF Wyvern Mod] Error loading configuration: {ex.Message}\n{ex.StackTrace}");
                    throw; // Re-throw to prevent mod from loading with broken config
                }

                // Log initial state
                Log.LogInfo("[ATF Wyvern Mod] v1.0.0 - Initializing...");
                Log.LogInfo($"[ATF Wyvern Mod] Features enabled: LaserDeconfliction={cfgLaserDeconfliction.Value}, TimeToImpact={cfgTimeToImpact.Value}, MasterSafeSlot={cfgMasterSafeSlot.Value}, LaserReticleVisibility={cfgLaserReticleVisibility.Value}");

                // Method discovery (if enabled)
                if (cfgDiscoverMethods.Value)
                {
                    try
                    {
                        DiscoverGameMethods();
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogWarning($"[ATF Wyvern Mod] Error during method discovery: {ex.Message}");
                    }
                }

                // Initialize helpers on scene load
                try
                {
                    SceneManager.sceneLoaded += OnSceneLoaded;
                    SceneManager.sceneUnloaded += OnSceneUnloaded;
                    Log.LogInfo("[ATF Wyvern Mod] Scene event handlers registered");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[ATF Wyvern Mod] Error registering scene handlers: {ex.Message}\n{ex.StackTrace}");
                }

                // Apply Harmony patches
                try
                {
                    var harmony = new Harmony("com.atf.wyvernmod");
                    harmony.PatchAll();
                    Log.LogInfo("[ATF Wyvern Mod] All Harmony patches applied successfully");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[ATF Wyvern Mod] Error applying Harmony patches: {ex.Message}\n{ex.StackTrace}");
                    // Don't throw - allow mod to load even if some patches fail
                }

                Log.LogInfo("[ATF Wyvern Mod] Initialization complete!");
            }
            catch (System.Exception ex)
            {
                // Last resort error handling - log to BepInEx's default logger if our logger isn't set
                if (Log != null)
                {
                    Log.LogFatal($"[ATF Wyvern Mod] FATAL ERROR during initialization: {ex.Message}\n{ex.StackTrace}");
                }
                else
                {
                    // Use BepInEx's base logger as fallback
                    Logger.LogFatal($"[ATF Wyvern Mod] FATAL ERROR during initialization: {ex.Message}\n{ex.StackTrace}");
                }
                throw; // Re-throw to let BepInEx know the mod failed to load
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Create TTI Display
            if (ttiDisplayInstance == null)
            {
                var go = new GameObject("[ATFWyvernMod_TTIDisplay]");
                ttiDisplayInstance = go.AddComponent<TimeToImpactDisplay>();
                Log.LogInfo($"[ATF Wyvern Mod] TTI Display created in scene: {scene.name}");
            }
            
            // Create Laser Helper
            if (laserHelperInstance == null && cfgLaserDeconfliction.Value)
            {
                var go = new GameObject("[ATFWyvernMod_LaserHelper]");
                laserHelperInstance = go.AddComponent<LaserDeconflictionHelper>();
                Log.LogInfo($"[ATF Wyvern Mod] Laser Helper created in scene: {scene.name}");
            }
            
            // Create Laser Reticle Helper
            if (laserReticleHelperInstance == null && cfgLaserReticleVisibility.Value)
            {
                var go = new GameObject("[ATFWyvernMod_LaserReticleHelper]");
                laserReticleHelperInstance = go.AddComponent<LaserReticleVisibilityHelper>();
                Log.LogInfo($"[ATF Wyvern Mod] Laser Reticle Visibility Helper created in scene: {scene.name}");
            }
            
            // Create Master Safe Slot Helper
            if (masterSafeSlotHelperInstance == null && cfgMasterSafeSlot.Value)
            {
                var go = new GameObject("[ATFWyvernMod_MasterSafeSlotHelper]");
                masterSafeSlotHelperInstance = go.AddComponent<MasterSafeSlotHelper>();
                Log.LogInfo($"[ATF Wyvern Mod] Master Safe Slot Helper created in scene: {scene.name}");
            }
            
            // Clear caches on scene load
            LaserDeconfliction.Clear();
            MasterSafeSlot.ClearCache();
            LaserReticleVisibility.ClearCache();
            
            // Log mod status when entering gameplay scenes (not MainMenu)
            if (scene.name != "MainMenu" && modEnabled)
            {
                Log.LogInfo($"[ATF Wyvern Mod] ===== Active in scene: {scene.name} =====");
                Log.LogInfo($"[ATF Wyvern Mod] - Master Safe Slot: {(cfgMasterSafeSlot.Value ? "ENABLED" : "DISABLED")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Time To Impact: {(cfgTimeToImpact.Value ? "ENABLED" : "DISABLED")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Deconfliction: {(cfgLaserDeconfliction.Value ? "ENABLED" : "DISABLED")}");
                Log.LogInfo($"[ATF Wyvern Mod] - Laser Reticle Visibility: {(cfgLaserReticleVisibility.Value ? "ENABLED" : "DISABLED")}");
            }
        }

        void OnSceneUnloaded(Scene scene)
        {
            // Clear caches on scene unload
            LaserDeconfliction.Clear();
            MasterSafeSlot.ClearCache();
            LaserReticleVisibility.ClearCache();
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
