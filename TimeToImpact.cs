using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
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
        private Canvas _ttiCanvas;
        private GameObject ttiDisplayGO;
        private Text ttiText;
        private RectTransform ttiRect;

        /// <summary>Neon lime similar to SHOOT / primary HUD symbology when we cannot read the vanilla Text style.</summary>
        private static readonly Color HudNeonLime = new Color(162f / 255f, 1f, 0f, 1f);

        private bool _hudTypographyBound;
        private float _nextTypographyAttemptTime;

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
            /// <summary>Used when Rigidbody.velocity stays zero (rail/kinematic) — estimate from motion.</summary>
            public Vector3 lastVelocitySamplePos;
            public float lastVelocitySampleTime;
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            CreateDisplay();
        }

        void CreateDisplay()
        {
            // Screen Space - Overlay: always draws on top and does not depend on cockpit camera binding
            // (Camera.main / URP RT / CameraMode mismatches previously kept the canvas disabled).
            var canvasGO = new GameObject("[ATFWyvernMod_TTI_Canvas]");
            canvasGO.transform.SetParent(transform, false);
            _ttiCanvas = canvasGO.AddComponent<Canvas>();
            _ttiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _ttiCanvas.sortingOrder = 4000;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            ttiDisplayGO = new GameObject("[TTI_Display]");
            ttiDisplayGO.transform.SetParent(_ttiCanvas.transform, false);

            ttiRect = ttiDisplayGO.AddComponent<RectTransform>();
            // Upper-center strip, just under the Range/Bearing line (MasterSafeSlot uses ~0.75–0.85 Y).
            ttiRect.anchorMin = new Vector2(0.2f, 0.70f);
            ttiRect.anchorMax = new Vector2(0.8f, 0.755f);
            ttiRect.anchoredPosition = Vector2.zero;
            ttiRect.sizeDelta = Vector2.zero;

            ttiText = ttiDisplayGO.AddComponent<Text>();
            ttiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ttiText.fontSize = 20;
            ttiText.color = HudNeonLime;
            ttiText.alignment = TextAnchor.MiddleCenter;
            ttiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            ttiText.verticalOverflow = VerticalWrapMode.Overflow;
            ttiText.raycastTarget = false;
            ttiText.supportRichText = false;
            ttiText.text = "";

            ttiDisplayGO.SetActive(false);
        }

        void OnEnable()
        {
            _hudTypographyBound = false;
            _nextTypographyAttemptTime = 0f;
        }

        /// <summary>Copy font/size/color from vanilla HUD Text (target info / weapon cues) so TTI matches in-cockpit style.</summary>
        void TryBindHudTypography()
        {
            if (ttiText == null || _hudTypographyBound) return;
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

                ttiText.font = refText.font;
                if (refText.fontSize > 0)
                    ttiText.fontSize = Mathf.Clamp(refText.fontSize, 14, 30);
                ttiText.fontStyle = refText.fontStyle;

                var c = refText.color;
                if (c.a < 0.05f || NearlyWhite(c))
                    c = HudNeonLime;
                ttiText.color = c;

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

        static string ShortWeaponTag(string weaponType)
        {
            if (string.IsNullOrEmpty(weaponType)) return "";
            switch (weaponType.Trim().ToUpperInvariant())
            {
                case "MISSILE": return "MSL";
                case "LASER": return "LGB";
                case "BOMB": return "BOMB";
                case "ROCKET": return "RKT";
                default:
                    var s = weaponType.Trim().ToUpperInvariant();
                    return s.Length <= 5 ? s : s.Substring(0, 5);
            }
        }

        static string FormatTtiHudLine(float ttiSeconds, string weaponType)
        {
            var sb = new StringBuilder(40);
            sb.Append("TTI ");
            // Explicit seconds with invariant formatting (avoids locale comma decimals; reads as "time in s").
            sb.Append(ttiSeconds.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append(" s");
            var tag = ShortWeaponTag(weaponType);
            if (!string.IsNullOrEmpty(tag))
            {
                sb.Append("  ");
                sb.Append(tag);
            }
            return sb.ToString();
        }

        static bool ShouldShowInGameplayHud()
        {
            if (CockpitHudCamera.IsLocalPlayerInAircraftHud())
                return true;
            // Some aircraft / HUD states leave CombatHUD.aircraft unset while the player is still in a flyable jet.
            try
            {
                return GameBindings.Player.Aircraft.GetAircraft(silent: true) != null;
            }
            catch
            {
                return false;
            }
        }

        void Update()
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value)
            {
                if (ttiDisplayGO != null) ttiDisplayGO.SetActive(false);
                if (_ttiCanvas != null) _ttiCanvas.enabled = false;
                return;
            }

            if (!ShouldShowInGameplayHud())
            {
                _hudTypographyBound = false;
                if (ttiDisplayGO != null) ttiDisplayGO.SetActive(false);
                if (_ttiCanvas != null) _ttiCanvas.enabled = false;
                return;
            }

            if (SceneSingleton<CombatHUD>.i == null)
                _hudTypographyBound = false;

            if (_ttiCanvas != null)
                _ttiCanvas.enabled = true;

            TryBindHudTypography();

            float tti = CalculateTimeToImpact(out string weaponForBestTti);
            
            if (tti >= 0.05f && tti < 300f) // Show if between ~0 and 5 minutes (exclude exact zero from bad target geometry)
            {
                if (ttiDisplayGO != null)
                {
                    ttiDisplayGO.SetActive(true);
                    if (ttiText != null)
                        ttiText.text = FormatTtiHudLine(tti, weaponForBestTti);
                }
            }
            else
            {
                if (ttiDisplayGO != null) ttiDisplayGO.SetActive(false);
            }
        }

        /// <summary>
        /// Calculates time to impact for current weapon/projectile. <paramref name="weaponForBestTti"/> matches the projectile with minimum TTI.
        /// </summary>
        private float CalculateTimeToImpact(out string weaponForBestTti)
        {
            weaponForBestTti = "Weapon";
            ProjectileData bestProjectile = null;
            float bestTTI = float.MaxValue;

            lock (projectilesLock)
            {
                var toRemove = new List<object>();
                
                foreach (var kvp in activeProjectiles)
                {
                    var proj = kvp.Value;

                    if (kvp.Key is UnityEngine.Object unityObj && unityObj == null)
                    {
                        toRemove.Add(kvp.Key);
                        continue;
                    }

                    if (kvp.Key is Missile mFlare && ProjectileRegistrationHelper.IsCountermeasureOrDecoyMissile(mFlare))
                    {
                        toRemove.Add(kvp.Key);
                        continue;
                    }

                    ProjectileRegistrationHelper.TryRefreshLiveState(kvp.Key, proj);

                    float dt = Time.timeSinceLevelLoad - proj.spawnTime;
                    Vector3 currentPos = proj.position;
                    
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
                        float distance = toTarget.magnitude;
                        float speed = proj.velocity.magnitude;
                        if (speed > 0.1f)
                        {
                            if (distance > 0.5f)
                            {
                                Vector3 dir = toTarget / distance;
                                float closing = -Vector3.Dot(proj.velocity, dir);
                                if (closing > 0.5f)
                                    tti = distance / closing;
                                else
                                    tti = distance / speed;
                            }
                            else
                            {
                                // Target merged with missile position (first frames / bad aim read): still show a coarse ETA
                                tti = Mathf.Max(0.05f, 8f / speed);
                            }
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
                    
                    // Remove stale entries. Missiles: rely on destroyed Object (null key) + timeout only —
                    // tti→0 near impact was dropping them early so the readout stopped "tracking".
                    if (dt > 60f || (!(kvp.Key is Missile) && tti > 0 && tti < 0.1f))
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
            
            if (bestProjectile != null && !string.IsNullOrEmpty(bestProjectile.weaponType))
                weaponForBestTti = bestProjectile.weaponType;

            return bestTTI < float.MaxValue ? bestTTI : -1f;
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
                float now = Time.timeSinceLevelLoad;
                activeProjectiles[projectile] = new ProjectileData
                {
                    position = position,
                    velocity = velocity,
                    targetPosition = targetPos,
                    isBallistic = ballistic,
                    isGuided = guided,
                    spawnTime = now,
                    weaponType = weaponType,
                    lastVelocitySamplePos = position,
                    lastVelocitySampleTime = now
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
    /// MountedMissile.Fire registers the <b>weapon</b> instance, not the spawned <see cref="Missile"/> unit.
    /// Live position/velocity/target refresh only works when the dictionary key is the flying missile; registration is done in <see cref="MissileSpawnTtiPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    static class MountedMissileFirePatch
    {
        static void Postfix()
        {
            // No-op: TTI tracks <see cref="Missile"/> via OnEnable.
        }
    }

    /// <summary>
    /// Register TTI against the actual in-world missile so per-frame refresh sees rigidbody motion and target updates.
    /// </summary>
    [HarmonyPatch]
    static class MissileSpawnTtiPatch
    {
        static bool Prepare()
        {
            return AccessTools.Method(typeof(Missile), "OnEnable") != null;
        }

        static MethodBase TargetMethod() => AccessTools.Method(typeof(Missile), "OnEnable");

        static void Postfix(Missile __instance)
        {
            if (!Plugin.modEnabled || !Plugin.cfgTimeToImpact.Value) return;
            if (__instance == null) return;
            ProjectileRegistrationHelper.RegisterMissileForTTI(__instance);
            // Owner / seeker target are often unset on the same frame as OnEnable; retry after physics.
            __instance.StartCoroutine(ProjectileRegistrationHelper.DelayedMissileTtiRegister(__instance));
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
            ProjectileRegistrationHelper.RegisterProjectileForTTI(__instance, "Laser", target, aimpoint, inheritedVelocity);
        }
    }

    /// <summary>
    /// Helper class for projectile registration
    /// </summary>
    static class ProjectileRegistrationHelper
    {
        /// <summary>Re-run registration after spawn so owner/target/seeker are wired (OnEnable is often too early).</summary>
        public static IEnumerator DelayedMissileTtiRegister(Missile missile)
        {
            if (missile == null) yield break;
            yield return null;
            yield return null;
            RegisterMissileForTTI(missile);
        }

        /// <summary>
        /// Each frame: sync position/velocity from the live entity so TTI works after launch (snapshots at Fire often have zero speed on the rail).
        /// </summary>
        public static void TryRefreshLiveState(object key, TimeToImpactDisplay.ProjectileData proj)
        {
            if (key == null || proj == null) return;

            try
            {
                if (key is Component comp && comp != null)
                {
                    Vector3 newPos = comp.transform.position;
                    proj.position = newPos;

                    Vector3 v = Vector3.zero;
                    bool haveRbVel = TryGetComponentRigidbodyVelocity(comp, out v);
                    if (!haveRbVel || v.magnitude < 0.25f)
                    {
                        float now = Time.timeSinceLevelLoad;
                        float dt = now - proj.lastVelocitySampleTime;
                        if (dt > 0.008f && dt < 2f)
                        {
                            Vector3 est = (newPos - proj.lastVelocitySamplePos) / dt;
                            if (est.magnitude > v.magnitude)
                                v = est;
                        }
                        proj.lastVelocitySamplePos = newPos;
                        proj.lastVelocitySampleTime = now;
                    }
                    else
                    {
                        proj.lastVelocitySamplePos = newPos;
                        proj.lastVelocitySampleTime = Time.timeSinceLevelLoad;
                    }

                    proj.velocity = v;

                    // Missiles: always try to resolve aimpoint/target from seeker. Registration often runs with
                    // isGuided=false because target was null on frame 0; without this we never update targetPosition.
                    if (key is Missile)
                    {
                        var tr = Traverse.Create(key);
                        Unit tgt = TryGetMissileTargetUnit(tr);
                        if (tgt != null && tgt.transform != null)
                        {
                            proj.targetPosition = tgt.transform.position;
                            proj.isGuided = true;
                        }
                        else if (TryReadGlobalPositionFromTraverse(tr, out var aim))
                        {
                            proj.targetPosition = aim;
                            proj.isGuided = true;
                        }
                    }
                    else if (proj.isGuided)
                    {
                        var tr = Traverse.Create(key);
                        Unit tgt = TryGetMissileTargetUnit(tr);
                        if (tgt != null && tgt.transform != null)
                            proj.targetPosition = tgt.transform.position;
                        else if (TryReadGlobalPositionFromTraverse(tr, out var aim))
                            proj.targetPosition = aim;
                    }
                }
            }
            catch
            {
                // Ignore reflection / missing fields on non-missile types
            }
        }

        static bool TryGetComponentRigidbodyVelocity(Component c, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (c == null) return false;

            if (c is Unit u && u.rb != null)
            {
                velocity = u.rb.velocity;
                return true;
            }

            object rbObj = GetRigidbodyMember(c);
            if (rbObj is Rigidbody rb && rb != null)
            {
                velocity = rb.velocity;
                return true;
            }

            rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                velocity = rb.velocity;
                return true;
            }

            // Missile bodies are often on child AeroPart; pick the child RB with the largest |velocity|
            var all = c.GetComponentsInChildren<Rigidbody>(true);
            if (all != null && all.Length > 0)
            {
                Rigidbody best = null;
                float bestMag = 0f;
                foreach (var r in all)
                {
                    if (r == null) continue;
                    float m = r.velocity.sqrMagnitude;
                    if (m > bestMag)
                    {
                        bestMag = m;
                        best = r;
                    }
                }
                if (best != null)
                {
                    velocity = best.velocity;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Resolves tracked unit: missile <c>target</c> is often null on spawn; seeker <c>targetUnit</c> is updated in flight.</summary>
        static Unit TryGetMissileTargetUnit(Traverse missileTr)
        {
            if (missileTr == null) return null;
            try
            {
                var tf = missileTr.Field("target");
                if (tf.FieldExists())
                {
                    var u = tf.GetValue() as Unit;
                    if (u != null) return u;
                }
                var tf2 = missileTr.Field("Target");
                if (tf2.FieldExists())
                {
                    var u2 = tf2.GetValue() as Unit;
                    if (u2 != null) return u2;
                }

                var seekerF = missileTr.Field("seeker");
                if (seekerF.FieldExists())
                {
                    var seekerObj = seekerF.GetValue();
                    if (seekerObj != null)
                    {
                        var seekerTr = Traverse.Create(seekerObj);
                        foreach (var name in new[] { "targetUnit", "TargetUnit", "trackedUnit", "TrackedUnit", "lockedTarget", "LockedTarget" })
                        {
                            var tu = seekerTr.Field(name);
                            if (tu.FieldExists())
                            {
                                var su = tu.GetValue() as Unit;
                                if (su != null) return su;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static bool CountermeasureTokenIn(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var t = text.ToLowerInvariant();
            return t.Contains("flare") || t.Contains("decoy") || t.Contains("chaff")
                || t.Contains("countermeasure") || t.Contains("ircm") || t.Contains("dispenser");
        }

        /// <summary>Flares/chaff spawn as <see cref="Missile"/>-like units; exclude them from TTI.</summary>
        public static bool IsCountermeasureOrDecoyMissile(Missile m)
        {
            if (m == null) return false;
            if (CountermeasureTokenIn(m.GetType().Name))
                return true;
            try
            {
                var tr = Traverse.Create(m);
                var defF = tr.Field("definition");
                if (defF.FieldExists())
                {
                    var def = defF.GetValue();
                    if (def != null)
                    {
                        var dtr = Traverse.Create(def);
                        foreach (var fn in new[] { "code", "Code" })
                        {
                            var cf = dtr.Field(fn);
                            if (cf.FieldExists() && cf.GetValue() is string codes && CountermeasureTokenIn(codes))
                                return true;
                        }
                    }
                }

                foreach (var fname in new[] { "missile", "missileDefinition", "MissileDefinition" })
                {
                    var f = tr.Field(fname);
                    if (!f.FieldExists()) continue;
                    var md = f.GetValue();
                    if (md == null) continue;
                    var mdtr = Traverse.Create(md);
                    foreach (var cn in new[] { "code", "Code", "name", "Name" })
                    {
                        var cf = mdtr.Field(cn);
                        if (cf.FieldExists() && cf.GetValue() is string s && CountermeasureTokenIn(s))
                            return true;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        static string GetMissileHudWeaponLabel(Missile m)
        {
            try
            {
                var tr = Traverse.Create(m);
                foreach (var fname in new[] { "missile", "missileDefinition", "MissileDefinition", "missile" })
                {
                    var f = tr.Field(fname);
                    if (!f.FieldExists()) continue;
                    var md = f.GetValue();
                    if (md == null) continue;
                    var mdtr = Traverse.Create(md);
                    foreach (var cn in new[] { "code", "Code" })
                    {
                        var cf = mdtr.Field(cn);
                        if (cf.FieldExists() && cf.GetValue() is string s && !string.IsNullOrEmpty(s))
                            return s;
                    }
                }

                var defF = tr.Field("definition");
                if (defF.FieldExists())
                {
                    var def = defF.GetValue();
                    if (def != null)
                    {
                        var dtr = Traverse.Create(def);
                        foreach (var fn in new[] { "code", "Code" })
                        {
                            var cf = dtr.Field(fn);
                            if (cf.FieldExists() && cf.GetValue() is string s && !string.IsNullOrEmpty(s))
                                return s;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return "Missile";
        }

        /// <summary>Registers a spawned <see cref="Missile"/> (not the rack <see cref="MountedMissile"/>).</summary>
        public static void RegisterMissileForTTI(Missile missile)
        {
            if (missile == null) return;
            if (IsCountermeasureOrDecoyMissile(missile)) return;
            if (!IsMissileFromLocalPlayer(missile)) return;

            var tr = Traverse.Create(missile);
            Unit target = TryGetMissileTargetUnit(tr);

            GlobalPosition? aimGp = null;
            if (TryGetMemberValue(tr, new[] { "aimPoint", "aimpoint", "AimPoint", "laserAimPoint", "LaserAimPoint" }, out object aimObj))
            {
                if (aimObj is GlobalPosition gp)
                    aimGp = gp;
            }

            Vector3? inherited = null;
            if (TryGetMemberValue(tr, new[] { "startingVelocity", "StartingVelocity", "initialVelocity", "InitialVelocity", "inheritedVelocity", "InheritedVelocity" }, out object velObj))
            {
                if (velObj is Vector3 v3 && v3.sqrMagnitude > 0.01f)
                    inherited = v3;
            }

            RegisterProjectileForTTI(missile, GetMissileHudWeaponLabel(missile), target, aimGp, inherited);
        }

        static bool IsMissileFromLocalPlayer(Missile missile)
        {
            try
            {
                var local = GameBindings.Player.Aircraft.GetAircraft(silent: true);
                if (local == null) return false;

                var tr = Traverse.Create(missile);
                if (TryGetMemberValue(tr, new[] { "owner", "Owner", "launchOwner", "LaunchOwner", "launchUnit", "LaunchUnit", "source", "Source", "launcher", "Launcher" }, out object o))
                {
                    if (o != null && ReferenceEquals(o, local))
                        return true;
                    if (o is Unit u && ReferenceEquals(u, local))
                        return true;
                }

                // ownerID vs local persistentID (common when owner reference is not wired on frame 0)
                var oidF = tr.Field("ownerID");
                var localTr = Traverse.Create(local);
                var pidF = localTr.Field("persistentID");
                if (oidF.FieldExists() && pidF.FieldExists())
                {
                    var oid = oidF.GetValue();
                    var pid = pidF.GetValue();
                    if (oid != null && pid != null && oid.Equals(pid))
                        return true;
                }

                var ilp = tr.Property("IsLocalPlayer");
                if (ilp.PropertyExists() && ilp.GetValue<bool>())
                    return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        static bool TryGetMemberValue(Traverse tr, string[] names, out object value)
        {
            value = null;
            foreach (var n in names)
            {
                try
                {
                    var f = tr.Field(n);
                    if (f.FieldExists())
                    {
                        value = f.GetValue();
                        if (value != null) return true;
                    }
                }
                catch
                {
                    // next name
                }

                try
                {
                    var p = tr.Property(n);
                    if (p.PropertyExists())
                    {
                        value = p.GetValue();
                        if (value != null) return true;
                    }
                }
                catch
                {
                    // next name
                }
            }

            return false;
        }

        static bool TryReadGlobalPositionFromTraverse(Traverse tr, out Vector3 world)
        {
            world = default;
            if (!TryGetMemberValue(tr, new[] { "aimPoint", "aimpoint", "AimPoint", "laserAimPoint", "LaserAimPoint" }, out object aimObj) || aimObj == null)
                return false;
            if (aimObj is GlobalPosition gp)
                return TryGlobalPositionToVector3(gp, out world);
            return false;
        }

        static bool TryGlobalPositionToVector3(GlobalPosition gp, out Vector3 v)
        {
            v = default;
            var m = typeof(GlobalPosition).GetMethod("AsVector3");
            if (m != null)
            {
                v = (Vector3)m.Invoke(gp, null);
                return true;
            }

            var xProp = typeof(GlobalPosition).GetProperty("x");
            var yProp = typeof(GlobalPosition).GetProperty("y");
            var zProp = typeof(GlobalPosition).GetProperty("z");
            if (xProp != null && yProp != null && zProp != null)
            {
                v = new Vector3((float)xProp.GetValue(gp), (float)yProp.GetValue(gp), (float)zProp.GetValue(gp));
                return true;
            }

            return false;
        }

        static object GetRigidbodyMember(Component c)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = c.GetType(); t != null && t != typeof(Component); t = t.BaseType)
            {
                foreach (var name in new[] { "rb", "rigidbody", "rigidBody", "Rb", "Rigidbody" })
                {
                    var p = t.GetProperty(name, flags);
                    if (p != null && typeof(Rigidbody).IsAssignableFrom(p.PropertyType))
                        return p.GetValue(c);
                    var f = t.GetField(name, flags);
                    if (f != null && typeof(Rigidbody).IsAssignableFrom(f.FieldType))
                        return f.GetValue(c);
                }
            }
            return null;
        }

        /// <summary>
        /// Helper method to register projectiles for TTI tracking
        /// </summary>
        public static void RegisterProjectileForTTI(object projectile, string weaponType, Unit target = null, GlobalPosition? targetPos = null, Vector3? inheritedVelocity = null)
        {
            try
            {
                if (projectile is Missile mcm && IsCountermeasureOrDecoyMissile(mcm))
                    return;

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
                    if (projectile is Component compVel)
                        TryGetComponentRigidbodyVelocity(compVel, out velocity);
                    else
                    {
                        const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        for (var t = projType; t != null && t != typeof(object); t = t.BaseType)
                        {
                            Rigidbody found = null;
                            foreach (var name in new[] { "rb", "rigidbody", "rigidBody" })
                            {
                                var rbProp = t.GetProperty(name, bf);
                                if (rbProp != null && typeof(Rigidbody).IsAssignableFrom(rbProp.PropertyType))
                                    found = rbProp.GetValue(projectile) as Rigidbody;
                                if (found == null)
                                {
                                    var rbField = t.GetField(name, bf);
                                    if (rbField != null && typeof(Rigidbody).IsAssignableFrom(rbField.FieldType))
                                        found = rbField.GetValue(projectile) as Rigidbody;
                                }
                                if (found != null) break;
                            }
                            if (found != null)
                            {
                                velocity = found.velocity;
                                break;
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

                // Rail launch / first frame: RB often reads zero; game passes platform + ejection speed here
                if (velocity.magnitude < 0.5f && inheritedVelocity.HasValue && inheritedVelocity.Value.magnitude > 0.5f)
                    velocity = inheritedVelocity.Value;

                // Get target position
                Vector3 finalTargetPos = position;
                bool isGuided = false;
                bool isBallistic = weaponType.Contains("Bomb");

                if (targetPos.HasValue && TryGlobalPositionToVector3(targetPos.Value, out var gpVec))
                {
                    finalTargetPos = gpVec;
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
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeToImpact] Error registering projectile: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Legacy patch using dynamic discovery (fallback)
    /// Note: This patch is disabled as it's optional and causes issues during Harmony discovery.
    /// The Time-To-Impact feature is handled by the patches for MountedMissile.Fire and Laser.Fire (plus live state refresh each frame).
    /// </summary>
    [HarmonyPatch]
    static class ProjectileSpawnPatch
    {
        static bool Prepare()
        {
            // Always return false to disable this optional patch
            // The TTI feature works through MountedMissileFirePatch, LaserFirePatch, and per-frame live state refresh.
            return false;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            try
            {
                var assembly = typeof(Unit).Assembly;
                if (assembly == null) return null;
                
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
                    return null;
                }

                if (allTypes == null || allTypes.Length == 0) return null;

                // Look for Bomb classes
                foreach (var type in allTypes)
                {
                    if (type == null) continue;
                    
                    if (type.Name.Contains("Bomb") && !type.Name.Contains("Missile"))
                    {
                        try
                        {
                            // Look for Awake or Fire methods
                            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | 
                                                          System.Reflection.BindingFlags.Instance | 
                                                          System.Reflection.BindingFlags.NonPublic);
                            if (methods == null) continue;
                            
                            foreach (var method in methods)
                            {
                                if (method == null) continue;
                                if ((method.Name == "Awake" || method.Name.Contains("Fire")) && 
                                    !method.IsAbstract && !method.IsVirtual)
                                {
                                    Plugin.Log.LogInfo($"[TimeToImpact] Found potential bomb spawn method: {type.FullName}.{method.Name}");
                                    return method;
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
            }
            catch
            {
                // Silently fail - this is optional
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
    /// Note: Disabled because patching MonoBehaviour.OnDestroy() causes Harmony errors.
    /// Projectile cleanup is handled by the TimeToImpactDisplay's internal tracking.
    /// </summary>
    [HarmonyPatch]
    static class ProjectileDestroyPatch
    {
        static bool Prepare()
        {
            // Disable this patch - it causes Harmony errors when trying to patch MonoBehaviour.OnDestroy()
            // Projectile cleanup is handled automatically by TimeToImpactDisplay
            return false;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            // This method will never be called since Prepare() returns false
            return null;
        }

        static void Postfix(MonoBehaviour __instance)
        {
            // This will never be called since Prepare() returns false
        }
    }
}
