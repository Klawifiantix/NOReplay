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
        static readonly List<Component> _doors = new List<Component>();
        static readonly List<Component> _ramps = new List<Component>();
        static readonly List<Component> _mounts = new List<Component>();
        static readonly List<Component> _canopies = new List<Component>();
        static readonly HashSet<int> _alive = new HashSet<int>();
        static readonly List<int> _glassIds = new List<int>();
        static Type _doorType, _rampType, _mountType, _canopyType;
        static FieldInfo _openAmount, _currentAngle, _defaultAngle, _hingeAngle, _ejection, _firing;
        static bool _fieldsReady;
        static float _scanAt = -1f;

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
            _doors.Clear();
            _ramps.Clear();
            _mounts.Clear();
            _canopies.Clear();
            _scanAt = -1f;
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
                EnsureFields();
                if (_scanAt < 0f || Time.unscaledTime >= _scanAt)
                {
                    _scanAt = Time.unscaledTime + 0.5f;
                    Fill(_doors, _doorType);
                    Fill(_ramps, _rampType);
                    Fill(_mounts, _mountType);
                    Fill(_canopies, _canopyType);
                }
                SampleDoors();
                SampleMounts();
                SampleGlass();
            }
            catch { }
        }

        static void EnsureFields()
        {
            if (_fieldsReady) return;
            _fieldsReady = true;
            _doorType = Plugin.FindGameType("BayDoor");
            _rampType = Plugin.FindGameType("CargoRamp");
            _mountType = Plugin.FindGameType("MountedCargo");
            _canopyType = Plugin.FindGameType("Canopy");
            _openAmount = _doorType?.GetField("openAmount", Flags);
            _currentAngle = _rampType?.GetField("currentAngle", Flags);
            _defaultAngle = _rampType?.GetField("defaultAngle", Flags);
            _hingeAngle = _doorType?.GetField("hingeAngle", Flags);
            _ejection = _canopyType?.GetField("ejectionTransform", Flags);
            _firing = _canopyType?.GetField("firing", Flags);
        }

        static void Fill(List<Component> into, Type type)
        {
            into.Clear();
            if (type == null) return;
            var all = Resources.FindObjectsOfTypeAll(type);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c) continue;
                if (!c.gameObject.scene.IsValid()) continue;
                into.Add(c);
            }
        }

        static void SampleDoors()
        {
            for (int i = 0; i < _doors.Count; i++)
            {
                var door = _doors[i];
                if (door == null) continue;
                if (_rampType != null && _rampType.IsInstanceOfType(door)) continue;
                float open = _openAmount?.GetValue(door) is float amount ? amount : 0f;
                WriteOpen(door, open);
            }
            for (int i = 0; i < _ramps.Count; i++)
            {
                var ramp = _ramps[i];
                if (ramp == null) continue;
                float current = _currentAngle?.GetValue(ramp) is float c ? c : 0f;
                float offset = _defaultAngle?.GetValue(ramp) is float d ? d : 0f;
                WriteHinge(ramp, current, offset);
            }
        }

        static void WriteHinge(Component ramp, float current, float offset)
        {
            int id = ramp.GetInstanceID();
            bool first = !_seen.ContainsKey(id);
            float sum = current + offset;
            _angle.TryGetValue(id, out float prev);
            if (!first && Math.Abs(sum - prev) < AngleStep) return;
            var unit = Owner(ramp);
            var host = Host(unit);
            if (host == null) return;
            _seen[id] = true;
            _angle[id] = sum;
            string key = "r/" + PathOf(unit!, ramp.transform);
            if (first)
                Record.Log?.LogInfo("Ramp rec " + NameOf(unit) + " key=" + key
                    + " current=" + current.ToString("0.##", CultureInfo.InvariantCulture)
                    + " default=" + offset.ToString("0.##", CultureInfo.InvariantCulture)
                    + " axis=right");
            Write(host, "Doors", key + ":c|"
                + current.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + offset.ToString("0.##", CultureInfo.InvariantCulture));
        }

        static void WriteOpen(Component door, float open)
        {
            int id = door.GetInstanceID();
            bool first = !_seen.ContainsKey(id);
            _angle.TryGetValue(id, out float prev);
            if (!first && Math.Abs(open - prev) < 0.01f) return;
            var unit = Owner(door);
            var host = Host(unit);
            if (host == null) return;
            _seen[id] = true;
            _angle[id] = open;
            string key = "b/" + PathOf(unit!, door.transform);
            if (first)
            {
                float hinge = _hingeAngle?.GetValue(door) is float h ? h : 0f;
                Record.Log?.LogInfo("Ramp rec " + NameOf(unit) + " key=" + key
                    + " open=" + open.ToString("0.##", CultureInfo.InvariantCulture)
                    + " hinge=" + hinge.ToString("0.##", CultureInfo.InvariantCulture)
                    + " axis=z");
            }
            Write(host, "Doors", key + ":o|" + open.ToString("0.###", CultureInfo.InvariantCulture));
        }

        static void SampleMounts()
        {
            for (int i = 0; i < _mounts.Count; i++)
            {
                var cargo = _mounts[i];
                if (cargo == null) continue;
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
            _alive.Clear();
            for (int i = 0; i < _canopies.Count; i++)
            {
                var canopy = _canopies[i];
                if (canopy == null) continue;
                int id = canopy.GetInstanceID();
                _alive.Add(id);
                var thrown = _ejection?.GetValue(canopy) as Transform;
                if (thrown == null) continue;
                bool firing = _firing?.GetValue(canopy) is bool f && f;
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
            _glassIds.Clear();
            foreach (var key in _glass.Keys) _glassIds.Add(key);
            for (int i = 0; i < _glassIds.Count; i++)
            {
                int id = _glassIds[i];
                if (_alive.Contains(id)) continue;
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
