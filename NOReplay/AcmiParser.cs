using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NOReplay
{
    public static class AcmiParser
    {
        public static ReplayDocument Load(string path)
        {
            using var fs = File.OpenRead(path);
            bool zipName = path.EndsWith(".zip.acmi", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            bool zipMagic = false;
            if (fs.Length >= 2)
            {
                int b0 = fs.ReadByte();
                int b1 = fs.ReadByte();
                zipMagic = b0 == 0x50 && b1 == 0x4B;
                fs.Position = 0;
            }
            string text;
            if (zipName || zipMagic)
            {
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
                if (zip.Entries.Count == 0)
                    throw new InvalidDataException("Leeres zip.acmi");
                using var sr = new StreamReader(zip.Entries[0].Open(), Encoding.UTF8);
                text = sr.ReadToEnd();
            }
            else
            {
                using var sr = new StreamReader(fs, Encoding.UTF8);
                text = sr.ReadToEnd();
            }
            return Parse(text);
        }

        static float ParseTime(ReadOnlySpan<char> raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0) return 0f;
            AcmiScan.TryFloat(raw, out var t);
            return t;
        }

        public static ReplayDocument Parse(string text)
        {
            var doc = new ReplayDocument();
            FeedLog.Collect(text, doc.Feeds);
            var objects = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
            var state = new Dictionary<string, ReplaySample>(StringComparer.OrdinalIgnoreCase);
            float time = 0f;

            int at = 0;
            int textN = text.Length;
            while (at < textN)
            {
                int lineStart = at;
                while (at < textN && text[at] != '\n') at++;
                int lineEnd = at;
                if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
                if (at < textN) at++;
                var line = text.AsSpan(lineStart, lineEnd - lineStart);
                if (line.Length == 0 || line[0] == '/') continue;

                if (line[0] == '#')
                {
                    time = ParseTime(line.Slice(1));
                    if (time > doc.Duration) doc.Duration = time;
                    continue;
                }

                if (line.StartsWith("FileType=".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("FileVersion=".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line[0] == '-')
                {
                    var raw = line.Slice(1);
                    int cut = raw.IndexOf(',');
                    var deadSpan = (cut < 0 ? raw : raw.Slice(0, cut)).Trim();
                    if (deadSpan.Length > 0)
                    {
                        string deadId = deadSpan.ToString();
                        if (objects.TryGetValue(deadId, out var dead))
                        {
                            dead.RemovedAt = time;
                            // These ids are reused after removal. Drop the finished event from the
                            // active lookup so the next line with this id creates a new object.
                            if (ReleasesIdOnRemove(dead.Type))
                            {
                                objects.Remove(deadId);
                                state.Remove(deadId);
                            }
                        }
                    }
                    if (time > doc.Duration) doc.Duration = time;
                    continue;
                }

                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string objId = line.Slice(0, comma).Trim().ToString();
                var rest = line.Slice(comma + 1);

                if (objId == "0")
                {
                    ApplyGlobal(doc, rest, time);
                    continue;
                }

                if (!objects.TryGetValue(objId, out var obj))
                {
                    obj = new ReplayObject { Id = objId };
                    objects[objId] = obj;
                    doc.Objects.Add(obj);
                }
                else if (IgnoreClosedGunBurstUpdate(obj, rest))
                {
                    continue;
                }

                ReplaySample sample = state.TryGetValue(objId, out var prev)
                    ? prev.Clone()
                    : new ReplaySample();
                sample.Time = time;
                bool hasPose = false;
                bool telemetry = false;

                var props = new AcmiScan.Props(rest);
                while (props.MoveNext())
                {
                    var key = props.Key;
                    var val = props.Value;
                    if (AcmiScan.KeyIs(key, "Name"))
                    {
                        if (val.Length > 0) obj.Name = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Map"))
                    {
                        if (val.Length > 0) obj.Map = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Yield"))
                    {
                        if (float.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float yieldKg))
                            obj.Yield = yieldKg;
                    }
                    else if (AcmiScan.KeyIs(key, "Type"))
                    {
                        if (val.Length > 0
                            && (obj.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) < 0)
                            obj.Type = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Coalition"))
                    {
                        if (val.Length > 0) obj.Coalition = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Pilot"))
                    {
                        if (val.Length > 0) obj.Pilot = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "CallSign"))
                    {
                        if (val.Length > 0) obj.CallSign = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Registration"))
                    {
                        if (val.Length > 0) obj.Registration = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Loadout"))
                    {
                        if (val.Length > 0) obj.Loadout = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Livery"))
                    {
                        if (val.Length > 0) obj.Livery = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Parent"))
                    {
                        if (val.Length > 0) obj.ParentId = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Weapon"))
                    {
                        if (val.Length > 0) obj.Weapon = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Gun"))
                    {
                        if (AcmiScan.TryInt(val, out var gunIndex))
                            obj.GunIndex = gunIndex;
                    }
                    else if (AcmiScan.KeyIs(key, "Turrets"))
                    {
                        ParseTurrets(obj, val, time);
                    }
                    else if (AcmiScan.KeyIs(key, "Lasers"))
                    {
                        if (val.Length > 0) obj.AddLaserLine(time, val.ToString());
                    }
                    else if (AcmiScan.KeyIs(key, "Chute"))
                    {
                        if (val.Length > 0) obj.AddChuteLine(time, val.ToString());
                    }
                    else if (AcmiScan.KeyIs(key, "Doors"))
                    {
                        if (val.Length > 0) obj.AddDoorLine(time, val.ToString());
                    }
                    else if (AcmiScan.KeyIs(key, "Glass"))
                    {
                        if (val.Length > 0) obj.AddGlassLine(time, val.ToString());
                    }
                    else if (AcmiScan.KeyIs(key, "Rpm"))
                    {
                        if (AcmiScan.TryFloat(val, out var rpm, false))
                            obj.Rpm = rpm;
                    }
                    else if (AcmiScan.KeyIs(key, "Muzzle"))
                    {
                        if (AcmiScan.TryFloat(val, out var mz, false))
                            obj.Muzzle = mz;
                    }
                    else if (AcmiScan.KeyIs(key, "Drag"))
                    {
                        if (AcmiScan.TryFloat(val, out var drag, false))
                            obj.Drag = drag;
                    }
                    else if (AcmiScan.KeyIs(key, "Grav"))
                    {
                        if (AcmiScan.TryFloat(val, out var grav, false))
                            obj.Grav = grav;
                    }
                    else if (AcmiScan.KeyIs(key, "Spread"))
                    {
                        if (AcmiScan.TryFloat(val, out var spread, false))
                            obj.Spread = spread;
                    }
                    else if (AcmiScan.KeyIs(key, "Part"))
                    {
                        if (AcmiScan.TryByte(val, out var partId))
                        {
                            obj.PartId = partId;
                            obj.HasPart = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "Kind"))
                    {
                        if (val.Length > 0) obj.PartKind = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Path"))
                    {
                        if (val.Length > 0) obj.PartPath = val.ToString();
                    }
                    else if (AcmiScan.KeyIs(key, "Seg"))
                    {
                        if (AcmiScan.TryInt(val, out var seg))
                            obj.PartSeg = seg;
                    }
                    else if (AcmiScan.KeyIs(key, "Rotor"))
                    {
                        if (AcmiScan.TryInt(val, out var rotor))
                            obj.PartRotor = rotor;
                    }
                    else if (AcmiScan.KeyIs(key, "Vx"))
                    {
                        if (AcmiScan.TryFloat(val, out var vx, false))
                            obj.PartVx = vx;
                    }
                    else if (AcmiScan.KeyIs(key, "Vy"))
                    {
                        if (AcmiScan.TryFloat(val, out var vy, false))
                            obj.PartVy = vy;
                    }
                    else if (AcmiScan.KeyIs(key, "Vz"))
                    {
                        if (AcmiScan.TryFloat(val, out var vz, false))
                            obj.PartVz = vz;
                    }
                    else if (AcmiScan.KeyIs(key, "Visible") && val.StartsWith("0".AsSpan(), StringComparison.Ordinal))
                    {
                        obj.RemovedAt = time;
                    }
                    else if (AcmiScan.KeyIs(key, "Sinking") && obj.KilledAt < 0f
                        && val.Length > 0 && val[0] == '1')
                    {
                        // Our recorder. Tacview skips the property. It is not a removal.
                        obj.KilledAt = time;
                    }
                    else if (AcmiScan.KeyIs(key, "Dying") && obj.DyingAt < 0f
                        && val.Length > 0 && val[0] == '1')
                    {
                        // Vehicle is disabled. The wreck spawn is a later Visible=0. Tacview ignores Dying.
                        obj.DyingAt = time;
                    }
                    else if (AcmiScan.KeyIs(key, "Boom") && obj.BoomAt < 0f
                        && val.Length > 0 && val[0] == '1')
                    {
                        // The blast the recorder saw. Tacview ignores Boom.
                        obj.BoomAt = time;
                    }
                    else if ((key.Length == 1 && key[0] == 'T') || AcmiScan.KeyIs(key, "Transform"))
                    {
                        if (ParseTransform(val, ref sample))
                            hasPose = true;
                    }
                    else if (AcmiScan.KeyIs(key, "Q"))
                    {
                        if (AcmiScan.ParseQuat(val, out var qx, out var qy, out var qz, out var qw))
                        {
                            sample.HasQuat = true;
                            sample.Qx = qx;
                            sample.Qy = qy;
                            sample.Qz = qz;
                            sample.Qw = qw;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "LandingGear") || AcmiScan.KeyIs(key, "Gear"))
                    {
                        if (AcmiScan.TryFloat(val, out var g))
                        {
                            sample.HasGear = true;
                            sample.Gear = g;
                            telemetry = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "Tilt"))
                    {
                        if (TiltText.Read(val.ToString(), out var tilt) && tilt.Count > 0)
                        {
                            sample.HasTilt = true;
                            sample.TiltCount = tilt.Count > 4 ? 4 : tilt.Count;
                            sample.Tilt0 = tilt.Count > 0 ? tilt.Angle[0] : 0f;
                            sample.Tilt1 = tilt.Count > 1 ? tilt.Angle[1] : 0f;
                            sample.Tilt2 = tilt.Count > 2 ? tilt.Angle[2] : 0f;
                            sample.Tilt3 = tilt.Count > 3 ? tilt.Angle[3] : 0f;
                            telemetry = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "Throttle"))
                    {
                        if (AcmiScan.TryFloat(val, out var th))
                        {
                            sample.HasThrottle = true;
                            sample.Throttle = th;
                            telemetry = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "RpmRatio")
                          || AcmiScan.KeyIs(key, "JetRpm")
                          || AcmiScan.KeyIs(key, "TurbineRpm"))
                    {
                        if (AcmiScan.TryFloat(val, out var rpm))
                        {
                            telemetry = true;
                            if (AcmiScan.KeyIs(key, "JetRpm"))
                            {
                                sample.HasJetRpm = true;
                                sample.JetRpm = rpm;
                            }
                            else if (AcmiScan.KeyIs(key, "TurbineRpm"))
                            {
                                sample.HasTurbineRpm = true;
                                sample.TurbineRpm = rpm;
                            }
                            else
                            {
                                sample.HasRpm = true;
                                sample.Rpm = rpm;
                            }
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "TAS") || AcmiScan.KeyIs(key, "IAS"))
                    {
                        if (AcmiScan.TryFloat(val, out var tas))
                        {
                            sample.HasTas = true;
                            sample.Tas = tas;
                            telemetry = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "AGL"))
                    {
                        if (AcmiScan.TryFloat(val, out var agl))
                        {
                            sample.HasAgl = true;
                            sample.Agl = agl;
                            telemetry = true;
                        }
                    }
                    else if (AcmiScan.KeyIs(key, "Tailhook") || AcmiScan.KeyIs(key, "TailHook"))
                    {
                        if (AcmiScan.TryFloat(val, out var hook))
                        {
                            sample.HasTailhook = true;
                            sample.Tailhook = hook;
                            telemetry = true;
                        }
                    }
                }

                if (obj.SpawnAt < 0f && ReplayDocument.IsPartBreak(obj.Type))
                    obj.SpawnAt = time;

                if (hasPose)
                {
                    sample.Time = time;
                    if (sample.HasQuat && state.TryGetValue(objId, out var prevPose) && prevPose.HasQuat)
                    {
                        OrientationMath.AlignHemisphere(
                            prevPose.Qx, prevPose.Qy, prevPose.Qz, prevPose.Qw,
                            ref sample.Qx, ref sample.Qy, ref sample.Qz, ref sample.Qw);
                    }
                    if (obj.SpawnAt < 0f) obj.SpawnAt = time;
                    if (sample.Time > doc.Duration)
                        doc.Duration = sample.Time;
                    obj.Samples.Add(sample);
                    state[objId] = sample;
                }
                else if (telemetry && state.ContainsKey(objId))
                {
                    // RPM and throttle while the aircraft is sitting still have no new
                    // position. Keep them, on the last pose, so a ground start is audible.
                    sample.Time = time;
                    if (obj.SpawnAt < 0f) obj.SpawnAt = time;
                    if (sample.Time > doc.Duration)
                        doc.Duration = sample.Time;
                    obj.Samples.Add(sample);
                    state[objId] = sample;
                }
            }

            int promoted = 0;
            foreach (var o in doc.Objects)
                if (PromoteNamedShell(o)) promoted++;
            if (promoted > 0)
                Plugin.Log.LogInfo("Geschosse als Munition: " + promoted);

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
            int turretUnits = 0;
            foreach (var o in doc.Objects)
                if (o.HasTurrets) turretUnits++;
            if (turretUnits > 0)
                Plugin.Log.LogInfo("Turmstellungen: " + turretUnits);

            doc.IndexChildren();
            int launches = 0, partBreaks = 0;
            foreach (var o in doc.Objects)
            {
                if (o.OrdnanceLaunches != null) launches += o.OrdnanceLaunches.Length;
                if (o.PartBreaks != null) partBreaks += o.PartBreaks.Length;
            }
            if (launches > 0 || partBreaks > 0)
                Plugin.Log.LogInfo("Waffen-Index Starts=" + launches + " Teile=" + partBreaks);
            if (doc.Traps.Count > 0)
                Plugin.Log.LogInfo("Fangseile: " + doc.Traps.Count);
            int feedChat = 0, feedKill = 0, feedMission = 0;
            for (int i = 0; i < doc.Feeds.Count; i++)
            {
                var kind = doc.Feeds[i].Kind;
                if (kind == FeedLog.Kind.Kill) feedKill++;
                else if (kind == FeedLog.Kind.Mission) feedMission++;
                else feedChat++;
            }
            Plugin.Log.LogInfo("Feed load chat=" + feedChat + " kill=" + feedKill + " mission=" + feedMission);

            float max = doc.Duration;
            foreach (var o in doc.Objects)
                if (o.Samples.Count > 0)
                    max = Math.Max(max, o.Samples[o.Samples.Count - 1].Time);
            doc.Duration = max;
            return doc;
        }

        static bool ReleasesIdOnRemove(string? type)
        {
            string t = type ?? "";
            return t.IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("PartBreak", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IgnoreClosedGunBurstUpdate(ReplayObject obj, ReadOnlySpan<char> rest)
        {
            if ((obj.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (obj.RemovedAt >= 0f)
                return true;
            if (rest.IndexOf("Type=Misc+Explosion".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return rest.IndexOf("Radius=".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void ApplyGlobal(ReplayDocument doc, ReadOnlySpan<char> rest, float time)
        {
            var props = new AcmiScan.Props(rest);
            while (props.MoveNext())
            {
                var key = props.Key;
                var val = props.Value;
                if (AcmiScan.KeyIs(key, "Event"))
                    ApplyTrapEvent(doc, val, time);
                else if (AcmiScan.KeyIs(key, "Title"))
                    doc.Title = val.ToString();
                else if (AcmiScan.KeyIs(key, "MapId"))
                    doc.MapId = val.ToString();
                else if (AcmiScan.KeyIs(key, "DataRecorder"))
                    doc.DataRecorder = val.ToString();
                else if (AcmiScan.KeyIs(key, "TimeOfDay") && AcmiScan.TryFloat(val, out float tod, false))
                    doc.TimeOfDay = tod;
                else if (AcmiScan.KeyIs(key, "Conditions") && AcmiScan.TryFloat(val, out float cond, false))
                    doc.Conditions = cond;
                else if (AcmiScan.KeyIs(key, "CloudHeight") && AcmiScan.TryFloat(val, out float ch, false))
                    doc.CloudHeight = ch;
                else if (AcmiScan.KeyIs(key, "WindSpeed") && AcmiScan.TryFloat(val, out float ws, false))
                    doc.WindSpeed = ws;
                else if (AcmiScan.KeyIs(key, "WindTurbulence") && AcmiScan.TryFloat(val, out float wt, false))
                    doc.WindTurbulence = wt;
                else if (AcmiScan.KeyIs(key, "MoonPhase") && AcmiScan.TryFloat(val, out float mp, false))
                    doc.MoonPhase = mp;
            }
        }

        static void ApplyTrapEvent(ReplayDocument doc, ReadOnlySpan<char> val, float time)
        {
            if (!CarrierOps.TryParseTrapEvent(val, out bool release, out string aircraft, out string carrier, out int wire))
                return;
            if (!release)
            {
                doc.Traps.Add(new CarrierTrap
                {
                    AircraftId = aircraft,
                    CarrierId = carrier,
                    Wire = wire,
                    Catch = time,
                    Release = -1f,
                    Recorded = true
                });
                return;
            }
            for (int i = doc.Traps.Count - 1; i >= 0; i--)
            {
                var trap = doc.Traps[i];
                if (trap.Release >= 0f) continue;
                if (!CarrierOps.SameId(trap.AircraftId, aircraft) || !CarrierOps.SameId(trap.CarrierId, carrier))
                    continue;
                trap.Release = time;
                doc.Traps[i] = trap;
                return;
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

        static bool PromoteNamedShell(ReplayObject shell)
        {
            string t = shell.Type ?? "";
            if (t.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (t.IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (string.IsNullOrWhiteSpace(shell.Name)) return false;
            shell.Type = "Weapon+Shell";
            return true;
        }

        static bool IsShell(string? type)
        {
            return (type ?? "").IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ParseTransform(ReadOnlySpan<char> val, ref ReplaySample s)
        {
            if (!AcmiScan.ParsePose(val, out var pose) && !pose.Any) return false;
            if (!pose.Any) return false;
            if (pose.HasX) s.X = pose.X;
            if (pose.HasY) s.Y = pose.Y;
            if (pose.HasZ) s.Z = pose.Z;
            if (pose.HasRoll) s.Roll = pose.Roll;
            if (pose.HasPitch) s.Pitch = pose.Pitch;
            if (pose.HasYaw) s.Yaw = pose.Yaw;
            return true;
        }

        static void ParseTurrets(ReplayObject obj, ReadOnlySpan<char> val, float time)
        {
            if (val.Length == 0) return;
            int i = 0;
            while (i <= val.Length)
            {
                int rel = i < val.Length ? val.Slice(i).IndexOf(';') : -1;
                var seg = rel < 0 ? val.Slice(i) : val.Slice(i, rel);
                int next = rel < 0 ? val.Length + 1 : i + rel + 1;
                i = next;
                if (seg.Length == 0) continue;
                int colon = seg.IndexOf(':');
                if (colon <= 0 || colon >= seg.Length - 1) continue;
                string key = seg.Slice(0, colon).ToString();
                var ang = seg.Slice(colon + 1);
                int bar = ang.IndexOf('|');
                if (bar <= 0) continue;
                if (!AcmiScan.TryFloat(ang.Slice(0, bar), out var trav, false)) continue;
                var elevSpan = ang.Slice(bar + 1);
                int bar2 = elevSpan.IndexOf('|');
                if (bar2 >= 0) elevSpan = elevSpan.Slice(0, bar2);
                if (!AcmiScan.TryFloat(elevSpan, out var elev, false)) continue;
                obj.AddTurretKey(time, key, trav, elev);
            }
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
                while (end + 1 < n && Math.Abs(raw[end + 1] - raw[end]) <= 0.001f)
                    end++;
                if (end > start)
                {
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
                }
                g = end + 1;
            }
        }
    }
}