using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Reads each Laser after LateUpdate, which is the beam the game is about
    /// to draw. The impact is the hit the weapon already stored. No second
    /// shot is fired and no target is invented from the turret heading.
    /// </summary>
    internal static class RecordLasers
    {
        const float MoveMeters = 0.5f;
        const float PowerStep = 0.05f;
        const float KeepSeconds = 0.1f;

        static bool _patched;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo? _beam;
        static FieldInfo? _hit;
        static FieldInfo? _offset;
        static FieldInfo? _color;
        static Type? _laserType;
        static readonly Dictionary<int, string> _keys = new Dictionary<int, string>();
        static readonly Dictionary<int, State> _state = new Dictionary<int, State>();
        static readonly Dictionary<RecordUnit, List<string>> _pending = new Dictionary<RecordUnit, List<string>>();
        static Pump? _pump;
        static readonly HashSet<int> _logged = new HashSet<int>();

        struct State
        {
            public bool On;
            public bool Seen;
            public bool HasPoint;
            public float X, Y, Z, Power;
            public float Wrote;
        }

        public static void Install()
        {
            if (_patched) return;
            var t = Plugin.FindGameType("Laser");
            if (t == null)
            {
                Plugin.Log?.LogInfo("Laser-Record: Laser fehlt");
                return;
            }
            var late = t.GetMethod("LateUpdate", Flags);
            if (late == null)
            {
                Plugin.Log?.LogInfo("Laser-Record: LateUpdate fehlt");
                return;
            }
            _laserType = t;
            _beam = t.GetField("beamRenderer", Flags);
            _hit = t.GetField("hitTransform", Flags);
            _offset = t.GetField("hitOffset", Flags);
            _color = t.GetField("color", Flags);
            var harmony = new Harmony("noreplay.lasers");
            harmony.Patch(late, postfix: new HarmonyMethod(typeof(RecordLasers), nameof(AfterLate)));
            _patched = true;
            Plugin.Log?.LogInfo("Laser-Record LateUpdate");
        }

        public static void Flush()
        {
            try
            {
                var ids = new List<int>(_state.Keys);
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    if (!_state.TryGetValue(id, out var st) || !st.On) continue;
                    st.On = false;
                    _state[id] = st;
                }
                FlushPending();
            }
            catch { }
            _keys.Clear();
            _state.Clear();
            _pending.Clear();
            _logged.Clear();
        }

        static void AfterLate(object __instance)
        {
            if (!Record.Active || Plugin.CurrentDoc != null) return;
            var laser = __instance as Component;
            if (laser == null || _laserType == null) return;
            if (laser.GetComponentInParent<ReplayGhost>() != null) return;
            var weapon = laser as Weapon;
            var unit = weapon != null ? weapon.attachedUnit : null;
            if (unit == null) return;
            var host = unit.GetComponent<RecordUnit>();
            if (host == null) return;
            EnsurePump();
            EnsureKeys(unit);
            if (!_keys.TryGetValue(laser.GetInstanceID(), out var key) || key.Length == 0) return;
            bool on = false;
            bool has = false;
            float x = 0f, y = 0f, z = 0f, power = 1f;
            try
            {
                if (_beam?.GetValue(laser) is Renderer beam)
                    on = beam.enabled;
                if (on && _hit?.GetValue(laser) is Transform hit)
                {
                    Vector3 offset = _offset?.GetValue(laser) is Vector3 o ? o : Vector3.zero;
                    Vector3 world = hit.TransformPoint(offset);
                    x = world.GlobalX();
                    y = world.GlobalY();
                    z = world.GlobalZ();
                    has = true;
                }
                if (on) power = ReadPower(laser);
            }
            catch { return; }
            int id = laser.GetInstanceID();
            _state.TryGetValue(id, out var prev);
            bool changed = !prev.Seen || prev.On != on || prev.HasPoint != has;
            if (!changed && on && has && prev.HasPoint)
            {
                float dx = x - prev.X, dy = y - prev.Y, dz = z - prev.Z;
                if (dx * dx + dy * dy + dz * dz > MoveMeters * MoveMeters) changed = true;
                if (Mathf.Abs(power - prev.Power) > PowerStep) changed = true;
            }
            if (!changed && on && Time.time - prev.Wrote >= KeepSeconds) changed = true;
            prev.Seen = true;
            prev.On = on;
            prev.HasPoint = has;
            prev.X = x;
            prev.Y = y;
            prev.Z = z;
            prev.Power = power;
            if (!changed)
            {
                _state[id] = prev;
                return;
            }
            prev.Wrote = Time.time;
            _state[id] = prev;
            if (_logged.Add(id))
                Record.Log?.LogInfo("Laser " + (unit.definition != null ? unit.definition.unitName : unit.name) + " " + key);
            Queue(host, Format(key, on, has, x, y, z, power));
            FlushPending();
        }

        static float ReadPower(Component laser)
        {
            if (_beam?.GetValue(laser) is not Renderer beam || beam.material == null) return 1f;
            if (_color?.GetValue(laser) is not Color tint) return 1f;
            float mag = tint.maxColorComponent;
            if (mag < 0.001f) return 1f;
            Color now;
            try { now = beam.material.GetColor("_Color"); }
            catch { return 1f; }
            float p = now.maxColorComponent / mag;
            if (p < 0f) p = 0f;
            if (p > 1f) p = 1f;
            return p;
        }

        static string Format(string key, bool on, bool has, float x, float y, float z, float power)
        {
            if (!on) return key + ":0";
            if (!has) return key + ":1";
            return key + ":1|"
                + x.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + y.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + z.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + power.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static void EnsureKeys(Unit unit)
        {
            if (_laserType == null) return;
            var all = unit.GetComponentsInChildren(_laserType, true);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c == null) continue;
                int id = c.GetInstanceID();
                if (_keys.TryGetValue(id, out var existing))
                {
                    string basePath = existing;
                    int hash = existing.LastIndexOf('#');
                    if (hash > 0) basePath = existing.Substring(0, hash);
                    counts.TryGetValue(basePath, out int n);
                    counts[basePath] = n + 1;
                    continue;
                }
                string path = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, c.transform));
                if (path.Length == 0) path = "laser";
                counts.TryGetValue(path, out int m);
                m++;
                counts[path] = m;
                _keys[id] = m == 1 ? path : path + "#" + m.ToString(CultureInfo.InvariantCulture);
            }
        }

        static void Queue(RecordUnit host, string segment)
        {
            if (!_pending.TryGetValue(host, out var list))
            {
                list = new List<string>();
                _pending[host] = list;
            }
            list.Add(segment);
        }

        internal static void FlushPending()
        {
            if (_pending.Count == 0) return;
            var drop = new List<RecordUnit>();
            foreach (var pair in _pending)
            {
                var host = pair.Key;
                var list = pair.Value;
                if (host == null || list.Count == 0)
                {
                    drop.Add(host);
                    continue;
                }
                if (!Record.Active || host.props.Count > 0) continue;
                var eng = host.Engine;
                if (eng == null) continue;
                host.props["Lasers"] = string.Join(";", list);
                list.Clear();
                try { eng.invokeWriterUpdate(host); }
                finally { host.props.Remove("Lasers"); }
                drop.Add(host);
            }
            for (int i = 0; i < drop.Count; i++)
                if (drop[i] != null) _pending.Remove(drop[i]);
        }

        static void EnsurePump()
        {
            if (_pump != null || Record.Host == null) return;
            _pump = Record.Host.GetComponent<Pump>();
            if (_pump == null) _pump = Record.Host.AddComponent<Pump>();
        }

        sealed class Pump : MonoBehaviour
        {
            void LateUpdate()
            {
                if (!Record.Active)
                {
                    Flush();
                    return;
                }
                FlushPending();
            }
        }
    }
}
