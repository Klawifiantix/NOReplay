using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Samples every Turret on a recorded unit. Angles are the visual local
    /// pose (traverse = turret yaw, elevation = elevation-transform pitch).
    /// traverseAngle / elevationAngle are only used when that transform is
    /// missing. Idle turrets are written once, then only when an angle moves
    /// more than half a degree.
    /// Aircraft stay attached even when no turret exists yet: the Chicane chin
    /// gun and gunpods are hardpoint mounts spawned after RegisterUnit.
    /// </summary>
    internal class RecordTurrets : MonoBehaviour
    {
        const float SampleInterval = 0.15f;
        const float ChangeDegrees = 0.5f;

        RecordUnit? _host;
        Unit? _unit;
        Slot[] _slots = System.Array.Empty<Slot>();
        float _timer;
        float _scan;
        string? _pending;
        bool _baseline;
        static FieldInfo? _traverse;
        static FieldInfo? _elevation;
        static FieldInfo? _elevTransform;

        internal static void Attach(RecordUnit host, Unit unit)
        {
            if (host == null || unit == null) return;
            if (host.GetComponent<RecordTurrets>() != null) return;
            var turrets = unit.GetComponentsInChildren<Turret>(true) ?? System.Array.Empty<Turret>();
            // Aircraft weapon mounts spawn in OnStartClient, after this registration.
            if (turrets.Length == 0 && unit is not Aircraft) return;
            var rec = host.gameObject.AddComponent<RecordTurrets>();
            rec.Init(host, unit, turrets);
        }

        void Init(RecordUnit host, Unit unit, Turret[] turrets)
        {
            _host = host;
            _unit = unit;
            BindFields();
            Rebuild(turrets);
            if (_slots.Length == 0) return;
            Capture(forceAll: true);
            _baseline = true;
        }

        static void BindFields()
        {
            if (_traverse != null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _traverse = typeof(Turret).GetField("traverseAngle", flags);
            _elevation = typeof(Turret).GetField("elevationAngle", flags);
            _elevTransform = typeof(Turret).GetField("elevationTransform", flags);
        }

        static Transform? ElevationOf(Turret turret)
        {
            if (_elevTransform == null) return null;
            try { return _elevTransform.GetValue(turret) as Transform; }
            catch { return null; }
        }

        void LateUpdate()
        {
            if (_host == null || _unit == null)
            {
                enabled = false;
                return;
            }
            if (!Record.Active)
            {
                // Keep a baseline that arrived before recording was armed.
                if (_pending == null) enabled = false;
                return;
            }
            if (_pending != null && _host.props.Count == 0)
                Flush(_pending);
            if (_unit.disabled)
            {
                enabled = false;
                return;
            }

            _timer += Time.deltaTime;
            _scan += Time.deltaTime;
            // Empty aircraft keep looking until SpawnWeapons instantiates the mounts.
            float scanEvery = _slots.Length == 0 && _unit is Aircraft ? 0.2f : 1f;
            if (_scan >= scanEvery)
            {
                _scan = 0f;
                if (Rescan() && _slots.Length > 0 && _baseline)
                    Capture(forceAll: false);
            }
            if (_slots.Length > 0 && !_baseline)
            {
                Capture(forceAll: true);
                _baseline = true;
                _timer = 0f;
                return;
            }
            if (_timer < SampleInterval) return;
            _timer = 0f;
            Capture(forceAll: false);
        }

        bool Rescan()
        {
            if (_unit == null) return false;
            var found = _unit.GetComponentsInChildren<Turret>(true);
            if (found == null) return false;
            if (found.Length == _slots.Length)
            {
                bool same = true;
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != _slots[i].Turret) { same = false; break; }
                }
                if (same) return false;
            }
            Rebuild(found);
            return true;
        }

        void Rebuild(Turret[] turrets)
        {
            if (_unit == null) return;
            var keys = TurretKeys.Build(_unit.transform, turrets);
            var prev = new Dictionary<Turret, Slot>();
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                if (s.Turret != null && !prev.ContainsKey(s.Turret))
                    prev[s.Turret] = s;
            }
            var next = new Slot[turrets.Length];
            for (int i = 0; i < turrets.Length; i++)
            {
                var t = turrets[i];
                if (t != null && prev.TryGetValue(t, out var old) && old.Key == keys[i])
                {
                    next[i] = old;
                    if (old.Elev == null) old.Elev = ElevationOf(t);
                    continue;
                }
                next[i] = new Slot
                {
                    Turret = t,
                    Key = keys[i],
                    Elev = t != null ? ElevationOf(t) : null
                };
            }
            _slots = next;
        }

        void Capture(bool forceAll)
        {
            if (_host == null || _slots.Length == 0) return;
            var sb = new StringBuilder();
            bool any = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                if (s.Turret == null || string.IsNullOrEmpty(s.Key)) continue;
                float trav = ReadTraverse(s);
                float elev = ReadElevation(s);
                bool changed = !s.Seen
                    || Mathf.Abs(Mathf.DeltaAngle(s.Trav, trav)) > ChangeDegrees
                    || Mathf.Abs(Mathf.DeltaAngle(s.ElevAng, elev)) > ChangeDegrees;
                if (!forceAll && !changed) continue;
                if (any) sb.Append(';');
                any = true;
                sb.Append(s.Key).Append(':');
                sb.Append(trav.ToString("0.00", CultureInfo.InvariantCulture));
                sb.Append('|');
                sb.Append(elev.ToString("0.00", CultureInfo.InvariantCulture));
                s.Trav = trav;
                s.ElevAng = elev;
                s.Seen = true;
            }
            if (any) Flush(sb.ToString());
        }

        void Flush(string value)
        {
            if (_host == null) return;
            // Never drop a captured pose. Recording may still be arming, or the
            // unit line may still own props until the next writer tick.
            if (!Record.Active || Record.Host == null || _host.props.Count > 0)
            {
                _pending = value;
                return;
            }
            var eng = _host.Engine;
            if (eng == null || RecordEngine.writer == null)
            {
                _pending = value;
                return;
            }
            _pending = null;
            _host.props["Turrets"] = value;
            eng.invokeWriterUpdate(_host);
            _host.props.Remove("Turrets");
        }

        static float ReadTraverse(Slot s)
        {
            var turret = s.Turret;
            if (turret == null) return 0f;
            var tr = turret.transform;
            if (tr == null) return Read(_traverse, turret);
            return Mathf.DeltaAngle(0f, tr.localEulerAngles.y);
        }

        static float ReadElevation(Slot s)
        {
            if (s.Elev != null)
                return Mathf.DeltaAngle(0f, s.Elev.localEulerAngles.x);
            return s.Turret != null ? Read(_elevation, s.Turret) : 0f;
        }

        static float Read(FieldInfo? field, Turret turret)
        {
            if (field == null) return 0f;
            try
            {
                if (field.GetValue(turret) is float v) return v;
            }
            catch { }
            return 0f;
        }

        sealed class Slot
        {
            public Turret? Turret;
            public Transform? Elev;
            public string Key = "";
            public float Trav;
            public float ElevAng;
            public bool Seen;
        }
    }
}
