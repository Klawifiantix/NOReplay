using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct HookRig
        {
            public Behaviour? Beh;
            public Transform Hook;
            public float Stowed;
            public float Deployed;
            public Vector3 LocalPos;
            public Vector3 TipInHook;
            public Matrix4x4 ParentInDrive;
        }

        struct WireRig
        {
            public Behaviour? Beh;
            public int Number;
            public Transform Left;
            public Transform Right;
            public Transform Center;
            public Transform Rest;
            public Transform CableL;
            public Transform CableR;
            public float MaxExtension;
            public float Ax, Ay, Az, Bx, By, Bz;
            public Vector3 RestLocal;
        }

        struct PlannedTrap
        {
            public string AircraftId;
            public string Label;
            public int Wire;
            public float Catch;
            public float Release;
            public bool SawCatch;
            public bool SawRelease;
            public bool HaveRelease;
            public Vector3 ReleaseLocal;
        }

        static bool _hookRefl;
        static System.Type? _tailType;
        static FieldInfo? _fHookTr;
        static FieldInfo? _fHookEnd;
        static FieldInfo? _fStowed;
        static FieldInfo? _fDeployedAng;
        static bool _wireRefl;
        static System.Type? _arrestType;
        static FieldInfo? _fWireNo;
        static FieldInfo? _fWireLeft;
        static FieldInfo? _fWireRight;
        static FieldInfo? _fWireCenter;
        static FieldInfo? _fWireRest;
        static FieldInfo? _fWireCableL;
        static FieldInfo? _fWireCableR;
        static FieldInfo? _fWireMax;

        HookRig[] _hooks = Array.Empty<HookRig>();
        CarrierOps.HookSeg[]? _hookSegs;
        bool _hookReady;
        bool _hookGaveUp;
        int _hookTries;
        float _hookNext;
        int _hookSegIndex = -1;
        WireRig[] _wires = Array.Empty<WireRig>();
        PlannedTrap[] _traps = Array.Empty<PlannedTrap>();
        bool _wiresReady;
        bool _wiresLogged;
        bool _trapsPlanned;

        static void BindHookRefl()
        {
            if (_hookRefl) return;
            _hookRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tailType = Plugin.FindGameType("TailHook");
            if (_tailType == null) return;
            _fHookTr = _tailType.GetField("hook", flags);
            _fHookEnd = _tailType.GetField("hookEnd", flags);
            _fStowed = _tailType.GetField("stowedAngle", flags);
            _fDeployedAng = _tailType.GetField("deployedAngle", flags);
        }

        static void BindWireRefl()
        {
            if (_wireRefl) return;
            _wireRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _arrestType = Plugin.FindGameType("ArrestorGear");
            if (_arrestType == null) return;
            _fWireNo = _arrestType.GetField("wireNumber", flags);
            _fWireLeft = _arrestType.GetField("leftSide", flags);
            _fWireRight = _arrestType.GetField("rightSide", flags);
            _fWireCenter = _arrestType.GetField("cableCenter", flags);
            _fWireRest = _arrestType.GetField("restCenter", flags);
            _fWireCableL = _arrestType.GetField("cableLeftSide", flags);
            _fWireCableR = _arrestType.GetField("cableRightSide", flags);
            _fWireMax = _arrestType.GetField("maxExtension", flags);
        }

        void DriveCarrier(float time)
        {
            if (_air) DriveHook(time);
            if (_ship)
            {
                DriveWires(time);
                DriveMeatball(time);
            }
        }

        void DriveHook(float time)
        {
            if (!_hookReady) TryBindHook();
            if (!_hookReady || _hookSegs == null) return;
            float amount = CarrierOps.AmountAt(_hookSegs, time);
            int seg = HookSegment(_hookSegs, time);
            if (seg != _hookSegIndex)
            {
                float rate = _hookSegs[seg].Rate;
                if (rate > 0.1f)
                    Plugin.Log.LogInfo("Hook deploy " + CarrierLabel() + " t=" + _hookSegs[seg].Time.ToString("0.00", CultureInfo.InvariantCulture));
                else if (rate < -0.1f)
                    Plugin.Log.LogInfo("Hook retract " + CarrierLabel() + " t=" + _hookSegs[seg].Time.ToString("0.00", CultureInfo.InvariantCulture));
                _hookSegIndex = seg;
            }
            for (int i = 0; i < _hooks.Length; i++)
            {
                var h = _hooks[i];
                if (h.Beh != null) h.Beh.enabled = false;
                if (h.Hook == null) continue;
                float ang = Mathf.Lerp(h.Stowed, h.Deployed, amount);
                h.Hook.localEulerAngles = new Vector3(ang, 0f, 0f);
            }
        }

        void TryBindHook()
        {
            if (_hookReady || _hookGaveUp) return;
            if (Track == null || !CarrierOps.LoadoutHasTailhook(Track.Loadout))
            {
                _hookGaveUp = true;
                return;
            }
            if (Time.unscaledTime < _hookNext) return;
            _hookNext = Time.unscaledTime + 0.5f;
            _hookTries++;
            if (_hookSegs == null)
                _hookSegs = CarrierOps.BuildHook(HookSamplesOf(Track));
            BindHookRefl();
            if (_tailType == null)
            {
                _hookGaveUp = true;
                return;
            }
            var found = GetComponentsInChildren(_tailType, true);
            if (found == null || found.Length == 0)
            {
                if (_hookTries > 20)
                {
                    _hookGaveUp = true;
                    Plugin.Log.LogInfo("Hook missing " + CarrierLabel());
                }
                return;
            }
            var rigs = new List<HookRig>(found.Length);
            var drive = Drive;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is not Component comp) continue;
                var beh = comp as Behaviour;
                if (beh != null) beh.enabled = false;
                if (_fHookTr?.GetValue(comp) is not Transform hook || hook == null) continue;
                if (hook.parent == null) continue;
                var end = _fHookEnd?.GetValue(comp) as Transform;
                if (end == null) end = hook;
                float stowed = _fStowed?.GetValue(comp) is float s ? s : 0f;
                float deployed = _fDeployedAng?.GetValue(comp) is float d ? d : 0f;
                rigs.Add(new HookRig
                {
                    Beh = beh,
                    Hook = hook,
                    Stowed = stowed,
                    Deployed = deployed,
                    LocalPos = hook.localPosition,
                    TipInHook = hook.InverseTransformPoint(end.position),
                    ParentInDrive = drive.worldToLocalMatrix * hook.parent.localToWorldMatrix
                });
            }
            _hooks = rigs.ToArray();
            _hookReady = _hooks.Length > 0;
            if (!_hookReady && _hookTries > 20)
            {
                _hookGaveUp = true;
                Plugin.Log.LogInfo("Hook missing " + CarrierLabel());
            }
        }

        void ForgetHook()
        {
            _hookReady = false;
            _hookGaveUp = false;
            _hookTries = 0;
            _hookNext = 0f;
            _hookSegIndex = -1;
            _hooks = Array.Empty<HookRig>();
        }

        static int HookSegment(CarrierOps.HookSeg[] segs, float time)
        {
            if (segs.Length == 0 || time <= segs[0].Time) return 0;
            int lo = 0;
            int hi = segs.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segs[mid].Time <= time) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        internal bool HookIsDown(float time)
        {
            if (Track == null || !CarrierOps.LoadoutHasTailhook(Track.Loadout)) return false;
            if (_hookSegs == null)
                _hookSegs = CarrierOps.BuildHook(HookSamplesOf(Track));
            return CarrierOps.AmountAt(_hookSegs, time) >= CarrierOps.HookDown;
        }

        internal bool TryHookTip(float time, out Vector3 tip)
        {
            tip = default;
            if (!_hookReady || _hooks.Length == 0 || _hookSegs == null) return false;
            if (!TryWorldPose(time, out var pos, out var rot)) return false;
            var h = _hooks[0];
            float amount = CarrierOps.AmountAt(_hookSegs, time);
            float ang = Mathf.Lerp(h.Stowed, h.Deployed, amount);
            var hookRot = Quaternion.Euler(ang, 0f, 0f);
            Vector3 tipInParent = h.LocalPos + hookRot * h.TipInHook;
            Vector3 tipInDrive = h.ParentInDrive.MultiplyPoint3x4(tipInParent);
            tip = pos + rot * tipInDrive;
            return true;
        }

        internal bool TryCablePoint(float time, out Vector3 world)
        {
            if (TryHookTip(time, out world)) return true;
            return TryWorldPose(time, out world, out _);
        }

        internal bool TryWorldPose(float time, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            if (Track == null || !Track.TrySample(time, out var s)) return false;
            pos = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
            rot = QuatOf(s);
            return true;
        }

        static Quaternion QuatOf(ReplaySample s)
        {
            if (s.HasQuat) return new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            return Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
        }

        void DriveWires(float time)
        {
            if (!_wiresReady) TryBindWires();
            if (!_wiresReady) return;
            if (!_trapsPlanned) PlanTraps();
            for (int w = 0; w < _wires.Length; w++)
            {
                var rig = _wires[w];
                if (rig.Beh != null) rig.Beh.enabled = false;
                if (rig.Center == null || rig.Rest == null) continue;
                int hit = ActiveTrap(time, rig.Number);
                if (hit < 0)
                {
                    rig.Center.position = rig.Rest.position;
                    DrawCable(rig);
                    continue;
                }
                var trap = _traps[hit];
                NoteTrap(ref trap, time);
                var ac = GhostById(trap.AircraftId);
                if (time < trap.Release && ac != null && ac.TryCablePoint(time, out var tip))
                {
                    rig.Center.position = tip;
                }
                else
                {
                    if (!trap.HaveRelease && ac != null && ac.TryHookTip(trap.Release, out var releaseTip)
                        && TryWorldPose(trap.Release, out var shipPos, out var shipRot))
                    {
                        trap.ReleaseLocal = Quaternion.Inverse(shipRot) * (releaseTip - shipPos);
                        trap.HaveRelease = true;
                    }
                    if (trap.HaveRelease)
                    {
                        float gap = Vector3.Distance(trap.ReleaseLocal, rig.RestLocal);
                        float along = CarrierOps.ReturnAlong(time - trap.Release, gap);
                        Vector3 delta = rig.RestLocal - trap.ReleaseLocal;
                        float mag = delta.magnitude;
                        Vector3 local = mag < 0.001f ? rig.RestLocal : trap.ReleaseLocal + delta / mag * along;
                        rig.Center.position = Drive.TransformPoint(local);
                    }
                    else
                        rig.Center.position = rig.Rest.position;
                }
                _traps[hit] = trap;
                DrawCable(rig);
            }
        }

        void NoteTrap(ref PlannedTrap trap, float time)
        {
            if (time < trap.Catch - 0.05f)
            {
                trap.SawCatch = false;
                trap.SawRelease = false;
                return;
            }
            if (time < trap.Release)
                trap.SawRelease = false;
            if (!trap.SawCatch && time >= trap.Catch && time < trap.Release + 0.05f)
            {
                trap.SawCatch = true;
                Plugin.Log.LogInfo("Trap caught " + trap.Label + " wire " + trap.Wire.ToString(CultureInfo.InvariantCulture)
                    + " t=" + trap.Catch.ToString("0.00", CultureInfo.InvariantCulture));
            }
            if (!trap.SawRelease && time >= trap.Release && time < trap.Release + 1f)
            {
                trap.SawRelease = true;
                Plugin.Log.LogInfo("Trap released " + trap.Label + " wire " + trap.Wire.ToString(CultureInfo.InvariantCulture)
                    + " t=" + trap.Release.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }

        int ActiveTrap(float time, int wire)
        {
            int best = -1;
            for (int i = 0; i < _traps.Length; i++)
            {
                var t = _traps[i];
                if (t.Wire != wire) continue;
                if (time < t.Catch) continue;
                float back = 30f;
                if (_traps[i].HaveRelease)
                {
                    float gap = Vector3.Distance(t.ReleaseLocal, RestOf(wire));
                    back = gap / CarrierOps.CableReturnSpeed + 0.2f;
                }
                if (time > t.Release + back) continue;
                if (best < 0 || t.Catch >= _traps[best].Catch) best = i;
            }
            return best;
        }

        Vector3 RestOf(int wire)
        {
            for (int i = 0; i < _wires.Length; i++)
                if (_wires[i].Number == wire) return _wires[i].RestLocal;
            return Vector3.zero;
        }

        void TryBindWires()
        {
            if (_wiresReady) return;
            BindWireRefl();
            if (_arrestType == null)
            {
                _wiresReady = true;
                return;
            }
            var found = GetComponentsInChildren(_arrestType, true);
            var rigs = new List<WireRig>(found != null ? found.Length : 0);
            var drive = Drive;
            if (found != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] is not Component comp) continue;
                    var beh = comp as Behaviour;
                    if (beh != null) beh.enabled = false;
                    var left = _fWireLeft?.GetValue(comp) as Transform;
                    var right = _fWireRight?.GetValue(comp) as Transform;
                    var center = _fWireCenter?.GetValue(comp) as Transform;
                    var rest = _fWireRest?.GetValue(comp) as Transform;
                    if (left == null || right == null || center == null || rest == null) continue;
                    int number = _fWireNo?.GetValue(comp) is int n ? n : 0;
                    if (number <= 0) number = i + 1;
                    float max = _fWireMax?.GetValue(comp) is float m ? m : 0f;
                    Vector3 l = drive.InverseTransformPoint(left.position);
                    Vector3 r = drive.InverseTransformPoint(right.position);
                    rigs.Add(new WireRig
                    {
                        Beh = beh,
                        Number = number,
                        Left = left,
                        Right = right,
                        Center = center,
                        Rest = rest,
                        CableL = _fWireCableL?.GetValue(comp) as Transform,
                        CableR = _fWireCableR?.GetValue(comp) as Transform,
                        MaxExtension = max,
                        Ax = l.x, Ay = l.y, Az = l.z,
                        Bx = r.x, By = r.y, Bz = r.z,
                        RestLocal = drive.InverseTransformPoint(rest.position)
                    });
                }
            }
            _wires = rigs.ToArray();
            _wiresReady = true;
            if (!_wiresLogged)
            {
                string nm = CarrierLabel();
                bool carrier = nm.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Fleet", StringComparison.OrdinalIgnoreCase) >= 0;
                if (_wires.Length > 0 || carrier)
                {
                    _wiresLogged = true;
                    Plugin.Log.LogInfo("Wires " + nm + " n=" + _wires.Length.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        void PlanTraps()
        {
            _trapsPlanned = true;
            var doc = Plugin.CurrentDoc;
            if (doc == null || Track == null || _wires.Length == 0) return;
            var planned = new List<PlannedTrap>();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < doc.Traps.Count; i++)
            {
                var t = doc.Traps[i];
                if (!CarrierOps.SameId(t.CarrierId, Track.Id)) continue;
                float release = t.Release >= 0f ? t.Release : t.Catch + CarrierOps.MaxHookSeconds;
                planned.Add(new PlannedTrap
                {
                    AircraftId = t.AircraftId,
                    Label = t.AircraftId,
                    Wire = t.Wire,
                    Catch = t.Catch,
                    Release = release
                });
                covered.Add(t.AircraftId);
                Plugin.Log.LogInfo("Trap recorded " + t.AircraftId + " wire " + t.Wire.ToString(CultureInfo.InvariantCulture)
                    + " catch=" + t.Catch.ToString("0.00", CultureInfo.InvariantCulture)
                    + " release=" + release.ToString("0.00", CultureInfo.InvariantCulture));
            }
            var lines = new CarrierOps.WireLine[_wires.Length];
            for (int i = 0; i < _wires.Length; i++)
            {
                var w = _wires[i];
                lines[i] = new CarrierOps.WireLine
                {
                    Number = w.Number,
                    Ax = w.Ax, Ay = w.Ay, Az = w.Az,
                    Bx = w.Bx, By = w.By, Bz = w.Bz,
                    MaxExtension = w.MaxExtension
                };
            }
            for (int i = 0; i < doc.Objects.Count; i++)
            {
                var o = doc.Objects[i];
                if (o == null || string.IsNullOrEmpty(o.Id) || covered.Contains(o.Id)) continue;
                if (!CarrierOps.LoadoutHasTailhook(o.Loadout)) continue;
                var segs = CarrierOps.BuildHook(HookSamplesOf(o));
                var path = LocalPath(o, segs);
                var hits = CarrierOps.DetectTraps(path, lines);
                for (int h = 0; h < hits.Length; h++)
                {
                    planned.Add(new PlannedTrap
                    {
                        AircraftId = o.Id,
                        Label = string.IsNullOrEmpty(o.Name) ? o.Id : o.Name,
                        Wire = hits[h].Wire,
                        Catch = hits[h].Catch,
                        Release = hits[h].Release
                    });
                    Plugin.Log.LogInfo("Trap detected " + (o.Name ?? o.Id)
                        + " wire " + hits[h].Wire.ToString(CultureInfo.InvariantCulture)
                        + " catch=" + hits[h].Catch.ToString("0.00", CultureInfo.InvariantCulture)
                        + " release=" + hits[h].Release.ToString("0.00", CultureInfo.InvariantCulture));
                }
            }
            _traps = planned.ToArray();
        }

        CarrierOps.LocalSample[] LocalPath(ReplayObject aircraft, CarrierOps.HookSeg[] segs)
        {
            var list = new List<CarrierOps.LocalSample>(aircraft.Samples.Count);
            var samples = aircraft.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (Track == null || !Track.TrySample(s.Time, out var ship)) continue;
                Vector3 ap = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
                Vector3 sp = Plugin.GlobalToLocal(ship.X, ship.Y, ship.Z);
                Quaternion sq = QuatOf(ship);
                Quaternion aq = QuatOf(s);
                Vector3 local = Quaternion.Inverse(sq) * (ap - sp);
                Vector3 fwd = Quaternion.Inverse(sq) * (aq * Vector3.forward);
                local.x -= fwd.x * CarrierOps.HookAft;
                local.z -= fwd.z * CarrierOps.HookAft;
                float rel = 0f;
                if (list.Count > 0)
                {
                    var prev = list[list.Count - 1];
                    float dt = s.Time - prev.Time;
                    if (dt > 0.02f)
                    {
                        float dx = local.x - prev.X;
                        float dz = local.z - prev.Z;
                        rel = Mathf.Sqrt(dx * dx + dz * dz) / dt;
                    }
                }
                list.Add(new CarrierOps.LocalSample
                {
                    Time = s.Time,
                    X = local.x,
                    Y = local.y,
                    Z = local.z,
                    Speed = rel,
                    Hook = CarrierOps.AmountAt(segs, s.Time),
                    Agl = s.HasAgl ? s.Agl : 0f,
                    HasAgl = s.HasAgl
                });
            }
            return list.ToArray();
        }

        static CarrierOps.HookSample[] HookSamplesOf(ReplayObject track)
        {
            int n = track.Samples.Count;
            var arr = new CarrierOps.HookSample[n];
            for (int i = 0; i < n; i++)
            {
                var s = track.Samples[i];
                float speed = s.HasTas ? s.Tas : 0f;
                if (!s.HasTas && i > 0)
                {
                    var p = track.Samples[i - 1];
                    float dt = s.Time - p.Time;
                    if (dt > 0.02f)
                    {
                        float dx = s.X - p.X;
                        float dy = s.Y - p.Y;
                        float dz = s.Z - p.Z;
                        speed = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
                    }
                }
                arr[i] = new CarrierOps.HookSample
                {
                    Time = s.Time,
                    Speed = speed,
                    Agl = s.Agl,
                    Y = s.Y,
                    HasAgl = s.HasAgl,
                    HasTailhook = s.HasTailhook,
                    Tailhook = s.Tailhook
                };
            }
            return arr;
        }

        static void DrawCable(WireRig rig)
        {
            if (rig.Center == null) return;
            Vector3 at = rig.Center.position;
            if (rig.Left != null && rig.CableL != null)
            {
                rig.CableL.LookAt(at);
                rig.CableL.localScale = new Vector3(1f, 1f, Vector3.Distance(at, rig.Left.position));
            }
            if (rig.Right != null && rig.CableR != null)
            {
                rig.CableR.LookAt(at);
                rig.CableR.localScale = new Vector3(1f, 1f, Vector3.Distance(at, rig.Right.position));
            }
        }

        static ReplayGhost? GhostById(string id)
        {
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g != null && g.Track != null && CarrierOps.SameId(g.Track.Id, id))
                    return g;
            }
            return null;
        }

        string CarrierLabel()
        {
            string n = Track?.Name ?? "";
            if (n.Length == 0) n = name;
            return n;
        }
    }
}
