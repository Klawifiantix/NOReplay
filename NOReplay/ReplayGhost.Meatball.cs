using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Runs after the ghost and the LOD group, so a later pass cannot
    /// switch the landing lights back off before they are drawn.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class ReplayBallLate : MonoBehaviour
    {
        internal ReplayGhost? Ghost;

        void LateUpdate()
        {
            if (Ghost == null || Plugin.CurrentDoc == null) return;
            Ghost.DriveMeatball(Plugin.Clock.Time);
        }
    }

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
        bool _ballVisLogged;
        float _ballStateAt = -999f;
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

        /// <summary>
        /// Carrier light particles are not damage smoke. SuppressDamageFx
        /// would stop them, and SetActive alone does not start them again.
        /// </summary>
        internal static bool IsOpticalLight(Transform? tr)
        {
            for (var t = tr; t != null; t = t.parent)
            {
                if (t.GetComponent<ReplayGhost>() != null) return false;
                string n = t.name ?? "";
                if (n.IndexOf("meatball", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("datum", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("optical", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("iflo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("waveoff", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("wave_off", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("fresnel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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
                if (rig.Meatball == null && rig.Ball != null)
                    rig.Meatball = rig.Ball.gameObject;
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
            if (_balls.Length == 0) return;
            var late = GetComponent<ReplayBallLate>();
            if (late == null) late = gameObject.AddComponent<ReplayBallLate>();
            late.Ghost = this;
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

        internal void DriveMeatball(float time)
        {
            if (!_ballReady) TryBindBall();
            if (_balls.Length == 0) return;
            string shown = "";
            float shownSlope = 0f;
            float shownErr = 0f;
            string shownLit = "off";
            int shownCell = -1;
            int shownCells = 0;
            string shownCellName = "";
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
                Vector3 start = rig.RunwayStart.position;
                Vector3 end = rig.RunwayEnd != null ? rig.RunwayEnd.position : start;
                Vector3 dir = end - start;
                float len = dir.magnitude;
                if (len > 0.01f) dir /= len;
                float along = ApproachPattern.MeatballTouchAlong(len);
                Vector3 touch = start + dir * along;
                var flat = new Vector3(acPos.x - touch.x, 0f, acPos.z - touch.z);
                float horiz = flat.magnitude;
                float aimY = touch.y + target.SpawnOffsetY() + ApproachPattern.GlideslopeRise * horiz;
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
                bool red = false, white = false, ball = true;
                string lit;
                if (slope < 0.2f)
                {
                    white = false;
                    red = slope < 0f ? flash : true;
                    lit = slope < 0f ? "red-flash" : "red";
                }
                else if (slope > 0.8f)
                {
                    red = false;
                    white = slope > 1f ? flash : true;
                    lit = slope > 1f ? "white-flash" : "white";
                }
                else
                {
                    red = false;
                    white = false;
                    ball = true;
                    lit = "ball";
                }
                bool wantLog = !_ballVisLogged || Time.unscaledTime - _ballStateAt >= 1f;
                string before = wantLog && !_ballVisLogged ? Snapshot(rig) : "";
                ShowLights(rig, ball, true, red, white);
                int cells = 0;
                int cell = -1;
                string cellName = "";
                if (wantLog)
                    cell = LitCell(rig, slope, out cells, out cellName);
                shownSlope = slope;
                shownErr = error;
                shownLit = lit;
                shownCell = cell;
                shownCells = cells;
                shownCellName = cellName;
                if (!_ballVisLogged)
                {
                    _ballVisLogged = true;
                    Plugin.Log.LogInfo("Meatball-Vis " + CarrierLabel()
                        + " before[" + before + "] after[" + Snapshot(rig) + "]"
                        + " lit=" + lit
                        + " slope=" + slope.ToString("0.00", CultureInfo.InvariantCulture)
                        + " err=" + error.ToString("0.0", CultureInfo.InvariantCulture)
                        + " cell=" + cell.ToString(CultureInfo.InvariantCulture)
                        + "/" + cells.ToString(CultureInfo.InvariantCulture)
                        + (cellName.Length > 0 ? " " + cellName : ""));
                }
            }
            if (shown.Length == 0)
                _ballVisLogged = false;
            if (shown != _ballLabel)
            {
                if (!(shown.Length == 0 && string.IsNullOrEmpty(_ballLabel)))
                {
                    Plugin.Log.LogInfo(shown.Length == 0
                        ? "Meatball " + CarrierLabel() + " off"
                        : "Meatball " + CarrierLabel() + " -> " + shown);
                }
                _ballLabel = shown;
            }
            if (shown.Length > 0 && Time.unscaledTime - _ballStateAt >= 1f)
            {
                _ballStateAt = Time.unscaledTime;
                Plugin.Log.LogInfo("Meatball-State " + CarrierLabel() + " -> " + shown
                    + " slope=" + shownSlope.ToString("0.00", CultureInfo.InvariantCulture)
                    + " err=" + shownErr.ToString("0.0", CultureInfo.InvariantCulture)
                    + " lit=" + shownLit
                    + " cell=" + shownCell.ToString(CultureInfo.InvariantCulture)
                    + "/" + shownCells.ToString(CultureInfo.InvariantCulture)
                    + (shownCellName.Length > 0 ? " " + shownCellName : ""));
            }
        }

        void ShowLights(BallRig rig, bool ball, bool datum, bool red, bool white)
        {
            Reveal(rig.Meatball, ball);
            Reveal(rig.Datum, datum);
            Reveal(rig.Red, red);
            Reveal(rig.White, white);
            Reveal(rig.WaveOff, false);
            if (rig.Ball != null)
                Reveal(rig.Ball.gameObject, ball);
        }

        void Reveal(GameObject? go, bool on)
        {
            if (go == null) return;
            if (!on)
            {
                if (go.activeSelf) go.SetActive(false);
                return;
            }
            var t = go.transform;
            while (t != null && t != transform)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t = t.parent;
            }
            int shipLayer = gameObject.layer;
            var nodes = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] != null && nodes[i].gameObject.layer != shipLayer)
                    nodes[i].gameObject.layer = shipLayer;
            }
            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                _disabledInPrefab.Remove(r);
                r.enabled = true;
                r.forceRenderingOff = false;
                if (r.gameObject.layer != shipLayer) r.gameObject.layer = shipLayer;
            }
            var lights = go.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].enabled = true;
            var pt = ParticleType();
            if (pt != null)
            {
                foreach (var c in go.GetComponentsInChildren(pt, true))
                {
                    if (c is not Component ps) continue;
                    EnableParticleEmission(ps);
                    PlayParticleSystem(ps);
                }
            }
            var behs = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behs.Length; i++)
            {
                var b = behs[i];
                if (b != null && b.GetType().Name == "MeshLightSettings")
                    b.enabled = true;
            }
        }

        static int LitCell(BallRig rig, float slope, out int count, out string name)
        {
            name = "";
            var list = new List<Renderer>(8);
            AddCells(rig.Datum, list);
            AddCells(rig.Meatball, list);
            if (rig.Ball != null) AddCells(rig.Ball.gameObject, list);
            count = list.Count;
            if (count == 0) return -1;
            if (count > 1 && rig.Low != null && rig.High != null)
            {
                Vector3 axis = rig.High.position - rig.Low.position;
                list.Sort((a, b) =>
                {
                    float da = a != null ? Vector3.Dot(a.transform.position, axis) : 0f;
                    float db = b != null ? Vector3.Dot(b.transform.position, axis) : 0f;
                    return da.CompareTo(db);
                });
            }
            float u = slope < 0f ? 0f : (slope > 1f ? 1f : slope);
            int index = count == 1 ? 0 : Mathf.RoundToInt(u * (count - 1));
            if (index < 0) index = 0;
            if (index >= count) index = count - 1;
            if (list[index] != null) name = list[index].name;
            return index;
        }

        static void AddCells(GameObject? go, List<Renderer> list)
        {
            if (go == null) return;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r != null && !list.Contains(r)) list.Add(r);
            }
        }

        static string Snapshot(BallRig rig)
        {
            var sb = new StringBuilder(256);
            sb.Append("ols=").Append(rig.Host != null ? rig.Host.name : "?");
            AppendGo(sb, "ball", rig.Meatball);
            AppendGo(sb, "datum", rig.Datum);
            AppendGo(sb, "red", rig.Red);
            AppendGo(sb, "white", rig.White);
            AppendGo(sb, "pos", rig.Ball != null ? rig.Ball.gameObject : null);
            return sb.ToString();
        }

        static void AppendGo(StringBuilder sb, string label, GameObject? go)
        {
            sb.Append(' ').Append(label).Append('=');
            if (go == null)
            {
                sb.Append("null");
                return;
            }
            int rends = 0, rendOn = 0, lights = 0, lightOn = 0;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                rends++;
                if (rs[i].enabled) rendOn++;
            }
            var ls = go.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < ls.Length; i++)
            {
                if (ls[i] == null) continue;
                lights++;
                if (ls[i].enabled) lightOn++;
            }
            sb.Append(go.name)
                .Append(" self=").Append(go.activeSelf ? "1" : "0")
                .Append(" hier=").Append(go.activeInHierarchy ? "1" : "0")
                .Append(" layer=").Append(go.layer.ToString(CultureInfo.InvariantCulture))
                .Append(" rend=").Append(rendOn.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(rends.ToString(CultureInfo.InvariantCulture))
                .Append(" light=").Append(lightOn.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(lights.ToString(CultureInfo.InvariantCulture));
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

        void HideBall(BallRig rig)
        {
            Reveal(rig.Meatball, false);
            Reveal(rig.Datum, false);
            Reveal(rig.WaveOff, false);
            Reveal(rig.Red, false);
            Reveal(rig.White, false);
        }

    }
}
