using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NOReplay
{
    public static class AcmiParser
    {
        public static ReplayDocument Load(string path)
        {
            string text;
            if (path.EndsWith(".zip.acmi", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(path);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                if (zip.Entries.Count == 0)
                    throw new InvalidDataException("Leeres zip.acmi");
                using var sr = new StreamReader(zip.Entries[0].Open(), Encoding.UTF8);
                text = sr.ReadToEnd();
            }
            else
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            return Parse(text);
        }

        static float ParseTime(string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return 0f;
            raw = raw.Replace(',', '.');
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t);
            return t;
        }

        public static ReplayDocument Parse(string text)
        {
            var doc = new ReplayDocument();
            var objects = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
            var state = new Dictionary<string, ReplaySample>(StringComparer.OrdinalIgnoreCase);
            float time = 0f;

            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '/') continue;

                if (line[0] == '#')
                {
                    time = ParseTime(line.Substring(1));
                    if (time > doc.Duration) doc.Duration = time;
                    continue;
                }

                if (line.StartsWith("FileType=", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("FileVersion=", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line[0] == '-')
                {
                    string raw = line.Substring(1);
                    int cut = raw.IndexOf(',');
                    string deadId = (cut < 0 ? raw : raw.Substring(0, cut)).Trim();
                    if (deadId.Length > 0 && objects.TryGetValue(deadId, out var dead))
                        dead.RemovedAt = time;
                    if (time > doc.Duration) doc.Duration = time;
                    continue;
                }

                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string objId = line.Substring(0, comma).Trim();
                string rest = line.Substring(comma + 1);

                if (objId == "0")
                {
                    ApplyGlobal(doc, rest);
                    continue;
                }

                if (!objects.TryGetValue(objId, out var obj))
                {
                    obj = new ReplayObject { Id = objId };
                    objects[objId] = obj;
                    doc.Objects.Add(obj);
                }

                ReplaySample sample = state.TryGetValue(objId, out var prev)
                    ? prev.Clone()
                    : new ReplaySample();
                sample.Time = time;
                bool hasPose = false;

                foreach (var part in rest.Split(','))
                {
                    if (part.Length == 0) continue;
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = part.Substring(0, eq);
                    string val = part.Substring(eq + 1);
                    if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Name = val;
                    }
                    else if (key.Equals("Type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)
                            && (obj.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) < 0)
                            obj.Type = val;
                    }
                    else if (key.Equals("Coalition", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Coalition = val;
                    }
                    else if (key.Equals("Pilot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Pilot = val;
                    }
                    else if (key.Equals("CallSign", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.CallSign = val;
                    }
                    else if (key.Equals("Registration", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Registration = val;
                    }
                    else if (key.Equals("Loadout", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Loadout = val;
                    }
                    else if (key.Equals("Livery", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Livery = val;
                    }
                    else if (key.Equals("Parent", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.ParentId = val;
                    }
                    else if (key.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val)) obj.Weapon = val;
                    }
                    else if (key.Equals("Rpm", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rpm))
                            obj.Rpm = rpm;
                    }
                    else if (key.Equals("Muzzle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var mz))
                            obj.Muzzle = mz;
                    }
                    else if (key.Equals("Drag", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var drag))
                            obj.Drag = drag;
                    }
                    else if (key.Equals("Grav", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var grav))
                            obj.Grav = grav;
                    }
                    else if (key.Equals("Spread", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var spread))
                            obj.Spread = spread;
                    }
                    else if (key.Equals("Visible", StringComparison.OrdinalIgnoreCase) && val.StartsWith("0"))
                    {
                        obj.RemovedAt = time;
                    }
                    else if (key == "T" || key.Equals("Transform", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ParseTransform(val, ref sample, obj.Type, obj.Name))
                            hasPose = true;
                    }
                    else if (key.Equals("LandingGear", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g))
                        {
                            sample.HasGear = true;
                            sample.Gear = g;
                        }
                    }
                    else if (key.Equals("Throttle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var th))
                        {
                            sample.HasThrottle = true;
                            sample.Throttle = th;
                        }
                    }
                    else if (key.Equals("TAS", StringComparison.OrdinalIgnoreCase)
                          || key.Equals("IAS", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var tas))
                        {
                            sample.HasTas = true;
                            sample.Tas = tas;
                        }
                    }
                }

                if (hasPose)
                {
                    sample.Time = time;
                    if (obj.SpawnAt < 0f) obj.SpawnAt = time;
                    if (sample.Time > doc.Duration)
                        doc.Duration = sample.Time;
                    obj.Samples.Add(sample);
                    state[objId] = sample;
                }
            }

            foreach (var o in doc.Objects)
            {
                if ((o.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) < 0)
                    StretchSampleTimes(o);
                if (o.RemovedAt < 0f && o.Samples.Count > 0 && (IsWeaponType(o.Type, o.Name) || IsShell(o.Type)
                    || (o.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0))
                    o.RemovedAt = o.Samples[o.Samples.Count - 1].Time;
            }
            int filled = 0;
            int bursts = 0;
            foreach (var o in doc.Objects)
            {
                if ((o.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0) { bursts++; continue; }
                if (IsShell(o.Type) && FillShell(o, doc)) filled++;
            }
            if (filled > 0)
                Plugin.Log.LogInfo("Shell-Pfad ergänzt: " + filled);
            if (bursts > 0)
                Plugin.Log.LogInfo("Kanonenstöße: " + bursts);

            float max = doc.Duration;
            foreach (var o in doc.Objects)
                if (o.Samples.Count > 0)
                    max = Math.Max(max, o.Samples[o.Samples.Count - 1].Time);
            doc.Duration = max;
            return doc;
        }

        static void ApplyGlobal(ReplayDocument doc, string rest)
        {
            foreach (var part in rest.Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq);
                string val = part.Substring(eq + 1);
                if (key.Equals("Title", StringComparison.OrdinalIgnoreCase))
                    doc.Title = val;
                else if (key.Equals("MapId", StringComparison.OrdinalIgnoreCase))
                    doc.MapId = val;
                else if (key.Equals("DataRecorder", StringComparison.OrdinalIgnoreCase))
                    doc.DataRecorder = val;
            }
        }

        static int filledLog;

        static bool FillShell(ReplayObject shell, ReplayDocument doc)
        {
            if (shell.Samples.Count == 0) return false;
            var first = shell.Samples[0];
            var next = shell.Samples.Count > 1 ? shell.Samples[1] : first;
            float dirX = next.X - first.X;
            float dirZ = next.Z - first.Z;
            float dirLen = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (dirLen < 1f) { dirX = 1f; dirZ = 0f; dirLen = 1f; }
            dirX /= dirLen;
            dirZ /= dirLen;
            float t0 = shell.SpawnAt >= 0f ? shell.SpawnAt : first.Time;
            if (t0 > first.Time) t0 = first.Time;
            ReplayObject? src = null;
            float bestSide = 9999f;
            float bestBack = 9999f;
            bool bestAir = false;
            foreach (var u in doc.Objects)
            {
                if (u == shell || u.Samples.Count == 0) continue;
                string t = u.Type ?? "";
                if (IsShell(t) || IsWeaponType(t, u.Name)) continue;
                bool air = t.IndexOf("Air", StringComparison.OrdinalIgnoreCase) >= 0;
                bool veh = t.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!air && !veh) continue;
                if (!u.TrySample(t0, out var p) && !u.TrySample(first.Time, out p)) continue;
                float bx = first.X - p.X;
                float bz = first.Z - p.Z;
                float back = bx * dirX + bz * dirZ;
                if (back < 20f || back > 900f) continue;
                float side = Math.Abs(bx * dirZ - bz * dirX);
                if (side > 180f) continue;
                if (src == null || (air && !bestAir) || (air == bestAir && side < bestSide))
                {
                    src = u;
                    bestSide = side;
                    bestBack = back;
                    bestAir = air;
                }
            }
            var path = new List<ReplaySample>();
            if (src != null && src.TrySample(t0, out var origin))
            {
                origin.Time = t0;
                path.Add(origin);
            }
            float from = path.Count > 0 ? t0 : first.Time;
            var end = shell.Samples[shell.Samples.Count - 1];
            for (float t = from; t < end.Time - 0.02f; t += 0.1f)
            {
                if (!shell.TrySample(Math.Max(t, first.Time), out var s)) continue;
                if (path.Count > 0 && t <= first.Time)
                {
                    float u = first.Time > from ? (t - from) / (first.Time - from) : 1f;
                    s.X = path[0].X + (first.X - path[0].X) * u;
                    s.Y = path[0].Y + (first.Y - path[0].Y) * u;
                    s.Z = path[0].Z + (first.Z - path[0].Z) * u;
                }
                s.Time = t;
                path.Add(s);
            }
            path.Add(end);
            if (path.Count < 2) return false;
            shell.Samples = path;
            shell.SpawnAt = path[0].Time;
            if (src != null && filledLog < 3)
            {
                filledLog++;
                Plugin.Log.LogInfo("Shell " + shell.Id + " Ursprung " + (src.Name ?? src.Id) + " back=" + bestBack.ToString("0") + " side=" + bestSide.ToString("0"));
            }
            return src != null;
        }

        static bool IsWeaponType(string? type, string? name)
        {
            string t = type ?? "";
            string n = name ?? "";
            if (t.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Rocket", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Weapon+", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("PAB", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("GPO", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("GBM", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool IsShell(string? type)
        {
            return (type ?? "").IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ParseTransform(string val, ref ReplaySample s, string? type, string? name)
        {
            var bits = val.Split('|');
            if (bits.Length < 3) return false;
            bool any = false;
            if (TryNum(bits, 2, out var alt)) { s.Y = alt; any = true; }
            if (bits.Length == 5)
            {
                if (TryNum(bits, 3, out var x5)) { s.X = x5; any = true; }
                if (TryNum(bits, 4, out var z5)) { s.Z = z5; any = true; }
                return any;
            }
            if (bits.Length > 3 && TryNum(bits, 3, out var roll)) { s.Roll = roll; any = true; }
            if (bits.Length > 4 && TryNum(bits, 4, out var pitch)) { s.Pitch = pitch; any = true; }
            if (bits.Length > 5 && TryNum(bits, 5, out var yaw)) { s.Yaw = yaw; any = true; }
            if (bits.Length > 6 && TryNum(bits, 6, out var x)) { s.X = x; any = true; }
            if (bits.Length > 7 && TryNum(bits, 7, out var z)) { s.Z = z; any = true; }
            return any;
        }

        static bool TryNum(string[] bits, int i, out float v)
        {
            v = 0f;
            if (i >= bits.Length || string.IsNullOrEmpty(bits[i])) return false;
            return float.TryParse(bits[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static void StretchSampleTimes(ReplayObject obj)
        {
            int n = obj.Samples.Count;
            if (n < 2) return;
            var raw = new float[n];
            for (int i = 0; i < n; i++)
                raw[i] = obj.Samples[i].Time;
            int g = 0;
            while (g < n)
            {
                int start = g;
                int end = start;
                while (end + 1 < n && raw[end + 1] - raw[end] <= 0.12f)
                    end++;
                float t0 = raw[start];
                float t1 = (end + 1 < n) ? raw[end + 1] : raw[end];
                if (t1 <= t0) t1 = t0 + Math.Max(0.05f, (end - start) * 0.05f);
                int count = end - start + 1;
                for (int k = 0; k < count; k++)
                {
                    float u = count <= 1 ? 0f : (float)k / count;
                    var s = obj.Samples[start + k];
                    s.Time = t0 + (t1 - t0) * u;
                    obj.Samples[start + k] = s;
                }
                g = end + 1;
            }
        }
    }
}