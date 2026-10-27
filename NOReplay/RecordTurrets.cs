using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Samples every Turret on a recorded unit. Angles are the game's own
    /// traverseAngle / elevationAngle. Idle turrets are written once, then only
    /// when an angle moves more than half a degree.
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

        internal static void Attach(RecordUnit host, Unit unit)
        {
            if (host == null || unit == null) return;
            if (host.GetComponent<RecordTurrets>() != null) return;
            var turrets = unit.GetComponentsInChildren<Turret>(true);
            if (turrets == null || turrets.Length == 0) return;
            var rec = host.gameObject.AddComponent<RecordTurrets>();
            rec.Init(host, unit, turrets);
        }

        void Init(RecordUnit host, Unit unit, Turret[] turrets)
        {
            _host = host;
            _unit = unit;
            BindFields();
            Rebuild(turrets);
            Capture(forceAll: true);
            _baseline = true;
        }

        static void BindFields()
        {
            if (_traverse != null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _traverse = typeof(Turret).GetField("traverseAngle", flags);
            _elevation = typeof(Turret).GetField("elevationAngle", flags);
        }

        void LateUpdate()
        {
            if (!Record.Active || _host == null || _unit == null)
            {
                enabled = false;
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
            if (_scan >= 1f)
            {
                _scan = 0f;
                Rescan();
            }
            if (!_baseline)
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

        void Rescan()
        {
            if (_unit == null) return;
            var found = _unit.GetComponentsInChildren<Turret>(true);
            if (found == null) return;
            if (found.Length == _slots.Length)
            {
                bool same = true;
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != _slots[i].Turret) { same = false; break; }
                }
                if (same) return;
            }
            Rebuild(found);
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
                    continue;
                }
                next[i] = new Slot { Turret = t, Key = keys[i] };
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
                float trav = Read(_traverse, s.Turret);
                float elev = Read(_elevation, s.Turret);
                bool changed = !s.Seen
                    || Mathf.Abs(Mathf.DeltaAngle(s.Trav, trav)) > ChangeDegrees
                    || Mathf.Abs(Mathf.DeltaAngle(s.Elev, elev)) > ChangeDegrees;
                if (!forceAll && !changed) continue;
                if (any) sb.Append(';');
                any = true;
                sb.Append(s.Key).Append(':');
                sb.Append(trav.ToString("0.00", CultureInfo.InvariantCulture));
                sb.Append('|');
                sb.Append(elev.ToString("0.00", CultureInfo.InvariantCulture));
                s.Trav = trav;
                s.Elev = elev;
                s.Seen = true;
            }
            if (any) Flush(sb.ToString());
        }

        void Flush(string value)
        {
            if (_host == null || !Record.Active || Record.Host == null) return;
            if (_host.props.Count > 0)
            {
                _pending = value;
                return;
            }
            var eng = _host.Engine;
            if (eng == null || RecordEngine.writer == null) return;
            _pending = null;
            _host.props["Turrets"] = value;
            eng.invokeWriterUpdate(_host);
            _host.props.Remove("Turrets");
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
            public string Key = "";
            public float Trav;
            public float Elev;
            public bool Seen;
        }
    }
}
