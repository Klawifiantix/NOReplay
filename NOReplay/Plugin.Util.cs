using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static readonly System.Collections.Generic.Dictionary<string, Type?> _typeCache =
            new System.Collections.Generic.Dictionary<string, Type?>();

        internal static Type? FindGameType(string typeName)
        {
            if (_typeCache.TryGetValue(typeName, out var cached)) return cached;
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { assemblies = Array.Empty<Assembly>(); }
            // Assembly-CSharp first. Another BepInEx plugin must not win a game type name.
            Type? found = FindNamedType(assemblies, typeName, gameOnly: true);
            if (found == null)
                found = FindNamedType(assemblies, typeName, gameOnly: false);
            _typeCache[typeName] = found;
            return found;
        }

        static Type? FindNamedType(Assembly[] assemblies, string typeName, bool gameOnly)
        {
            foreach (var a in assemblies)
            {
                string an = a.GetName().Name ?? "";
                bool game = an == "Assembly-CSharp";
                if (gameOnly != game) continue;
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t != null && t.Name == typeName)
                        return t;
                }
            }
            return null;
        }

        static Transform? _datum;
        static bool _datumLogged;

        internal static Transform? Datum
        {
            get { EnsureDatum(); return _datum; }
        }

        internal static void EnsureDatum()
        {
            if (_datum != null) return;
            _datum = ResolveDatum();
            if (_datum == null || _datumLogged) return;
            _datumLogged = true;
            Log.LogInfo("Datum bereit " + _datum.position.ToString("F0"));
            if (CurrentDoc != null) ApplyWeather(CurrentDoc);
        }

        internal static Vector3 GlobalToLocal(float gx, float gy, float gz)
        {
            if (_datum == null) EnsureDatum();
            if (_datum == null) return new Vector3(gx, gy, gz);
            return _datum.TransformPoint(new Vector3(gx, gy, gz));
        }

        internal static Vector3 OrdnancePos(float gx, float gy, float gz)
        {
            Vector3 flat = GlobalToLocal(gx, 0f, gz);
            return new Vector3(flat.x, flat.y + gy, flat.z);
        }

        static Transform? ResolveDatum()
        {
            var foType = FindGameType("FloatingOrigin");
            if (foType == null) return null;
            var inst = foType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                       ?.GetValue(null);
            if (inst == null)
            {
                var all = Resources.FindObjectsOfTypeAll(foType);
                if (all.Length > 0) inst = all[0];
            }
            if (inst == null) return null;
            return foType.GetField("globalDatum", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?.GetValue(inst) as Transform;
        }

        internal static bool IsAircraftPublic(string? type, string? name) => IsAircraft(type ?? "", name);
        internal static bool IsOrdnancePublic(string? type, string? name) => IsOrdnance(type, name);

        static bool IsAircraft(string type) => IsAircraft(type, null);

        static bool IsAircraft(string type, string? name)
        {
            if ((type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if ((type ?? "").IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if ((type ?? "").IndexOf("Misc", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (IsOrdnance(type, name)) return false;
            if (!string.IsNullOrEmpty(type))
            {
                if (type.IndexOf("FixedWing", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (type.IndexOf("Rotorcraft", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (type.IndexOf("+Air+", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (type.StartsWith("Air+", StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            string[] keys =
            {
                "vagrant", "chicane", "compass", "cricket", "tarantula", "ibis",
                "revoker", "darkreach", "medusa", "scorpion", "ifrit", "vortex",
                "vt-7", "sah-46", "t/a-30", "ci-22", "vl-49", "uh-90", "kr-67", "fs-20"
            };
            foreach (var k in keys)
                if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        const float StartWindow = 2.5f;

        static float TrackBorn(ReplayObject obj)
        {
            if (obj.SpawnAt >= 0f) return obj.SpawnAt;
            if (obj.Samples != null && obj.Samples.Count > 0) return obj.Samples[0].Time;
            return 9999f;
        }

        static bool IsStartUnit(ReplayObject obj) => TrackBorn(obj) <= StartWindow;

        static readonly string[] SceneryHints =
        {
            "Platform", "Cylinder", "Column", "Wall", "Ring", "Container",
            "Bend", "Truss", "Cone", "Camo", "Pipeline", "Hull down",
            "Concrete Pad", "Crane", "Gabion",
            "Cooling Tower", "Flare Stack", "CoolingTower", "FlareStack"
        };

        static bool IsEmplacement(string? type, string? name)
        {
            string n = name ?? "";
            if (n.IndexOf("Emplacement", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("IRM-S1", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("AT-145", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("23mm", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("12.7", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Pillbox", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool IsBuilding(string? type, string? name)
        {
            if (IsScenery(type, name)) return false;
            if (IsEmplacement(type, name)) return true;
            string t = type ?? "";
            if (t.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0
                && t.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) < 0
                && t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            return false;
        }

        static bool IsScenery(string? type, string? name)
        {
            string t = type ?? "";
            string n = name ?? "";
            // Vehicles are never scenery
            if (t.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Scenery", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Prop", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Scenery", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int i = 0; i < SceneryHints.Length; i++)
                if (n.IndexOf(SceneryHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static readonly string[] OrdnanceHints =
        {
            "IRM", "MMR", "AAM", "AGR", "AGM", "ARAD", "ASHM", "ALM-", "ALND",
            "PAB", "GPO", "GBM", "RAM-", "NL-98", "Piledriver", "Tusko",
            "ATP-1", "Eyeball", "Lynchpin", "Kingpin", "Scimitar", "Scythe"
        };

        static bool IsOrdnance(string? type, string? name)
        {
            string t = type ?? "";
            string n = name ?? "";
            if (n.IndexOf("Emplacement", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Vagrant", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("VT-7", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Chicane", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Compass", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Cricket", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Tarantula", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ibis", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Revoker", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ifrit", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Vortex", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Darkreach", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Medusa", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Brawler", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (t.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Rocket", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Weapon+", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int i = 0; i < OrdnanceHints.Length; i++)
                if (n.IndexOf(OrdnanceHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static bool ShouldQueue(ReplayObject obj)
        {
            if ((obj.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (obj.Samples.Count == 0) return false;
            if (IsAircraft(obj.Type, obj.Name)) return true;
            if (IsOrdnance(obj.Type, obj.Name)) return true;
            if (IsScenery(obj.Type, obj.Name)) return true;
            if (IsEmplacement(obj.Type, obj.Name)) return true;
            if (IsBuilding(obj.Type, obj.Name)) return true;
            string t = obj.Type ?? "";
            if (t.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t == "Ground") return true;
            if (t.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void DumpTypeShape(string typeName)
        {
            var t = FindGameType(typeName);
            if (t == null) { Log.LogWarning("Typ fehlt: " + typeName); return; }
            Log.LogInfo("--- " + t.FullName + " ---");
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var c in t.GetConstructors(flags))
            {
                var ps = c.GetParameters();
                string sig = "ctor(";
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sig += ", ";
                    sig += ps[i].ParameterType.Name + " " + ps[i].Name;
                }
                Log.LogInfo("  " + sig + ")");
            }
            foreach (var f in t.GetFields(flags))
            {
                if (f.Name.Contains("BackingField")) continue;
                Log.LogInfo("  field " + f.FieldType.Name + " " + f.Name);
            }
            foreach (var p in t.GetProperties(flags))
                Log.LogInfo("  prop  " + p.PropertyType.Name + " " + p.Name);
        }

        internal static void RaiseAudioVoices()
        {
            try
            {
                var ast = Type.GetType("UnityEngine.AudioSettings, UnityEngine.AudioModule");
                if (ast == null) return;
                var get = ast.GetMethod("GetConfiguration", BindingFlags.Static | BindingFlags.Public);
                var reset = ast.GetMethod("Reset", BindingFlags.Static | BindingFlags.Public);
                if (get == null || reset == null) return;
                object cfg = get.Invoke(null, null);
                var ct = cfg.GetType();
                var real = ct.GetField("numRealVoices");
                var virt = ct.GetField("numVirtualVoices");
                if (real?.GetValue(cfg) is int r && r >= 64 && virt?.GetValue(cfg) is int v && v >= 256)
                    return;
                real?.SetValue(cfg, 64);
                virt?.SetValue(cfg, 256);
                reset.Invoke(null, new[] { cfg });
                Log.LogInfo("Audio-Voices real=64 virt=256");
            }
            catch (Exception ex) { Log.LogWarning("Audio-Voices: " + ex.Message); }
        }

        static int _listenFrame = -1;
        static Vector3 _listenPos;
        static bool _listenOk;
        static Component? _followUnit;
        static Component? _camMgr;
        static FieldInfo? _followField;
        static bool _followFieldReady;
        static MethodInfo? _setDoppler;
        static Type? _dopplerType;
        static Component? _dopplerTarget;
        static readonly object[] _dopplerOff = { false };
        static readonly object[] _dopplerOn = { true };
        static bool? _dopplerSpatial;
        static PropertyInfo? _camStateProp;
        static FieldInfo? _tvStateField;
        static FieldInfo? _freeStateField;
        static bool _camStateReady;

        internal static bool CameraIsTvOrFree()
        {
            CacheListener();
            return CamStateIsTvOrFree();
        }

        static bool CamStateIsTvOrFree()
        {
            if (_camMgr == null) return false;
            if (!_camStateReady)
            {
                _camStateReady = true;
                var t = _camMgr.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _camStateProp = t.GetProperty("currentState", flags);
                _tvStateField = t.GetField("TVState", flags) ?? t.GetField("tvState", flags);
                _freeStateField = t.GetField("freeState", flags);
            }
            var state = _camStateProp?.GetValue(_camMgr);
            if (state == null) return false;
            var tv = _tvStateField?.GetValue(_camMgr);
            var free = _freeStateField?.GetValue(_camMgr);
            return (tv != null && ReferenceEquals(state, tv)) || (free != null && ReferenceEquals(state, free));
        }

        internal static void CacheListener()
        {
            if (_listenFrame == Time.frameCount) return;
            _listenFrame = Time.frameCount;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (_camMgr == null || _camMgr.gameObject == null || !_camMgr.gameObject.scene.IsValid())
            {
                _camMgr = null;
                _followFieldReady = false;
                _followField = null;
                var camType = FindGameType("CameraStateManager");
                if (camType != null)
                {
                    Component? best = null;
                    foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
                    {
                        if (cam is not Component cc) continue;
                        if (!cc.gameObject.scene.IsValid() || !cc.gameObject.activeInHierarchy) continue;
                        bool listen = cc.GetComponent("AudioListener") != null;
                        if (listen) { best = cc; break; }
                        if (best == null) best = cc;
                    }
                    _camMgr = best;
                }
            }
            _followUnit = null;
            _listenOk = false;
            if (_camMgr != null)
            {
                _listenPos = _camMgr.transform.position;
                _listenOk = true;
                if (!_followFieldReady)
                {
                    _followField = _camMgr.GetType().GetField("followingUnit", flags);
                    _followFieldReady = true;
                }
                if (_followField?.GetValue(_camMgr) is Component u)
                {
                    _followUnit = u;
                    bool spatial = CamStateIsTvOrFree();
                    if (!ReferenceEquals(u, _dopplerTarget) || _dopplerSpatial != spatial)
                    {
                        _dopplerTarget = u;
                        _dopplerSpatial = spatial;
                        if (_setDoppler == null || _dopplerType != u.GetType())
                        {
                            _dopplerType = u.GetType();
                            _setDoppler = _dopplerType.GetMethod("SetDoppler", flags);
                        }
                        try { _setDoppler?.Invoke(u, spatial ? _dopplerOn : _dopplerOff); }
                        catch { }
                    }
                }
                else
                {
                    _dopplerTarget = null;
                    _dopplerSpatial = null;
                }
            }
            else if (Camera.main != null)
            {
                _listenPos = Camera.main.transform.position;
                _listenOk = true;
            }
        }

        internal static GameObject? FollowedObject()
        {
            CacheListener();
            return _followUnit != null ? _followUnit.gameObject : null;
        }

        internal static bool IsFollowed(GameObject go)
        {
            if (go == null) return false;
            CacheListener();
            if (_followUnit == null) return false;
            var ut = _followUnit.transform;
            if (ut == null) return false;
            return ut.root == go.transform.root || go.transform.IsChildOf(ut) || ut.IsChildOf(go.transform);
        }

        internal static bool AudioNear(GameObject go, float maxM)
        {
            if (go == null) return false;
            CacheListener();
            if (_followUnit != null)
            {
                var ut = _followUnit.transform;
                if (ut != null && (ut.root == go.transform.root
                    || go.transform.IsChildOf(ut) || ut.IsChildOf(go.transform)))
                    return true;
            }
            return AudioNearWorld(go.transform.position, maxM);
        }

        internal static bool ListenPoint(out Vector3 pos)
        {
            CacheListener();
            pos = _listenPos;
            return _listenOk;
        }

        internal static bool AudioNearWorld(Vector3 world, float maxM)
        {
            CacheListener();
            if (!_listenOk) return false;
            return (world - _listenPos).sqrMagnitude <= maxM * maxM;
        }
    }
}