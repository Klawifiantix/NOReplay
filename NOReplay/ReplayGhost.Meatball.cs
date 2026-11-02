using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct BallRig
        {
            public Behaviour? Beh;
            public Transform Host;
            public GameObject? Meatball;
            public GameObject? Datum;
            public GameObject? WaveOff;
            public GameObject? Red;
            public GameObject? White;
            public Transform? Ball;
            public Transform? Low;
            public Transform? High;
            public float Measured;
            public Transform? RunwayStart;
            public Transform? RunwayEnd;
        }

        static bool _ballRefl;
        static System.Type? _olsType;
        static FieldInfo? _fBallBase;
        static FieldInfo? _fBall;
        static FieldInfo? _fDatum;
        static FieldInfo? _fWave;
        static FieldInfo? _fRed;
        static FieldInfo? _fWhite;
        static FieldInfo? _fBallPos;
        static FieldInfo? _fBallLow;
        static FieldInfo? _fBallHigh;
        static FieldInfo? _fMeasured;
        static FieldInfo? _fRunwayStart;
        static FieldInfo? _fRunwayEnd;

        BallRig[] _balls = Array.Empty<BallRig>();
        bool _ballReady;
        string? _ballLabel;
        float _spawnOffY;
        bool _spawnOffKnown;

        static void BindBallRefl()
        {
            if (_ballRefl) return;
            _ballRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _olsType = Plugin.FindGameType("OpticalLandingSystem");
            if (_olsType == null) return;
            _fBallBase = _olsType.GetField("airbase", flags);
            _fBall = _olsType.GetField("meatball", flags);
            _fDatum = _olsType.GetField("datumLights", flags);
            _fWave = _olsType.GetField("waveOffLights", flags);
            _fRed = _olsType.GetField("redLight", flags);
            _fWhite = _olsType.GetField("whiteLight", flags);
            _fBallPos = _olsType.GetField("meatballPos", flags);
            _fBallLow = _olsType.GetField("meatballPosLow", flags);
            _fBallHigh = _olsType.GetField("meatballPosHigh", flags);
            _fMeasured = _olsType.GetField("meatballMeasuredRange", flags);
        }

        void TryBindBall()
        {
            if (_ballReady) return;
            _ballReady = true;
            BindBallRefl();
            if (_olsType == null) return;
            var found = GetComponentsInChildren(_olsType, true);
            if (found == null || found.Length == 0) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var rigs = new List<BallRig>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is not Component comp) continue;
                var beh = comp as Behaviour;
                if (beh != null) beh.enabled = false;
                var rig = new BallRig
                {
                    Beh = beh,
                    Host = comp.transform,
                    Meatball = _fBall?.GetValue(comp) as GameObject,
                    Datum = _fDatum?.GetValue(comp) as GameObject,
                    WaveOff = _fWave?.GetValue(comp) as GameObject,
                    Red = _fRed?.GetValue(comp) as GameObject,
                    White = _fWhite?.GetValue(comp) as GameObject,
                    Ball = _fBallPos?.GetValue(comp) as Transform,
                    Low = _fBallLow?.GetValue(comp) as Transform,
                    High = _fBallHigh?.GetValue(comp) as Transform,
                    Measured = _fMeasured?.GetValue(comp) is float m ? m : 1f
                };
                object? runway = null;
                var airbase = _fBallBase?.GetValue(comp);
                if (airbase != null)
                {
                    try
                    {
                        var method = airbase.GetType().GetMethod("GetLandingRunway", flags);
                        runway = method?.Invoke(airbase, null);
                    }
                    catch { runway = null; }
                    if (runway == null)
                        runway = ArrestorRunway(airbase, flags);
                }
                if (runway != null)
                {
                    var rt = runway.GetType();
                    _fRunwayStart ??= rt.GetField("Start", flags);
                    _fRunwayEnd ??= rt.GetField("End", flags);
                    rig.RunwayStart = _fRunwayStart?.GetValue(runway) as Transform;
                    rig.RunwayEnd = _fRunwayEnd?.GetValue(runway) as Transform;
                }
                HideBall(rig);
                rigs.Add(rig);
            }
            _balls = rigs.ToArray();
        }

        static object? ArrestorRunway(object airbase, BindingFlags flags)
        {
            if (airbase.GetType().GetField("runways", flags)?.GetValue(airbase) is not Array arr)
                return null;
            object? landing = null;
            for (int i = 0; i < arr.Length; i++)
            {
                var rw = arr.GetValue(i);
                if (rw == null) continue;
                var t = rw.GetType();
                if (t.GetField("Arrestor", flags)?.GetValue(rw) is bool arrest && arrest)
                    return rw;
                if (landing == null && t.GetField("Landing", flags)?.GetValue(rw) is bool land && land)
                    landing = rw;
            }
            return landing;
        }

        void DriveMeatball(float time)
        {
            if (!_ballReady) TryBindBall();
            if (_balls.Length == 0) return;
            string shown = "";
            for (int i = 0; i < _balls.Length; i++)
            {
                var rig = _balls[i];
                if (rig.Beh != null) rig.Beh.enabled = false;
                var target = PickBallTarget(rig.Host, time);
                if (target == null || rig.RunwayStart == null || rig.Ball == null || rig.Low == null || rig.High == null)
                {
                    HideBall(rig);
                    continue;
                }
                if (!target.TryWorldPose(time, out var acPos, out _))
                {
                    HideBall(rig);
                    continue;
                }
                shown = target.CarrierLabel();
                ShowDatum(rig);
                Vector3 start = rig.RunwayStart.position;
                Vector3 end = rig.RunwayEnd != null ? rig.RunwayEnd.position : start;
                Vector3 dir = end - start;
                float len = dir.magnitude;
                if (len > 0.01f) dir /= len;
                float along = len > 500f ? Mathf.Lerp(20f, 300f, (len - 500f) * 0.0005f) : 0f;
                Vector3 touch = start + dir * along;
                var flat = new Vector3(acPos.x - touch.x, 0f, acPos.z - touch.z);
                float horiz = flat.magnitude;
                float aimY = touch.y + target.SpawnOffsetY() + 0.06f * horiz;
                float error = acPos.y - aimY;
                Vector3 toStart = start - acPos;
                float range = toStart.magnitude;
                TryComputeTrackVelocity(time, out var shipVel);
                target.TryComputeTrackVelocity(time, out var acVel);
                if (range > 1f && shipVel.sqrMagnitude > 0.25f)
                {
                    float closing = Vector3.Dot(acVel - shipVel, toStart / range);
                    if (closing > 1f)
                    {
                        float lead = Mathf.Min(range / closing, 30f);
                        range = (start + shipVel * lead - acPos).magnitude;
                    }
                }
                float measured = rig.Measured > 0.05f ? rig.Measured : 1f;
                float slope = (error * (1000f / Mathf.Max(range, 100f)) + measured * 0.5f) / measured;
                rig.Ball.position = Vector3.Lerp(rig.Low.position, rig.High.position, slope);
                bool flash = Mathf.Sin(Time.unscaledTime * 16f) > 0f;
                if (slope < 0.2f)
                {
                    SetGo(rig.White, false);
                    SetGo(rig.Red, slope < 0f ? flash : true);
                }
                else if (slope > 0.8f)
                {
                    SetGo(rig.Red, false);
                    SetGo(rig.White, slope > 1f ? flash : true);
                }
                else
                {
                    SetGo(rig.Red, false);
                    SetGo(rig.White, false);
                    SetGo(rig.Meatball, true);
                }
                SetGo(rig.WaveOff, false);
            }
            if (shown == _ballLabel) return;
            if (shown.Length == 0 && string.IsNullOrEmpty(_ballLabel))
            {
                _ballLabel = "";
                return;
            }
            _ballLabel = shown;
            Plugin.Log.LogInfo(shown.Length == 0
                ? "Meatball " + CarrierLabel() + " off"
                : "Meatball " + CarrierLabel() + " -> " + shown);
        }

        ReplayGhost? PickBallTarget(Transform ols, float time)
        {
            if (ols == null) return null;
            var sel = Plugin.SelectedGhost;
            if (BallCandidate(sel, time) && InBallCone(ols, sel!, time))
                return sel;
            ReplayGhost? best = null;
            float bestD = float.MaxValue;
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (!BallCandidate(g, time) || !InBallCone(ols, g!, time)) continue;
                if (!g!.TryWorldPose(time, out var p, out _)) continue;
                float d = (p - ols.position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = g;
                }
            }
            return best;
        }

        static bool BallCandidate(ReplayGhost? ghost, float time)
        {
            return ghost != null && ghost._air && ghost.IsTrackAlive() && ghost.HookIsDown(time);
        }

        static bool InBallCone(Transform ols, ReplayGhost ghost, float time)
        {
            if (!ghost.TryWorldPose(time, out var p, out _)) return false;
            Vector3 dir = p - ols.position;
            if (dir.sqrMagnitude < 4f) return false;
            return Vector3.Dot(dir.normalized, -ols.forward) >= 0.8f;
        }

        internal float SpawnOffsetY()
        {
            if (_spawnOffKnown) return _spawnOffY;
            _spawnOffKnown = true;
            _spawnOffY = 0f;
            try
            {
                var acType = Plugin.FindGameType("Aircraft");
                if (acType == null) return 0f;
                var ac = GetComponent(acType) ?? GetComponentInChildren(acType, true);
                if (ac == null) return 0f;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object? def = ac.GetType().GetProperty("definition", flags)?.GetValue(ac);
                if (def == null) return 0f;
                if (def.GetType().GetField("spawnOffset", flags)?.GetValue(def) is Vector3 v)
                    _spawnOffY = v.y;
            }
            catch { }
            return _spawnOffY;
        }

        static void ShowDatum(BallRig rig)
        {
            SetGo(rig.Meatball, true);
            SetGo(rig.Datum, true);
            SetGo(rig.WaveOff, false);
        }

        static void HideBall(BallRig rig)
        {
            SetGo(rig.Meatball, false);
            SetGo(rig.Datum, false);
            SetGo(rig.WaveOff, false);
            SetGo(rig.Red, false);
            SetGo(rig.White, false);
        }

        static void SetGo(GameObject? go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }
}
