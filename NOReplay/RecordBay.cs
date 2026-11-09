using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// CargoRamp hinge, BayDoor hinge, MountedCargo sliding off the rail, and
    /// the glass Canopy.Eject throws clear of the aircraft. The recorder is
    /// RecordEngine.unitObjects. Nothing here opens a door or adds a rigidbody.
    /// </summary>
    internal static class RecordBay
    {
        const float AngleStep = 0.5f;
        const float MoveMeters = 0.05f;
        const float GlassMeters = 0.4f;

        static bool _patched;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<int, float> _angle = new Dictionary<int, float>();
        static readonly Dictionary<int, bool> _seen = new Dictionary<int, bool>();
        static readonly Dictionary<int, Vector3> _mount = new Dictionary<int, Vector3>();
        static readonly Dictionary<int, bool> _mountOn = new Dictionary<int, bool>();
        static readonly Dictionary<int, GlassState> _glass = new Dictionary<int, GlassState>();
        static readonly HashSet<int> _logged = new HashSet<int>();

        struct GlassState
        {
            public bool Thrown;
            public bool Gone;
            public float X, Y, Z;
            public float Wrote;
            public string Key;
            public long UnitId;
        }

        public static void Install()
        {
            if (_patched) return;
            var harmony = new Harmony("noreplay.bay");
            int n = 0;
            n += Patch(harmony, "Canopy", "Eject", nameof(AfterEject));
            _patched = true;
            Plugin.Log?.LogInfo("Bay-Record eject=" + n);
        }

        public static void Flush()
        {
            _angle.Clear();
            _seen.Clear();
            _mount.Clear();
            _mountOn.Clear();
            _glass.Clear();
            _logged.Clear();
            Record.Log?.LogInfo("Bay rec stopped");
        }

        static int Patch(Harmony harmony, string typeName, string method, string postfix)
        {
            var t = Plugin.FindGameType(typeName);
            var m = t?.GetMethod(method, Flags);
            if (m == null) return 0;
            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(RecordBay), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Bay-Record " + typeName + "." + method + " " + ex.Message);
                return 0;
            }
        }

        static void AfterEject(object __instance)
        {
            if (!Record.Active || __instance is not Component canopy) return;
            int id = canopy.GetInstanceID();
            if (!_logged.Add(id)) return;
            var unit = Owner(canopy);
            Record.Log?.LogInfo("Glass rec " + NameOf(unit) + " eject=1 t="
                + Time.time.ToString("0.00", CultureInfo.InvariantCulture));
        }

        internal static void Sample()
        {
            if (!Record.Active) return;
            try
            {
                SampleDoors();
                SampleMounts();
                SampleGlass();
            }
            catch { }
        }

        static void SampleDoors()
        {
            var rampType = Plugin.FindGameType("CargoRamp");
            var doorType = Plugin.FindGameType("BayDoor");
            if (doorType != null)
            {
                foreach (var o in SceneObjects(doorType))
                {
                    if (o is not Component door) continue;
                    if (rampType != null && rampType.IsInstanceOfType(door)) continue;
                    float ang = door.transform.localEulerAngles.z;
                    WriteAngle(door, "b/", ang);
                }
            }
            if (rampType == null) return;
            foreach (var o in SceneObjects(rampType))
            {
                if (o is not Component ramp) continue;
                var part = ramp.GetType().GetField("rampPart", Flags)?.GetValue(ramp) as Component;
                if (part == null) continue;
                WriteAngle(ramp, "r/", part.transform.localEulerAngles.x);
            }
        }

        static void WriteAngle(Component door, string prefix, float ang)
        {
            int id = door.GetInstanceID();
            bool first = !_seen.ContainsKey(id);
            _angle.TryGetValue(id, out float prev);
            if (!first && Math.Abs(Mathf.DeltaAngle(prev, ang)) < AngleStep) return;
            var unit = Owner(door);
            var host = Host(unit);
            if (host == null) return;
            _seen[id] = true;
            _angle[id] = ang;
            string key = prefix + PathOf(unit!, door.transform);
            if (first || _logged.Add(id + 100000))
                Record.Log?.LogInfo("Ramp rec " + NameOf(unit) + " key=" + key
                    + " angle=" + ang.ToString("0.0", CultureInfo.InvariantCulture));
            Write(host, "Doors", key + ":" + ang.ToString("0.##", CultureInfo.InvariantCulture));
        }

        static void SampleMounts()
        {
            var t = Plugin.FindGameType("MountedCargo");
            if (t == null) return;
            foreach (var o in SceneObjects(t))
            {
                if (o is not Component cargo) continue;
                var unit = Owner(cargo);
                var host = Host(unit);
                if (host == null) continue;
                bool on = cargo.gameObject.activeInHierarchy;
                var lp = cargo.transform.localPosition;
                int id = cargo.GetInstanceID();
                bool first = !_mountOn.ContainsKey(id);
                _mount.TryGetValue(id, out var prev);
                _mountOn.TryGetValue(id, out bool was);
                bool moved = (lp - prev).sqrMagnitude > MoveMeters * MoveMeters;
                if (!first && was == on && !moved) continue;
                _mount[id] = lp;
                _mountOn[id] = on;
                string key = "m/" + PathOf(unit!, cargo.transform);
                if (first || was != on)
                    Record.Log?.LogInfo("Cargo mount " + NameOf(unit) + " key=" + key
                        + " show=" + (on ? 1 : 0));
                string seg = on
                    ? key + ":1|" + lp.x.ToString("0.##", CultureInfo.InvariantCulture)
                        + "|" + lp.y.ToString("0.##", CultureInfo.InvariantCulture)
                        + "|" + lp.z.ToString("0.##", CultureInfo.InvariantCulture)
                    : key + ":0";
                Write(host, "Doors", seg);
            }
        }

        static void SampleGlass()
        {
            var t = Plugin.FindGameType("Canopy");
            if (t == null) return;
            var alive = new HashSet<int>();
            foreach (var o in SceneObjects(t))
            {
                if (o is not Component canopy) continue;
                int id = canopy.GetInstanceID();
                alive.Add(id);
                var thrown = canopy.GetType().GetField("ejectionTransform", Flags)?.GetValue(canopy) as Transform;
                if (thrown == null) continue;
                bool firing = canopy.GetType().GetField("firing", Flags)?.GetValue(canopy) is bool f && f;
                bool loose = thrown.parent == null || firing;
                _glass.TryGetValue(id, out var st);
                var unit = Owner(canopy);
                if (!loose)
                {
                    if (string.IsNullOrEmpty(st.Key) && unit != null)
                        st.Key = "g/" + PathOf(unit, canopy.transform);
                    if (!st.Thrown) _glass[id] = st;
                    continue;
                }
                var host = Host(unit);
                if (host == null) continue;
                if (string.IsNullOrEmpty(st.Key))
                    st.Key = "g/" + PathOf(unit!, canopy.transform);
                st.UnitId = unit!.persistentID.Id;
                var p = thrown.position;
                float x = p.GlobalX(), y = p.GlobalY(), z = p.GlobalZ();
                float dx = x - st.X, dy = y - st.Y, dz = z - st.Z;
                bool moved = !st.Thrown || dx * dx + dy * dy + dz * dz > GlassMeters * GlassMeters
                    || Time.time - st.Wrote >= 0.15f;
                st.Thrown = true;
                st.X = x; st.Y = y; st.Z = z;
                if (!moved)
                {
                    _glass[id] = st;
                    continue;
                }
                st.Wrote = Time.time;
                _glass[id] = st;
                var q = thrown.rotation;
                Write(host, "Glass", st.Key + ":1|"
                    + x.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + y.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + z.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + q.x.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.y.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.z.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.w.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + "1|1|1|1|1|1|0|0");
            }
            var ids = new List<int>(_glass.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                if (alive.Contains(id)) continue;
                var st = _glass[id];
                if (!st.Thrown || st.Gone || string.IsNullOrEmpty(st.Key)) continue;
                st.Gone = true;
                _glass[id] = st;
                var host = HostById(st.UnitId);
                if (host != null)
                {
                    Record.Log?.LogInfo("Glass rec id=" + st.UnitId.ToString(CultureInfo.InvariantCulture)
                        + " key=" + st.Key + " gone=1");
                    Write(host, "Glass", st.Key + ":0");
                }
            }
        }

        static IEnumerable<UnityEngine.Object> SceneObjects(Type type)
        {
            var all = Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c) continue;
                if (!c.gameObject.scene.IsValid()) continue;
                yield return c;
            }
        }

        static RecordUnit? HostById(long id)
        {
            var go = Record.Host;
            if (go == null) return null;
            var eng = go.GetComponent<RecordEngine>();
            if (eng == null || !eng.unitObjects.TryGetValue(id, out var rec) || rec == null)
                return null;
            return rec.GetComponent<RecordUnit>();
        }

        static void Write(RecordUnit host, string prop, string segment)
        {
            if (!Record.Active) return;
            var eng = host.Engine;
            if (eng == null) return;
            var saved = new Dictionary<string, string>(host.props);
            host.props.Clear();
            host.props[prop] = segment;
            try { eng.invokeWriterUpdate(host); }
            finally
            {
                host.props.Clear();
                foreach (var kv in saved)
                    host.props[kv.Key] = kv.Value;
            }
        }

        static Unit? Owner(Component c)
        {
            var unit = c.GetComponentInParent<Unit>();
            if (unit != null) return unit;
            var part = c.GetType().GetField("attachedPart", Flags)?.GetValue(c) as Component
                ?? c.GetType().GetField("rampPart", Flags)?.GetValue(c) as Component;
            if (part != null && part.GetType().GetField("parentUnit", Flags)?.GetValue(part) is Unit owner)
                return owner;
            return null;
        }

        static RecordUnit? Host(Unit? unit)
        {
            if (unit == null || !unit.persistentID.IsValid) return null;
            var go = Record.Host;
            if (go == null) return null;
            var eng = go.GetComponent<RecordEngine>();
            if (eng == null || !eng.unitObjects.TryGetValue(unit.persistentID.Id, out var rec) || rec == null)
                return null;
            return rec.GetComponent<RecordUnit>();
        }

        static string NameOf(Unit? unit)
        {
            if (unit == null) return "no-unit";
            if (unit.definition != null && !string.IsNullOrEmpty(unit.definition.unitName))
                return unit.definition.unitName;
            return string.IsNullOrEmpty(unit.unitName) ? unit.name : unit.unitName;
        }

        static string PathOf(Unit unit, Transform t)
        {
            string path = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, t));
            return path.Length == 0 ? "door" : path;
        }

    }
}
