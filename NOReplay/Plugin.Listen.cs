using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static bool _listenPatched;
        static int _nAir;
        static int _nVeh;
        static int _nShip;
        static float _lastSum;

        static void InstallListenPatches()
        {
            if (_listenPatched) return;
            _listenPatched = true;
            PatchNamed("NetworkManagerNuclearOption", "StartHost", false);
            PatchNamed("NetworkManagerNuclearOption", "ServerMissionStart", false);
            PatchNamed("MissionManager", "SetMission", false);
            PatchNamed("MissionManager", "StartMission", false);
            PatchNamed("Spawner", "SpawnAircraft", true);
            PatchNamed("Spawner", "SpawnVehicle", true);
            PatchNamed("Spawner", "SpawnShip", true);
            PatchNamed("Spawner", "SpawnBuilding", true);
            PatchNamed("Spawner", "SpawnScenery", true);
            PatchNamed("Spawner", "SpawnLocal", true);
            PatchSkip("Spawner", "TrySpawnPlayerControlled");
            BlockEjectMethods();
            BlockBreakMethods();
            BlockComplexPhysics();
            BlockFireMethods();
            BlockLaserSim();
            BlockDeadMapClicks();
            BlockFollowOrdnance();
            BlockMissileDeath();
            BlockMissileFlight();
            BlockAiGear();
            BlockAirbrakeJobs();
            BlockFarBoom();
            GateAudioPlay();
            BlockSonicMute();
            BlockStateLabel();
            Log.LogInfo("Listen-Patches gesetzt (ohne SOM.Spawn-Spam).");
        }

        static void BlockStateLabel()
        {
            int n = 0;
            n += PatchTextSetter("UnityEngine.UI.Text, UnityEngine.UI");
            n += PatchTextSetter("TMPro.TMP_Text, Unity.TextMeshPro");
            Log.LogInfo("State-Text blockiert: " + n);
        }

        static int PatchTextSetter(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t == null) return 0;
            var m = t.GetMethod("set_text", BindingFlags.Instance | BindingFlags.Public);
            if (m == null) return 0;
            try
            {
                Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipStateText)));
                Log.LogInfo("State-Text " + t.Name + ".set_text");
                return 1;
            }
            catch (Exception ex)
            {
                Log.LogWarning("State-Text " + t.Name + ": " + ex.Message);
                return 0;
            }
        }

        internal static int StateTextId;

        static bool SkipStateText(object __instance, string value)
        {
            if (CurrentDoc == null || StateTextId == 0) return true;
            if (value == "Replay") return true;
            var c = __instance as Component;
            if (c == null || c.GetInstanceID() != StateTextId) return true;
            return false;
        }

        static bool SkipIfGhost(object __instance)
        {
            var c = __instance as Component;
            if (c == null) return true;
            return c.GetComponent<ReplayGhost>() == null
                && c.GetComponentInParent<ReplayGhost>() == null;
        }

        internal static bool AllowOrdnanceSpawn;
        internal static bool AllowReplayGear;

        static bool SkipGearIfGhost(object __instance)
        {
            if (AllowReplayGear) return true;
            return SkipIfGhost(__instance);
        }

        static void BlockAiGear()
        {
            var t = FindGameType("Aircraft");
            if (t == null) return;
            int n = 0;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.DeclaringType != t || m.Name != "SetGear") continue;
                var p = m.GetParameters();
                if (p.Length != 1 || p[0].ParameterType != typeof(bool)) continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipGearIfGhost)));
                    n++;
                }
                catch (Exception ex) { Log.LogWarning("Gear-Block SetGear: " + ex.Message); }
            }
            Log.LogInfo("Gear-Block Aircraft.SetGear(bool) x" + n);
        }

        static void BlockSonicMute()
        {
            var t = FindGameType("Unit");
            var m = t?.GetMethod("SetSoundsMuted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            try
            {
                Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipSonicMute)));
                Log.LogInfo("Sonic-Mute-Block Unit.SetSoundsMuted");
            }
            catch (Exception ex) { Log.LogWarning("Sonic-Mute: " + ex.Message); }
        }

        static bool SkipSonicMute()
        {
            return CurrentDoc == null;
        }

        static void GateAudioPlay()
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? FindGameType("AudioSource");
            if (at == null)
            {
                Log.LogWarning("Audio-Gate: AudioSource fehlt");
                return;
            }
            int n = 0;
            foreach (var m in at.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name != "Play" && m.Name != "PlayOneShot") continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipFarPlay)));
                    n++;
                }
                catch (Exception ex) { Log.LogWarning("Audio-Gate " + m.Name + ": " + ex.Message); }
            }
            Log.LogInfo("Audio-Gate Play x" + n);
        }

        static bool SkipFarPlay(object __instance)
        {
            if (CurrentDoc == null) return true;
            var c = __instance as Component;
            if (c == null) return true;
            if (c.gameObject.name == "NOReplay_GunShot" || c.gameObject.name == "NOReplay_GunLoop") return true;
            var rg = c.GetComponentInParent<ReplayGhost>();
            if (rg != null && rg.IsOrdnancePublic) return true;
            CacheListener();
            if (!_listenOk) return true;
            if (AudioNear(c.gameObject, 1400f)) return true;
            if (rg != null && _followUnit != null)
            {
                var ut = _followUnit.transform;
                if (ut != null && (ut.root == rg.transform.root || rg.transform.IsChildOf(ut) || ut.IsChildOf(rg.transform)))
                    return true;
            }
            return false;
        }

        static void BlockFarBoom()
        {
            var t = FindGameType("ExplosionAudio");
            if (t == null) return;
            var m = t.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            try
            {
                Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipFarExplosion)));
                Log.LogInfo("Boom-Block ExplosionAudio.Start");
            }
            catch (Exception ex) { Log.LogWarning("Boom-Block: " + ex.Message); }
        }

        static void StripMissileAudio(object __instance)
        {
        }

        static bool SkipFarExplosion(object __instance)
        {
            var c = __instance as Component;
            if (c == null) return true;
            if (AudioNearWorld(c.transform.position, 700f)) return true;
            try
            {
                var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                         ?? FindGameType("AudioSource");
                if (at != null)
                {
                    var stop = at.GetMethod("Stop", Type.EmptyTypes);
                    foreach (var s in c.GetComponentsInChildren(at, true))
                    {
                        if (s == null) continue;
                        at.GetProperty("volume")?.SetValue(s, 0f);
                        stop?.Invoke(s, null);
                    }
                }
            }
            catch { }
            return false;
        }

        static void BlockAirbrakeJobs()
        {
            int n = 0;
            void one(string type, string method)
            {
                var t = FindGameType(type);
                if (t == null) return;
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.DeclaringType != t || m.Name != method) continue;
                    try
                    {
                        Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipSurfacesInReplay)));
                        n++;
                    }
                    catch (Exception ex) { Log.LogWarning(type + "." + method + ": " + ex.Message); }
                }
            }
            one("Airbrake", "Update");
            one("Airbrake", "FixedUpdate");
            one("ControlSurface", "UpdateJobFields");
            one("ControlSurface", "ApplyJobFields");
            one("ControlSurface", "Aero");
            var jm = FindGameType("JobManager") ?? FindGameType("NuclearOption.Jobs.JobManager");
            if (jm != null)
            {
                foreach (var m in jm.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "Add") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    if (!ps[0].ParameterType.FullName.Contains("ControlSurface")) continue;
                    try
                    {
                        Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipSurfacesInReplay)));
                        n++;
                    }
                    catch (Exception ex) { Log.LogWarning("JobManager.Add CS: " + ex.Message); }
                }
            }
            Log.LogInfo("Airbrake/Surface-Block x" + n);
        }

        static bool SkipSurfacesInReplay()
        {
            return CurrentDoc == null;
        }

        static bool SkipFireInReplay()
        {
            if (AllowOrdnanceSpawn) return true;
            return CurrentDoc == null;
        }

        static void BlockDeadMapClicks()
        {
            var t = FindGameType("UnitMapIcon");
            if (t == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "ClickIcon", "UpdateIcon", "OnSelectIcon" })
            {
                foreach (var m in t.GetMethods(flags))
                {
                    if (m.DeclaringType != t && name != "ClickIcon") continue;
                    if (m.Name != name) continue;
                    try
                    {
                        if (name == "ClickIcon")
                            Harmony.Patch(m,
                                prefix: new HarmonyMethod(typeof(Plugin), nameof(PrefixMapClick)),
                                postfix: new HarmonyMethod(typeof(Plugin), nameof(PostfixMapClick)));
                        else
                            Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipDeadMapIcon)));
                        Log.LogInfo("Map-Block UnitMapIcon." + name);
                    }
                    catch (Exception ex) { Log.LogWarning("Map-Block " + name + ": " + ex.Message); }
                }
            }
        }

        static bool PrefixMapClick(object __instance)
        {
            _mapClickFollow = true;
            return SkipDeadMapIcon(__instance);
        }

        static void PostfixMapClick() => _mapClickFollow = false;

        static bool SkipDeadMapIcon(object __instance)
        {
            if (CurrentDoc == null) return true;
            var c = __instance as Component;
            if (c == null) return true;
            object? unit = null;
            try { unit = __instance.GetType().GetProperty("unit")?.GetValue(__instance); } catch { }
            var uc = unit as Component;
            ReplayGhost? rg = null;
            if (uc != null)
                rg = uc.GetComponent<ReplayGhost>() ?? uc.GetComponentInParent<ReplayGhost>();
            if (rg == null) return true;
            if (!rg.IsTrackAlive())
            {
                if (c.gameObject.activeSelf) c.gameObject.SetActive(false);
                return false;
            }
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            return true;
        }

        static void BlockMissileDeath()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = FindGameType("Missile");
            if (t == null) return;
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.DeclaringType != t) continue;
                if (m.Name != "Disappear" && m.Name != "Detonate" && m.Name != "RpcDetonate" && m.Name != "DelayedDestroy")
                    continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                    n++;
                }
                catch { }
            }
            Log.LogInfo("Missile-Death-Block x" + n);
        }

        static void BlockMissileFlight()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = FindGameType("Missile");
            if (t == null) return;
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.DeclaringType != t) continue;
                if (m.Name != "FixedUpdate" && m.Name != "ServerFixedUpdate" && m.Name != "Update")
                    continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                    n++;
                }
                catch { }
            }
            Log.LogInfo("Missile-Flight-Block x" + n);
        }

        static void BlockMissileDisappear()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var typeName in new[] { "Missile", "Rocket", "Bomb", "Unit" })
            {
                var t = FindGameType(typeName);
                if (t == null) continue;
                foreach (var name in new[] { "Disappear", "SetTangible" })
                {
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.DeclaringType != t || m.Name != name) continue;
                        try
                        {
                            Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                            Log.LogInfo("Disappear-Block " + typeName + "." + name);
                        }
                        catch { }
                    }
                }
            }
        }

        static void BlockMissileSim()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = FindGameType("Missile");
            if (t == null) return;
            string[] names =
            {
                "FixedUpdate", "ServerFixedUpdate", "Detonate", "RpcDetonate",
                "UnitDisabled", "DelayedDestroy", "ImpactDelayedFuse", "Arm"
            };
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.DeclaringType != t) continue;
                bool hit = false;
                for (int i = 0; i < names.Length; i++)
                    if (m.Name == names[i] || m.Name.IndexOf(names[i], StringComparison.Ordinal) >= 0)
                        hit = true;
                if (!hit) continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                    n++;
                }
                catch { }
            }
            Log.LogInfo("Missile-Sim-Block Methoden: " + n);
        }

        static void BlockFollowOrdnance()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var typeName in new[] { "CameraStateManager", "SpectatorCamera", "CameraDirector", "CockpitCamera" })
            {
                var t = FindGameType(typeName);
                if (t == null) continue;
                foreach (var m in t.GetMethods(flags))
                {
                    if (m.Name != "SetFollowingUnit")
                        continue;
                    try
                    {
                        Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipFollowIfMissile)));
                        Log.LogInfo("Cam-Block " + t.Name + "." + m.Name);
                    }
                    catch { }
                }
            }
        }

        static bool _mapClickFollow;

        static bool SkipFollowIfMissile(object __0)
        {
            if (CurrentDoc == null) return true;
            if (_mapClickFollow) return true;
            var a = __0 as Component;
            if (a == null) return true;
            var rg = a.GetComponent<ReplayGhost>() ?? a.GetComponentInParent<ReplayGhost>();
            if (rg != null && IsOrdnance(rg.Track?.Type, rg.Track?.Name))
                return false;
            return true;
        }

        static void BlockFireMethods()
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] keys =
            {
                "Fire", "Launch", "Shoot", "SpawnBullet", "SpawnMissile", "SpawnRocket",
                "SpawnBomb", "ReleaseWeapon", "DropBomb", "DropStore", "TryFire"
            };
            int n = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if ((a.GetName().Name ?? "") != "Assembly-CSharp") continue;
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.DeclaringType != t) continue;
                        if (m.IsGenericMethod || m.IsAbstract || m.ContainsGenericParameters) continue;
                        if (m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal))
                            continue;
                        if (m.Name.StartsWith("get_", StringComparison.Ordinal) || m.Name.StartsWith("set_", StringComparison.Ordinal))
                            continue;
                        if (m.Name.StartsWith("UserCode_", StringComparison.Ordinal) || m.Name.StartsWith("Skeleton_", StringComparison.Ordinal))
                            continue;
                        if (m.Name.IndexOf("OnFire", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (m.Name.IndexOf("CatchFire", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (m.Name.IndexOf("FireDam", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (m.Name.IndexOf("OnFireChange", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        bool hit = false;
                        foreach (var k in keys)
                            if (string.Equals(m.Name, k, StringComparison.OrdinalIgnoreCase)
                                || m.Name.EndsWith(k, StringComparison.OrdinalIgnoreCase))
                            { hit = true; break; }
                        if (!hit) continue;
                        if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) continue;
                        try
                        {
                            Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipFireInReplay)));
                            n++;
                            if (n <= 24)
                                Log.LogInfo("Fire-Block " + t.Name + "." + m.Name);
                        }
                        catch { }
                    }
                }
            }
            Log.LogInfo("Fire-Block Methoden: " + n);
        }

        static int PatchFireType(string typeName, string methodName)
        {
            var t = FindGameType(typeName);
            if (t == null) return 0;
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.DeclaringType != t) continue;
                if (m.Name != methodName) continue;
                if (m.IsGenericMethod || m.ContainsGenericParameters) continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipFireInReplay)));
                    n++;
                    Log.LogInfo("Fire-Block " + typeName + "." + methodName);
                }
                catch (Exception ex) { Log.LogWarning("Fire-Block " + typeName + "." + methodName + ": " + ex.Message); }
            }
            return n;
        }

        static void BlockLaserSim()
        {
            var t = FindGameType("Laser");
            if (t == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.DeclaringType != t) continue;
                if (m.IsGenericMethod || m.ContainsGenericParameters) continue;
                if (m.Name != "Update" && m.Name != "LateUpdate" && m.Name != "FixedUpdate"
                    && m.Name != "Fire" && m.Name != "SetFiring" && m.Name != "CommandFire")
                    continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                    n++;
                }
                catch { }
            }
            Log.LogInfo("Laser-Sim-Block Methoden: " + n);
        }

        static void BlockComplexPhysics()
        {
            var t = FindGameType("Aircraft");
            if (t == null)
            {
                Log.LogInfo("ComplexPhys: Aircraft fehlt");
                return;
            }
            var m = t.GetMethod("SetComplexPhysics", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null)
            {
                Log.LogInfo("ComplexPhys: SetComplexPhysics fehlt");
                return;
            }
            try
            {
                Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipIfGhost)));
                Log.LogInfo("ComplexPhys-Block Aircraft.SetComplexPhysics");
            }
            catch (Exception ex)
            {
                Log.LogInfo("ComplexPhys-Block Fehler: " + ex.Message);
            }
        }

        static bool SkipEject() => CurrentDoc == null;

        static void BlockEjectMethods()
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] keys = { "Eject", "Bail", "Abandon", "LeaveAircraft", "ExitAircraft", "GetOut" };
            int n = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if ((a.GetName().Name ?? "") != "Assembly-CSharp") continue;
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.IsGenericMethod || m.IsAbstract || m.ContainsGenericParameters) continue;
                        bool hit = false;
                        foreach (var k in keys)
                            if (m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                        if (!hit) continue;
                        if (m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal))
                            continue;
                        if (m.Name.IndexOf("OnEject", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) continue;
                        try
                        {
                            Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipEject)));
                            n++;
                            if (n <= 12)
                                Log.LogInfo("Eject-Block " + t.Name + "." + m.Name);
                        }
                        catch { }
                    }
                }
            }
            Log.LogInfo("Eject-Block Methoden: " + n);
        }

        static void BlockBreakMethods()
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] keys = { "OverG", "Overload", "BreakPart", "DetachPart", "Jettison", "Structural", "PartFail", "ShedPart", "BecomeWreck", "MakeWreck", "CrashCheck" };
            int n = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if ((a.GetName().Name ?? "") != "Assembly-CSharp") continue;
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.DeclaringType != t) continue;
                        if (m.IsGenericMethod || m.IsAbstract || m.ContainsGenericParameters) continue;
                        if (m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal))
                            continue;
                        if (m.Name.StartsWith("UserCode_", StringComparison.Ordinal) || m.Name.StartsWith("Skeleton_", StringComparison.Ordinal))
                            continue;
                        bool hit = false;
                        foreach (var k in keys)
                            if (m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                        if (!hit) continue;
                        if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) continue;
                        try
                        {
                            Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipEject)));
                            n++;
                            if (n <= 16)
                                Log.LogInfo("Break-Block " + t.Name + "." + m.Name);
                        }
                        catch { }
                    }
                }
            }
            Log.LogInfo("Break-Block Methoden: " + n);
        }

        static void PatchNamed(string typeName, string methodName, bool hasResult)
        {
            var t = FindGameType(typeName);
            if (t == null)
            {
                Log.LogInfo("Listen: Typ fehlt " + typeName);
                return;
            }
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            int n = 0;
            string hook = hasResult ? nameof(ListenAfterCallResult) : nameof(ListenAfterCall);
            foreach (var m in t.GetMethods(flags))
            {
                if (m.Name != methodName) continue;
                if (m.IsGenericMethod) continue;
                try
                {
                    Harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), hook));
                    n++;
                }
                catch (Exception ex)
                {
                    Log.LogWarning("Patch " + typeName + "." + methodName + ": " + ex.Message);
                }
            }
            Log.LogInfo("Patch " + typeName + "." + methodName + " x" + n);
        }

        static bool SkipPlayerAircraftSpawn()
        {
            return CurrentDoc == null;
        }

        static void PatchSkip(string typeName, string methodName)
        {
            var t = FindGameType(typeName);
            if (t == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            int n = 0;
            foreach (var m in t.GetMethods(flags))
            {
                if (m.Name != methodName) continue;
                try
                {
                    Harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipPlayerAircraftSpawn)));
                    n++;
                }
                catch (Exception ex) { Log.LogWarning("Skip-Patch " + methodName + ": " + ex.Message); }
            }
            Log.LogInfo("Skip-Patch " + typeName + "." + methodName + " x" + n);
        }

        static void ListenAfterCall(MethodBase __originalMethod, object[] __args)
        {
            HandleListen(__originalMethod, __args, null);
        }

        static void ListenAfterCallResult(MethodBase __originalMethod, object[] __args, object __result)
        {
            HandleListen(__originalMethod, __args, __result);
        }

        static void HandleListen(MethodBase __originalMethod, object[] __args, object? __result)
        {
            if (__originalMethod == null) return;
            string name = __originalMethod.Name;
            if (name == "SpawnAircraft") _nAir++;
            else if (name == "SpawnVehicle") _nVeh++;
            else if (name == "SpawnShip") _nShip++;
            if (name == "SpawnAircraft" || name == "SpawnVehicle" || name == "SpawnShip"
                || name == "SpawnBuilding" || name == "SpawnScenery")
                TryAttachFromSpawn(__args, __result, name == "SpawnAircraft");

            bool detail = name == "StartHost" || name == "ServerMissionStart"
                          || name == "SetMission" || name == "StartMission"
                          || name == "SpawnLocal"
                          || (name == "SpawnAircraft" && _nAir <= 8)
                          || (name == "SpawnVehicle" && _nVeh <= 8)
                          || (name == "SpawnShip" && _nShip <= 4);

            if (name == "StartHost" || name == "StartMission")
            {
                Clock.Seek(0f);
                Clock.Paused = true;
                _holdClock = true;
            }
            if (name == "SpawnLocal")
                MuteSpawnedFlare(__args, __result);

            if (detail)
            {
                string owner = __originalMethod.DeclaringType != null ? __originalMethod.DeclaringType.Name : "?";
                Log.LogInfo("[Listen] " + owner + "." + name);
                if (__args != null)
                {
                    for (int i = 0; i < __args.Length && i < 14; i++)
                    {
                        object? a = __args[i];
                        if (a == null) { Log.LogInfo("  arg" + i + "=null"); continue; }
                        Log.LogInfo("  arg" + i + " " + a.GetType().Name + "=" + Short(a));
                        if (a.GetType().Name == "HostOptions")
                            DumpPublicish(a);
                    }
                }
            }

            if (Time.unscaledTime - _lastSum >= 5f)
            {
                _lastSum = Time.unscaledTime;
                Log.LogInfo("[Listen-Sum] air=" + _nAir + " veh=" + _nVeh + " ship=" + _nShip
                            + " fps~" + (1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)).ToString("0"));
            }
        }

        static void MuteSpawnedFlare(object[]? args, object? result)
        {
            string n = "";
            if (args != null && args.Length > 0 && args[0] is UnityEngine.Object u)
                n = u.name ?? "";
            if (n.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            var go = result as GameObject;
            if (go == null && result is Component rc) go = rc.gameObject;
            if (go == null) return;
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var s in go.GetComponentsInChildren(at, true))
            {
                if (s == null) continue;
                try
                {
                    at.GetProperty("volume")?.SetValue(s, 0f);
                    at.GetProperty("mute")?.SetValue(s, true);
                    at.GetProperty("priority")?.SetValue(s, 255);
                    at.GetProperty("playOnAwake")?.SetValue(s, false);
                    at.GetProperty("enabled")?.SetValue(s, false);
                    stop?.Invoke(s, null);
                }
                catch { }
            }
        }

        static string Short(object a)
        {
            if (a is UnityEngine.Object u)
                return u.name + " (" + u.GetType().Name + ")";
            string s = a.ToString() ?? "";
            if (s.Length > 80) s = s.Substring(0, 80);
            return s;
        }

        static void DumpPublicish(object obj)
        {
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in t.GetFields(flags))
            {
                if (f.Name.Contains("BackingField")) continue;
                try { Log.LogInfo("    ." + f.Name + "=" + f.GetValue(obj)); } catch { }
            }
        }

        static void DumpSession(string tag)
        {
            Log.LogInfo("=== Session " + tag + " ===");
            Log.LogInfo("Server aktiv=" + IsServerActive());
            var p = FindLocalNetworkPlayer();
            Log.LogInfo("Live Player=" + (p == null ? "null" : Short(p)));
        }
    }
}