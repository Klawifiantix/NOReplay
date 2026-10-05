using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    [DefaultExecutionOrder(-200)]
    public partial class ReplayGhost : MonoBehaviour
    {
        public ReplayObject? Track;

        static Type? _shaftType;
        static Type? _propType;
        static Type? _cspType;
        static Type? _fanType;
        static Type? _tfanType;
        static Type? _tjetType;
        static Type? _acType;
        static Type? _engType;
        static FieldInfo? _fAcEngines;
        static FieldInfo? _fTjetSrc;
        static FieldInfo? _fTjetRpm;
        static FieldInfo? _fTjetMax;
        static Type? _gearType;
        static FieldInfo? _fRotorSrc;
        static FieldInfo? _fRotorExt;
        static FieldInfo? _fRotorHub;
        static FieldInfo? _fRotorDir;
        static FieldInfo? _fRotorUnfold;
        static FieldInfo? _fTfanSrc;
        static FieldInfo? _fTfanRpm;
        static FieldInfo? _fTfanMax;
        static FieldInfo? _fFanSrc;
        static FieldInfo? _fPfanSrc;
        static FieldInfo? _fCspRpm;
        static FieldInfo? _fCspHub;
        static FieldInfo? _fCspDir;
        static FieldInfo? _fCspAudio;
        static FieldInfo? _fCspVol;
        static FieldInfo? _fCspPitch;
        static FieldInfo? _fFanRpm;
        static FieldInfo? _fGearDoors;
        static FieldInfo? _fRadarAlt;
        static FieldInfo? _fGearHinge;
        static FieldInfo? _fHingeBase;
        static FieldInfo? _fFoldDeg;
        static FieldInfo? _fHingePos;
        static FieldInfo? _fHingeMotion;
        static FieldInfo? _fAngSpeed;
        static FieldInfo? _fAngPos;
        static FieldInfo? _fNomRpm;
        static FieldInfo? _fStartup;
        static MethodInfo? _mAnimate;
        static FieldInfo? _fThrottle;
        static FieldInfo? _fRpm;
        static FieldInfo? _fStarted;
        static FieldInfo? _fOperable;
        static FieldInfo? _fHasFuel;
        static FieldInfo? _fIgnition;
        static FieldInfo? _fFold;
        static MethodInfo? _mSetGear;
        static MethodInfo? _mMoveGear;
        static MethodInfo? _mUpdateParts;
        static bool _bound;
        static bool _dumpedKids;

        Renderer[] _rend = Array.Empty<Renderer>();
        Component[] _shafts = Array.Empty<Component>();
        Component[] _props = Array.Empty<Component>();
        Component[] _engs = Array.Empty<Component>();
        Component[] _tfans = Array.Empty<Component>();
        Component[] _engineList = Array.Empty<Component>();
        bool _engineListReady;
        bool _surfDone;
        Component[] _gears = Array.Empty<Component>();
        Component[] _shipProps = Array.Empty<Component>();
        Component? _shipUnit;
        object? _vehIdle;
        object? _vehDrive;
        static FieldInfo? _fPropRpm;
        static FieldInfo? _fPropRatio;
        static FieldInfo? _fPropNom;
        float _spawnY = float.NaN;
        bool _cspAudioOn;
        bool _gearLockedUp;
        bool? _gearDownSent;
        bool _haveGearAcmi;
        bool _gearFromAcmi;
        Light[] _landLights = Array.Empty<Light>();
        Renderer[] _navRends = Array.Empty<Renderer>();
        bool _landLightsBound;
        float _prevX, _prevY, _prevZ, _prevT = -1f;
        float _spdMs;
        Vector3 _velMs;
        bool _visible = true;
        bool _asleep;
        float _fold;
        bool _air;
        bool _ship;
        bool _ordnance;
        internal bool IsOrdnancePublic => _ordnance;
        bool _neutral;
        bool _haveThr;
        float _thr;
        bool _brakeClosed;
        readonly List<Transform> _brakeTr = new();
        readonly List<Quaternion> _brakeRest = new();
        bool _boomed;
        bool _ordAudioOn;
        bool _ordVoiceHeld;
        Component[] _ordFx = Array.Empty<Component>();
        static GameObject? _boomPrefab;
        static bool _boomTried;
        Rigidbody[] _rbs = Array.Empty<Rigidbody>();
        Collider[] _cols = Array.Empty<Collider>();
        Transform[] _parts = Array.Empty<Transform>();
        Vector3[] _partPos = Array.Empty<Vector3>();
        Quaternion[] _partRot = Array.Empty<Quaternion>();
        Transform[] _partPar = Array.Empty<Transform>();
        bool[] _partSkip = Array.Empty<bool>();
        Vector3 _lastP;
        bool _haveLast;
        internal Transform Drive => _drive != null ? _drive : transform;
        Transform _drive;
        int _lastKids = -1;

        const float AirVis = 80000f;
        const float GroundVis = 2500f;
        bool _colsOff;
        static Camera? _cam;
        static int _camFrame;

        void DetectKind()
        {
            string t = Track?.Type ?? "";
            string gn = gameObject.name ?? "";
            _air = gn.IndexOf("COIN", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Helo", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("VTOL", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("trainer", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Darkreach", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Revoker", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Tarantula", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Ifrit", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Medusa", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Ibis", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Vagrant", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Brawler", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("FixedWing", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Rotorcraft", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("+Air+", StringComparison.OrdinalIgnoreCase) >= 0
                || Plugin.IsAircraftPublic(t, Track?.Name)
                || Plugin.IsAircraftPublic(gn, Track?.Name ?? gn);
            string coal = Track?.Coalition ?? "";
            _neutral = coal.IndexOf("Neutral", StringComparison.OrdinalIgnoreCase) >= 0
                       || coal.IndexOf("Green", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal void Adopt(ReplayObject track)
        {
            Track = track;
            DetectKind();
            _ordnance = Plugin.IsOrdnancePublic(track.Type, track.Name);
            _drive = _ordnance ? transform : FindDrive();
            if (_drive == null) _drive = transform;
            _asleep = false;
            _visible = false;
            if (_ordnance)
            {
                enabled = true;
                transform.SetParent(null, true);
                if (_rend == null || _rend.Length == 0)
                    _rend = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < _rend.Length; i++)
                    if (_rend[i] != null) _rend[i].enabled = true;
                _visible = true;
            }
        }

        void Awake()
        {
            string n = gameObject.name ?? "";
            _ordnance = Plugin.IsOrdnancePublic(Track?.Type, Track?.Name)
                || Plugin.IsOrdnancePublic(null, n.Replace("NOReplay_", ""));
            _rend = GetComponentsInChildren<Renderer>(true);
            if (!_ordnance)
            {
                for (int i = 0; i < _rend.Length; i++)
                {
                    if (_rend[i] != null) _rend[i].enabled = false;
                }
                _visible = false;
            }
            else
            {
                for (int i = 0; i < _rend.Length; i++)
                    if (_rend[i] != null) _rend[i].enabled = true;
                _visible = true;
            }
            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }
            var rbs = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] == null) continue;
                rbs[i].isKinematic = true;
                rbs[i].detectCollisions = false;
            }
            SnapSurfacesNow();
        }

        void Update()
        {
            if (!_air && !_ship && !_ordnance) return;
            if (!Plugin.AudioNear(gameObject, 800f) && !_air) return;
            WriteUnitMotion(_spdMs, _velMs);
        }

        void OnDestroy()
        {
            try
            {
                Plugin.Log.LogInfo("GO weg " + name + " " + (Track != null ? Track.Name : "?")
                    + " t=" + Plugin.Clock.Time.ToString("0.0"));
            }
            catch { }
        }

        void Start()
        {
            DetectKind();
            string t = Track?.Type ?? "";
            string gn = gameObject.name ?? "";
            bool ship = t.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Corvette", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Corvette", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0;
            _ship = ship;
            _ordnance = Plugin.IsOrdnancePublic(t, Track?.Name);
            _drive = _ordnance ? transform : FindDrive();
            if (_drive == null) _drive = transform;
            if (_ordnance)
            {
                SetGhostVisible(true);
                return;
            }
            if (_air)
            {
                int rc = _drive.GetComponentsInChildren<Renderer>(true).Length;
                string par = transform.parent != null ? transform.parent.name : "-";
                Plugin.Log.LogInfo("Ghost-Hier " + gameObject.name
                    + " parent=" + par
                    + " children=" + transform.childCount
                    + " drive=" + _drive.name
                    + " rend=" + rc);
            }
            FreezePhysics();
            SilenceAirframe();
            SuppressDamageFx();
            DisableFlightAssist();
            SnapSurfacesNow();
            CaptureSurfaceRest();
            if (!_neutral) NeutralizeSurfaces();
            HideLoadoutBits();
            if (_neutral)
            {
                Plugin.Log.LogInfo("Neutral-Freeze " + (Track?.Name ?? name)
                    + " coal=" + (Track?.Coalition ?? "-"));
                FreezeNeutral();
            }
            if (_air)
            {
                ForceSimplePhysics();
                CacheLocals();
                PaintMissingLivery();
                Invoke(nameof(PaintMissingLivery), 1.5f);
                Invoke(nameof(Recache), 2f);
                Bind();
                CacheFx();
                DumpFxNames();
            }
            if (_ship)
            {
                Bind();
                CacheShipFx();
            }
            Plugin.ApplyReplayIdentity(gameObject, Track);
            if (!_air && !_ship && !_ordnance)
            {
                CacheVehicleAudio();
                Plugin.KillLaserBeams(gameObject);
            }
            if (_ordnance) IgniteMissileFx();
            SpatializeSources();
            if (!_neutral) Invoke(nameof(SpatializeSources), 1.5f);
            if (!_ordnance)
                SetGhostVisible(false);
            else
                SetGhostVisible(true);
            _lastKids = Drive.childCount;
            if (_air && !_dumpedKids)
            {
                _dumpedKids = true;
                var trs = GetComponentsInChildren<Transform>(true);
                var names = new System.Text.StringBuilder("Kids ");
                names.Append(gameObject.name).Append(':');
                for (int i = 0; i < trs.Length && i < 40; i++)
                    names.Append(' ').Append(trs[i].name);
                Plugin.Log.LogInfo(names.ToString());
            }
        }

        internal void SleepFar(Vector3 camPos)
        {
            if (!IsTrackAlive()) return;
            if (_air || _ship || _ordnance) return;
            float lim = GroundVis * GroundVis;
            bool far = (transform.position - camPos).sqrMagnitude > lim;
            if (far)
            {
                if (!_asleep) EnterSleep();
                enabled = false;
            }
            else if (!enabled)
            {
                enabled = true;
            }
        }

        static bool IsIgnored(Transform t)
        {
            while (t != null)
            {
                string n = t.name ?? "";
                if (n.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("pivot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("scrape", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                t = t.parent;
            }
            return false;
        }

        static bool IsMapGeo(string n)
        {
            return n.IndexOf("Hangar", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Revet", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Helipad", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Taxi", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Tower", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Pad", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Recache()
        {
            if (!_air) return;
            ForceSimplePhysics();
            FreezePhysics();
            SilenceAirframe();
            SuppressDamageFx();
            CacheFx();
            AdoptOrphanFx();
            CacheLocals();
            Plugin.Log.LogInfo("Ghost-Recache " + gameObject.name
                + " drive=" + Drive.name
                + " kids=" + Drive.childCount
                + " parts=" + _parts.Length
                + " shaft=" + _shafts.Length
                + " prop=" + _props.Length
                + " gear=" + _gears.Length
                + " eng=" + _engs.Length);
        }

        void ForceSimplePhysics()
        {
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null) continue;
                if (beh.GetType().Name != "Aircraft") continue;
                var m = beh.GetType().GetMethod("SetSimplePhysics",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    try { m.Invoke(beh, null); }
                    catch { }
                    Plugin.Log.LogInfo("Ghost-Simple " + gameObject.name);
                }
            }
        }

        void FindVisualBuddy()
        {
            var origin = transform.position;
            float max = 8f * 8f;
            Transform best = null;
            int bestR = Drive.GetComponentsInChildren<Renderer>(true).Length;
            var rends = FindObjectsOfType<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                var tr = r.transform;
                if (IsIgnored(tr) || IsMapGeo(tr.name)) continue;
                if ((tr.position - origin).sqrMagnitude > max) continue;
                var g = tr.GetComponentInParent<ReplayGhost>();
                if (g != null && g != this) continue;
                var root = tr;
                while (root.parent != null && root.parent.GetComponent<ReplayGhost>() == null
                    && !IsIgnored(root.parent) && !IsMapGeo(root.parent.name))
                    root = root.parent;
                int rc = root.GetComponentsInChildren<Renderer>(true).Length;
                if (rc > bestR)
                {
                    best = root;
                    bestR = rc;
                }
            }
            if (best != null && best != Drive)
            {
                _drive = best;
                Plugin.Log.LogInfo("Ghost-Vis " + gameObject.name + " -> " + best.name + " rend=" + bestR);
            }
        }

        void OnTransformChildrenChanged()
        {
            if (!_air) return;
            int n = Drive.childCount;
            if (n == _lastKids) return;
            string extra = "";
            Transform ch = n > 0 ? Drive.GetChild(n - 1) : null;
            if (ch != null) extra = " +" + ch.name;
            Plugin.Log.LogInfo("Ghost-Kids " + gameObject.name + " " + _lastKids + " -> " + n
                + extra + " t=" + Plugin.Clock.Time.ToString("0.0"));
            _lastKids = n;
            if (ch != null && IsIgnored(ch)) return;
            FreezePhysics();
            HideLoadoutBits();
            CacheLocals();
        }

        static bool IsLoadoutProp(string n)
        {
            return n.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("boarding", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("gantry", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("scaffold", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void HideLoadoutBits()
        {
            var trs = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
            {
                var t = trs[i];
                if (t == null || t == transform) continue;
                if (IsLoadoutProp(t.name))
                    t.gameObject.SetActive(false);
            }
        }

        Transform FindDrive()
        {
            Transform best = transform;
            int bestR = transform.GetComponentsInChildren<Renderer>(true).Length;
            for (var t = transform; t != null; t = t.parent)
            {
                int r = t.GetComponentsInChildren<Renderer>(true).Length;
                if (r >= bestR)
                {
                    best = t;
                    bestR = r;
                }
            }
            if (transform.parent != null)
            {
                var p = transform.parent;
                for (int i = 0; i < p.childCount; i++)
                {
                    var sib = p.GetChild(i);
                    int r = sib.GetComponentsInChildren<Renderer>(true).Length;
                    if (r > bestR)
                    {
                        best = sib;
                        bestR = r;
                    }
                }
            }
            return best;
        }

        void FreezePhysics()
        {
            var root = Drive;
            _rbs = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < _rbs.Length; i++)
            {
                var rb = _rbs[i];
                if (rb == null || IsIgnored(rb.transform)) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            var joints = root.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                var j = joints[i];
                if (j == null) continue;
                j.breakForce = Mathf.Infinity;
                j.breakTorque = Mathf.Infinity;
                j.enableCollision = false;
            }
            _cols = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < _cols.Length; i++)
            {
                if (_cols[i] == null || IsIgnored(_cols[i].transform)) continue;
                _cols[i].enabled = false;
            }
        }

        void CacheLocals()
        {
            var root = Drive;
            var all = root.GetComponentsInChildren<Transform>(true);
            if (_parts.Length > 0 && all.Length <= _parts.Length + 1) return;
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == root || IsIgnored(all[i])) continue;
                n++;
            }
            _parts = new Transform[n];
            _partPos = new Vector3[n];
            _partRot = new Quaternion[n];
            _partPar = new Transform[n];
            _partSkip = new bool[n];
            int k = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var tr = all[i];
                if (tr == null || tr == root || IsIgnored(tr)) continue;
                string nm = tr.name ?? "";
                bool skip = nm.IndexOf("Rotor", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Shaft", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Blade", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Hub", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Prop", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Swash", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Gear", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Hinge", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Split", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Airbrake", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0;
                _parts[k] = tr;
                _partPar[k] = tr.parent;
                _partPos[k] = tr.localPosition;
                _partRot[k] = tr.localRotation;
                _partSkip[k] = skip;
                k++;
            }
        }

        void HoldLocals()
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                var tr = _parts[i];
                if (tr == null) continue;
                var par = _partPar[i];
                if (par != null && tr.parent != par)
                    tr.SetParent(par, false);
                if (_partSkip[i]) continue;
                tr.localPosition = _partPos[i];
                tr.localRotation = _partRot[i];
            }
        }

        Component[] CollectFx(Type? t)
        {
            if (t == null) return Array.Empty<Component>();
            var raw = GetComponentsInChildren(t, true);
            var list = new System.Collections.Generic.List<Component>(raw.Length + 4);
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] != null) list.Add((Component)raw[i]);
            return list.ToArray();
        }

        internal void SetGhostVisible(bool on)
        {
            if (_rend == null || _rend.Length == 0)
                _rend = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < _rend.Length; i++)
            {
                if (_rend[i] != null) _rend[i].enabled = on;
            }
            if (_ordnance)
            {
                for (int i = 0; i < _rend.Length; i++)
                    if (_rend[i] != null) _rend[i].enabled = on;
                if (on)
                {
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        r.enabled = true;
                        r.gameObject.layer = 0;
                        if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    }
                    IgniteMissileFx();
                }
                ToggleOrdnanceFx(on);
            }
            if (_visible != on)
            {
                _visible = on;
                Plugin.SetMapIcon(gameObject, on);
            }
        }

        internal bool IsTrackAlive()
        {
            if (Track == null || Track.Samples == null || Track.Samples.Count == 0)
                return false;
            float t = Plugin.Clock.Time;
            if (t < 0.05f) return false;
            float born = Track.Samples[0].Time;
            if (Track.SpawnAt > born) born = Track.SpawnAt;
            if (t < born) return false;
            if (Track.RemovedAt >= 0f && t > Track.RemovedAt) return false;
            return true;
        }

        void LateUpdate()
        {
            if (Track == null) return;
            if (!IsTrackAlive())
            {
                if (!_boomed && Track.RemovedAt >= 0f
                    && Plugin.Clock.Time >= Track.RemovedAt)
                {
                    _boomed = true;
                    if (_ordnance && !IsFlareTrack())
                        PlayWarheadBoom(transform.position);
                    else if (!_ordnance)
                        PlayUnitWreckFx(transform.position, transform.rotation);
                }
                if (_visible)
                {
                    Plugin.ReleaseCameraIfOn(gameObject);
                    StopOrdnanceAudio();
                    SetGhostVisible(false);
                }
                return;
            }
            _boomed = false;
            if (IsFlareTrack())
            {
                SetGhostVisible(true);
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = true;
            }
            else if (!_visible) SetGhostVisible(true);
            if (_asleep && (_air || _ship))
                ExitSleep();
            if (!_air && !_ship && !_ordnance)
            {
                if (_asleep)
                {
                    if (((Time.frameCount + gameObject.GetInstanceID()) & 15) != 0)
                        return;
                    if (IsFar()) return;
                    ExitSleep();
                }
                else if (IsFar())
                {
                    EnterSleep();
                    return;
                }
            }
            if (!Track.TrySample(Plugin.Clock.Time, out var s)) return;
            if (!_colsOff)
            {
                for (int i = 0; i < _cols.Length; i++)
                {
                    if (_cols[i] == null || IsIgnored(_cols[i].transform)) continue;
                    _cols[i].enabled = false;
                }
                _colsOff = true;
            }
            var p = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
            Quaternion q = Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
            var drive = _drive != null ? _drive : transform;
            if (_air && Drive.childCount != _lastKids)
            {
                _lastKids = Drive.childCount;
                CacheLocals();
                FreezePhysics();
            }
            drive.SetPositionAndRotation(p, q);
            if (_air)
            {
                if (!_surfBound) CaptureSurfaceRest();
                HoldLocals();
                HoldAirbrakes();
            }
            if (!_ordnance && !_neutral) AimGuns();
            if (_ordnance && _prevT >= 0f && Plugin.Clock.Time + 0.05f < _prevT)
            {
                _ordAudioOn = false;
                _ordVoiceHeld = false;
            }
            if (_ordnance && _visible && !_ordAudioOn) IgniteMissileFx();
            if (_ordnance) LateOrdnanceAudio();
            for (int i = 0; i < _rbs.Length; i++)
            {
                var rb = _rbs[i];
                if (rb == null || IsIgnored(rb.transform)) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                if (_ordnance)
                    rb.velocity = (p - drive.position) / Mathf.Max(Time.deltaTime, 0.02f);
                else
                    rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            _prevX = s.X;
            _prevZ = s.Z;
            _prevT = Plugin.Clock.Time;
            if (_air && !_neutral)
                ApplyFx(s);
            else
            {
                float spd = 0f;
                if (_prevT >= 0f && Plugin.Clock.Time > _prevT)
                {
                    float dx = s.X - _prevX;
                    float dz = s.Z - _prevZ;
                    spd = Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(Plugin.Clock.Time - _prevT, 0.001f);
                }
                _prevX = s.X;
                _prevZ = s.Z;
                _prevT = Plugin.Clock.Time;
                if (NearMe(90f))
                {
                    if (_ship) DriveShipAudio(spd);
                    else DriveVehicleAudio(spd);
                }
            }
        }

        bool IsFar()
        {
            if (_camFrame != Time.frameCount)
            {
                _camFrame = Time.frameCount;
                _cam = Camera.main;
            }
            if (_cam == null) return false;
            float lim = (_air || _ordnance) ? AirVis : GroundVis;
            return (transform.position - _cam.transform.position).sqrMagnitude > lim * lim;
        }

        void EnterSleep()
        {
            _asleep = true;
            SetVisible(false);
            var tr = transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var ch = tr.GetChild(i);
                if (ch != null) ch.gameObject.SetActive(false);
            }
        }

        void ExitSleep()
        {
            _asleep = false;
            var tr = transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var ch = tr.GetChild(i);
                if (ch != null) ch.gameObject.SetActive(true);
            }
            SetVisible(true);
            if (!_air && !_ship && !_ordnance)
                Plugin.KillLaserBeams(gameObject);
        }

        void SetVisible(bool on)
        {
            if (_visible == on) return;
            _visible = on;
            for (int i = 0; i < _rend.Length; i++)
            {
                if (_rend[i] != null)
                    _rend[i].enabled = on;
            }
        }

        void WriteUnitMotion(float spdMs, Vector3 vel)
        {
            foreach (var c in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                var f = FindInstField(c.GetType(), "speed");
                if (f == null || f.FieldType != typeof(float)) continue;
                try { f.SetValue(c, spdMs); } catch { }
            }
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;
                try
                {
                    rb.velocity = vel;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }
        }

        static MethodInfo? _mSetGearBool;
        static Type? _aircraftType;
    }
}
