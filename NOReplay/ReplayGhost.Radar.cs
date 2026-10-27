using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        // GroundVehicle folds a radar dish below this speed and stows it at or above it.
        // The managed job that sets networkStationary is not in the decompiled scripts;
        // playback uses the same threshold the live deploy sequence is driven from.
        const float RadarDeploySpeed = 0.5f;

        static bool _radarRefl;
        static bool _radarReflWarned;
        static FieldInfo? _fRotators;
        static FieldInfo? _fRotTransform;
        static FieldInfo? _fRotAxis;
        static FieldInfo? _fDeployParts;
        static FieldInfo? _fPartTransform;
        static FieldInfo? _fPartRate;
        static FieldInfo? _fPartAngStow;
        static FieldInfo? _fPartAngDep;
        static FieldInfo? _fPartPosStow;
        static FieldInfo? _fPartPosDep;
        static FieldInfo? _fPartDisableStowed;
        static FieldInfo? _fPartStays;
        static FieldInfo? _fPartRadar;

        readonly HashSet<Transform> _radarHoldFree = new HashSet<Transform>();
        Radar[] _radars = System.Array.Empty<Radar>();
        RadarDish[] _dishes = System.Array.Empty<RadarDish>();
        DeployState[] _deploys = System.Array.Empty<DeployState>();
        bool _radarsBound;
        bool _radarStampValid;
        int _radarStamp;
        float _nextRadarScan;
        float _radarWatchStart = -1f;
        float _radarClock = -1f;
        bool _radarPoseReady;

        struct RadarDish
        {
            public Transform? Transform;
            public Vector3 Axis;
            public int Gate;
        }

        sealed class DeployState
        {
            public Transform? Transform;
            public float Rate;
            public Vector3 AngStow;
            public Vector3 AngDep;
            public Vector3 PosStow;
            public Vector3 PosDep;
            public bool DisableWhenStowed;
            public bool StaysDeployed;
            public Radar? Radar;
            public float Amount;
            public bool Deploying;
            public bool Started;
        }

        static void BindRadarRefl()
        {
            if (_radarRefl) return;
            _radarRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fRotators = typeof(TargetDetector).GetField("rotators", flags);
            var rotType = typeof(TargetDetector).GetNestedType("Rotator", flags);
            if (rotType != null)
            {
                _fRotTransform = rotType.GetField("transform", flags);
                _fRotAxis = rotType.GetField("axis", flags);
            }
            _fDeployParts = typeof(GroundVehicle).GetField("deployableParts", flags);
            var partType = typeof(GroundVehicle).GetNestedType("DeployablePart", flags);
            if (partType != null)
            {
                _fPartTransform = partType.GetField("transform", flags);
                _fPartRate = partType.GetField("deployRate", flags);
                _fPartAngStow = partType.GetField("angleStowed", flags);
                _fPartAngDep = partType.GetField("angleDeployed", flags);
                _fPartPosStow = partType.GetField("positionStowed", flags);
                _fPartPosDep = partType.GetField("positionDeployed", flags);
                _fPartDisableStowed = partType.GetField("disableWhenStowed", flags);
                _fPartStays = partType.GetField("staysDeployed", flags);
                _fPartRadar = partType.GetField("radar", flags);
            }
            if ((_fRotators == null || _fDeployParts == null) && !_radarReflWarned)
            {
                _radarReflWarned = true;
                Plugin.Log?.LogWarning("Radar reflection incomplete. rotators="
                    + (_fRotators != null) + " deployableParts=" + (_fDeployParts != null));
            }
        }

        float NextRadarInterval()
        {
            if (_radarWatchStart < 0f) _radarWatchStart = Time.unscaledTime;
            bool waiting = _air && _radars.Length == 0 && Time.unscaledTime - _radarWatchStart < 20f;
            return waiting ? 0.35f : 1.25f;
        }

        int ComputeRadarStamp()
        {
            int h = 17;
            var radars = GetComponentsInChildren<Radar>(true);
            h = h * 31 + radars.Length;
            for (int i = 0; i < radars.Length; i++)
            {
                if (radars[i] != null) h = h * 31 + radars[i].GetInstanceID();
            }
            var gv = GetComponentInChildren<GroundVehicle>(true);
            h = h * 31 + (gv != null ? gv.GetInstanceID() : 0);
            return h;
        }

        void EnsureRadarsCurrent(bool force)
        {
            if (_ordnance) return;
            if (!force && _radarsBound && Time.unscaledTime < _nextRadarScan) return;
            int stamp = ComputeRadarStamp();
            if (_radarsBound && _radarStampValid && stamp == _radarStamp)
            {
                _nextRadarScan = Time.unscaledTime + NextRadarInterval();
                return;
            }
            BindRadars();
            _nextRadarScan = Time.unscaledTime + NextRadarInterval();
            ProtectRadarParts();
        }

        void BindRadars()
        {
            BindRadarRefl();
            var prev = new Dictionary<Transform, DeployState>();
            for (int i = 0; i < _deploys.Length; i++)
            {
                var old = _deploys[i];
                if (old == null) continue;
                var key = old.Transform;
                if (key == null || prev.ContainsKey(key)) continue;
                prev[key] = old;
            }

            var radars = GetComponentsInChildren<Radar>(true);
            var dishList = new List<RadarDish>(8);
            var gateOf = new Dictionary<int, int>();
            _radarHoldFree.Clear();

            var deploys = new List<DeployState>(4);
            var gv = GetComponentInChildren<GroundVehicle>(true);
            var parts = gv != null ? _fDeployParts?.GetValue(gv) as System.Array : null;
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    var elem = parts.GetValue(i);
                    if (elem == null) continue;
                    var tr = _fPartTransform?.GetValue(elem) as Transform;
                    if (tr == null) continue;
                    var state = new DeployState
                    {
                        Transform = tr,
                        Rate = _fPartRate?.GetValue(elem) is float rate ? rate : 1f,
                        AngStow = _fPartAngStow?.GetValue(elem) is Vector3 ast ? ast : Vector3.zero,
                        AngDep = _fPartAngDep?.GetValue(elem) is Vector3 ade ? ade : Vector3.zero,
                        PosStow = _fPartPosStow?.GetValue(elem) is Vector3 pst ? pst : tr.localPosition,
                        PosDep = _fPartPosDep?.GetValue(elem) is Vector3 pde ? pde : tr.localPosition,
                        DisableWhenStowed = _fPartDisableStowed?.GetValue(elem) is bool dis && dis,
                        StaysDeployed = _fPartStays?.GetValue(elem) is bool stay && stay,
                        Radar = _fPartRadar?.GetValue(elem) as Radar
                    };
                    if (prev.TryGetValue(tr, out var old))
                    {
                        state.Amount = old.Amount;
                        state.Deploying = old.Deploying;
                        state.Started = old.Started;
                    }
                    int index = deploys.Count;
                    deploys.Add(state);
                    _radarHoldFree.Add(tr);
                    if (state.Radar != null)
                        gateOf[state.Radar.GetInstanceID()] = index;
                }
            }

            for (int i = 0; i < radars.Length; i++)
            {
                var radar = radars[i];
                if (radar == null) continue;
                if (radar is Behaviour beh && beh.enabled) beh.enabled = false;
                int gate = -1;
                if (gateOf.TryGetValue(radar.GetInstanceID(), out int g)) gate = g;
                var rotators = _fRotators?.GetValue(radar) as System.Array;
                if (rotators == null) continue;
                for (int r = 0; r < rotators.Length; r++)
                {
                    var elem = rotators.GetValue(r);
                    if (elem == null) continue;
                    var tr = _fRotTransform?.GetValue(elem) as Transform;
                    if (tr == null) continue;
                    var axis = _fRotAxis?.GetValue(elem) is Vector3 ax ? ax : Vector3.zero;
                    dishList.Add(new RadarDish { Transform = tr, Axis = axis, Gate = gate });
                    _radarHoldFree.Add(tr);
                }
            }

            _radars = radars;
            _dishes = dishList.ToArray();
            _deploys = deploys.ToArray();
            _radarsBound = true;
            _radarStamp = ComputeRadarStamp();
            _radarStampValid = true;
        }

        void ProtectRadarParts()
        {
            if (_parts.Length == 0 || _radarHoldFree.Count == 0) return;
            for (int i = 0; i < _parts.Length; i++)
            {
                var tr = _parts[i];
                if (tr != null && _radarHoldFree.Contains(tr))
                    _partSkip[i] = true;
            }
        }

        void TickRadars()
        {
            if (_ordnance) return;
            if (!_radarsBound) EnsureRadarsCurrent(true);
            if (_dishes.Length == 0 && _deploys.Length == 0) return;

            bool paused = Plugin.Clock == null || Plugin.Clock.Paused;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            bool jump = _radarClock >= 0f && Mathf.Abs(now - _radarClock) > 0.4f;
            _radarClock = now;
            float speed = Plugin.Clock != null ? Plugin.Clock.Speed : 1f;
            bool snap = !_radarPoseReady || jump;
            _radarPoseReady = true;
            float spinDt = (paused || snap) ? 0f : Time.unscaledDeltaTime * speed;
            float animDt = (paused || snap) ? 0f : Time.unscaledDeltaTime * Mathf.Abs(speed);
            bool wantOpen = _spdMs < RadarDeploySpeed;

            for (int i = 0; i < _radars.Length; i++)
            {
                if (_radars[i] is Behaviour beh && beh.enabled)
                    beh.enabled = false;
            }
            for (int i = 0; i < _deploys.Length; i++)
                DriveDeploy(_deploys[i], wantOpen, animDt, snap);
            if (spinDt == 0f) return;
            for (int i = 0; i < _dishes.Length; i++)
            {
                var dish = _dishes[i];
                if (dish.Transform == null) continue;
                if (dish.Gate >= 0)
                {
                    var part = _deploys[dish.Gate];
                    if (part == null || !part.Started || !part.Deploying || part.Amount < 1f)
                        continue;
                }
                dish.Transform.localEulerAngles += dish.Axis * spinDt;
            }
        }

        static void DriveDeploy(DeployState p, bool wantOpen, float animDt, bool snap)
        {
            if (p == null) return;
            if (!p.Started)
            {
                p.Started = true;
                p.Deploying = wantOpen;
                if (snap) p.Amount = wantOpen ? 1f : 0f;
                ApplyDeploySideEffects(p);
            }
            else if (snap)
            {
                p.Deploying = wantOpen;
                p.Amount = wantOpen ? 1f : 0f;
                if (!wantOpen)
                    ApplyDeploySideEffects(p);
                else if (p.DisableWhenStowed && p.Transform != null)
                    p.Transform.gameObject.SetActive(true);
            }
            else if (p.Deploying != wantOpen && !(p.StaysDeployed && p.Deploying))
            {
                p.Deploying = wantOpen;
                ApplyDeploySideEffects(p);
            }

            if (!snap && !(p.StaysDeployed && p.Amount >= 1f && p.Deploying))
            {
                float dir = p.Deploying ? 1f : -1f;
                p.Amount = Mathf.Clamp01(p.Amount + dir * p.Rate * animDt);
            }
            if (p.Transform == null) return;
            p.Transform.localEulerAngles = Vector3.Lerp(p.AngStow, p.AngDep, p.Amount);
            p.Transform.localPosition = Vector3.Lerp(p.PosStow, p.PosDep, p.Amount);
        }

        static void ApplyDeploySideEffects(DeployState p)
        {
            if (p.Radar != null)
            {
                if (p.Radar is Behaviour beh) beh.enabled = false;
                try { p.Radar.ResetRotators(); } catch { }
            }
            if (p.DisableWhenStowed && p.Transform != null)
                p.Transform.gameObject.SetActive(p.Deploying);
        }
    }
}
