using System;
using System.Collections.Generic;
using UnityEngine;

namespace ATFWyvernMod
{
    /// <summary>
    /// Centralized helper class for accessing game state and components.
    /// Provides safe access to frequently used game objects and their properties.
    /// </summary>
    public static class GameBindings
    {
        public static class Player
        {
            public static class Aircraft
            {
                /// <summary>
                /// Gets the player's aircraft instance.
                /// </summary>
                public static global::Aircraft GetAircraft(bool silent = false)
                {
                    try
                    {
                        return SceneSingleton<CombatHUD>.i.aircraft;
                    }
                    catch (NullReferenceException e)
                    {
                        if (!silent)
                            Plugin.Log.LogWarning($"[GameBindings] Error getting aircraft: {e.Message}");
                        return null;
                    }
                }

                /// <summary>
                /// Gets the aircraft's weapon manager.
                /// </summary>
                public static WeaponManager GetWeaponManager(bool silent = false)
                {
                    try
                    {
                        var aircraft = GetAircraft(silent: true);
                        if (aircraft == null) return null;
                        return aircraft.weaponManager;
                    }
                    catch (NullReferenceException e)
                    {
                        if (!silent)
                            Plugin.Log.LogWarning($"[GameBindings] Error getting weapon manager: {e.Message}");
                        return null;
                    }
                }

                /// <summary>
                /// Gets the aircraft's target camera.
                /// </summary>
                public static TargetCam GetTargetCam(bool silent = false)
                {
                    try
                    {
                        var aircraft = GetAircraft(silent: true);
                        if (aircraft == null) return null;
                        return aircraft.targetCam;
                    }
                    catch (NullReferenceException e)
                    {
                        if (!silent)
                            Plugin.Log.LogWarning($"[GameBindings] Error getting target cam: {e.Message}");
                        return null;
                    }
                }
            }

            public static class TargetList
            {
                private static readonly TraverseCache<CombatHUD, List<Unit>> targetListCache = new("targetList");

                /// <summary>
                /// Gets the current target list from CombatHUD.
                /// </summary>
                public static List<Unit> GetTargets(bool silent = false)
                {
                    try
                    {
                        var combatHUD = SceneSingleton<CombatHUD>.i;
                        if (combatHUD == null) return new List<Unit>();
                        var targetList = targetListCache.GetValue(combatHUD, silent: true);
                        return targetList ?? new List<Unit>();
                    }
                    catch (NullReferenceException e)
                    {
                        if (!silent)
                            Plugin.Log.LogWarning($"[GameBindings] Error getting target list: {e.Message}");
                        return new List<Unit>();
                    }
                }
            }
        }

        public static class GameState
        {
            /// <summary>
            /// Checks if the game is currently paused.
            /// </summary>
            public static bool IsGamePaused()
            {
                try
                {
                    return GameplayUI.GameIsPaused;
                }
                catch (NullReferenceException e)
                {
                    Plugin.Log.LogDebug($"[GameBindings] Error checking game pause state: {e.Message}");
                    return false;
                }
            }
        }

        public static class UI
        {
            /// <summary>
            /// Gets the CombatHUD component.
            /// </summary>
            public static CombatHUD GetCombatHUD(bool silent = false)
            {
                try
                {
                    return SceneSingleton<CombatHUD>.i;
                }
                catch (NullReferenceException e)
                {
                    if (!silent)
                        Plugin.Log.LogWarning($"[GameBindings] Error getting CombatHUD: {e.Message}");
                    return null;
                }
            }

            /// <summary>
            /// Gets the FlightHUD component.
            /// </summary>
            public static FlightHud GetFlightHUD(bool silent = false)
            {
                try
                {
                    return SceneSingleton<FlightHud>.i;
                }
                catch (NullReferenceException e)
                {
                    if (!silent)
                        Plugin.Log.LogWarning($"[GameBindings] Error getting FlightHUD: {e.Message}");
                    return null;
                }
            }
        }
    }
}
