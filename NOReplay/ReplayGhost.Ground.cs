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

        static void PlayLoop(object? src, float volume, float pitch)
        {
            if (src == null) return;
            var st = src.GetType();
            st.GetProperty("loop")?.SetValue(src, true);
            st.GetProperty("mute")?.SetValue(src, false);
            st.GetProperty("enabled")?.SetValue(src, true);
            try { st.GetProperty("ignoreListenerPause")?.SetValue(src, true); } catch { }
            st.GetProperty("spatialBlend")?.SetValue(src, 1f);
            st.GetProperty("minDistance")?.SetValue(src, 20f);
            st.GetProperty("maxDistance")?.SetValue(src, 2500f);
            st.GetProperty("pitch")?.SetValue(src, pitch);
            st.GetProperty("volume")?.SetValue(src, volume);
            object? playing = st.GetProperty("isPlaying")?.GetValue(src);
            if (!(playing is bool on && on))
                st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
        }

        void CacheVehicleAudio()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] idleNames = { "engineIdleSound", "idleSound", "engineSound" };
            string[] driveNames = { "engineDriveSound", "driveSound", "moveSound", "trackSound", "rumbleSound" };
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null) continue;
                string tn = beh.GetType().Name;
                if (tn != "GroundVehicle" && tn != "Vehicle" && tn != "Unit") continue;
                var t = beh.GetType();
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
            }
        }

        void DriveVehicleAudio(float spd)
        {
            float throttle = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd / 16f);
            if (throttle < 0.12f)
            {
                PlayLoop(_vehIdle, Plugin.Clock.Paused ? 0f : 0.35f, 0.9f);
                PlayLoop(_vehDrive, 0f, 1f);
            }
            else
            {
                PlayLoop(_vehIdle, 0.12f, 0.95f);
                PlayLoop(_vehDrive, 0.25f + throttle * 0.55f, 0.85f + throttle * 0.4f);
            }
        }

        void DriveShipAudio(float spd)
        {
            float throttle = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd / 18f);
            float pitch = Mathf.Min(0.55f + throttle * 0.7f, 1.4f);
            float vol = Plugin.Clock.Paused ? 0f : (0.15f + throttle * 0.55f);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] propFields = { "engineSound", "thrustSound", "propSound", "turbineSound" };
            for (int i = 0; i < _shipProps.Length; i++)
            {
                var c = _shipProps[i];
                if (c == null) continue;
                var t = c.GetType();
                for (int k = 0; k < propFields.Length; k++)
                    PlayLoop(t.GetField(propFields[k], flags)?.GetValue(c), vol, pitch);
            }
            if (_shipUnit == null) return;
            float waterVol = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd * spd * 0.0004f + throttle * 0.25f);
            float hullVol = Plugin.Clock.Paused ? 0f : (0.2f + Mathf.Clamp01(spd * 0.03f) * 0.4f);
            string[] arrNames = { "waterSounds", "hullSounds", "engineSounds", "wakeSounds" };
            var st = _shipUnit.GetType();
            for (int i = 0; i < arrNames.Length; i++)
                SetArrayVol(st.GetField(arrNames[i], flags)?.GetValue(_shipUnit), i < 2 ? (i == 0 ? waterVol : hullVol) : vol);
        }

        static void SetArrayVol(object? arr, float vol)
        {
            if (arr is not Array a) return;
            for (int i = 0; i < a.Length; i++)
                PlayLoop(a.GetValue(i), vol, 1f);
        }
    }
}