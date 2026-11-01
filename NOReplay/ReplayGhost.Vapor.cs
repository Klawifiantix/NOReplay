using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct VaporSlot
        {
            public object Emitter;
            public Component Particles;
            public Transform Axes;
            public bool Contrail;
            public bool Transonic;
            public bool LocalSpace;
            public AnimationCurve Curve;
            public float MinSpeed;
            public float EmitFrequency;
            public Transform[] Emits;
            public float MinAlt;
            public float MaxAlt;
            public float Counter;
            public float Opacity;
        }

        struct VaporRibbon
        {
            public TrailRenderer Trail;
            public bool Contrail;
        }

        VaporSlot[] _vapor = Array.Empty<VaporSlot>();
        VaporRibbon[] _vaporRibbons = Array.Empty<VaporRibbon>();
        bool _vaporBound;
        bool _vaporLogged;
        bool _vaporHard;
        bool _vaporWasPaused;
        float _vaporClock = -1f;
        float _groundUntil = -1f;
        bool _onGround;
        readonly HashSet<Component> _vaporPs = new HashSet<Component>();

        static bool _vaporRefl;
        static Type? _vaporEffectType;
        static FieldInfo? _fVaporEmitters;
        static FieldInfo? _fVaporParticles;
        static FieldInfo? _fVaporEmits;
        static FieldInfo? _fVaporFreq;
        static FieldInfo? _fVaporMinSpeed;
        static FieldInfo? _fVaporCurve;
        static FieldInfo? _fVaporLocal;
        static FieldInfo? _fVaporTransonic;
        static FieldInfo? _fVaporContrail;
        static FieldInfo? _fVaporMinAlt;
        static FieldInfo? _fVaporMaxAlt;
        static FieldInfo? _fGForce;
        static PropertyInfo? _pMain;
        static PropertyInfo? _pStartSpeed;
        static PropertyInfo? _pConstant;
        static PropertyInfo? _pPlayOnAwake;
        static bool _emitReady;
        static bool _emitOk;
        static MethodInfo? _mPsEmit;
        static object? _emitBox;
        static Func<object, Vector3, object>? _setEmitPos;
        static Func<object, Vector3, object>? _setEmitVel;
        static Func<object, Color, object>? _setEmitColor;
        static MethodInfo? _mNativeEmit;

        static void BindVaporRefl()
        {
            if (_vaporRefl) return;
            _vaporRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _vaporEffectType = Plugin.FindGameType("VaporEffect");
            if (_vaporEffectType != null)
                _fVaporEmitters = _vaporEffectType.GetField("emitters", flags);
            var emitter = Plugin.FindGameType("VaporEmitter");
            if (emitter != null)
            {
                _fVaporParticles = emitter.GetField("particles", flags);
                _fVaporEmits = emitter.GetField("emitTransforms", flags);
                _fVaporFreq = emitter.GetField("emitFrequency", flags);
                _fVaporMinSpeed = emitter.GetField("minSpeed", flags);
                _fVaporCurve = emitter.GetField("alphaThresholdVelocity", flags);
                _fVaporLocal = emitter.GetField("localSpace", flags);
                _fVaporTransonic = emitter.GetField("transonic", flags);
                _fVaporContrail = emitter.GetField("contrail", flags);
                _fVaporMinAlt = emitter.GetField("minAltitude", flags);
                _fVaporMaxAlt = emitter.GetField("maxAltitude", flags);
                _mNativeEmit = emitter.GetMethod("Emit", flags, null,
                    new[] { typeof(float), typeof(float), typeof(Vector3), typeof(float), typeof(float) }, null);
            }
        }

        void RememberVaporHost(Component host, Type pt)
        {
            if (host == null || pt == null) return;
            foreach (var c in host.GetComponentsInChildren(pt, true))
                if (c != null) _vaporPs.Add(c);
            BindVaporRefl();
            var emitters = _fVaporEmitters?.GetValue(host) as Array;
            if (emitters == null || _fVaporParticles == null) return;
            for (int i = 0; i < emitters.Length; i++)
            {
                var em = emitters.GetValue(i);
                if (em == null) continue;
                if (_fVaporParticles.GetValue(em) is Component ps && ps != null)
                    _vaporPs.Add(ps);
            }
        }

        static bool IsVaporPath(string path)
        {
            return path.IndexOf("vapor", StringComparison.Ordinal) >= 0
                || path.IndexOf("vortex", StringComparison.Ordinal) >= 0
                || path.IndexOf("contrail", StringComparison.Ordinal) >= 0
                || path.IndexOf("condens", StringComparison.Ordinal) >= 0;
        }

        void EnsureWingVapor()
        {
            if (_ordnance || !_air) return;
            bool first = !_vaporBound;
            _vaporBound = true;
            BindWingVapor(first);
        }

        void BindWingVapor(bool clear)
        {
            BindVaporRefl();
            var prev = _vapor;
            var slots = new List<VaporSlot>(4);
            var ribbons = new List<VaporRibbon>(4);
            QuiesceTrailEmitters(clear);
            if (_vaporEffectType != null)
            {
                foreach (var c in GetComponentsInChildren(_vaporEffectType, true))
                {
                    if (c == null) continue;
                    if (c is Behaviour beh && beh.enabled) beh.enabled = false;
                    var emitters = _fVaporEmitters?.GetValue(c) as Array;
                    if (emitters == null) continue;
                    var axes = (c as Component)?.transform;
                    for (int i = 0; i < emitters.Length; i++)
                    {
                        var em = emitters.GetValue(i);
                        if (em == null) continue;
                        var slot = ReadSlot(em, axes);
                        for (int p = 0; p < prev.Length; p++)
                        {
                            if (!ReferenceEquals(prev[p].Emitter, em)) continue;
                            slot.Counter = prev[p].Counter;
                            slot.Opacity = prev[p].Opacity;
                            break;
                        }
                        if (slot.Particles != null)
                        {
                            _vaporPs.Add(slot.Particles);
                            QuiesceVaporParticle(slot.Particles, clear);
                        }
                        slots.Add(slot);
                    }
                }
            }
            foreach (var tr in GetComponentsInChildren<TrailRenderer>(true))
            {
                if (tr == null) continue;
                string n = TrailPath(tr);
                if (!IsRibbonPath(n)) continue;
                ribbons.Add(new VaporRibbon
                {
                    Trail = tr,
                    Contrail = n.IndexOf("contrail", StringComparison.Ordinal) >= 0
                        || n.IndexOf("condens", StringComparison.Ordinal) >= 0
                });
                if (clear)
                {
                    tr.emitting = false;
                    tr.Clear();
                }
            }
            _vapor = slots.ToArray();
            _vaporRibbons = ribbons.ToArray();
            if (!_vaporLogged && (_vapor.Length > 0 || _vaporRibbons.Length > 0))
            {
                _vaporLogged = true;
                Plugin.Log?.LogInfo("Vapor " + gameObject.name
                    + " emitters=" + _vapor.Length
                    + " ribbons=" + _vaporRibbons.Length);
            }
        }

        void QuiesceTrailEmitters(bool clear)
        {
            var trailEmit = Plugin.FindGameType("TrailEmitter");
            if (trailEmit == null) return;
            var trailField = trailEmit.GetField("trailSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var c in GetComponentsInChildren(trailEmit, true))
            {
                if (c == null) continue;
                string n = ((c.name ?? "") + " " + (c.transform.parent != null ? c.transform.parent.name : "")).ToLowerInvariant();
                if (!IsVaporPath(n) && !IsRibbonPath(n)) continue;
                if (c is Behaviour trailBeh && trailBeh.enabled) trailBeh.enabled = false;
                if (trailField?.GetValue(c) is Component trailPs && trailPs != null)
                {
                    _vaporPs.Add(trailPs);
                    QuiesceVaporParticle(trailPs, clear);
                }
            }
        }

        static string TrailPath(TrailRenderer tr)
        {
            string n = (tr.name ?? "").ToLowerInvariant();
            var p = tr.transform.parent;
            if (p != null) n = p.name.ToLowerInvariant() + "/" + n;
            if (p != null && p.parent != null)
                n = p.parent.name.ToLowerInvariant() + "/" + n;
            return n;
        }

        static bool IsRibbonPath(string path)
        {
            return IsVaporPath(path)
                || path.IndexOf("wingtip", StringComparison.Ordinal) >= 0
                || path.IndexOf("wing tip", StringComparison.Ordinal) >= 0;
        }

        VaporSlot ReadSlot(object em, Transform axes)
        {
            var slot = new VaporSlot
            {
                Emitter = em,
                Axes = axes,
                Particles = _fVaporParticles?.GetValue(em) as Component,
                Contrail = _fVaporContrail?.GetValue(em) is bool c && c,
                Transonic = _fVaporTransonic?.GetValue(em) is bool t && t,
                LocalSpace = _fVaporLocal?.GetValue(em) is bool l && l,
                Curve = _fVaporCurve?.GetValue(em) as AnimationCurve,
                MinSpeed = Num(_fVaporMinSpeed?.GetValue(em), VaporMath.DefaultMinSpeed),
                EmitFrequency = Num(_fVaporFreq?.GetValue(em), 0f),
                Emits = _fVaporEmits?.GetValue(em) as Transform[] ?? Array.Empty<Transform>(),
                MinAlt = Num(_fVaporMinAlt?.GetValue(em), VaporMath.DefaultContrailMin),
                MaxAlt = Num(_fVaporMaxAlt?.GetValue(em), VaporMath.DefaultContrailMax),
                Opacity = -0.5f
            };
            if (slot.MinSpeed < 0.5f) slot.MinSpeed = VaporMath.DefaultMinSpeed;
            return slot;
        }

        static void QuiesceVaporParticle(Component ps, bool clear)
        {
            DisableParticleEmission(ps);
            SetPlayOnAwake(ps, false);
            if (clear) StopParticleSystem(ps, true);
        }

        static void SetPlayOnAwake(Component ps, bool on)
        {
            try
            {
                BindMainRefl(ps.GetType());
                if (_pMain == null || _pPlayOnAwake == null) return;
                var main = _pMain.GetValue(ps);
                if (main != null) _pPlayOnAwake.SetValue(main, on);
            }
            catch { }
        }

        static void BindMainRefl(Type pt)
        {
            if (_pMain != null) return;
            _pMain = pt.GetProperty("main");
            if (_pMain == null) return;
            var mainT = _pMain.PropertyType;
            _pStartSpeed = mainT.GetProperty("startSpeed");
            _pPlayOnAwake = mainT.GetProperty("playOnAwake");
            if (_pStartSpeed != null)
                _pConstant = _pStartSpeed.PropertyType.GetProperty("constant");
        }

        void DriveWingVapor(ReplaySample s, bool forceOff)
        {
            if (!_air) return;
            try
            {
                if (!_vaporBound) EnsureWingVapor();
                float t = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
                bool paused = Plugin.Clock == null || Plugin.Clock.Paused;
                bool seek = _vaporClock >= 0f && (t < _vaporClock - 0.02f || t > _vaporClock + 0.75f);
                float dt = 0f;
                if (!paused && !seek && _vaporClock >= 0f && t > _vaporClock)
                    dt = t - _vaporClock;
                _vaporClock = t;

                SmoothFlight(t, out var vel, out float gLoad);
                PublishGForce(gLoad);
                float airspeed = vel.magnitude;
                if (airspeed < 0.5f && _spdMs > 0.5f)
                {
                    airspeed = _spdMs;
                    if (vel.sqrMagnitude < 0.01f) vel = transform.forward * airspeed;
                }
                bool onGround = forceOff || _neutral || ReplayOnGround();
                float slowAt = SlowGate();
                bool hard = forceOff || _neutral || paused || onGround || !_visible || airspeed < slowAt;
                if (seek || (hard && !_vaporHard) || (paused && !_vaporWasPaused))
                    ClearVapor(true);
                else if (hard)
                    HoldVapor(paused);
                _vaporHard = hard;
                _vaporWasPaused = paused;
                if (_vapor.Length == 0 && _vaporRibbons.Length == 0) return;

                float detail = VaporDetail();
                float altitude = s.Y;
                bool wingLive = false;
                bool contrailLive = false;
                for (int i = 0; i < _vapor.Length; i++)
                {
                    var slot = _vapor[i];
                    bool live = TickSlot(ref slot, vel, airspeed, altitude, detail, dt, hard, onGround, paused);
                    _vapor[i] = slot;
                    if (!live) continue;
                    if (slot.Contrail) contrailLive = true;
                    else wingLive = true;
                }
                for (int i = 0; i < _vaporRibbons.Length; i++)
                {
                    var rib = _vaporRibbons[i];
                    if (rib.Trail == null) continue;
                    rib.Trail.emitting = !hard && (rib.Contrail ? contrailLive : wingLive);
                }
            }
            catch (Exception ex)
            {
                LogSubsystemOnce("Vapor", ex);
            }
        }

        bool TickSlot(ref VaporSlot slot, Vector3 worldVel, float airspeed, float altitude, float detail,
            float dt, bool hard, bool onGround, bool paused)
        {
            if (slot.Transonic && slot.Curve != null && !slot.Contrail)
                RetuneTransonic(slot.Curve, altitude);
            float minSpeed = slot.MinSpeed;
            bool low = airspeed < minSpeed;
            bool blocked = hard || low;
            if (blocked)
            {
                slot.Opacity = -0.5f;
                slot.Counter = 0f;
                if (slot.Particles != null)
                {
                    DisableParticleEmission(slot.Particles);
                    if (paused) PauseOne(slot.Particles);
                    else StopParticleSystem(slot.Particles, false);
                }
                return false;
            }
            float alpha = 0f;
            var axes = slot.Axes != null ? slot.Axes : transform;
            var fwd = axes.forward;
            var right = axes.right;
            alpha = VaporMath.AngleOnAxis(
                fwd.x, fwd.y, fwd.z,
                worldVel.x, worldVel.y, worldVel.z,
                right.x, right.y, right.z);
            float threshold = slot.Curve != null ? slot.Curve.Evaluate(airspeed) : 90f;
            bool flag = slot.Contrail
                ? VaporMath.EngineContrail(altitude, slot.MinAlt, slot.MaxAlt, airspeed, minSpeed, false, false)
                : VaporMath.WingCondensation(alpha, threshold, detail, airspeed, minSpeed, false, false);
            slot.Opacity = VaporMath.StepOpacity(slot.Opacity, flag, dt);
            if (!VaporMath.FadeStillVisible(flag, slot.Opacity))
            {
                slot.Counter = 0f;
                if (slot.LocalSpace && slot.Particles != null)
                {
                    DisableParticleEmission(slot.Particles);
                    StopParticleSystem(slot.Particles, false);
                }
                return false;
            }
            if (!slot.LocalSpace && slot.Particles != null && !EnsureEmitApi(slot.Particles.GetType()))
            {
                TryNativeEmit(slot.Emitter, alpha, airspeed, worldVel, altitude, detail);
                return true;
            }
            float rate = VaporMath.EmitRate(slot.EmitFrequency, airspeed, minSpeed);
            slot.Counter += dt * rate;
            if (slot.Counter <= 1f) return true;
            slot.Counter = 0f;
            if (slot.Particles == null || slot.Emits == null) return true;
            if (slot.LocalSpace)
            {
                EnableParticleEmission(slot.Particles);
                for (int i = 0; i < slot.Emits.Length; i++)
                {
                    var et = slot.Emits[i];
                    if (et == null || worldVel.sqrMagnitude < 0.01f) continue;
                    et.rotation = Quaternion.LookRotation(-worldVel);
                }
                PlayParticleSystem(slot.Particles);
            }
            else
            {
                DisableParticleEmission(slot.Particles);
                float speed = StartSpeedOf(slot.Particles);
                var origin = DatumOrigin();
                for (int i = 0; i < slot.Emits.Length; i++)
                {
                    var et = slot.Emits[i];
                    if (et == null) continue;
                    var pos = et.position - origin;
                    var jitter = new Vector3(
                        UnityEngine.Random.Range(-1, 1),
                        UnityEngine.Random.Range(-1, 1),
                        UnityEngine.Random.Range(-1, 1));
                    EmitOne(slot.Particles, pos, worldVel + speed * jitter, new Color(1f, 1f, 1f, slot.Opacity));
                }
            }
            return true;
        }

        static void RetuneTransonic(AnimationCurve curve, float altitude)
        {
            var keys = curve.keys;
            if (keys == null || keys.Length < 5) return;
            float sos = VaporMath.SpeedOfSound(altitude);
            if (Mathf.Abs(keys[3].time - sos) < 0.5f) return;
            keys[2].time = sos - 40f;
            keys[3].time = sos;
            keys[4].time = sos + 40f;
            curve.keys = keys;
        }

        void ClearVapor(bool particles)
        {
            if (particles)
            {
                for (int i = 0; i < _vapor.Length; i++)
                {
                    var ps = _vapor[i].Particles;
                    if (ps == null) continue;
                    DisableParticleEmission(ps);
                    StopParticleSystem(ps, true);
                    _vapor[i].Counter = 0f;
                    _vapor[i].Opacity = -0.5f;
                }
            }
            for (int i = 0; i < _vaporRibbons.Length; i++)
            {
                var tr = _vaporRibbons[i].Trail;
                if (tr == null) continue;
                tr.emitting = false;
                tr.Clear();
            }
        }

        void HoldVapor(bool paused)
        {
            for (int i = 0; i < _vapor.Length; i++)
            {
                var ps = _vapor[i].Particles;
                if (ps == null) continue;
                DisableParticleEmission(ps);
                if (paused) PauseOne(ps);
                else StopParticleSystem(ps, false);
            }
            for (int i = 0; i < _vaporRibbons.Length; i++)
            {
                var tr = _vaporRibbons[i].Trail;
                if (tr == null) continue;
                tr.emitting = false;
            }
        }

        static void PauseOne(Component ps)
        {
            BindParticleRefl();
            try
            {
                if (_pPsIsPlaying?.GetValue(ps) is bool playing && playing)
                    _mPsPause?.Invoke(ps, null);
            }
            catch { }
        }

        void SmoothFlight(float t, out Vector3 vel, out float gLoad)
        {
            const float window = 0.30f;
            gLoad = 0f;
            if (!WindowVelocity(t, window, out vel))
            {
                vel = _velMs;
                return;
            }
            if (WindowVelocity(t - window, window, out var prev))
                gLoad = VaporMath.GLoad(vel.x, vel.y, vel.z, prev.x, prev.y, prev.z, window);
        }

        bool WindowVelocity(float t, float window, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (Track == null) return false;
            float half = window * 0.5f;
            if (!Track.TrySample(t - half, out var a) || !Track.TrySample(t + half, out var b))
                return false;
            float dt = b.Time - a.Time;
            if (dt < 0.04f) return false;
            vel = new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z) / dt;
            return true;
        }

        void PublishGForce(float gLoad)
        {
            if (_aircraftComp == null) return;
            if (_fGForce == null)
                _fGForce = FindInstField(_aircraftComp.GetType(), "gForce");
            if (_fGForce == null || _fGForce.FieldType != typeof(float)) return;
            try { _fGForce.SetValue(_aircraftComp, gLoad); }
            catch { }
        }

        float SlowGate()
        {
            if (_vapor.Length == 0) return VaporMath.DefaultMinSpeed;
            float gate = _vapor[0].MinSpeed;
            for (int i = 1; i < _vapor.Length; i++)
                if (_vapor[i].MinSpeed < gate) gate = _vapor[i].MinSpeed;
            if (gate < 0.5f) gate = VaporMath.DefaultMinSpeed;
            return gate;
        }

        float VaporDetail()
        {
            if (!_visible || _neutral) return 0f;
            if (gameObject == Plugin._ghost) return 1000f;
            var cam = Camera.main;
            if (cam == null) return 2f;
            float dist = Vector3.Distance(transform.position, cam.transform.position);
            return 1000f / Mathf.Max(dist, 1f);
        }

        bool ReplayOnGround()
        {
            if (Time.unscaledTime < _groundUntil) return _onGround;
            _groundUntil = Time.unscaledTime + 0.15f;
            Vector3 from = transform.position + Vector3.up * 4f;
            _onGround = Physics.Raycast(from, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                && hit.distance < 7f;
            return _onGround;
        }

        static void TryNativeEmit(object emitter, float alpha, float airspeed, Vector3 vel, float altitude, float detail)
        {
            if (_mNativeEmit == null || emitter == null) return;
            try { _mNativeEmit.Invoke(emitter, new object[] { alpha, airspeed, vel, altitude, detail }); }
            catch { }
        }

        static Vector3 DatumOrigin()
        {
            var datum = Plugin.Datum;
            return datum != null ? datum.position : Vector3.zero;
        }

        static float StartSpeedOf(Component ps)
        {
            try
            {
                BindMainRefl(ps.GetType());
                if (_pMain == null || _pStartSpeed == null || _pConstant == null) return 0f;
                var main = _pMain.GetValue(ps);
                if (main == null) return 0f;
                var curve = _pStartSpeed.GetValue(main);
                if (curve == null) return 0f;
                return _pConstant.GetValue(curve) is float f ? f : 0f;
            }
            catch { return 0f; }
        }

        static void EmitOne(Component ps, Vector3 pos, Vector3 vel, Color color)
        {
            if (!EnsureEmitApi(ps.GetType())) return;
            try
            {
                object box = _emitBox;
                box = _setEmitPos(box, pos);
                box = _setEmitVel(box, vel);
                box = _setEmitColor(box, color);
                _emitBox = box;
                _mPsEmit.Invoke(ps, new[] { box, (object)1 });
            }
            catch
            {
                _emitOk = false;
            }
        }

        static bool EnsureEmitApi(Type pt)
        {
            if (_emitReady) return _emitOk;
            _emitReady = true;
            try
            {
                var ep = pt.GetNestedType("EmitParams");
                if (ep == null) return false;
                _setEmitPos = MakeMutator<Vector3>(ep, "position");
                _setEmitVel = MakeMutator<Vector3>(ep, "velocity");
                _setEmitColor = MakeMutator<Color>(ep, "startColor");
                _mPsEmit = pt.GetMethod("Emit", new[] { ep, typeof(int) });
                if (_setEmitPos == null || _setEmitVel == null || _setEmitColor == null || _mPsEmit == null)
                    return false;
                _emitBox = Activator.CreateInstance(ep);
                _emitOk = _emitBox != null;
                return _emitOk;
            }
            catch
            {
                _emitOk = false;
                return false;
            }
        }

        static Func<object, T, object> MakeMutator<T>(Type structType, string propName)
        {
            var prop = structType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var set = prop?.GetSetMethod(true);
            if (set == null) return null;
            var box = Expression.Parameter(typeof(object), "box");
            var value = Expression.Parameter(typeof(T), "value");
            var local = Expression.Variable(structType, "s");
            var body = Expression.Block(
                new[] { local },
                Expression.Assign(local, Expression.Convert(box, structType)),
                Expression.Call(local, set, value),
                Expression.Convert(local, typeof(object)));
            return Expression.Lambda<Func<object, T, object>>(body, box, value).Compile();
        }
    }
}
