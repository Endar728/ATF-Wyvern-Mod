using System;
using NuclearOption;
using UnityEngine;

namespace ATFWyvernMod
{
    /// <summary>
    /// Resolves the same cockpit HUD camera BasicGrowl-style overlays use:
    /// prefer <c>cockpitRenderer</c>, fall back to <see cref="Camera.main"/>,
    /// only when <see cref="CameraMode.cockpit"/> is active (when that API exists),
    /// and only for game view cameras without a render target.
    /// Use this to bind <see cref="Canvas"/> in Screen Space - Camera mode or for custom GL draws.
    /// </summary>
    public static class CockpitHudCamera
    {
        const string PreferredCameraName = "cockpitRenderer";
        const float CameraSelectionRefreshIntervalSeconds = 0.25f;

        static int _selectionFrame = -1;
        static Camera _cachedPreferredCamera;
        static Camera _cachedTargetCamera;
        static float _nextCameraSelectionRefreshTime;

        /// <summary>Returns the camera that should receive mod HUD elements in the current cockpit view.</summary>
        public static bool TryGetCockpitHudCamera(out Camera camera)
        {
            camera = null;
            RefreshSelectionForFrame();

            var target = _cachedTargetCamera;
            if (target == null)
                return false;

            if (target.cameraType != CameraType.Game)
                return false;

            // Do not reject targetTexture: the main game camera may render via an intermediate RT in URP.

            if (TryIsCockpitModeAvailable(out var isCockpit) && !isCockpit && !IsLocalPlayerInAircraftHud())
                return false;

            camera = target;
            return true;
        }

        static Camera TryGetCameraStateManagerMainCamera()
        {
            try
            {
                return SceneSingleton<CameraStateManager>.i != null
                    ? SceneSingleton<CameraStateManager>.i.mainCamera
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>True when CombatHUD is active with a local aircraft (mission gameplay).</summary>
        public static bool IsLocalPlayerInAircraftHud()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                return hud != null && hud.aircraft != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Use from SRP <c>endCameraRendering</c> or <see cref="Camera.onPostRender"/> so custom GL HUD runs only on the cockpit pass.
        /// </summary>
        public static bool IsCockpitHudRenderCamera(Camera candidate)
        {
            return candidate != null && TryGetCockpitHudCamera(out var hud) && candidate == hud;
        }

        static void RefreshSelectionForFrame()
        {
            int frame = Time.frameCount;
            if (_selectionFrame == frame)
                return;

            _selectionFrame = frame;

            bool hasCachedTarget = _cachedTargetCamera != null;
            bool refreshDue = Time.unscaledTime >= _nextCameraSelectionRefreshTime;
            if (hasCachedTarget && !refreshDue)
                return;

            _cachedPreferredCamera = FindPreferredCamera();
            _cachedTargetCamera = _cachedPreferredCamera
                ?? TryGetCameraStateManagerMainCamera()
                ?? Camera.main;
            _nextCameraSelectionRefreshTime = Time.unscaledTime + CameraSelectionRefreshIntervalSeconds;
        }

        static Camera FindPreferredCamera()
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null) continue;
                if (string.Equals(c.name, PreferredCameraName, StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return null;
        }

        static bool TryIsCockpitModeAvailable(out bool isCockpitMode)
        {
            try
            {
                isCockpitMode = CameraStateManager.cameraMode == CameraMode.cockpit;
                return true;
            }
            catch
            {
                isCockpitMode = false;
                return false;
            }
        }
    }
}
