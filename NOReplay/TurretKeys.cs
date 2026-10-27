using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Stable turret identity shared by the recorder and the ghost.
    /// The key is the hierarchy path under the unit, so a turret keeps the same
    /// name when gunpods spawn and GetComponentsInChildren order shifts.
    /// </summary>
    internal static class TurretKeys
    {
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

        public static string Relative(Transform root, Transform t)
        {
            var parts = new List<string>();
            var c = t;
            while (c != null && c != root)
            {
                parts.Add(c.name ?? "");
                c = c.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static string Sanitize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace(',', '_').Replace(';', '_').Replace(':', '_').Replace('|', '_').Replace('=', '_');
        }
    }
}
