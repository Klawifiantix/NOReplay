using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static Type? _shipType;
        static Type? _shipPropType;
        static FieldInfo? _fShipWakeParticles;
        static MethodInfo? _mShipUpdateParticles;
        static MethodInfo? _mShipGetInputs;
        static FieldInfo? _fShipInThrottle;
        static FieldInfo? _fWakeSystem;
        static MethodInfo? _mWakeInit;
        static MethodInfo? _mWakeUpdate;
        static MethodInfo? _mWakeStop;
        static FieldInfo? _fShipPropParticles;
        static FieldInfo? _fShipPropRotators;
        static MethodInfo? _mShipRotAnimate;
        static FieldInfo? _fShipRotTransform;
        static FieldInfo? _fShipRotSpeed;
        static bool _shipReflBound;

        Component[] _shipWakeSystems = Array.Empty<Component>();
        Component[] _shipPropSystems = Array.Empty<Component>();
        bool _shipFxBound;
        bool _shipWakeReinit;
        bool _shipFxPaused;

        static void BindShipRefl()
        {
            if (_shipReflBound) return;
            _shipReflBound = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _shipType = Plugin.FindGameType("Ship");
            _shipPropType = Plugin.FindGameType("ShipPropulsion");
            if (_shipType != null)
            {
                _fShipWakeParticles = _shipType.GetField("wakeParticles", flags);
                _mShipUpdateParticles = _shipType.GetMethod("UpdateParticles", flags);
                _mShipGetInputs = _shipType.GetMethod("GetInputs", flags);
                var wakeType = _shipType.GetNestedType("WakeParticles", flags);
                if (wakeType != null)
                {
                    _fWakeSystem = wakeType.GetField("system", flags);
                    _mWakeInit = wakeType.GetMethod("Initialize", flags);
                    _mWakeUpdate = wakeType.GetMethod("Update", flags, null, new[] { typeof(float), typeof(Vector3) }, null);
                    _mWakeStop = wakeType.GetMethod("Stop", flags, null, Type.EmptyTypes, null);
                }
            }
            var shipInputsType = Plugin.FindGameType("ShipInputs");
            if (shipInputsType != null)
                _fShipInThrottle = shipInputsType.GetField("throttle", flags);
            if (_shipPropType != null)
            {
                _fShipPropParticles = _shipPropType.GetField("particles", flags);
                _fShipPropRotators = _shipPropType.GetField("rotators", flags);
                var rotType = _shipPropType.GetNestedType("Rotator", flags);
                if (rotType != null)
                {
                    _mShipRotAnimate = rotType.GetMethod("Animate", flags, null, new[] { typeof(float) }, null);
                    _fShipRotTransform = rotType.GetField("transform", flags);
                    _fShipRotSpeed = rotType.GetField("speed", flags);
                }
            }
        }

        void CacheShipFx()
        {
            BindShipRefl();
            if (_shipPropType != null)
            {
                var raw = GetComponentsInChildren(_shipPropType, true);
                var list = new List<Component>(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                {
                    if (raw[i] is Component c)
                    {
                        if (c is Behaviour b) b.enabled = false;
                        list.Add(c);
                    }
                }
                _shipProps = list.ToArray();
            }
            if (_shipType != null)
            {
                _shipUnit = GetComponent(_shipType)
                         ?? GetComponentInChildren(_shipType, true)
                         ?? GetComponentInParent(_shipType);
                if (_shipUnit is Behaviour sb) sb.enabled = false;
            }

            var wakeList = new List<Component>(8);
            if (_shipUnit != null && _fShipWakeParticles?.GetValue(_shipUnit) is Array wakeArr)
            {
                for (int i = 0; i < wakeArr.Length; i++)
                {
                    var elem = wakeArr.GetValue(i);
                    if (elem == null) continue;
                    try
                    {
                        if (_mWakeInit != null)
                        {
                            _mWakeInit.Invoke(elem, new object[] { _shipUnit });
                            wakeArr.SetValue(elem, i);
                        }
                    }
                    catch { }
                    if (_fWakeSystem?.GetValue(elem) is Component ps && ps != null)
                    {
                        EnableParticleEmission(ps);
                        wakeList.Add(ps);
                    }
                }
            }
            _shipWakeSystems = wakeList.ToArray();

            var propPsList = new List<Component>(8);
            for (int i = 0; i < _shipProps.Length; i++)
            {
                var prop = _shipProps[i];
                if (prop == null) continue;
                if (_fShipPropParticles?.GetValue(prop) is Array pArr)
                {
                    for (int j = 0; j < pArr.Length; j++)
                    {
                        if (pArr.GetValue(j) is Component ps && ps != null)
                        {
                            EnableParticleEmission(ps);
                            propPsList.Add(ps);
                        }
                    }
                }
            }
            _shipPropSystems = propPsList.ToArray();
            _shipFxBound = true;
        }

        void ReinitShipWakeIfNeeded()
        {
            if (_shipWakeReinit || _shipUnit == null) return;
            _shipWakeReinit = true;
            if (_fShipWakeParticles?.GetValue(_shipUnit) is Array wakeArr && _mWakeInit != null)
            {
                for (int i = 0; i < wakeArr.Length; i++)
                {
                    var elem = wakeArr.GetValue(i);
                    if (elem == null) continue;
                    try
                    {
                        _mWakeInit.Invoke(elem, new object[] { _shipUnit });
                        wakeArr.SetValue(elem, i);
                    }
                    catch { }
                    if (_fWakeSystem?.GetValue(elem) is Component ps && ps != null)
                        EnableParticleEmission(ps);
                }
            }
        }

        void DriveShipFx(float spd, Vector3 vel)
        {
            if (!_shipFxBound || (_shipUnit == null && _shipProps.Length == 0))
                CacheShipFx();
            if (!_visible || _neutral || !IsTrackAlive())
            {
                StopShipFx(false);
                return;
            }
            ReinitShipWakeIfNeeded();
            if (Plugin.Clock.Paused)
            {
                if (!_shipFxPaused)
                {
                    _shipFxPaused = true;
                    PauseParticleSystems(_shipWakeSystems);
                    PauseParticleSystems(_shipPropSystems);
                }
                return;
            }
            if (_shipFxPaused)
            {
                _shipFxPaused = false;
                if (spd > 0.1f)
                {
                    for (int i = 0; i < _shipWakeSystems.Length; i++)
                        PlayParticleSystem(_shipWakeSystems[i]);
                }
            }

            WriteUnitMotion(spd, vel);
            float throttle = spd > 0.15f ? Mathf.Clamp(spd / 12f, 0.15f, 1f) : 0f;

            if (_shipUnit != null)
            {
                try
                {
                    if (_mShipGetInputs != null && _fShipInThrottle != null)
                    {
                        var inputs = _mShipGetInputs.Invoke(_shipUnit, null);
                        if (inputs != null) _fShipInThrottle.SetValue(inputs, throttle);
                    }
                }
                catch { }

                for (int i = 0; i < _shipWakeSystems.Length; i++)
                    EnableParticleEmission(_shipWakeSystems[i]);

                bool updated = false;
                if (_mShipUpdateParticles != null)
                {
                    try
                    {
                        _mShipUpdateParticles.Invoke(_shipUnit, null);
                        updated = true;
                    }
                    catch { }
                }
                if (!updated && _fShipWakeParticles?.GetValue(_shipUnit) is Array wakeArr && _mWakeUpdate != null)
                {
                    object[] args = { spd, vel };
                    for (int i = 0; i < wakeArr.Length; i++)
                    {
                        var elem = wakeArr.GetValue(i);
                        if (elem == null) continue;
                        try
                        {
                            _mWakeUpdate.Invoke(elem, args);
                            wakeArr.SetValue(elem, i);
                        }
                        catch { }
                    }
                }
            }

            for (int i = 0; i < _shipProps.Length; i++)
            {
                var prop = _shipProps[i];
                if (prop == null) continue;
                if (throttle > 0.01f && _fShipPropRotators?.GetValue(prop) is Array rotArr)
                {
                    object[] rotArgs = { throttle };
                    for (int r = 0; r < rotArr.Length; r++)
                    {
                        var rot = rotArr.GetValue(r);
                        if (rot == null) continue;
                        try
                        {
                            if (_mShipRotAnimate != null)
                                _mShipRotAnimate.Invoke(rot, rotArgs);
                            else if (_fShipRotTransform?.GetValue(rot) is Transform rt && rt != null)
                            {
                                float rSpd = _fShipRotSpeed?.GetValue(rot) is float s ? s : 360f;
                                rt.Rotate(Vector3.forward * (throttle * rSpd * Time.deltaTime), Space.Self);
                            }
                        }
                        catch { }
                    }
                }
            }

            bool playPropPs = throttle > 0.1f;
            for (int i = 0; i < _shipPropSystems.Length; i++)
            {
                var ps = _shipPropSystems[i];
                if (ps == null) continue;
                EnableParticleEmission(ps);
                if (playPropPs) PlayParticleSystem(ps);
                else StopParticleSystem(ps, false);
            }
        }

        void StopShipFx(bool clear)
        {
            _shipFxPaused = false;
            if (_shipUnit != null && _fShipWakeParticles?.GetValue(_shipUnit) is Array wakeArr && _mWakeStop != null)
            {
                for (int i = 0; i < wakeArr.Length; i++)
                {
                    var elem = wakeArr.GetValue(i);
                    if (elem == null) continue;
                    try
                    {
                        _mWakeStop.Invoke(elem, null);
                        wakeArr.SetValue(elem, i);
                    }
                    catch { }
                }
            }
            for (int i = 0; i < _shipWakeSystems.Length; i++)
                StopParticleSystem(_shipWakeSystems[i], clear);
            for (int i = 0; i < _shipPropSystems.Length; i++)
                StopParticleSystem(_shipPropSystems[i], clear);
        }

        object? _shipAudio;
        float _vehIdlePitch = 1f;
        float _vehMinPitch = 0.5f;
        float _vehMaxPitch = 1f;
        float _vehPitchMult = 5f;
        float _vehTopSpeed = 1f;
        float _shipMaxVol = 0.8f;
        float _shipPitch = 1f;
        bool _groundAudioBound;

        bool WantsFlatAudio()
        {
            return Plugin.IsFollowed(gameObject) && !Plugin.CameraIsTvOrFree();
        }

        static float SourcePitch(object? src, float fallback)
        {
            if (src == null) return fallback;
            RememberSource(src);
            return OrigPitch(src, fallback);
        }

        static void PlayLoop(object? src, float volume, float pitch, bool flat = false)
        {
            if (src == null) return;
            if (volume <= 0.01f)
            {
                StopLoop(src);
                return;
            }
            RememberSource(src);
            var st = src.GetType();
            st.GetProperty("loop")?.SetValue(src, true);
            st.GetProperty("mute")?.SetValue(src, false);
            st.GetProperty("enabled")?.SetValue(src, true);
            try { st.GetProperty("ignoreListenerPause")?.SetValue(src, true); } catch { }
            st.GetProperty("spatialBlend")?.SetValue(src, flat ? 0f : 1f);
            st.GetProperty("pitch")?.SetValue(src, pitch);
            st.GetProperty("volume")?.SetValue(src, volume);
            object? playing = st.GetProperty("isPlaying")?.GetValue(src);
            if (!(playing is bool on && on))
                st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
        }

        static void StopLoop(object? src)
        {
            if (src == null) return;
            try
            {
                var st = src.GetType();
                st.GetProperty("volume")?.SetValue(src, 0f);
                st.GetMethod("Stop", Type.EmptyTypes)?.Invoke(src, null);
            }
            catch { }
        }

        void CacheVehicleAudio()
        {
            BindGroundAudio();
        }

        void BindGroundAudio()
        {
            if (_groundAudioBound) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] idleNames = { "engineIdleSound", "idleSound", "engineSound" };
            string[] driveNames = { "engineDriveSound", "driveSound", "moveSound", "trackSound", "rumbleSound" };
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null) continue;
                string tn = beh.GetType().Name;
                var t = beh.GetType();
                if (tn == "GroundVehicle" || tn == "Vehicle")
                {
                    if (_vehIdle == null)
                    {
                        for (int i = 0; i < idleNames.Length; i++)
                        {
                            var v = t.GetField(idleNames[i], flags)?.GetValue(beh);
                            if (v != null) { _vehIdle = v; break; }
                        }
                    }
                    if (_vehDrive == null)
                    {
                        for (int i = 0; i < driveNames.Length; i++)
                        {
                            var v = t.GetField(driveNames[i], flags)?.GetValue(beh);
                            if (v != null) { _vehDrive = v; break; }
                        }
                    }
                    _vehMinPitch = NumOr(t.GetField("engineMinPitch", flags)?.GetValue(beh), _vehMinPitch);
                    _vehMaxPitch = NumOr(t.GetField("engineMaxPitch", flags)?.GetValue(beh), _vehMaxPitch);
                    _vehPitchMult = NumOr(t.GetField("enginePitchMult", flags)?.GetValue(beh), _vehPitchMult);
                    float top = NumOr(t.GetField("topSpeedOnroad", flags)?.GetValue(beh), 0f);
                    if (top > 0.5f) _vehTopSpeed = top;
                }
                if (tn == "Ship" || tn == "ShipPropulsion")
                {
                    if (_shipAudio == null)
                    {
                        _shipAudio = t.GetField("engineAudioSource", flags)?.GetValue(beh)
                            ?? t.GetField("engineSound", flags)?.GetValue(beh);
                    }
                    float maxVol = NumOr(t.GetField("engineSoundMaxVolume", flags)?.GetValue(beh), 0f);
                    if (maxVol <= 0.001f)
                        maxVol = NumOr(t.GetField("engineMaxVolume", flags)?.GetValue(beh), 0f);
                    if (maxVol > 0.001f) _shipMaxVol = maxVol;
                }
            }
            _vehIdlePitch = SourcePitch(_vehIdle, 1f);
            _shipPitch = SourcePitch(_shipAudio, 1f);
            _groundAudioBound = true;
        }

        static float NumOr(object? value, float fallback)
        {
            if (value is float f) return f;
            if (value is int i) return i;
            if (value is double d) return (float)d;
            return fallback;
        }

        void DriveVehicleAudio(float spd)
        {
            BindGroundAudio();
            if (Plugin.Clock.Paused || spd < 0.05f)
            {
                StopLoop(_vehIdle);
                StopLoop(_vehDrive);
                return;
            }
            bool flat = WantsFlatAudio();
            if (spd < 3f)
            {
                PlayLoop(_vehIdle, 0.5f, _vehIdlePitch > 0.01f ? _vehIdlePitch : 1f, flat);
                StopLoop(_vehDrive);
                return;
            }
            StopLoop(_vehIdle);
            float drivePitch = Mathf.Clamp(spd * _vehPitchMult / Mathf.Max(_vehTopSpeed, 1f), _vehMinPitch, _vehMaxPitch);
            float vol = 0.5f + drivePitch * 0.5f;
            PlayLoop(_vehDrive, vol, drivePitch, flat);
        }

        void DriveShipAudio(float spd)
        {
            BindGroundAudio();
            if (!_shipFxBound)
                CacheShipFx();
            bool paused = Plugin.Clock.Paused;
            float throttle = paused ? 0f : Mathf.Clamp01(spd / 12f);
            float engPitch = Mathf.Min(0.5f + spd * 0.005f + throttle * 0.35f, 1.5f);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bool droveShip = false;
            for (int i = 0; i < _shipProps.Length; i++)
            {
                var c = _shipProps[i];
                if (c == null) continue;
                var t = c.GetType();
                var engine = t.GetField("engineSound", flags)?.GetValue(c);
                var thrust = t.GetField("thrustSound", flags)?.GetValue(c);
                if (ReferenceEquals(engine, _shipAudio) || ReferenceEquals(thrust, _shipAudio))
                    droveShip = true;
                DriveShipEngine(engine, paused, engPitch);
                DriveShipThrust(thrust, paused, throttle);
                StopLoop(t.GetField("propSound", flags)?.GetValue(c));
                StopLoop(t.GetField("turbineSound", flags)?.GetValue(c));
            }
            if (_shipAudio != null && !droveShip)
                DriveShipEngine(_shipAudio, paused, engPitch);
            if (_shipUnit == null) return;
            float waterVol = paused ? 0f : Mathf.Clamp01(spd * spd * 0.005f) * 0.5f;
            var st = _shipUnit.GetType();
            SetArrayVol(st.GetField("waterSounds", flags)?.GetValue(_shipUnit), waterVol, false);
            DriveHullSounds(st.GetField("hullSounds", flags)?.GetValue(_shipUnit), spd, paused);
            SetArrayVol(st.GetField("wakeSounds", flags)?.GetValue(_shipUnit), waterVol, false);
            var engines = st.GetField("engineSounds", flags)?.GetValue(_shipUnit);
            if (engines is Array engArr)
            {
                for (int i = 0; i < engArr.Length; i++)
                    DriveShipEngine(engArr.GetValue(i), paused, engPitch);
            }
        }

        static void DriveShipEngine(object? src, bool paused, float engPitch)
        {
            if (src == null || paused || IsInteriorSource(src))
            {
                StopLoop(src);
                return;
            }
            float vol = engPitch * 0.45f * PrefabVolScale(src);
            PlayLoop(src, vol, engPitch, false);
        }

        static void DriveShipThrust(object? src, bool paused, float throttle)
        {
            if (src == null || paused || throttle <= 0.1f || IsInteriorSource(src))
            {
                StopLoop(src);
                return;
            }
            PlayLoop(src, throttle * 0.45f * PrefabVolScale(src), SourcePitch(src, 1f), false);
        }

        static void DriveHullSounds(object? arr, float spd, bool paused)
        {
            if (arr is not Array a) return;
            float hull = 0.15f + Mathf.Clamp01(spd * 0.03f);
            for (int i = 0; i < a.Length; i++)
            {
                var src = a.GetValue(i);
                if (src == null) continue;
                if (paused || IsInteriorSource(src))
                {
                    StopLoop(src);
                    continue;
                }
                float vol = hull * OrigVolume(src, 1f);
                if (vol > 0.35f) vol = 0.35f;
                PlayLoop(src, vol, SourcePitch(src, 1f), false);
            }
        }

        static float PrefabVolScale(object? src)
        {
            float orig = OrigVolume(src, 1f);
            return orig < 1f ? orig : 1f;
        }

        static bool IsInteriorSource(object? src)
        {
            if (src is not Component c || c.gameObject == null) return false;
            string n = c.gameObject.name ?? "";
            return n.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void StopGroundAudio()
        {
            BindGroundAudio();
            if (_ship && !_shipFxBound)
                CacheShipFx();
            StopLoop(_vehIdle);
            StopLoop(_vehDrive);
            StopLoop(_shipAudio);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] propFields = { "engineSound", "thrustSound", "propSound", "turbineSound" };
            for (int i = 0; i < _shipProps.Length; i++)
            {
                var c = _shipProps[i];
                if (c == null) continue;
                var t = c.GetType();
                for (int k = 0; k < propFields.Length; k++)
                    StopLoop(t.GetField(propFields[k], flags)?.GetValue(c));
            }
            if (_shipUnit == null) return;
            string[] arrNames = { "waterSounds", "hullSounds", "engineSounds", "wakeSounds" };
            var st = _shipUnit.GetType();
            for (int i = 0; i < arrNames.Length; i++)
                SetArrayVol(st.GetField(arrNames[i], flags)?.GetValue(_shipUnit), 0f, false);
        }

        static void SetArrayVol(object? arr, float vol, bool flat)
        {
            if (arr is not Array a) return;
            for (int i = 0; i < a.Length; i++)
            {
                var src = a.GetValue(i);
                if (vol <= 0.01f) StopLoop(src);
                else PlayLoop(src, vol, SourcePitch(src, 1f), flat);
            }
        }
    }
}