using System.Globalization;
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

        struct TvcUnit
        {
            public Transform[] Meshes;
            public float GainX;
            public float GainZ;
            public float VecX;
            public float VecY;
            public float VecZ;
            public float MaxAir;
            public float Pitch;
            public float Yaw;
        }

        SurfUnit[] _surfs = System.Array.Empty<SurfUnit>();
        TvcUnit[] _tvcs = System.Array.Empty<TvcUnit>();
        int _tvcCount;
        bool _surfsCached;
        bool _surfLogged;
        bool _surfPosed;
        float _surfStateAt = -100f;
        ReplayObject? _surfTrack;
        float[] _stickT = System.Array.Empty<float>();
        float[] _stickP = System.Array.Empty<float>();
        float[] _stickR = System.Array.Empty<float>();
        float[] _stickY = System.Array.Empty<float>();
        float[] _stickTh = System.Array.Empty<float>();
        bool[] _stickHasTh = System.Array.Empty<bool>();
        int _stickPitchN, _stickRollN, _stickYawN;
        int _surfSerial = -1;

        static FieldInfo? _sfPitch, _sfRoll, _sfYaw, _sfServo, _sfFlap, _sfMax, _sfYawSplit, _sfMesh, _sfUp, _sfLo;
        static bool _sfBound;
        static readonly CultureInfo SurfInv = CultureInfo.InvariantCulture;

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

        void SkipHold(Transform? tr)
        {
            if (tr == null) return;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] != tr) continue;
                _partSkip[i] = true;
                return;
            }
        }

        void CacheSurfaces()
        {
            _surfsCached = true;
            var t = Plugin.FindGameType("ControlSurface");
            int flaps = 0;
            int splits = 0;
            if (t != null)
            {
                BindSurfFields(t);
                var found = GetComponentsInChildren(t, true);
                var list = new System.Collections.Generic.List<SurfUnit>(found.Length);
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
                    SkipHold(mesh.transform);
                    SkipHold(upper);
                    SkipHold(lower);
                    if (c is Behaviour beh) beh.enabled = false;
                }
                _surfs = list.ToArray();
            }
            CacheTvc();
            if (!_surfLogged)
            {
                _surfLogged = true;
                Plugin.Log?.LogInfo("Surf drive " + gameObject.name
                    + " n=" + _surfs.Length
                    + " flaps=" + flaps
                    + " split=" + splits
                    + " tvc=" + _tvcCount);
            }
        }

        void CacheTvc()
        {
            var list = new System.Collections.Generic.List<TvcUnit>(4);
            int n = 0;
            n += CacheTvcType("Turbojet", list);
            n += CacheTvcType("Turbofan", list);
            _tvcs = list.ToArray();
            _tvcCount = n;
        }

        int CacheTvcType(string typeName, System.Collections.Generic.List<TvcUnit> list)
        {
            var t = Plugin.FindGameType(typeName);
            if (t == null) return 0;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fVec = t.GetField("thrustVectoring", flags);
            var fGain = t.GetField("thrustVectoringGain", flags);
            var fMax = t.GetField("thrustVectoringMaxAirspeed", flags);
            var fTr = t.GetField("vectoringTransforms", flags);
            if (fTr == null) return 0;
            int count = 0;
            foreach (var c in GetComponentsInChildren(t, true))
            {
                if (c is not Component eng) continue;
                if (fTr.GetValue(eng) is not Transform[] meshes || meshes.Length == 0) continue;
                Vector3 vec = Vector3.zero;
                Vector3 gain = new Vector3(1f, 1f, 4f);
                try { if (fVec?.GetValue(eng) is Vector3 v) vec = v; } catch { }
                try { if (fGain?.GetValue(eng) is Vector3 g) gain = g; } catch { }
                float maxAir = SurfFloat(fMax, eng, 0f);
                if (vec.sqrMagnitude < 0.01f && maxAir <= 0f) continue;
                var kept = new System.Collections.Generic.List<Transform>(meshes.Length);
                for (int i = 0; i < meshes.Length; i++)
                {
                    if (meshes[i] == null) continue;
                    kept.Add(meshes[i]);
                    SkipHold(meshes[i]);
                }
                if (kept.Count == 0) continue;
                list.Add(new TvcUnit
                {
                    Meshes = kept.ToArray(),
                    GainX = gain.x,
                    GainZ = gain.z,
                    VecX = vec.x,
                    VecY = vec.y,
                    VecZ = vec.z,
                    MaxAir = maxAir
                });
                count += kept.Count;
                if (eng is Behaviour beh) beh.enabled = false;
            }
            return count;
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
            _stickPitchN = 0;
            _stickRollN = 0;
            _stickYawN = 0;
            if (samples == null || n == 0) return;
            float pitch = 0f, roll = 0f, yaw = 0f, thr = 0f;
            bool hasTh = false;
            bool seenP = false, seenR = false, seenY = false;
            float prevP = 0f, prevR = 0f, prevY = 0f;
            int w = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                bool any = false;
                if (s.HasPitchIn)
                {
                    if (!seenP || s.PitchIn != prevP) _stickPitchN++;
                    seenP = true;
                    prevP = s.PitchIn;
                    pitch = s.PitchIn;
                    any = true;
                }
                if (s.HasRollIn)
                {
                    if (!seenR || s.RollIn != prevR) _stickRollN++;
                    seenR = true;
                    prevR = s.RollIn;
                    roll = s.RollIn;
                    any = true;
                }
                if (s.HasYawIn)
                {
                    if (!seenY || s.YawIn != prevY) _stickYawN++;
                    seenY = true;
                    prevY = s.YawIn;
                    yaw = s.YawIn;
                    any = true;
                }
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

        void DriveSurfaces(bool gearDown, float airspeed, float radarAlt)
        {
            if (!_air) return;
            if (!_surfsCached) CacheSurfaces();
            if (_surfs.Length == 0 && _tvcs.Length == 0) return;
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
            float defl0 = 0f;
            Transform mesh0 = null;
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
                if (i == 0)
                {
                    defl0 = angle;
                    mesh0 = s.Mesh;
                }
                _surfs[i] = s;
            }
            for (int i = 0; i < _tvcs.Length; i++)
            {
                var v = _tvcs[i];
                SurfServo.Tvc(ref v.Pitch, ref v.Yaw, pitchIn, rollIn, yawIn,
                    v.GainX, v.GainZ, v.VecX, v.VecY, v.VecZ,
                    airspeed, v.MaxAir, radarAlt, dt, snap);
                float ex = SurfServo.TvcPitchEuler(v.Pitch);
                var euler = new Vector3(ex, v.Yaw, 0f);
                var meshes = v.Meshes;
                for (int m = 0; m < meshes.Length; m++)
                {
                    if (meshes[m] == null) continue;
                    meshes[m].localEulerAngles = euler;
                }
                _tvcs[i] = v;
            }
            float wall = Time.unscaledTime;
            if (wall - _surfStateAt >= 5f)
            {
                _surfStateAt = wall;
                string meshName = mesh0 != null ? mesh0.name : "none";
                string rot = "none";
                if (mesh0 != null)
                {
                    var e = mesh0.localEulerAngles;
                    rot = "(" + e.x.ToString("0.0", SurfInv) + "," + e.y.ToString("0.0", SurfInv) + "," + e.z.ToString("0.0", SurfInv) + ")";
                }
                Plugin.Log?.LogInfo("Surf state " + gameObject.name
                    + " keys=" + _stickPitchN + "/" + _stickRollN + "/" + _stickYawN
                    + " stick=(" + pitchIn.ToString("0.00", SurfInv) + "," + rollIn.ToString("0.00", SurfInv) + "," + yawIn.ToString("0.00", SurfInv) + ")"
                    + " defl0=" + defl0.ToString("0.0", SurfInv)
                    + " mesh=" + meshName
                    + " localRot=" + rot);
            }
        }
    }
}
