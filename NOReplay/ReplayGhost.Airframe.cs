using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {

        float _dmgNext;
        Component[] _killPs = Array.Empty<Component>();
        bool _killPsBound;

        static bool KeepFxName(string p)
        {
            return p.IndexOf("flare", StringComparison.Ordinal) >= 0
                || p.IndexOf("chaff", StringComparison.Ordinal) >= 0
                || p.IndexOf("counter", StringComparison.Ordinal) >= 0
                || p.IndexOf("nav", StringComparison.Ordinal) >= 0
                || p.IndexOf("strobe", StringComparison.Ordinal) >= 0
                || p.IndexOf("beacon", StringComparison.Ordinal) >= 0
                || p.IndexOf("muzzle", StringComparison.Ordinal) >= 0
                || p.IndexOf("tracer", StringComparison.Ordinal) >= 0
                || p.IndexOf("rotor", StringComparison.Ordinal) >= 0
                || p.IndexOf("prop", StringComparison.Ordinal) >= 0
                || p.IndexOf("wake", StringComparison.Ordinal) >= 0
                || p.IndexOf("water", StringComparison.Ordinal) >= 0
                || p.IndexOf("foam", StringComparison.Ordinal) >= 0
                || p.IndexOf("spray", StringComparison.Ordinal) >= 0
                || p.IndexOf("bow", StringComparison.Ordinal) >= 0
                || p.IndexOf("stern", StringComparison.Ordinal) >= 0
                || p.IndexOf("wave", StringComparison.Ordinal) >= 0
                || p.IndexOf("splash", StringComparison.Ordinal) >= 0
                || p.IndexOf("wash", StringComparison.Ordinal) >= 0
                || p.IndexOf("dust", StringComparison.Ordinal) >= 0
                || p.IndexOf("haze", StringComparison.Ordinal) >= 0
                || p.IndexOf("exhaust", StringComparison.Ordinal) >= 0
                || p.IndexOf("afterburn", StringComparison.Ordinal) >= 0
                || p.IndexOf("vapor", StringComparison.Ordinal) >= 0
                || p.IndexOf("vortex", StringComparison.Ordinal) >= 0;
        }

        void AdoptOrphanFx()
        {
            AdoptType(_shaftType, ref _shafts);
            AdoptType(_cspType, ref _props);
            AdoptType(_propType, ref _props);
            AdoptType(_fanType, ref _props);
            AdoptType(_tfanType, ref _tfans);
            AdoptType(_gearType, ref _gears);
            AdoptType(_downwashType, ref _downwashes);
            AdoptType(_downwashEffectType, ref _downwashEffects);
            BindDownwashSources();
        }

        void AdoptType(Type? t, ref Component[] dst)
        {
            if (t == null) return;
            var all = UnityEngine.Object.FindObjectsOfType(t);
            var af = t.GetField("aircraft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (af == null || all == null) return;
            var extra = new System.Collections.Generic.List<Component>(dst);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c == null) continue;
                var a = af.GetValue(c) as Component;
                if (a == null) continue;
                if (a.gameObject != gameObject && !a.transform.IsChildOf(transform)) continue;
                if (!extra.Contains(c)) extra.Add(c);
            }
            dst = extra.ToArray();
        }

        internal void DriveDownwashThrust(bool active, float throttle)
        {
            if (!_air) return;
            if (!active || _neutral || !_visible || !IsTrackAlive())
            {
                for (int i = 0; i < _dwTargets.Length; i++)
                {
                    var t = _dwTargets[i];
                    if (t.ThrustComp == null) continue;
                    try { t.ThrustField.SetValue(t.ThrustComp, 0f); } catch { }
                }
                StopSurfaceFx(false);
                return;
            }

            if (Plugin.Clock.Paused)
            {
                if (!_dwPaused)
                {
                    _dwPaused = true;
                    PauseParticleSystems(_dwParticleSystems);
                }
                return;
            }

            bool justResumed = false;
            if (_dwPaused)
            {
                _dwPaused = false;
                justResumed = true;
            }

            float thrustRatio = Mathf.Clamp(throttle, 0.75f, 1f);
            for (int i = 0; i < _dwTargets.Length; i++)
            {
                var t = _dwTargets[i];
                if (t.ThrustComp == null) continue;
                try { t.ThrustField.SetValue(t.ThrustComp, t.MaxThrust * thrustRatio); } catch { }
            }

            for (int i = 0; i < _dwParticleSystems.Length; i++)
                EnableParticleEmission(_dwParticleSystems[i]);

            for (int i = 0; i < _props.Length; i++)
            {
                var c = _props[i];
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn == "DuctedFan")
                {
                    float maxRpm = 2200f;
                    if (ReadFit(_fFanMaxRpm, c) is float mr && mr > 10f) maxRpm = mr;
                    WriteFit(_fFanRpm, c, maxRpm * Mathf.Clamp(thrustRatio, 0.6f, 0.95f));
                }
                else if (tn == "Turbofan")
                {
                    float max = 10000f;
                    if (ReadFit(_fTfanMax, c) is float mx && mx > 1f) max = mx;
                    WriteFit(_fTfanRpm, c, max * Mathf.Clamp(thrustRatio, 0.6f, 0.95f));
                }
                else if (tn == "PropFan")
                {
                    float nom = 2200f;
                    if (ReadFit(_fPropNom, c) is float n && n > 10f) nom = n;
                    float ratio = Mathf.Clamp(thrustRatio, 0.6f, 0.95f);
                    WriteFit(_fPropRpm, c, nom * ratio);
                    WriteFit(_fPropRatio, c, ratio);
                }
            }

            if (justResumed)
            {
                for (int i = 0; i < _downwashes.Length; i++)
                {
                    var dw = _downwashes[i];
                    if (dw == null) continue;
                    try { _mDwCheckSurface?.Invoke(dw, null); } catch { }
                }
            }
        }

        internal void StopSurfaceFx(bool clear)
        {
            _dwPaused = false;
            for (int i = 0; i < _dwTargets.Length; i++)
            {
                var t = _dwTargets[i];
                if (t.ThrustComp == null) continue;
                try { t.ThrustField.SetValue(t.ThrustComp, 0f); } catch { }
            }
            if (_mDwUpdateSystems != null)
            {
                object[] offArgs = { false, false };
                for (int i = 0; i < _downwashes.Length; i++)
                {
                    var dw = _downwashes[i];
                    if (dw == null) continue;
                    try { _mDwUpdateSystems.Invoke(dw, offArgs); } catch { }
                    if (dw is Behaviour b) b.enabled = false;
                }
            }
            if (_mDweStop != null)
            {
                for (int i = 0; i < _downwashEffects.Length; i++)
                {
                    var dwe = _downwashEffects[i];
                    if (dwe == null) continue;
                    try { _mDweStop.Invoke(dwe, null); } catch { }
                }
            }
            for (int i = 0; i < _dwParticleSystems.Length; i++)
                StopParticleSystem(_dwParticleSystems[i], clear);
            if (_ship)
                StopShipFx(clear);
        }

        internal void DestroyDownwashTransforms()
        {
            for (int i = 0; i < _spawnedDownwashTransforms.Count; i++)
            {
                var tr = _spawnedDownwashTransforms[i];
                if (tr != null && tr.gameObject != null)
                {
                    try { UnityEngine.Object.Destroy(tr.gameObject); } catch { }
                }
            }
            _spawnedDownwashTransforms.Clear();
        }

        static object? ReadFit(FieldInfo? field, object target)
        {
            var decl = field?.DeclaringType;
            if (decl == null || !decl.IsInstanceOfType(target)) return null;
            try { return field!.GetValue(target); }
            catch { return null; }
        }

        static void WriteFit(FieldInfo? field, object target, object value)
        {
            var decl = field?.DeclaringType;
            if (decl == null || !decl.IsInstanceOfType(target)) return;
            try { field!.SetValue(target, value); }
            catch { }
        }

        static float Num(object? v, float fallback)
        {
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is double d) return (float)d;
            return fallback;
        }

        float CompNum(Component c, string name, float fallback)
        {
            return Num(FieldOn(c, name), fallback);
        }

        void DriveCspAudio(Component c, float throttle)
        {
            object? src = ReadFit(_fCspAudio, c) ?? FieldOn(c, "propAudio");
            if (throttle <= 0.02f) { StopEngineSrc(src); return; }
            float limit = CompNum(c, "rpmLimit", 2000f);
            float rpm = Num(ReadFit(_fCspRpm, c), 0f);
            float rpmRatio = limit > 1f && rpm > 1f ? Mathf.Clamp(rpm / limit, 0f, 1.5f) : Mathf.Clamp01(throttle);
            float targetAoa = CompNum(c, "targetAOA", 0f);
            float aoa = CompNum(c, "averageAoA", 0f);
            if (Mathf.Abs(aoa) < 0.01f) aoa = throttle * targetAoa;
            float pitchBase = CompNum(c, "pitchBase", 1f);
            float volumeBase = CompNum(c, "volumeBase", 0.2f);
            float pitch = pitchBase
                + rpmRatio * CompNum(c, "rpmModifyPitch", 0f)
                + CompNum(c, "angleModifyPitch", 0f) * aoa * rpmRatio;
            float vol = volumeBase
                + rpmRatio * CompNum(c, "rpmModifyVolume", 0f)
                + CompNum(c, "angleModifyVolume", 0f) * aoa * rpmRatio;
            if (pitch < 0.05f) pitch = pitchBase > 0.05f ? pitchBase : 1f;
            DriveEngineSrc(c, src, ReadFit(_fCspExt, c) ?? FieldOn(c, "exteriorSound"), Mathf.Max(0f, vol), pitch);
        }

        void DriveRotorAudio(Component c, float ratio)
        {
            object? src = ReadFit(_fRotorSrc, c) ?? FieldOn(c, "rotorSource");
            if (ratio <= 0.02f) { StopEngineSrc(src); return; }
            float pitchF = CompNum(c, "pitch", 1f);
            float volF = CompNum(c, "volume", 1f);
            float condition = CompNum(c, "condition", 1f);
            if (condition <= 0.001f) condition = 1f;
            float speedRatio = ratio;
            float limit = CompNum(c, "angularSpeedLimit", 0f);
            float speed = CompNum(c, "angularSpeed", 0f);
            if (limit > 0.01f && speed > 0.01f)
                speedRatio = Mathf.Clamp(speed / limit, 0f, 1.5f);
            DriveEngineSrc(c, src, ReadFit(_fRotorExt, c) ?? FieldOn(c, "exteriorSound"),
                Mathf.Max(0f, speedRatio * volF * condition), Mathf.Max(0.05f, speedRatio * pitchF));
        }

        void DriveTurbofanAudio(Component c, float ratio)
        {
            object? src = null;
            if (_tfanType != null && _tfanType.IsInstanceOfType(c))
                src = ReadFit(_fTfanSrc, c);
            if (src == null) src = FieldOn(c, "turbineAudio");
            DriveSpoolAudio(c, src, ratio);
        }

        void DriveJetAudio(Component c, float ratio)
        {
            if (c is Behaviour jb) jb.enabled = false;
            var bind = JetRpmOf(c.GetType());
            float max = 10000f;
            if (bind.MaxRpm?.GetValue(c) is float mx && mx > 1f) max = mx;
            bind.Rpm?.SetValue(c, max * Mathf.Clamp01(ratio));
            object? src = bind.TurbineAudio != null ? bind.TurbineAudio.GetValue(c) : FieldOn(c, "turbineAudio");
            DriveSpoolAudio(c, src, ratio);
        }

        void DriveSpoolAudio(Component c, object? src, float ratio)
        {
            if (ratio <= 0.02f) { StopEngineSrc(src); return; }
            float maxPitch = CompNum(c, "turbineMaxPitch", 1f);
            if (maxPitch < 0.05f) maxPitch = 1f;
            float rpmRatio = Mathf.Clamp01(ratio);
            RememberSource(src);
            DriveEngineSrc(c, src, null, OrigVolume(src, 1f), rpmRatio * maxPitch);
        }

        void DriveTurbineEngineAudio(Component c, float ratio)
        {
            object? src = FieldOn(c, "turbineAudio");
            if (c is Behaviour eb) eb.enabled = false;
            if (ratio <= 0.02f) { StopEngineSrc(src); return; }
            float pitchF = CompNum(c, "pitch", 1f);
            float volF = CompNum(c, "volume", 1f);
            float rpmRatio = 1f;
            float powerRatio = ratio * ratio;
            float pitch = rpmRatio - powerRatio * pitchF + pitchF;
            float vol = (rpmRatio * 0.25f + powerRatio * 0.75f) * volF;
            if (pitch < 0.05f) pitch = 1f;
            DriveEngineSrc(c, src, null, Mathf.Max(0f, vol), pitch);
        }

        void DriveFanAudio(Component c, float ratio)
        {
            object? src = null;
            if (_fanType != null && _fanType.IsInstanceOfType(c))
                src = ReadFit(_fFanSrc, c);
            if (src == null) src = FieldOn(c, "fanSource");
            if (ratio <= 0.02f) { StopEngineSrc(src); return; }
            float rpmRatio = Mathf.Clamp01(ratio);
            float pitchF = CompNum(c, "pitch", 1f);
            if (pitchF < 0.05f) pitchF = 1f;
            float unthrottled = CompNum(c, "unthrottledVolume", 0.5f);
            float throttled = CompNum(c, "throttledVolume", 1f);
            float thrust = Mathf.Abs(CompNum(c, "currentThrust", 0f));
            float maxThrust = CompNum(c, "maxThrust", 0f);
            float thrustRatio = maxThrust > 1f && thrust > 0.01f ? Mathf.Clamp01(thrust / maxThrust) : rpmRatio;
            float vol = rpmRatio * (unthrottled + thrustRatio * throttled);
            DriveEngineSrc(c, src, FieldOn(c, "exteriorSound"), Mathf.Max(0f, vol), rpmRatio * pitchF);
        }

        void DrivePropFanAudio(Component c, float ratio)
        {
            object? src = null;
            if (_propType != null && _propType.IsInstanceOfType(c))
                src = ReadFit(_fPfanSrc, c);
            if (src == null) src = FieldOn(c, "source");
            if (ratio <= 0.02f) { StopEngineSrc(src); return; }
            float rpmRatio = Mathf.Clamp01(ratio);
            float vol = CompNum(c, "volumeBase", 0.2f)
                + CompNum(c, "rpmVolumePortion", 0.5f) * rpmRatio
                + CompNum(c, "aoaVolumePortion", 0.3f) * ratio * rpmRatio;
            float pitch = CompNum(c, "pitchMultiplier", 1f)
                * (CompNum(c, "rpmPitchPortion", 0.9f) * rpmRatio
                    + CompNum(c, "aoaPitchPortion", 0.1f) * ratio * rpmRatio);
            if (pitch < 0.05f) pitch = 1f;
            DriveEngineSrc(c, src, FieldOn(c, "exteriorSound"), Mathf.Max(0f, vol), pitch);
        }

        struct JetRpmBind
        {
            public FieldInfo? Rpm;
            public FieldInfo? MaxRpm;
            public FieldInfo? TurbineAudio;
        }
        static readonly Dictionary<Type, JetRpmBind> _jetRpm = new Dictionary<Type, JetRpmBind>();
        static readonly Dictionary<Type, FieldInfo?> _rotorHingeField = new Dictionary<Type, FieldInfo?>();
        static readonly Dictionary<Type, MethodInfo?> _hingeInit = new Dictionary<Type, MethodInfo?>();
        static readonly object[] _hingeInitArgs = { 1f, 0f };
        static readonly Dictionary<Type, MethodInfo?> _hazeUpdate = new Dictionary<Type, MethodInfo?>();
        static readonly Dictionary<Type, AbFields> _abFields = new Dictionary<Type, AbFields>();
        static readonly Dictionary<Type, MethodInfo?> _fbwByType = new Dictionary<Type, MethodInfo?>();
        static readonly Dictionary<Type, MethodInfo?> _hoverByType = new Dictionary<Type, MethodInfo?>();

        struct AbFields
        {
            public FieldInfo? Amount;
            public FieldInfo? Flame;
            public FieldInfo? Glow;
            public FieldInfo? Source;
        }

        static JetRpmBind JetRpmOf(Type t)
        {
            if (_jetRpm.TryGetValue(t, out var b)) return b;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            b = new JetRpmBind
            {
                Rpm = t.GetField("rpm", flags) ?? t.GetField("currentRPM", flags),
                MaxRpm = t.GetField("maxRPM", flags),
                TurbineAudio = t.GetField("turbineAudio", flags),
            };
            _jetRpm[t] = b;
            return b;
        }

        void DriveOneEngine(Component c, float ratio)
        {
            if (c == null) return;
            switch (c.GetType().Name)
            {
                case "RotorShaft":
                    DriveRotorAudio(c, ratio > 0.02f ? 1f : 0f);
                    break;
                case "ConstantSpeedProp":
                    DriveCspAudio(c, ratio);
                    break;
                case "Turbojet":
                    DriveJetAudio(c, ratio);
                    break;
                case "Turbofan":
                    DriveTurbofanAudio(c, ratio);
                    break;
                case "TurbineEngine":
                    DriveTurbineEngineAudio(c, ratio);
                    break;
                case "DuctedFan":
                    DriveFanAudio(c, ratio);
                    break;
                case "PropFan":
                    DrivePropFanAudio(c, ratio);
                    break;
            }
        }

        void DriveAircraftEngines(float ratio)
        {
            bool near = Plugin.AudioNear(gameObject, 550f);
            if (!near) ratio = 0f;
            if (!_engineListReady)
            {
                var found = new System.Collections.Generic.List<Component>(4);
                if (_acType == null) _acType = Plugin.FindGameType("Aircraft");
                Component? ac = _acType != null ? GetComponent(_acType) ?? GetComponentInChildren(_acType, true) : null;
                object? list = null;
                if (ac != null)
                {
                    if (_fAcEngines == null)
                        _fAcEngines = _acType!.GetField("engines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    list = _fAcEngines?.GetValue(ac);
                    if (list == null)
                        list = _acType!.GetField("engineStates", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ac);
                }
                if (list is System.Collections.IEnumerable en)
                {
                    foreach (var e in en)
                        if (e is Component c) found.Add(c);
                }
                if (found.Count == 0)
                {
                    foreach (var c in GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (c == null || c is ReplayGhost) continue;
                        string tn = c.GetType().Name;
                        if (tn == "Turbojet" || tn == "Turbofan" || tn == "TurbineEngine"
                            || tn == "RotorShaft" || tn == "ConstantSpeedProp"
                            || tn == "DuctedFan" || tn == "PropFan")
                            found.Add(c);
                    }
                }
                _engineList = found.ToArray();
                _engineListReady = true;
            }
            for (int i = 0; i < _engineList.Length; i++)
                DriveOneEngine(_engineList[i], ratio);
        }

        bool _gearStartChecked;
        bool _startedAir;
        int _gearLog;

        float ReadAgl()
        {
            if (!float.IsNaN(_spawnY))
                return Mathf.Max(0f, transform.position.y - _spawnY);
            return 0f;
        }

        void ApplyFx(ReplaySample s)
        {
            if (_neutral)
            {
                WriteThrottle(0f);
                NeutralizeSurfaces();
                CloseAirbrakes();
                DriveJetExhaust(false, 0f);
                DriveDownwashThrust(false, 0f);
                return;
            }
            Bind();
            float dt = Time.unscaledDeltaTime * Mathf.Max(Plugin.Clock.Speed, 0.01f);
            if (float.IsNaN(_spawnY)) _spawnY = s.Y;
            float agl = ReadAgl();
            float spd = _spdMs;
            if (s.HasTas && s.Tas > 0.05f)
            {
                float tas = s.Tas;
                if (tas > 15f && spd > 1f && tas > spd * 1.6f)
                    tas = tas / 1.94384f;
                spd = tas;
                _spdMs = tas;
            }
            if (spd > 0.5f && _velMs.sqrMagnitude < 0.01f)
                _velMs = transform.forward * spd;
            WriteUnitMotion(spd, _velMs);
            CloseAirbrakes();
            if (Time.unscaledTime >= _dmgNext)
            {
                _dmgNext = Time.unscaledTime + 1.5f;
                SuppressDamageFx();
            }
            bool airborne = agl > 8f;
            if (_air && !_gearStartChecked && Physics.Raycast(transform.position + Vector3.up * 4f, Vector3.down, out var ground, 1200f, ~0, QueryTriggerInteraction.Ignore))
            {
                _gearStartChecked = true;
                _startedAir = ground.distance > 2f;
                if (_startedAir) _fold = 1f;
            }
            if (s.HasGear)
            {
                _haveGearAcmi = true;
                _gearFromAcmi = s.Gear > 0.5f;
                if (_gearLog < 4)
                {
                    _gearLog++;
                    Plugin.Log.LogInfo("Gear ACMI " + gameObject.name + " val=" + s.Gear + " down=" + _gearFromAcmi);
                }
            }
            if (s.HasThrottle)
            {
                _haveThr = true;
                _thr = s.Throttle;
            }
            float thr = s.HasThrottle && s.Throttle >= 0f
                ? Mathf.Clamp01(s.Throttle)
                : (airborne ? 0.75f : 0.05f);
            bool gearDown = _haveGearAcmi ? _gearFromAcmi : !airborne;
            // Respect ACMI gear state – do not override with height check
            if (!_haveGearAcmi && _startedAir && gearDown && Physics.Raycast(transform.position + Vector3.up * 4f, Vector3.down, out var still, 1200f, ~0, QueryTriggerInteraction.Ignore) && still.distance > 2f)
                gearDown = false;
            if (_startedAir && !gearDown) _fold = 1f;
            ApplyReplayGear(gearDown);
            KeepIgnition();
            if (_shafts.Length > 0)
            {
                for (int i = 0; i < _shafts.Length; i++)
                {
                    var c = _shafts[i];
                    if (c == null) continue;
                    if (c is Behaviour sb) sb.enabled = false;
                    float nom = 300f;
                    if (ReadFit(_fNomRpm, c) is float f) nom = f;
                    WriteFit(_fStartup, c, 1f);
                    WriteFit(_fRotorUnfold, c, true);
                    // Unfold rotor hinges visually.
                    try
                    {
                        var shaftType = c.GetType();
                        if (!_rotorHingeField.TryGetValue(shaftType, out var hingeField))
                        {
                            hingeField = shaftType.GetField("rotorHinges", BindingFlags.Instance | BindingFlags.NonPublic);
                            _rotorHingeField[shaftType] = hingeField;
                        }
                        var hinges = hingeField?.GetValue(c) as System.Array;
                        if (hinges != null)
                        {
                            for (int h = 0; h < hinges.Length; h++)
                            {
                                var hinge = hinges.GetValue(h);
                                if (hinge == null) continue;
                                var ht = hinge.GetType();
                                if (!_hingeInit.TryGetValue(ht, out var init))
                                {
                                    init = ht.GetMethod("Initialize");
                                    _hingeInit[ht] = init;
                                }
                                _hingeInitArgs[0] = 1f;
                                _hingeInitArgs[1] = 0f;
                                init?.Invoke(hinge, _hingeInitArgs);
                            }
                        }
                    }
                    catch { }
                    // Diagnostic: dump Ibis renderers once.
                    if (gameObject.name.IndexOf("Ibis", StringComparison.OrdinalIgnoreCase) >= 0 && _ibisDump == 0)
                    {
                        _ibisDump = 1;
                        // Dump only this Ibis, not the whole scene.
                        string[] doorNames = { "door_FL", "door_FR", "slidingDoor_L", "slidingDoor_R", "windowCover_L", "windowCover_R" };
                        var ibisAll = gameObject.GetComponentsInChildren<Transform>(true);
                        for (int ii = 0; ii < ibisAll.Length; ii++)
                        {
                            var tt = ibisAll[ii];
                            if (tt == null) continue;
                            string nn = tt.gameObject.name;
                            bool match = false;
                            for (int k = 0; k < doorNames.Length; k++)
                                if (nn == doorNames[k]) { match = true; break; }
                            if (!match) continue;
                            var rends = tt.GetComponentsInChildren<Renderer>(true);
                            for (int ri = 0; ri < rends.Length; ri++)
                            {
                                var r = rends[ri];
                                if (r == null) continue;
                                Plugin.Log.LogInfo("Ibis-Door " + nn + "/" + r.gameObject.name + " en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy);
                            }
                        }
                    }
                    float rad = Plugin.Clock.Paused ? 0f : nom * 0.1047f;
                    WriteFit(_fAngSpeed, c, rad);
                    if (!Plugin.Clock.Paused)
                    {
                        var hub = ReadFit(_fRotorHub, c) as Transform;
                        float dir = 1f;
                        object? dv = ReadFit(_fRotorDir, c);
                        if (dv != null) dir = Convert.ToSingle(dv);
                        if (hub != null)
                            hub.Rotate(0f, nom * 6f * dir * dt, 0f, Space.Self);
                    }
                }
            }
            if (_props.Length > 0 && !Plugin.Clock.Paused)
            {
                for (int i = 0; i < _props.Length; i++)
                {
                    var c = _props[i];
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    float spin = Mathf.Lerp(0.4f, 1f, thr);
                    if (tn == "ConstantSpeedProp")
                    {
                        float limit = CompNum(c, "rpmLimit", 2000f);
                        if (limit < 10f) limit = 2000f;
                        float rpm = limit * spin;
                        WriteFit(_fCspRpm, c, rpm);
                        var hubGo = ReadFit(_fCspHub, c) as GameObject;
                        float dir = 1f;
                        object? dv = ReadFit(_fCspDir, c);
                        if (dv != null) dir = Convert.ToSingle(dv);
                        if (hubGo != null)
                            hubGo.transform.Rotate(0f, 0f, rpm * -6f * dir * dt, Space.Self);
                    }
                    else if (tn == "DuctedFan")
                    {
                        float maxRpm = 2200f;
                        if (ReadFit(_fFanMaxRpm, c) is float mr && mr > 10f) maxRpm = mr;
                        WriteFit(_fFanRpm, c, maxRpm * spin);
                    }
                    else if (tn == "Turbofan")
                    {
                        float max = 10000f;
                        if (ReadFit(_fTfanMax, c) is float mx && mx > 1f) max = mx;
                        WriteFit(_fTfanRpm, c, max * spin);
                    }
                    else
                    {
                        float nom = 2200f;
                        if (ReadFit(_fPropNom, c) is float n && n > 10f) nom = n;
                        WriteFit(_fPropRpm, c, nom * spin);
                        WriteFit(_fPropRatio, c, spin);
                    }
                }
            }
            if (_engs.Length > 0)
            {
                for (int i = 0; i < _engs.Length; i++)
                {
                    var c = _engs[i];
                    if (c == null) continue;
                    WriteFit(_fOperable, c, true);
                    WriteFit(_fHasFuel, c, true);
                    WriteFit(_fStarted, c, 1f);
                    WriteFit(_fThrottle, c, thr);
                    float rpm = 0f;
                    if (_engType != null && thr > 0.02f)
                    {
                        float max = 10000f;
                        if (ReadFit(_fEngMaxPublic, c) is float mx && mx > 1f) max = mx;
                        rpm = max;
                    }
                    WriteFit(_fRpm, c, rpm);
                }
            }
            if (_tfans.Length > 0)
            {
                for (int i = 0; i < _tfans.Length; i++)
                {
                    var c = _tfans[i];
                    if (c == null) continue;
                    if (c is Behaviour tb) tb.enabled = false;
                    float max = 10000f;
                    if (ReadFit(_fTfanMax, c) is float mx && mx > 1f) max = mx;
                    WriteFit(_fTfanRpm, c, max * Mathf.Lerp(0.35f, 1f, thr));
                }
            }
            DriveAircraftEngines(Plugin.Clock.Paused ? 0f : thr);
            WriteThrottle(thr);
            CloseAirbrakes();
            DriveJetExhaust(true, thr);
            DriveDownwashThrust(_visible && !_neutral, thr);
            if (_gears.Length > 0)
            {
                // Gear animation driven by game's MoveGear via Extending/Retracting states
                // Gear animation handled by game's MoveGear
                if (_gearLog < 8 && _gears.Length > 0)
                {
                    var c = _gears[0];
                    if (c != null)
                    {
                        var hinge = _fGearHinge?.GetValue(c) as Transform;
                        var strut = _fStrut?.GetValue(c) as Transform;
                        float fold = _fFold?.GetValue(c) is float f ? f : -1f;
                        float strutRot = _fStrutRot?.GetValue(c) is float sr ? sr : -999f;
                        string h = hinge != null ? hinge.localEulerAngles.ToString("F1") : "?";
                        string strutAng = strut != null ? strut.localEulerAngles.ToString("F1") : "?";
                        Plugin.Log.LogInfo("Gear " + gameObject.name + " fold=" + fold.ToString("F2") + " strutRot=" + strutRot.ToString("F1") + " hinge=" + h + " strut=" + strutAng);
                        _gearLog++;
                    }
                }
                ApplyLandingLights(gearDown);
            }
            else if (_air)
            {
                ApplyLandingLights(gearDown);
            }
            if (_ship) DriveShipAudio(spd);
        }

        static Type? _surfType;
        static Type? _inType;
        static MethodInfo? _mGetInputs;
        static MethodInfo? _mSetLocked;
        static FieldInfo? _fVisMesh;
        static FieldInfo? _fSplitUp;
        static FieldInfo? _fSplitLo;
        static FieldInfo? _fFlap;
        static FieldInfo? _fPitchRange;
        static bool _surfRefl;
        Quaternion[] _surfRest = Array.Empty<Quaternion>();
        Transform[] _surfMesh = Array.Empty<Transform>();
        bool _surfBound;


        void BindSurfaceRefl()
        {
            if (_surfRefl) return;
            _surfRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _surfType = Plugin.FindGameType("ControlSurface");
            var ac = Plugin.FindGameType("Aircraft");
            _mGetInputs = ac?.GetMethod("GetInputs", flags);
            _mSetLocked = _surfType?.GetMethod("SetLocked", flags);
            if (_surfType != null)
            {
                _fVisMesh = _surfType.GetField("visibleMesh", flags);
                _fSplitUp = _surfType.GetField("splitUpper", flags);
                _fSplitLo = _surfType.GetField("splitLower", flags);
                _fFlap = _surfType.GetField("flap", flags);
                _fPitchRange = _surfType.GetField("pitchRange", flags);
            }
        }


        void DisableFlightAssist()
        {
            if (!_air) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (!_assistTypesBound)
            {
                _assistTypesBound = true;
                string[] names =
                {
                    "ControlsFilter", "HeloControlsFilter", "RelaxedStabilityController",
                    "MagicTorqueController", "TiltWingController", "SwingWingController",
                    "CompoundHeloController", "PilotPlayerState"
                };
                _assistTypes = new Type?[names.Length];
                for (int i = 0; i < names.Length; i++)
                    _assistTypes[i] = Plugin.FindGameType(names[i]);
                if (_acType == null) _acType = Plugin.FindGameType("Aircraft");
                if (_acType != null)
                {
                    _mSetFlightAssist = _acType.GetMethod("SetFlightAssist", flags);
                    _mGetControlsFilter = _acType.GetMethod("GetControlsFilter", flags);
                }
            }
            var types = _assistTypes;
            if (types != null)
            {
                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null) continue;
                    var comps = GetComponentsInChildren(t, true);
                    for (int c = 0; c < comps.Length; c++)
                    {
                        if (comps[c] is Behaviour b) b.enabled = false;
                    }
                }
            }
            if (_acType == null) return;
            var ac = GetComponent(_acType) ?? GetComponentInChildren(_acType, true);
            if (ac == null) return;
            try { _mSetFlightAssist?.Invoke(ac, new object[] { false }); } catch { }
            try
            {
                var filter = _mGetControlsFilter?.Invoke(ac, null);
                if (filter == null) return;
                var ft = filter.GetType();
                if (!_fbwByType.TryGetValue(ft, out var setFbw))
                {
                    setFbw = ft.GetMethod("SetFlyByWireParameters", flags);
                    _fbwByType[ft] = setFbw;
                }
                if (!_hoverByType.TryGetValue(ft, out var setHover))
                {
                    setHover = ft.GetMethod("SetAutoHover", flags);
                    _hoverByType[ft] = setHover;
                }
                setFbw?.Invoke(filter, new object[] { false, Array.Empty<float>() });
                setHover?.Invoke(filter, new object[] { false });
            }
            catch { }
        }

        static Type? _gearStateEnum;
        static object? _gsLockedUp;
        static object? _gsLockedDown;
        static MethodInfo? _mSetGearState;

        void ApplyReplayGear(bool gearDown)
        {
            if (_gearDownSent == gearDown) return;
            _gearDownSent = gearDown;
            var act = Plugin.FindGameType("Aircraft");
            if (act == null) return;
            var ac = GetComponent(act) ?? GetComponentInChildren(act, true);
            if (ac == null) return;
            Plugin.AllowReplayGear = true;
            try { act.GetMethod("SetGear", new[] { typeof(bool) })?.Invoke(ac, new object[] { gearDown }); }
            catch { }
            finally { Plugin.AllowReplayGear = false; }
            try
            {
                if (_gearStateEnum == null)
                {
                    var lg = Plugin.FindGameType("LandingGear");
                    _gearStateEnum = lg?.GetNestedType("GearState");
                    if (_gearStateEnum != null)
                    {
                        _gsLockedUp = Enum.Parse(_gearStateEnum, "Retracting");
                        _gsLockedDown = Enum.Parse(_gearStateEnum, "Extending");
                        _mSetGearState = act.GetMethod("SetGear", new[] { _gearStateEnum });
                    }
                }
                if (_mSetGearState != null)
                    _mSetGearState.Invoke(ac, new[] { gearDown ? _gsLockedDown : _gsLockedUp });
            }
            catch { }
        }


        void HoldAirbrakes()
        {
            for (int i = 0; i < _brakeTr.Count; i++)
            {
                var t = _brakeTr[i];
                if (t != null) t.localRotation = _brakeRest[i];
            }
        }

        void RememberBrake(Transform? t, Quaternion rest)
        {
            if (t == null) return;
            _brakeTr.Add(t);
            _brakeRest.Add(rest);
        }

        void CloseAirbrakes()
        {
            if (!_air) return;
            if (_brakeClosed)
            {
                HoldAirbrakes();
                return;
            }
            _brakeClosed = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var act = Plugin.FindGameType("Aircraft");
            if (act != null)
            {
                var ac = GetComponent(act) ?? GetComponentInChildren(act, true);
                if (ac != null)
                {
                    try
                    {
                        var inputs = act.GetMethod("GetInputs", flags)?.Invoke(ac, null);
                        if (inputs != null)
                        {
                            var it = inputs.GetType();
                            it.GetField("brake")?.SetValue(inputs, 0f);
                        }
                    }
                    catch { }
                }
            }
            var abt = Plugin.FindGameType("Airbrake");
            if (abt != null)
                foreach (var c in GetComponentsInChildren(abt, true))
                {
                    if (c == null) continue;
                    try
                    {
                        var transforms = abt.GetField("transforms", flags)?.GetValue(c) as Array;
                        var bases = abt.GetField("baseAngles", flags)?.GetValue(c) as System.Collections.IList;
                        if (transforms != null)
                        {
                            for (int i = 0; i < transforms.Length; i++)
                            {
                                var tr = transforms.GetValue(i) as Transform;
                                if (tr == null) continue;
                                if (bases != null && i < bases.Count && bases[i] is Vector3 ang)
                                    tr.localEulerAngles = ang;
                                else
                                    continue;
                                RememberBrake(tr, tr.localRotation);
                            }
                        }
                        var cons = abt.GetField("constraints", flags)?.GetValue(c) as Array;
                        if (cons != null)
                        {
                            foreach (var o in cons)
                            {
                                if (o is Behaviour b) b.enabled = false;
                            }
                        }
                        abt.GetField("openAmount", flags)?.SetValue(c, 0f);
                        abt.GetField("active", flags)?.SetValue(c, false);
                        var snd = abt.GetField("airbrakeSound", flags)?.GetValue(c);
                        snd?.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(snd, null);
                    }
                    catch { }
                    try { UnityEngine.Object.Destroy(c); } catch { }
                }
            var cst = Plugin.FindGameType("ControlSurface");
            if (cst == null) return;
            var fMax = cst.GetField("maxSplit", flags);
            var fUp = cst.GetField("splitUpper", flags);
            var fLo = cst.GetField("splitLower", flags);
            var fMesh = cst.GetField("visibleMesh", flags);
            var fJobs = cst.GetField("JobFields", flags);
            foreach (var c in GetComponentsInChildren(cst, true))
            {
                if (c == null) continue;
                try
                {
                    object mv = fMax?.GetValue(c);
                    float max = mv is float f ? f : 0f;
                    if (max <= 0f) continue;
                    var up = fUp?.GetValue(c) as Transform;
                    var lo = fLo?.GetValue(c) as Transform;
                    Quaternion restSplit = Quaternion.identity;
                    Quaternion restMain = Quaternion.identity;
                    bool have = false;
                    object alloc = fJobs?.GetValue(c);
                    if (alloc != null)
                    {
                        var vt = alloc.GetType();
                        object created = vt.GetProperty("IsCreated")?.GetValue(alloc);
                        if (created is bool ok && ok)
                        {
                            object fields = vt.GetMethod("Value")?.Invoke(alloc, null);
                            if (fields != null)
                            {
                                var ft = fields.GetType();
                                object rs = ft.GetField("restingSplitRotation")?.GetValue(fields);
                                object rm = ft.GetField("restingRotation")?.GetValue(fields);
                                if (rs is Quaternion qs) { restSplit = qs; have = true; }
                                if (rm is Quaternion qm) restMain = qm;
                            }
                        }
                    }
                    if (have)
                    {
                        if (up != null)
                        {
                            up.localRotation = restSplit;
                            RememberBrake(up, restSplit);
                        }
                        if (lo != null)
                        {
                            lo.localRotation = restSplit;
                            RememberBrake(lo, restSplit);
                        }
                        var mesh = fMesh?.GetValue(c) as GameObject;
                        if (mesh != null)
                        {
                            mesh.transform.localRotation = restMain;
                            RememberBrake(mesh.transform, restMain);
                        }
                    }
                    cst.GetField("splitAmount", flags)?.SetValue(c, 0f);
                    if (c is Behaviour cb) cb.enabled = false;
                }
                catch { }
            }
        }

        void SnapSurfacesNow()
        {
            CloseAirbrakes();
        }

        void FreezeNeutral()
        {
            SnapSurfacesNow();
            CloseAirbrakes();
            NeutralizeSurfaces();
            HideJetVisuals();
            KillJetFlames();
            DriveDownwashThrust(false, 0f);
            Invoke(nameof(KillJetFlames), 0.5f);
            Invoke(nameof(KillJetFlames), 2f);
            MuteAllSources();
            _shafts = Array.Empty<Component>();
            _props = Array.Empty<Component>();
            _engs = Array.Empty<Component>();
            _tfans = Array.Empty<Component>();
            _gears = Array.Empty<Component>();
            _downwashes = Array.Empty<Component>();
            _downwashEffects = Array.Empty<Component>();
            _dwTargets = Array.Empty<DownwashThrustTarget>();
        }

        void KillJetFlames()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var typeName in new[] { "Turbojet", "Turbofan", "JetNozzle" })
            {
                var t = Plugin.FindGameType(typeName);
                if (t == null) continue;
                foreach (var c in GetComponentsInChildren(t, true))
                {
                    if (c == null) continue;
                    if (c is Behaviour b) b.enabled = false;
                }
            }
            var jn = Plugin.FindGameType("JetNozzle");
            if (jn != null)
            {
                foreach (var c in GetComponentsInChildren(jn, true))
                {
                    if (c == null) continue;
                    try
                    {
                        var abs = jn.GetField("afterburners", flags)?.GetValue(c) as Array;
                        if (abs != null)
                        {
                            foreach (var ab in abs)
                            {
                                if (ab == null) continue;
                                var at = ab.GetType();
                                at.GetField("afterburnerAmount", flags)?.SetValue(ab, 0f);
                                var flame = at.GetField("flameRenderer", flags)?.GetValue(ab) as Renderer;
                                if (flame != null)
                                {
                                    flame.enabled = false;
                                    try { UnityEngine.Object.Destroy(flame); } catch { }
                                }
                                var glowR = at.GetField("nozzleGlowRenderer", flags)?.GetValue(ab) as Renderer;
                                if (glowR != null)
                                {
                                    glowR.enabled = false;
                                    try { UnityEngine.Object.Destroy(glowR); } catch { }
                                }
                            }
                        }
                        var haze = jn.GetField("heatHaze", flags)?.GetValue(c);
                        var sys = haze != null ? haze.GetType().GetField("system", flags)?.GetValue(haze) : null;
                        if (sys is Component ps)
                        {
                            try { ps.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(ps, null); } catch { }
                            if (ps is Behaviour pb) pb.enabled = false;
                        }
                    }
                    catch { }
                }
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string n = r.gameObject.name ?? "";
                if (n.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("afterburn", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("ab_flame", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    r.enabled = false;
                    try { UnityEngine.Object.Destroy(r); } catch { }
                }
            }
        }

        void HideJetVisuals()
        {
            DriveJetExhaust(false, 0f);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string n = r.gameObject.name ?? "";
                if (n.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("afterburn", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("ab_flame", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("exhaust", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("haze", StringComparison.OrdinalIgnoreCase) >= 0)
                    r.enabled = false;
            }
        }

        void KeepIgnition()
        {
            if (!_ignitionBound)
            {
                var behs = GetComponents<MonoBehaviour>();
                var found = new List<Component>(1);
                for (int i = 0; i < behs.Length; i++)
                {
                    var beh = behs[i];
                    if (beh == null || beh.GetType().Name != "Aircraft") continue;
                    found.Add(beh);
                }
                if (found.Count == 0) return;
                if (_fIgnition == null)
                    _fIgnition = found[0].GetType().GetField("Ignition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _ignitionTargets = found.ToArray();
                _ignitionBound = true;
            }
            for (int i = 0; i < _ignitionTargets.Length; i++)
            {
                var beh = _ignitionTargets[i];
                if (beh != null)
                    _fIgnition?.SetValue(beh, true);
            }
        }

        void WriteThrottle(float throttle)
        {
            if (_throttleGiveUp) return;
            if (!_throttleBound || _throttleAc == null)
            {
                Bind();
                if (_acType == null)
                {
                    _throttleGiveUp = true;
                    return;
                }
                _throttleAc = GetComponent(_acType) ?? GetComponentInChildren(_acType, true);
                if (_throttleAc == null) return;
                if (_mGetInputs == null && !_getInputsLooked)
                {
                    _getInputsLooked = true;
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _mGetInputs = _acType.GetMethod("GetInputs", flags);
                }
                if (_mGetInputs == null)
                {
                    _throttleGiveUp = true;
                    return;
                }
                _throttleBound = true;
            }
            try
            {
                var inputs = _mGetInputs?.Invoke(_throttleAc, null);
                if (inputs == null) return;
                if (!_inputFieldsLooked)
                {
                    _inputFieldsLooked = true;
                    var it = inputs.GetType();
                    _fInThrottle = it.GetField("throttle");
                    _fInBrake = it.GetField("brake");
                }
                _fInThrottle?.SetValue(inputs, Mathf.Clamp01(throttle));
                _fInBrake?.SetValue(inputs, 0f);
            }
            catch { }
        }

        void BindNozzles()
        {
            Bind();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (_nozzleType == null)
                _nozzleType = Plugin.FindGameType("JetNozzle");
            if (_nozzleType == null)
            {
                _nozzles = Array.Empty<Component>();
                _nozzlesBound = true;
                return;
            }
            if (_fHeatHaze == null)
                _fHeatHaze = _nozzleType.GetField("heatHaze", flags);
            if (_fAfterburners == null)
                _fAfterburners = _nozzleType.GetField("afterburners", flags);
            if (_mThrust == null)
            {
                try { _mThrust = _nozzleType.GetMethod("Thrust", flags); }
                catch { }
            }
            _nozzles = CollectFx(_nozzleType);
            _nozzlesBound = true;
        }

        void DriveJetExhaust(bool on, float throttle)
        {
            if (!_nozzlesBound) BindNozzles();
            if (_nozzleType == null || _nozzles.Length == 0) return;
            throttle = on ? Mathf.Clamp01(throttle) : 0f;
            float rpmRatio = on ? Mathf.Lerp(0.25f, 1f, throttle) : 0f;
            float thrustRatio = throttle;
            bool allowAb = on && throttle > 0.92f;
            float abAmt = allowAb ? Mathf.Clamp01((throttle - 0.92f) / 0.08f) : 0f;
            bool hear = on && !Plugin.Clock.Paused && Plugin.AudioNear(gameObject, 550f);
            var abFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < _nozzles.Length; i++)
            {
                var c = _nozzles[i];
                if (c == null) continue;
                try
                {
                    _thrustArgs[0] = 0f;
                    _thrustArgs[1] = rpmRatio;
                    _thrustArgs[2] = thrustRatio;
                    _thrustArgs[3] = throttle;
                    _thrustArgs[4] = allowAb;
                    _mThrust?.Invoke(c, _thrustArgs);
                    var haze = _fHeatHaze?.GetValue(c);
                    if (haze != null)
                    {
                        var ht = haze.GetType();
                        if (!_hazeUpdate.TryGetValue(ht, out var upd))
                        {
                            upd = ht.GetMethod("UpdateParticles");
                            _hazeUpdate[ht] = upd;
                        }
                        _particleArgs[0] = thrustRatio;
                        _particleArgs[1] = rpmRatio;
                        _particleArgs[2] = _spdMs;
                        upd?.Invoke(haze, _particleArgs);
                    }
                    var abs = _fAfterburners?.GetValue(c) as Array;
                    if (abs != null)
                    {
                        foreach (var ab in abs)
                        {
                            if (ab == null) continue;
                            var at = ab.GetType();
                            if (!_abFields.TryGetValue(at, out var af))
                            {
                                af = new AbFields
                                {
                                    Amount = at.GetField("afterburnerAmount", abFlags),
                                    Flame = at.GetField("flameRenderer", abFlags),
                                    Glow = at.GetField("nozzleGlowRenderer", abFlags),
                                    Source = at.GetField("source", abFlags),
                                };
                                _abFields[at] = af;
                            }
                            af.Amount?.SetValue(ab, abAmt);
                            var flame = af.Flame?.GetValue(ab) as Renderer;
                            if (flame != null)
                            {
                                flame.enabled = abAmt > 0.01f;
                                if (flame.material != null)
                                {
                                    flame.material.SetFloat("_Brightness", abAmt);
                                    flame.material.SetFloat("_Temperature", abAmt);
                                }
                            }
                            var glowR = af.Glow?.GetValue(ab) as Renderer;
                            if (glowR != null) glowR.enabled = abAmt > 0.01f;
                            object? abSrc = af.Source?.GetValue(ab);
                            if (hear && abAmt > 0.01f)
                            {
                                RememberSource(abSrc);
                                float dir = NozzleDirection(c);
                                DriveEngineSrc(c, abSrc, null, abAmt * dir, OrigPitch(abSrc, 1f));
                            }
                            else
                                StopEngineSrc(abSrc);
                        }
                    }
                    object? thrustSrc = FieldOn(c, "thrustAudio");
                    if (hear && thrustRatio > 0.02f)
                    {
                        RememberSource(thrustSrc);
                        float maxVol = CompNum(c, "thrustMaxVolume", 1f);
                        DriveEngineSrc(c, thrustSrc, null, thrustRatio * maxVol * NozzleDirection(c), OrigPitch(thrustSrc, 1f));
                    }
                    else
                        StopEngineSrc(thrustSrc);
                }
                catch { }
            }
        }

        float NozzleDirection(Component nozzle)
        {
            var cam = Camera.main;
            if (cam == null || nozzle == null) return 1f;
            Vector3 toNozzle = nozzle.transform.position - cam.transform.position;
            if (toNozzle.sqrMagnitude < 0.01f) return 1f;
            float facing = Vector3.Dot(toNozzle.normalized, nozzle.transform.forward);
            return Mathf.Lerp(0.5f, 2f, facing);
        }


        static FieldInfo? _fUnitSpeed;
        static FieldInfo? _fUnitRb;

        static FieldInfo? FindInstField(Type? t, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            while (t != null && t != typeof(MonoBehaviour) && t != typeof(object))
            {
                var f = t.GetField(name, flags);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }


        void NotifyAircraftGear(bool gearDown)
        {
            if (_gearDownSent.HasValue && _gearDownSent.Value == gearDown) return;
            _gearDownSent = gearDown;
            if (_aircraftType == null)
                _aircraftType = Plugin.FindGameType("Aircraft");
            if (_aircraftType == null) return;
            var ac = GetComponentInChildren(_aircraftType);
            if (ac == null) ac = GetComponent(_aircraftType);
            if (ac == null) return;
            if (_mSetGearBool == null)
                _mSetGearBool = _aircraftType.GetMethod("SetGear", new[] { typeof(bool) });
            try { _mSetGearBool?.Invoke(ac, new object[] { gearDown }); }
            catch { }
        }

        static bool IsExteriorLightName(string n)
        {
            return n.IndexOf("nav", StringComparison.Ordinal) >= 0
                || n.IndexOf("strobe", StringComparison.Ordinal) >= 0
                || n.IndexOf("beacon", StringComparison.Ordinal) >= 0
                || n.IndexOf("position", StringComparison.Ordinal) >= 0
                || n.IndexOf("poslight", StringComparison.Ordinal) >= 0
                || n.IndexOf("formation", StringComparison.Ordinal) >= 0
                || n.IndexOf("wingtip", StringComparison.Ordinal) >= 0
                || n.IndexOf("wing_light", StringComparison.Ordinal) >= 0
                || n.IndexOf("anticol", StringComparison.Ordinal) >= 0
                || n.IndexOf("anti-col", StringComparison.Ordinal) >= 0
                || n.IndexOf("collision", StringComparison.Ordinal) >= 0
                || n.IndexOf("exterior", StringComparison.Ordinal) >= 0
                || n.IndexOf("land", StringComparison.Ordinal) >= 0
                || n.IndexOf("taxi", StringComparison.Ordinal) >= 0
                || n.IndexOf("spot", StringComparison.Ordinal) >= 0
                || n.IndexOf("tail", StringComparison.Ordinal) >= 0
                || n.IndexOf("aft", StringComparison.Ordinal) >= 0
                || n.IndexOf("rear", StringComparison.Ordinal) >= 0
                || n.IndexOf("heck", StringComparison.Ordinal) >= 0
                || n.IndexOf("lamp", StringComparison.Ordinal) >= 0
                || n.IndexOf("bulb", StringComparison.Ordinal) >= 0
                || n.IndexOf("glow", StringComparison.Ordinal) >= 0
                || (n.IndexOf("rotor", StringComparison.Ordinal) >= 0
                    && n.IndexOf("light", StringComparison.Ordinal) >= 0);
        }

        void ApplyLandingLights(bool gearDown)
        {
            if (!_landLightsBound)
            {
                _landLightsBound = true;
                var lights = new System.Collections.Generic.List<Light>();
                var rends = new System.Collections.Generic.List<Renderer>();
                var names = new System.Text.StringBuilder();
                foreach (var l in GetComponentsInChildren<Light>(true))
                {
                    if (l == null) continue;
                    string n = (l.name + " " + l.gameObject.name).ToLowerInvariant();
                    if (n.IndexOf("cockpit", StringComparison.Ordinal) >= 0
                        || n.IndexOf("cabin", StringComparison.Ordinal) >= 0
                        || n.IndexOf("internal", StringComparison.Ordinal) >= 0)
                        continue;
                    lights.Add(l);
                    if (names.Length < 180) names.Append("L:").Append(l.name).Append(' ');
                }
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var tr = r.transform;
                    string n = (r.name + " " + r.gameObject.name).ToLowerInvariant();
                    if (tr.parent != null) n += " " + tr.parent.name.ToLowerInvariant();
                    if (tr.parent != null && tr.parent.parent != null)
                        n += " " + tr.parent.parent.name.ToLowerInvariant();
                    if (!IsExteriorLightName(n)) continue;
                    if (n.IndexOf("gear", StringComparison.Ordinal) >= 0
                        && n.IndexOf("light", StringComparison.Ordinal) < 0)
                        continue;
                    rends.Add(r);
                    if (names.Length < 240) names.Append("R:").Append(r.name).Append(' ');
                }
                _landLights = lights.ToArray();
                _navRends = rends.ToArray();
                if (_air)
                    Plugin.Log.LogInfo("Lights " + gameObject.name
                        + " L=" + lights.Count + " R=" + rends.Count + " " + names);
            }
            for (int i = 0; i < _landLights.Length; i++)
                if (_landLights[i] != null) _landLights[i].enabled = gearDown;
            ApplyGameNavLights(gearDown);
        }

        static FieldInfo? _fNavArr;
        static FieldInfo? _fNavRend;
        static FieldInfo? _fNavLit;
        static FieldInfo? _fNavBase;
        static FieldInfo? _fNavObjs;
        static MethodInfo? _mNavToggle;
        static bool _navRefl;

        void ApplyGameNavLights(bool on)
        {
            var t = Plugin.FindGameType("NavLights");
            if (t == null) return;
            if (!_navRefl)
            {
                _navRefl = true;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _fNavArr = t.GetField("navLights", flags);
                var inner = t.GetNestedType("NavLight", flags);
                if (inner != null)
                {
                    _fNavRend = inner.GetField("renderer", flags);
                    _fNavLit = inner.GetField("litMaterial", flags);
                    _fNavBase = inner.GetField("baseMaterial", flags);
                    _fNavObjs = inner.GetField("objects", flags);
                    _mNavToggle = inner.GetMethod("Toggle", flags, null, new[] { typeof(bool) }, null);
                }
            }
            foreach (var c in GetComponentsInChildren(t, true))
            {
                if (c == null) continue;
                var beh = c as Behaviour;
                if (beh != null) beh.enabled = true;
                var arr = _fNavArr?.GetValue(c) as Array;
                if (arr == null) continue;
                for (int i = 0; i < arr.Length; i++)
                {
                    var item = arr.GetValue(i);
                    if (item == null) continue;
                    try { _mNavToggle?.Invoke(item, new object[] { on }); }
                    catch { }
                    var rend = _fNavRend?.GetValue(item) as Renderer;
                    if (rend != null)
                    {
                        var mat = on
                            ? _fNavLit?.GetValue(item) as Material
                            : _fNavBase?.GetValue(item) as Material;
                        if (mat != null) rend.sharedMaterial = mat;
                        SetParticlesOn(rend.gameObject, on);
                    }
                    var objs = _fNavObjs?.GetValue(item) as GameObject[];
                    if (objs == null) continue;
                    for (int j = 0; j < objs.Length; j++)
                    {
                        if (objs[j] == null) continue;
                        objs[j].SetActive(true);
                        SetParticlesOn(objs[j], on);
                    }
                }
            }
        }

        static void SetParticlesOn(GameObject go, bool on)
        {
            var pt = ParticleType();
            if (pt == null || go == null) return;
            var play = pt.GetMethod("Play", new[] { typeof(bool) }) ?? pt.GetMethod("Play", Type.EmptyTypes);
            var emProp = pt.GetProperty("emission");
            var emBool = pt.GetProperty("enableEmission");
            foreach (var c in go.GetComponentsInChildren(pt, true))
            {
                if (c == null) continue;
                try
                {
                    emBool?.SetValue(c, on);
                    if (emProp != null)
                    {
                        var em = emProp.GetValue(c);
                        if (em != null)
                        {
                            var en = em.GetType().GetProperty("enabled");
                            en?.SetValue(em, on);
                        }
                    }
                    if (on)
                    {
                        if (play != null && play.GetParameters().Length == 1)
                            play.Invoke(c, new object[] { true });
                        else
                            play?.Invoke(c, null);
                    }
                }
                catch { }
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true))
                if (l != null) l.enabled = on;
        }

        static int _fxDumpLeft = 4;
        int _ibisDump;
    }
}