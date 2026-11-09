using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Samples the live Parachute after it has placed the canopy, and the
    /// pilot state and ejection seat beside it. The recorder is the object
    /// in RecordEngine.unitObjects. Nothing here calls DeployChute, Fire,
    /// or the cargo rail.
    /// </summary>
    internal static class RecordChutes
    {
        const float MoveMeters = 0.4f;
        const float ScaleStep = 0.02f;
        const float KeepSeconds = 0.15f;

        static bool _patched;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<int, string> _keys = new Dictionary<int, string>();
        static readonly Dictionary<int, CanopyState> _canopy = new Dictionary<int, CanopyState>();
        static readonly Dictionary<int, SeatState> _seats = new Dictionary<int, SeatState>();
        static readonly Dictionary<int, int> _pilotState = new Dictionary<int, int>();
        static readonly Dictionary<RecordUnit, List<string>> _pending = new Dictionary<RecordUnit, List<string>>();
        static readonly Dictionary<int, UnitRec> _units = new Dictionary<int, UnitRec>();
        static readonly HashSet<int> _missLog = new HashSet<int>();
        static readonly HashSet<int> _frameOff = new HashSet<int>();
        static int _writes;

        struct UnitRec
        {
            public string Name;
            public int Writes;
            public int Skips;
            public bool SawOpen;
            public float Deploy;
        }

        struct CanopyState
        {
            public bool Seen;
            public bool Open;
            public bool Rig;
            public float X, Y, Z;
            public float Qx, Qy, Qz, Qw;
            public float Sx, Sy, Sz;
            public float Lx, Ly, Lz;
            public float Wrinkle, Strength;
            public float Wrote;
        }

        struct SeatState
        {
            public bool Seen;
            public bool Detached;
            public bool Fired;
            public float X, Y, Z;
            public float Qx, Qy, Qz, Qw;
            public float Wrote;
        }

        public static void Install()
        {
            if (_patched) return;
            var harmony = new Harmony("noreplay.chutes");
            int n = 0;
            n += Patch(harmony, "Parachute", "Update", nameof(AfterCanopy));
            n += Patch(harmony, "Parachute", "DeployChute", nameof(AfterCanopy));
            n += Patch(harmony, "Parachute", "OnDestroy", nameof(AfterCanopyGone));
            n += Patch(harmony, "CargoDeploymentSystem", "OnDestroy", nameof(AfterFrameGone));
            n += Patch(harmony, "PilotDismounted", "Update", nameof(AfterPilot));
            n += Patch(harmony, "EjectionSeat", "Fire", nameof(AfterSeatFire));
            n += Patch(harmony, "EjectionSeat", "LateUpdate", nameof(AfterSeat));
            n += Patch(harmony, "EjectionSeat", "Detach", nameof(AfterSeat));
            _patched = n > 0;
            Plugin.Log?.LogInfo("Chute-Record methods=" + n
                + " pilotConfig=" + (RecordConfig.RecordEjectedPilots.Value ? 1 : 0));
        }

        public static void Flush()
        {
            try { FlushPending(); }
            catch { }
            if (_units.Count == 0)
                Record.Log?.LogInfo("Chute rec stopped units=0 writes=0");
            else
            {
                foreach (var pair in _units)
                {
                    var u = pair.Value;
                    Record.Log?.LogInfo("Chute rec summary " + u.Name
                        + " writes=" + u.Writes
                        + " skips=" + u.Skips
                        + " sawOpen=" + (u.SawOpen ? 1 : 0)
                        + " deploy=" + (u.Deploy > 0f ? u.Deploy.ToString("0.00", CultureInfo.InvariantCulture) : "-"));
                }
            }
            _keys.Clear();
            _canopy.Clear();
            _seats.Clear();
            _pilotState.Clear();
            _pending.Clear();
            _units.Clear();
            _missLog.Clear();
            _frameOff.Clear();
            _writes = 0;
        }

        static int Patch(Harmony harmony, string typeName, string method, string postfix)
        {
            var t = Plugin.FindGameType(typeName);
            var m = t?.GetMethod(method, Flags);
            if (m == null)
            {
                Plugin.Log?.LogInfo("Chute-Record missing " + typeName + "." + method);
                return 0;
            }
            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(RecordChutes), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Chute-Record " + typeName + "." + method + " " + ex.Message);
                return 0;
            }
        }

        static void AfterCanopy(object __instance)
        {
            if (!Record.Active || __instance is not Component chute) return;
            try { SampleCanopy(chute, gone: false); }
            catch { }
        }

        static void AfterFrameGone(object __instance)
        {
            if (!Record.Active || __instance is not Component sys) return;
            try
            {
                var unit = sys.GetType().GetField("attachedUnit", Flags)?.GetValue(sys) as Unit;
                if (unit == null) return;
                _frameOff.Add(unit.GetInstanceID());
                var host = RecorderFor(unit);
                if (host == null)
                {
                    Note(unit, wrote: false, sawOpen: false, skip: true, deploy: 0f);
                    if (_missLog.Add(unit.GetInstanceID()))
                        Record.Log?.LogInfo("Chute frame no-recorder " + NameOf(unit));
                    return;
                }
                Record.Log?.LogInfo("Chute frame off " + NameOf(unit)
                    + " t=" + Time.time.ToString("0.00", CultureInfo.InvariantCulture));
                Queue(host, "r:0");
                FlushPending();
            }
            catch { }
        }

        static void AfterCanopyGone(object __instance)
        {
            if (!Record.Active || __instance is not Component chute) return;
            try { SampleCanopy(chute, gone: true); }
            catch { }
        }

        static void AfterPilot(object __instance)
        {
            if (!Record.Active || __instance is not Unit unit) return;
            try { SamplePilot(unit); }
            catch { }
        }

        static void AfterSeatFire(object __instance)
        {
            if (!Record.Active || __instance is not Component seat) return;
            try { SampleSeat(seat, fired: true); }
            catch { }
        }

        static void AfterSeat(object __instance)
        {
            if (!Record.Active || __instance is not Component seat) return;
            try { SampleSeat(seat, fired: false); }
            catch { }
        }

        static void SampleCanopy(Component chute, bool gone)
        {
            var unit = ResolveUnit(chute);
            var host = RecorderFor(unit);
            if (host == null)
            {
                Note(unit, wrote: false, sawOpen: false, skip: true, deploy: 0f);
                if (unit != null && _missLog.Add(unit.GetInstanceID()))
                    Record.Log?.LogInfo("Chute rec no-recorder " + NameOf(unit)
                        + " id=" + unit.persistentID.Id.ToString(CultureInfo.InvariantCulture));
                return;
            }
            string key = KeyFor(chute, unit!);
            var t = chute.GetType();
            var canopy = t.GetField("canopy", Flags)?.GetValue(chute) as GameObject;
            var lines = t.GetField("lines", Flags)?.GetValue(chute) as GameObject;
            bool open = !gone && canopy != null && canopy.activeSelf;
            bool rig = !gone && (unit == null || !_frameOff.Contains(unit.GetInstanceID()));
            var pose = PoseOf(canopy, lines, chute);
            _canopy.TryGetValue(chute.GetInstanceID(), out var prev);
            bool changed = !prev.Seen || prev.Open != open || prev.Rig != rig;
            if (!changed && open)
            {
                float dx = pose.X - prev.X, dy = pose.Y - prev.Y, dz = pose.Z - prev.Z;
                float ds = Math.Abs(pose.Sx - prev.Sx) + Math.Abs(pose.Sz - prev.Sz);
                changed = dx * dx + dy * dy + dz * dz > MoveMeters * MoveMeters
                    || ds > ScaleStep
                    || Time.time - prev.Wrote >= KeepSeconds;
            }
            prev.Seen = true;
            prev.Open = open;
            prev.Rig = rig;
            prev.X = pose.X; prev.Y = pose.Y; prev.Z = pose.Z;
            prev.Qx = pose.Qx; prev.Qy = pose.Qy; prev.Qz = pose.Qz; prev.Qw = pose.Qw;
            prev.Sx = pose.Sx; prev.Sy = pose.Sy; prev.Sz = pose.Sz;
            prev.Lx = pose.Lx; prev.Ly = pose.Ly; prev.Lz = pose.Lz;
            prev.Wrinkle = pose.Wrinkle; prev.Strength = pose.Strength;
            if (!changed)
            {
                _canopy[chute.GetInstanceID()] = prev;
                return;
            }
            prev.Wrote = Time.time;
            _canopy[chute.GetInstanceID()] = prev;
            _writes++;
            float deploy = open ? Time.time : 0f;
            bool firstOpen = open && unit != null
                && (!_units.TryGetValue(unit.GetInstanceID(), out var before) || !before.SawOpen);
            Note(unit, wrote: true, sawOpen: open, skip: false, deploy: deploy);
            if (firstOpen)
                Record.Log?.LogInfo("Chute rec " + NameOf(unit) + " key=" + key + " open=1 deploy="
                    + deploy.ToString("0.00", CultureInfo.InvariantCulture));
            Queue(host, open
                ? key + ":1|" + FormatPose(pose)
                : key + ":0");
            Queue(host, rig ? "r:1" : "r:0");
            FlushPending();
        }

        static void SamplePilot(Unit unit)
        {
            int state = 0;
            var raw = unit.GetType().GetField("animationState", Flags)?.GetValue(unit);
            if (raw is byte b) state = b;
            else if (raw != null && raw.GetType().IsEnum) state = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            int id = unit.GetInstanceID();
            if (_pilotState.TryGetValue(id, out int prev) && prev == state) return;
            _pilotState[id] = state;
            var host = RecorderFor(unit);
            if (host == null)
            {
                Note(unit, wrote: false, sawOpen: false, skip: true, deploy: 0f);
                return;
            }
            Note(unit, wrote: true, sawOpen: false, skip: false, deploy: 0f);
            Record.Log?.LogInfo("Pilot rec state " + NameOf(unit) + " state=" + state.ToString(CultureInfo.InvariantCulture));
            Queue(host, "p:" + state.ToString(CultureInfo.InvariantCulture));
            FlushPending();
        }

        static void SampleSeat(Component seat, bool fired)
        {
            var t = seat.GetType();
            var pilot = t.GetField("pilotDismounted", Flags)?.GetValue(seat) as Unit;
            if (pilot == null) pilot = seat.GetComponentInParent<Unit>();
            var host = RecorderFor(pilot);
            if (host == null)
            {
                if (pilot != null) Note(pilot, wrote: false, sawOpen: false, skip: true, deploy: 0f);
                return;
            }
            bool detached = t.GetField("detachedRB", Flags)?.GetValue(seat) != null;
            var tr = seat.transform;
            var p = tr.position;
            var q = tr.rotation;
            float x = p.GlobalX(), y = p.GlobalY(), z = p.GlobalZ();
            int id = seat.GetInstanceID();
            _seats.TryGetValue(id, out var prev);
            bool changed = fired && !prev.Fired;
            changed = changed || !prev.Seen || prev.Detached != detached;
            if (!changed && detached)
            {
                float dx = x - prev.X, dy = y - prev.Y, dz = z - prev.Z;
                changed = dx * dx + dy * dy + dz * dz > MoveMeters * MoveMeters
                    || Time.time - prev.Wrote >= KeepSeconds;
            }
            bool wasDetached = prev.Detached;
            if (fired) prev.Fired = true;
            prev.Seen = true;
            prev.Detached = detached;
            prev.X = x; prev.Y = y; prev.Z = z;
            prev.Qx = q.x; prev.Qy = q.y; prev.Qz = q.z; prev.Qw = q.w;
            if (!changed)
            {
                _seats[id] = prev;
                return;
            }
            prev.Wrote = Time.time;
            _seats[id] = prev;
            if (fired)
            {
                Record.Log?.LogInfo("Seat rec " + NameOf(pilot) + " fire=1");
                Queue(host, "f:1");
            }
            if (detached)
            {
                if (!wasDetached)
                    Record.Log?.LogInfo("Seat rec " + NameOf(pilot) + " detached=1");
                Queue(host, "s:1|"
                    + x.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + y.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + z.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                    + q.x.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.y.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.z.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                    + q.w.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else
                Queue(host, "s:0");
            FlushPending();
        }

        struct Pose
        {
            public float X, Y, Z, Qx, Qy, Qz, Qw, Sx, Sy, Sz, Lx, Ly, Lz, Wrinkle, Strength;
        }

        static Pose PoseOf(GameObject? canopy, GameObject? lines, Component chute)
        {
            var pose = new Pose { Sx = 1f, Sy = 1f, Sz = 1f, Lx = 1f, Ly = 1f, Lz = 1f, Qw = 1f };
            if (canopy != null)
            {
                var p = canopy.transform.position;
                var q = canopy.transform.rotation;
                var s = canopy.transform.localScale;
                pose.X = p.GlobalX();
                pose.Y = p.GlobalY();
                pose.Z = p.GlobalZ();
                pose.Qx = q.x; pose.Qy = q.y; pose.Qz = q.z; pose.Qw = q.w;
                pose.Sx = s.x; pose.Sy = s.y; pose.Sz = s.z;
                var rend = chute.GetType().GetField("parachuteRenderer", Flags)?.GetValue(chute) as Renderer;
                if (rend != null && rend.material != null)
                {
                    try { pose.Wrinkle = rend.material.GetFloat("_wrinkleDisplacment"); } catch { }
                    try { pose.Strength = rend.material.GetFloat("_wrinkleStrength"); } catch { }
                }
            }
            if (lines != null)
            {
                var s = lines.transform.localScale;
                pose.Lx = s.x; pose.Ly = s.y; pose.Lz = s.z;
            }
            return pose;
        }

        static string FormatPose(Pose pose)
        {
            return pose.X.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Y.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Z.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Qx.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + pose.Qy.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + pose.Qz.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + pose.Qw.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + pose.Sx.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Sy.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Sz.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Lx.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Ly.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Lz.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Wrinkle.ToString("0.##", CultureInfo.InvariantCulture) + "|"
                + pose.Strength.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static Unit? ResolveUnit(Component chute)
        {
            var t = chute.GetType();
            if (t.GetField("attachedUnit", Flags)?.GetValue(chute) is Unit unit && unit != null)
                return unit;
            if (t.GetField("attachedUnitPart", Flags)?.GetValue(chute) is Component part && part != null)
            {
                if (part.GetType().GetField("parentUnit", Flags)?.GetValue(part) is Unit owner && owner != null)
                    return owner;
            }
            return chute.GetComponentInParent<Unit>();
        }

        static string KeyFor(Component chute, Unit unit)
        {
            int id = chute.GetInstanceID();
            if (_keys.TryGetValue(id, out var existing)) return existing;
            var all = unit.GetComponentsInChildren(chute.GetType(), true);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c) continue;
                n++;
                int cid = c.GetInstanceID();
                if (_keys.ContainsKey(cid)) continue;
                _keys[cid] = "c" + (n - 1).ToString(CultureInfo.InvariantCulture);
            }
            if (_keys.TryGetValue(id, out existing)) return existing;
            _keys[id] = "c" + n.ToString(CultureInfo.InvariantCulture);
            return _keys[id];
        }

        static RecordUnit? RecorderFor(Unit? unit)
        {
            if (unit == null || !unit.persistentID.IsValid) return null;
            var hostGo = Record.Host;
            if (hostGo == null) return null;
            var eng = hostGo.GetComponent<RecordEngine>();
            if (eng == null || !eng.unitObjects.TryGetValue(unit.persistentID.Id, out var go) || go == null)
                return null;
            return go.GetComponent<RecordUnit>();
        }

        static string NameOf(Unit? unit)
        {
            if (unit == null) return "no-unit";
            if (unit.definition != null && !string.IsNullOrEmpty(unit.definition.unitName))
                return unit.definition.unitName;
            if (!string.IsNullOrEmpty(unit.unitName)) return unit.unitName;
            return unit.name;
        }

        static void Note(Unit? unit, bool wrote, bool sawOpen, bool skip, float deploy)
        {
            int id = unit != null ? unit.GetInstanceID() : 0;
            _units.TryGetValue(id, out var rec);
            rec.Name = NameOf(unit);
            if (wrote) rec.Writes++;
            if (skip) rec.Skips++;
            if (sawOpen)
            {
                if (!rec.SawOpen && deploy > 0f) rec.Deploy = deploy;
                rec.SawOpen = true;
            }
            _units[id] = rec;
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
                if (!Record.Active) continue;
                var eng = host.Engine;
                if (eng == null) continue;
                var saved = new Dictionary<string, string>(host.props);
                host.props.Clear();
                host.props["Chute"] = string.Join(";", list);
                list.Clear();
                try { eng.invokeWriterUpdate(host); }
                finally
                {
                    host.props.Clear();
                    foreach (var kv in saved)
                        host.props[kv.Key] = kv.Value;
                }
                drop.Add(host);
            }
            for (int i = 0; i < drop.Count; i++)
                if (drop[i] != null) _pending.Remove(drop[i]);
        }
    }
}
