using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct SurfUnit
        {
            public Transform Mesh;
            public Transform Upper;
            public Transform Lower;
            public Quaternion Rest;
            public Quaternion RestSplit;
            public float PitchRange;
            public float RollRange;
            public float YawRange;
            public float Servo;
            public float MaxSplit;
            public float YawSplit;
            public bool Flap;
            public float Pitch;
            public float Roll;
            public float Yaw;
            public float Split;
        }

        SurfUnit[] _surfs = System.Array.Empty<SurfUnit>();
        bool _surfsCached;
        bool _surfLogged;
        bool _surfPosed;
        ReplayObject? _surfTrack;
        float[] _stickT = System.Array.Empty<float>();
        float[] _stickP = System.Array.Empty<float>();
        float[] _stickR = System.Array.Empty<float>();
        float[] _stickY = System.Array.Empty<float>();
        float[] _stickTh = System.Array.Empty<float>();
        bool[] _stickHasTh = System.Array.Empty<bool>();
        int _surfSerial = -1;

        static FieldInfo? _sfPitch, _sfRoll, _sfYaw, _sfServo, _sfFlap, _sfMax, _sfYawSplit, _sfMesh, _sfUp, _sfLo;
        static bool _sfBound;

        static void BindSurfFields(System.Type t)
        {
            if (_sfBound) return;
            _sfBound = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _sfPitch = t.GetField("pitchRange", flags);
            _sfRoll = t.GetField("rollRange", flags);
            _sfYaw = t.GetField("yawRange", flags);
            _sfServo = t.GetField("servoSpeed", flags);
            _sfFlap = t.GetField("flap", flags);
            _sfMax = t.GetField("maxSplit", flags);
            _sfYawSplit = t.GetField("yawSplitFactor", flags);
            _sfMesh = t.GetField("visibleMesh", flags);
            _sfUp = t.GetField("splitUpper", flags);
            _sfLo = t.GetField("splitLower", flags);
        }

        static float SurfFloat(FieldInfo? field, Component c, float fallback)
        {
            try
            {
                if (field?.GetValue(c) is float v) return v;
            }
            catch { }
            return fallback;
        }

        void CacheSurfaces()
        {
            _surfsCached = true;
            var t = Plugin.FindGameType("ControlSurface");
            if (t == null) return;
            BindSurfFields(t);
            var found = GetComponentsInChildren(t, true);
            var list = new System.Collections.Generic.List<SurfUnit>(found.Length);
            int flaps = 0;
            int splits = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is not Component c) continue;
                var mesh = _sfMesh?.GetValue(c) as GameObject;
                if (mesh == null) continue;
                float max = SurfFloat(_sfMax, c, 0f);
                bool flap = false;
                try { if (_sfFlap?.GetValue(c) is bool b) flap = b; } catch { }
                var upper = _sfUp?.GetValue(c) as Transform;
                var lower = _sfLo?.GetValue(c) as Transform;
                if (flap) flaps++;
                if (max > 0f) splits++;
                list.Add(new SurfUnit
                {
                    Mesh = mesh.transform,
                    Upper = upper,
                    Lower = lower,
                    Rest = mesh.transform.localRotation,
                    RestSplit = upper != null ? upper.localRotation : Quaternion.identity,
                    PitchRange = SurfFloat(_sfPitch, c, 0f),
                    RollRange = SurfFloat(_sfRoll, c, 0f),
                    YawRange = SurfFloat(_sfYaw, c, 0f),
                    Servo = SurfFloat(_sfServo, c, 20f),
                    MaxSplit = max,
                    YawSplit = SurfFloat(_sfYawSplit, c, 0f),
                    Flap = flap
                });
                if (c is Behaviour beh) beh.enabled = false;
            }
            _surfs = list.ToArray();
            if (!_surfLogged)
            {
                _surfLogged = true;
                Plugin.Log?.LogInfo("Surf drive " + gameObject.name
                    + " n=" + _surfs.Length
                    + " flaps=" + flaps
                    + " split=" + splits);
            }
        }

        void EnsureStickKeys()
        {
            if (ReferenceEquals(_surfTrack, Track)) return;
            _surfTrack = Track;
            var samples = Track?.Samples;
            int n = 0;
            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    if (s.HasPitchIn || s.HasRollIn || s.HasYawIn || s.HasThrottle) n++;
                }
            }
            _stickT = new float[n];
            _stickP = new float[n];
            _stickR = new float[n];
            _stickY = new float[n];
            _stickTh = new float[n];
            _stickHasTh = new bool[n];
            if (samples == null || n == 0) return;
            float pitch = 0f, roll = 0f, yaw = 0f, thr = 0f;
            bool hasTh = false;
            int w = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                bool any = false;
                if (s.HasPitchIn) { pitch = s.PitchIn; any = true; }
                if (s.HasRollIn) { roll = s.RollIn; any = true; }
                if (s.HasYawIn) { yaw = s.YawIn; any = true; }
                if (s.HasThrottle) { thr = s.Throttle; hasTh = true; any = true; }
                if (!any) continue;
                _stickT[w] = s.Time;
                _stickP[w] = pitch;
                _stickR[w] = roll;
                _stickY[w] = yaw;
                _stickTh[w] = thr;
                _stickHasTh[w] = hasTh;
                w++;
            }
        }

        void StickAt(float time, out float pitch, out float roll, out float yaw, out float throttle, out bool hasThrottle)
        {
            pitch = 0f;
            roll = 0f;
            yaw = 0f;
            throttle = 0f;
            hasThrottle = false;
            int key = GearClock.ActiveKey(time, _stickT);
            if (key < 0) return;
            pitch = _stickP[key];
            roll = _stickR[key];
            yaw = _stickY[key];
            throttle = _stickTh[key];
            hasThrottle = _stickHasTh[key];
        }

        void DriveSurfaces(bool gearDown)
        {
            if (!_air) return;
            if (!_surfsCached) CacheSurfaces();
            if (_surfs.Length == 0) return;
            EnsureStickKeys();
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            StickAt(now, out float pitchIn, out float rollIn, out float yawIn, out float throttle, out bool hasThrottle);
            var clock = Plugin.Clock;
            int serial = clock != null ? clock.SeekSerial : 0;
            bool seek = _surfSerial >= 0 && serial != _surfSerial;
            bool paused = clock == null || clock.Paused || clock.Speed <= 0.01f;
            bool snap = !_surfPosed || seek || paused;
            _surfPosed = true;
            float dt = snap || clock == null ? 0f : Time.unscaledDeltaTime * Mathf.Abs(clock.Speed);
            _surfSerial = serial;
            var right = Vector3.right;
            for (int i = 0; i < _surfs.Length; i++)
            {
                var s = _surfs[i];
                if (s.Mesh == null) continue;
                if (s.Flap)
                    SurfServo.Flap(ref s.Pitch, s.PitchRange, gearDown, s.Servo, dt, snap);
                else
                {
                    SurfServo.Axis(ref s.Pitch, pitchIn, s.PitchRange, s.Servo, dt, snap);
                    SurfServo.Axis(ref s.Roll, rollIn, s.RollRange, s.Servo, dt, snap);
                    SurfServo.Axis(ref s.Yaw, yawIn, s.YawRange, s.Servo, dt, snap);
                }
                float angle = s.Pitch + s.Roll + s.Yaw;
                s.Mesh.localRotation = s.Rest * Quaternion.AngleAxis(angle, right);
                if (s.MaxSplit > 0f && s.Upper != null && s.Lower != null)
                {
                    SurfServo.Split(ref s.Split, throttle, hasThrottle, yawIn, s.MaxSplit, s.YawSplit, s.Servo, dt, snap);
                    s.Upper.localRotation = s.RestSplit * Quaternion.AngleAxis(s.Split, right);
                    s.Lower.localRotation = s.RestSplit * Quaternion.AngleAxis(0f - s.Split, right);
                }
                _surfs[i] = s;
            }
        }
    }
}
