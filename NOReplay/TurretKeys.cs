using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Stable turret identity shared by the recorder and the ghost.
    /// The key is the hierarchy path under the unit, so a turret keeps the same
    /// name when gunpods spawn and GetComponentsInChildren order shifts.
    /// A live aircraft in complex physics unparents every AeroPart, so search
    /// also walks partLookup and the path follows attachInfo.parentPart when
    /// transform.parent is null.
    /// </summary>
    internal static class TurretKeys
    {
        static readonly BindingFlags ParentFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo? _attachInfo;
        static FieldInfo? _parentPart;
        static bool _parentFields;

        public static string[] Build(Transform root, Turret[] turrets)
        {
            var keys = new string[turrets.Length];
            var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int i = 0; i < turrets.Length; i++)
            {
                var t = turrets[i];
                if (t == null)
                {
                    keys[i] = "";
                    continue;
                }
                string path = Sanitize(Relative(root, t.transform));
                if (path.Length == 0) path = "turret";
                counts.TryGetValue(path, out int n);
                n++;
                counts[path] = n;
                keys[i] = n == 1 ? path : path + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return keys;
        }

        public static Turret[] FindTurrets(Unit unit)
        {
            var found = new List<Turret>();
            var seen = new HashSet<int>();
            Collect(unit, found, seen);
            var stations = unit != null ? unit.weaponStations : null;
            if (stations == null) return found.ToArray();
            for (int i = 0; i < stations.Count; i++)
            {
                var ws = stations[i];
                if (ws?.Turrets == null) continue;
                for (int j = 0; j < ws.Turrets.Count; j++)
                    Add(found, seen, ws.Turrets[j]);
            }
            return found.ToArray();
        }

        public static Gun[] FindGuns(Unit unit) => FindInUnit<Gun>(unit);

        public static T[] FindInUnit<T>(Unit unit) where T : Component
        {
            var found = new List<T>();
            var seen = new HashSet<int>();
            Collect(unit, found, seen);
            return found.ToArray();
        }

        static void Collect<T>(Unit? unit, List<T> found, HashSet<int> seen) where T : Component
        {
            if (unit == null) return;
            var local = unit.GetComponentsInChildren<T>(true);
            if (local != null)
            {
                for (int i = 0; i < local.Length; i++)
                    Add(found, seen, local[i]);
            }
            var parts = unit.partLookup;
            if (parts == null) return;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.IsDetached()) continue;
                var nested = part.GetComponentsInChildren<T>(true);
                if (nested == null) continue;
                for (int j = 0; j < nested.Length; j++)
                    Add(found, seen, nested[j]);
            }
        }

        static void Add<T>(List<T> found, HashSet<int> seen, T? item) where T : Component
        {
            if (item == null) return;
            if (!seen.Add(item.GetInstanceID())) return;
            found.Add(item);
        }

        public static string Relative(Transform root, Transform t)
        {
            var parts = new List<string>();
            var c = t;
            var seen = new HashSet<int>();
            int depth = 0;
            while (c != null && c != root && depth++ < 64)
            {
                if (!seen.Add(c.GetInstanceID())) break;
                parts.Add(c.name ?? "");
                if (c.parent != null)
                {
                    c = c.parent;
                    continue;
                }
                var part = c.GetComponent<UnitPart>();
                var logical = part != null ? LogicalParent(part) : null;
                var next = logical != null ? logical.transform : null;
                c = next != null && next != c ? next : null;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // UnitPart.attachInfo is protected. AeroPart.CreateRB clears transform.parent;
        // the prefab parent stays in attachInfo.parentPart.
        static UnitPart? LogicalParent(UnitPart part)
        {
            if (!_parentFields)
            {
                _parentFields = true;
                _attachInfo = typeof(UnitPart).GetField("attachInfo", ParentFlags);
            }
            if (_attachInfo == null) return null;
            object? info;
            try { info = _attachInfo.GetValue(part); }
            catch { return null; }
            if (info == null) return null;
            if (_parentPart == null)
                _parentPart = info.GetType().GetField("parentPart", ParentFlags);
            if (_parentPart == null) return null;
            try { return _parentPart.GetValue(info) as UnitPart; }
            catch { return null; }
        }

        public static string Sanitize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace(',', '_').Replace(';', '_').Replace(':', '_').Replace('|', '_').Replace('=', '_');
        }
    }
}
