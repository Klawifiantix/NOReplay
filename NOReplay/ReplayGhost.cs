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
        static FieldInfo? _fStrut;
        static FieldInfo? _fStrutRot;
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
        static FieldInfo? _fEngMaxPublic;
        static FieldInfo? _fCspExt;
        static Type? _nozzleType;
        static MethodInfo? _mThrust;
        static FieldInfo? _fHeatHaze;
        static FieldInfo? _fAfterburners;
        static MethodInfo? _mSetFlightAssist;
        static MethodInfo? _mGetControlsFilter;
        static Type?[]? _assistTypes;
        static bool _assistTypesBound;
        static bool _getInputsLooked;
        static bool _inputFieldsLooked;
        static FieldInfo? _fInThrottle;
        static FieldInfo? _fInBrake;
        static FieldInfo? _fFold;
        static MethodInfo? _mSetGear;
        static MethodInfo? _mMoveGear;
        static MethodInfo? _mUpdateParts;
        static bool _bound;
        static bool _dumpedKids;

        Renderer[] _rend = Array.Empty<Renderer>();
        readonly HashSet<Renderer> _knownRends = new();
        readonly HashSet<Renderer> _disabledInPrefab = new();
        bool _liveryPainted;
        Component[] _shafts = Array.Empty<Component>();
        Component[] _props = Array.Empty<Component>();
        Component[] _engs = Array.Empty<Component>();
        Component[] _tfans = Array.Empty<Component>();
        Component[] _engineList = Array.Empty<Component>();
        bool _engineListReady;
        Component[] _nozzles = Array.Empty<Component>();
        bool _nozzlesBound;
        bool _ignitionBound;
        Component[] _ignitionTargets = Array.Empty<Component>();
        bool _throttleBound;
        bool _throttleGiveUp;
        Component? _throttleAc;
        readonly object[] _thrustArgs = new object[5];
        readonly object[] _particleArgs = new object[3];
        struct SpeedWrite
        {
            public object Target;
            public FieldInfo Field;
        }
        SpeedWrite[] _speeds = Array.Empty<SpeedWrite>();
        Component[] _unitComps = Array.Empty<Component>();
        Component? _aircraftComp;
        float _nextRadarAltAt;
        Rigidbody[] _motionRbs = Array.Empty<Rigidbody>();
        bool _motionBound;
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
        Vector3 _prevLocalPos;
        float _spdMs;
        Vector3 _velMs;
        bool _visible = true;
        bool _asleep;
        float _fold;
        float _doorAmt = 1f;
        bool _air;
        LineRenderer? _trail;
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
        internal bool IsWrecked => _boomed;
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
        public static readonly System.Collections.Generic.Dictionary<int, Vector3> Velocities = new System.Collections.Generic.Dictionary<int, Vector3>();
        float _camTimer;
        Vector3 _lastCamPos;

        void RefreshRenderers()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (_knownRends.Add(r) && !r.enabled && !RendererForcedOff(r))
                    _disabledInPrefab.Add(r);
            }
            _rend = all;
            SyncHardpointRenderers();
            ReapplyReplayHides();
        }

        void SyncHardpointRenderers()
        {
            var wmT = Plugin.FindGameType("WeaponManager");
            if (wmT == null) return;
            var wm = GetComponent(wmT) ?? GetComponentInChildren(wmT, true);
            if (wm == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sets = wmT.GetField("hardpointSets", flags)?.GetValue(wm) as Array;
            if (sets == null) return;

            FieldInfo? fSetMount = null;
            FieldInfo? fHardpoints = null;
            FieldInfo? fHpMount = null;
            FieldInfo? fPlug = null;
            FieldInfo? fPylonOpts = null;
            MethodInfo? mShowPylon = null;
            FieldInfo? fPylonRend = null;

            for (int i = 0; i < sets.Length; i++)
            {
                var set = sets.GetValue(i);
                if (set == null) continue;
                var st = set.GetType();
                fSetMount ??= st.GetField("weaponMount", flags);
                fHardpoints ??= st.GetField("hardpoints", flags);

                var setMount = fSetMount?.GetValue(set) as UnityEngine.Object;
                if (fHardpoints?.GetValue(set) is not System.Collections.IList hps) continue;

                for (int j = 0; j < hps.Count; j++)
                {
                    var hp = hps[j];
                    if (hp == null) continue;
                    var ht = hp.GetType();
                    fHpMount ??= ht.GetField("mount", flags);
                    fPlug ??= ht.GetField("Plug", flags);
                    fPylonOpts ??= ht.GetField("pylonOptions", flags);
                    mShowPylon ??= ht.GetMethod("ShowPylon", flags);

                    var hpMount = fHpMount?.GetValue(hp) as UnityEngine.Object;
                    if (setMount != null && hpMount == null)
                    {
                        try { fHpMount?.SetValue(hp, setMount); } catch { }
                        hpMount = setMount;
                    }

                    bool loaded = setMount != null || hpMount != null;
                    try { mShowPylon?.Invoke(hp, new object[] { loaded }); } catch { }

                    if (fPlug?.GetValue(hp) is Renderer plug && plug != null)
                        ApplyHardpointRendererState(plug, plug.enabled);

                    if (fPylonOpts?.GetValue(hp) is Array opts)
                    {
                        for (int k = 0; k < opts.Length; k++)
                        {
                            var opt = opts.GetValue(k);
                            if (opt == null) continue;
                            fPylonRend ??= opt.GetType().GetField("renderer", flags);
                            if (fPylonRend?.GetValue(opt) is Renderer pr && pr != null)
                                ApplyHardpointRendererState(pr, pr.enabled);
                        }
                    }
                }
            }

            // On UH-90 Ibis (UtilityHelo1), the rear side doorway is split into:
            //   - windowCover_L / windowCover_R (upper half with two small windows, enabled in raw prefab)
            //   - slidingDoor_L / slidingDoor_R (sliding door panel including the lower half, disabled in raw prefab)
            // Ensure slidingDoor_L/R is enabled alongside windowCover_L/R (or when front door_FL/FR is open)
            // so the lower half of the rear side door is never missing.
            Renderer? doorFL = null, doorFR = null;
            Renderer? winL = null, winR = null;
            Renderer? slideL = null, slideR = null;
            for (int i = 0; i < _rend.Length; i++)
            {
                var r = _rend[i];
                if (r == null) continue;
                switch (r.name)
                {
                    case "door_FL": doorFL = r; break;
                    case "door_FR": doorFR = r; break;
                    case "windowCover_L": winL = r; break;
                    case "windowCover_R": winR = r; break;
                    case "slidingDoor_L": slideL = r; break;
                    case "slidingDoor_R": slideR = r; break;
                }
            }
            if (slideL != null)
            {
                bool showL = (winL != null && !_disabledInPrefab.Contains(winL))
                          || (doorFL != null && _disabledInPrefab.Contains(doorFL));
                if (showL) ApplyHardpointRendererState(slideL, true);
            }
            if (slideR != null)
            {
                bool showR = (winR != null && !_disabledInPrefab.Contains(winR))
                          || (doorFR != null && _disabledInPrefab.Contains(doorFR));
                if (showR) ApplyHardpointRendererState(slideR, true);
            }
        }

        void ApplyHardpointRendererState(Renderer r, bool shouldRender)
        {
            _knownRends.Add(r);
            if (shouldRender)
            {
                _disabledInPrefab.Remove(r);
                r.enabled = _visible;
            }
            else
            {
                _disabledInPrefab.Add(r);
                r.enabled = false;
            }
        }

        void CheckCameraReposition(Vector3 vel)
        {
            var cam = Camera.main;
            if (cam == null || Drive == null) return;
            Vector3 camPos = cam.transform.position;
            Vector3 toCam = camPos - Drive.position;
            // If target is flying away from camera (dot < 0)
            if (Vector3.Dot(vel, toCam) < 0f)
            {
                _camTimer += Plugin.Clock.Speed * Time.deltaTime;
                if (_camTimer > 4f)
                {
                    // Trigger reposition by calling GetTVCamPoint via reflection
                    TriggerTVReposition(vel);
                    _camTimer = 0f;
                }
            }
            else
            {
                _camTimer = 0f;
            }
        }

        void TriggerTVReposition(Vector3 vel)
        {
            var camType = Plugin.FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var tv = camType.GetField("TVState", flags)?.GetValue(cam);
                if (tv == null) continue;
                var method = tv.GetType().GetMethod("GetTVCamPoint", flags);
                if (method != null)
                {
                    method.Invoke(tv, new object[] { Drive.position, vel });
                    Plugin.Log.LogInfo("TV reposition triggered");
                }
                break;
            }
        }

        void LogCameraState()
        {
            var camType = Plugin.FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var cur = camType.GetField("currentState", flags)?.GetValue(cam);
                var tv = camType.GetField("TVState", flags)?.GetValue(cam);
                var orbit = camType.GetField("orbitState", flags)?.GetValue(cam);
                string state = cur == tv ? "TV" : cur == orbit ? "Orbit" : cur?.GetType().Name ?? "?";
                Plugin.Log.LogInfo("CamState=" + state);
                break;
            }
        }

        static int _camFrame;

        bool CheckIsShip(string t, string nm, string gn)
        {
            if (t.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Watercraft", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Sea", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Corvette", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Destroyer", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Frigate", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Landing", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Barge", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Corvette", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Destroyer", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Frigate", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Landing Craft", StringComparison.OrdinalIgnoreCase) >= 0
                || nm.IndexOf("Barge", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Corvette", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Destroyer", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Frigate", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Cruiser", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("LandingCraft", StringComparison.OrdinalIgnoreCase) >= 0
                || gn.IndexOf("Barge", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var tShip = Plugin.FindGameType("Ship");
            if (tShip != null && (GetComponent(tShip) != null || GetComponentInChildren(tShip, true) != null))
                return true;
            return false;
        }

        void DetectKind()
        {
            string t = Track?.Type ?? "";
            string nm = Track?.Name ?? "";
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
            if (!_air)
                _ship = CheckIsShip(t, nm, gn);
            string coal = Track?.Coalition ?? "";
            _neutral = coal.IndexOf("Neutral", StringComparison.OrdinalIgnoreCase) >= 0
                       || coal.IndexOf("Green", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal void Adopt(ReplayObject track)
        {
            Track = track;
            ClearAppliedLoadout();
            DetectKind();
            _ordnance = Plugin.IsOrdnancePublic(track.Type, track.Name);
            if (_ordnance) _ship = false;
            _drive = _ordnance ? transform : FindDrive();
            if (_drive == null) _drive = transform;
            _asleep = false;
            _visible = false;
            if (_ordnance)
            {
                enabled = true;
                transform.SetParent(null, true);
                if (_rend == null || _rend.Length == 0)
                    RefreshRenderers();
                for (int i = 0; i < _rend.Length; i++)
                    if (_rend[i] != null && !_disabledInPrefab.Contains(_rend[i])) _rend[i].enabled = true;
                _visible = true;
            }
        }

        void Awake()
        {
            string n = gameObject.name ?? "";
            _ordnance = Plugin.IsOrdnancePublic(Track?.Type, Track?.Name)
                || Plugin.IsOrdnancePublic(null, n.Replace("NOReplay_", ""));
            RefreshRenderers();
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
                    if (_rend[i] != null && !_disabledInPrefab.Contains(_rend[i])) _rend[i].enabled = true;
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
            // Runs at DefaultExecutionOrder(-200), after FixedUpdate() and before SlowUpdateRunner / Downwash / DownwashEffect Update().
            if (_air && !_neutral)
            {
                WriteUnitMotion(_spdMs, _velMs);
                DriveDownwashThrust(_visible && !_neutral, _haveThr ? _thr : 0.75f);
                return;
            }
            if (_ship && !_neutral)
            {
                WriteUnitMotion(_spdMs, _velMs);
                return;
            }
            if (!_air && !_ship && !_ordnance) return;
            if (!Plugin.AudioNear(gameObject, 800f) && !_air && !_ship) return;
            WriteUnitMotion(_spdMs, _velMs);
        }

        void OnDisable()
        {
            StopSurfaceFx(false);
        }

        void OnDestroy()
        {
            ClearStructural();
            DestroyDownwashTransforms();
            ReleaseGunVisuals();
            if (_trail != null)
            {
                UnityEngine.Object.Destroy(_trail.gameObject);
                _trail = null;
            }
            try
            {
                // Plugin.Log.LogInfo("GO weg " + name + " " + (Track != null ? Track.Name : "?")
                //     + " t=" + Plugin.Clock.Time.ToString("0.0"));
            }
            catch { }
        }

        void Start()
        {
            DetectKind();
            string t = Track?.Type ?? "";
            string nm = Track?.Name ?? "";
            string gn = gameObject.name ?? "";
            _ordnance = Plugin.IsOrdnancePublic(t, Track?.Name);
            _ship = !_ordnance && !_air && CheckIsShip(t, nm, gn);
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
                // Plugin.Log.LogInfo("Ghost-Hier " + gameObject.name
                // + " parent=" + par
                // + " children=" + transform.childCount
                // + " drive=" + _drive.name
                // + " rend=" + rc);
            }
            FreezePhysics();
            SilenceAirframe();
            if (!_ordnance)
                EnsureRadarsCurrent(true);
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
                Invoke(nameof(Recache), 2f);
                Bind();
                CacheFx();
                DumpFxNames();
            }
            if (_ship)
            {
                Bind();
                CacheShipFx();
                BindMotion();
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
            RefreshRenderers();
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
            RefreshRenderers();
            ForceSimplePhysics();
            FreezePhysics();
            SilenceAirframe();
            _killPsBound = false;
            SuppressDamageFx();
            DisableFlightAssist();
            CacheFx();
            AdoptOrphanFx();
            EnsureGunsCurrent(true);
            EnsureRadarsCurrent(true);
            CacheLocals();
            PaintMissingLivery();
            // Plugin.Log.LogInfo("Ghost-Recache " + gameObject.name
            // + " drive=" + Drive.name
            // + " kids=" + Drive.childCount
            // + " parts=" + _parts.Length
            // + " shaft=" + _shafts.Length
            // + " prop=" + _props.Length
            // + " gear=" + _gears.Length
            // + " eng=" + _engs.Length);
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
                    // Plugin.Log.LogInfo("Ghost-Simple " + gameObject.name);
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
                // Plugin.Log.LogInfo("Ghost-Vis " + gameObject.name + " -> " + best.name + " rend=" + bestR);
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
            // Plugin.Log.LogInfo("Ghost-Kids " + gameObject.name + " " + _lastKids + " -> " + n
            // + extra + " t=" + Plugin.Clock.Time.ToString("0.0"));
            _lastKids = n;
            if (ch != null && IsIgnored(ch)) return;
            RefreshRenderers();
            _liveryPainted = false;
            FreezePhysics();
            HideLoadoutBits();
            InvalidateStructure();
            EnsureGunsCurrent(true);
            EnsureRadarsCurrent(true);
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

        static bool HasSurfaceFxComponent(Transform tr)
        {
            var behs = tr.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behs.Length; i++)
            {
                var b = behs[i];
                if (b == null) continue;
                string tn = b.GetType().Name;
                if (tn == "DownwashEffect" || tn == "Downwash")
                    return true;
            }
            return false;
        }

        void CacheLocals()
        {
            var root = Drive;
            var all = root.GetComponentsInChildren<Transform>(true);
            if (_parts.Length > 0 && all.Length <= _parts.Length + 1)
            {
                ProtectGunParts();
                ProtectRadarParts();
                return;
            }
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
                    || nm.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Swash", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Gear", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Strut", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Unsprung", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Sprung", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Hinge", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Split", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Airbrake", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("Downwash", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("WaterWash", StringComparison.OrdinalIgnoreCase) >= 0
                    || nm.IndexOf("LandWash", StringComparison.OrdinalIgnoreCase) >= 0
                    || HasSurfaceFxComponent(tr);
                _parts[k] = tr;
                _partPar[k] = tr.parent;
                _partPos[k] = tr.localPosition;
                _partRot[k] = tr.localRotation;
                _partSkip[k] = skip;
                k++;
            }
            ProtectGunParts();
            ProtectRadarParts();
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
                var wantPos = _partPos[i];
                var havePos = tr.localPosition;
                if (havePos.x != wantPos.x || havePos.y != wantPos.y || havePos.z != wantPos.z)
                    tr.localPosition = wantPos;
                var wantRot = _partRot[i];
                var haveRot = tr.localRotation;
                if (haveRot.x != wantRot.x || haveRot.y != wantRot.y || haveRot.z != wantRot.z || haveRot.w != wantRot.w)
                    tr.localRotation = wantRot;
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
                RefreshRenderers();
            for (int i = 0; i < _rend.Length; i++)
            {
                var r = _rend[i];
                if (r == null) continue;
                if (RendererForcedOff(r)) { r.enabled = false; continue; }
                if (on && _disabledInPrefab.Contains(r)) continue;
                r.enabled = on;
            }
            if (_ordnance)
            {
                for (int i = 0; i < _rend.Length; i++)
                {
                    var r = _rend[i];
                    if (r == null) continue;
                    if (RendererForcedOff(r)) { r.enabled = false; continue; }
                    if (on && _disabledInPrefab.Contains(r)) continue;
                    r.enabled = on;
                }
                if (on)
                {
                    foreach (var r in GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || _disabledInPrefab.Contains(r) || RendererForcedOff(r)) continue;
                        if (IsAmmoInactive(r.gameObject)) continue;
                        r.enabled = true;
                        r.gameObject.layer = 0;
                        if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    }
                    IgniteMissileFx();
                }
                ToggleOrdnanceFx(on);
            }
            if (!on)
                StopSurfaceFx(false);
            if (_visible != on)
            {
                _visible = on;
                Plugin.SetMapIcon(gameObject, on);
            }
            ReapplyReplayHides();
        }

        internal void TryBoom(float t)
        {
            if (_boomed || !_ordnance || IsFlareTrack() || Track == null) return;
            float end = Track.RemovedAt;
            if (end < 0f && Track.Samples != null && Track.Samples.Count > 0)
                end = Track.Samples[Track.Samples.Count - 1].Time;
            if (end < 0f || t < end) return;
            _boomed = true;
            // Plugin.Log?.LogInfo("Boom-Trigger " + (Track.Name ?? name)
            //     + " removed=" + end.ToString("0.00") + " t=" + t.ToString("0.00"));
            PlayWarheadBoom(transform.position);
            SetGhostVisible(false);
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
            // Explicit death from ACMI.
            if (Track.RemovedAt >= 0f && t >= Track.RemovedAt) return false;
            // No RemovedAt: stay alive until the replay ends.
            // (No static/moving distinction — all units get the same treatment.)
            float end = Plugin.CurrentDoc != null ? Plugin.CurrentDoc.Duration : 0f;
            if (end > 0f && t >= end + 0.05f) return false;
            return true;
        }

        bool TryComputeTrackVelocity(float t, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (Track == null || Track.Samples == null || Track.Samples.Count < 2) return false;
            float t0 = Track.Samples[0].Time;
            float tEnd = Track.Samples[Track.Samples.Count - 1].Time;
            float ta = Mathf.Max(t0, t - 0.1f);
            float tb = Mathf.Min(tEnd, t + 0.1f);
            if (tb - ta < 0.01f) return false;
            if (!Track.TrySample(ta, out var sa) || !Track.TrySample(tb, out var sb)) return false;
            float dt = tb - ta;
            vel = new Vector3(sb.X - sa.X, sb.Y - sa.Y, sb.Z - sa.Z) / dt;
            return true;
        }

        void LateUpdate()
        {
            if (Track == null) return;
            if (!IsTrackAlive())
            {
                float end = Track.RemovedAt;
                if (end < 0f && Track.Samples.Count > 0)
                    end = Track.Samples[Track.Samples.Count - 1].Time;
                if (!_boomed && (end < 0f || Plugin.Clock.Time >= end))
                {
                    _boomed = true;
                    if (_ordnance && !IsFlareTrack())
                    {
                        // Plugin.Log?.LogInfo("Boom-Trigger " + (Track.Name ?? name)
                        //     + " removed=" + Track.RemovedAt.ToString("0.00")
                        //     + " t=" + Plugin.Clock.Time.ToString("0.00"));
                        PlayWarheadBoom(transform.position);
                    }
                    else if (!_ordnance)
                        PlayUnitWreckFx(transform.position, transform.rotation);
                }
                if (_visible)
                {
                    Plugin.ReleaseCameraIfOn(gameObject);
                    StopOrdnanceAudio();
                    SetGhostVisible(false);
                }
                if (!_ordnance) TickStructural(false);
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
            Quaternion q;
            if (s.HasQuat)
                q = new Quaternion(s.Qx, s.Qy, s.Qz, s.Qw);
            else if (Track.Samples.Count > 0 && Track.Samples[0].HasQuat)
                q = new Quaternion(Track.Samples[0].Qx, Track.Samples[0].Qy, Track.Samples[0].Qz, Track.Samples[0].Qw);
            else
                q = Quaternion.Euler(-s.Pitch, s.Yaw, -s.Roll);
            var drive = _drive != null ? _drive : transform;
            if (_air && Drive.childCount != _lastKids)
            {
                _lastKids = Drive.childCount;
                InvalidateGuns();
                RefreshRenderers();
                if (_visible) SetGhostVisible(true);
                _liveryPainted = false;
                CacheLocals();
                FreezePhysics();
                InvalidatePlaybackCaches();
                BindNozzles();
                BindMotion();
                _nextGunScan = 0f;
                _nextRadarScan = 0f;
            }
            drive.SetPositionAndRotation(p, q);
            if (!_ordnance)
            {
                EnsureGunsCurrent(false);
                EnsureRadarsCurrent(false);
            }
            if (_air)
            {
                if (!_surfBound) CaptureSurfaceRest();
                if (!_liveryPainted && ((Time.frameCount + gameObject.GetInstanceID()) & 15) == 0)
                    PaintMissingLivery();
                HoldLocals();
                HoldAirbrakes();
            }
            if (!_ordnance) AimGuns();
            TickGunVisuals();

            float nowT = Plugin.Clock.Time;
            bool rewound = _prevT >= 0f && nowT + 0.02f < _prevT;
            if (rewound)
            {
                if (_ordnance)
                {
                    StopOrdnanceAudio();
                    _startupPlayed = false;
                    _nextVoiceTry = 0f;
                    _ordPrevT = -1f;
                }
                else
                {
                    StopSurfaceFx(true);
                }
                if (TryComputeTrackVelocity(nowT, out var rVel))
                {
                    _velMs = rVel;
                    _spdMs = rVel.magnitude;
                }
                else
                {
                    _velMs = Vector3.zero;
                    _spdMs = 0f;
                }
                _prevX = s.X;
                _prevY = s.Y;
                _prevZ = s.Z;
                _prevLocalPos = p;
                _prevT = nowT;
            }
            else if (_prevT < 0f)
            {
                if (TryComputeTrackVelocity(nowT, out var initVel))
                {
                    _velMs = initVel;
                    _spdMs = initVel.magnitude;
                }
                _prevX = s.X;
                _prevY = s.Y;
                _prevZ = s.Z;
                _prevLocalPos = p;
                _prevT = nowT;
            }
            else if (nowT > _prevT)
            {
                float dtS = nowT - _prevT;
                if (dtS < 1.0f && (_prevX != 0f || _prevY != 0f || _prevZ != 0f))
                {
                    float dx = s.X - _prevX;
                    float dy = s.Y - _prevY;
                    float dz = s.Z - _prevZ;
                    _velMs = new Vector3(dx, dy, dz) / Mathf.Max(dtS, 0.001f);
                    _spdMs = _velMs.magnitude;
                }
                else if (TryComputeTrackVelocity(nowT, out var seekVel))
                {
                    _velMs = seekVel;
                    _spdMs = seekVel.magnitude;
                }
                _prevX = s.X;
                _prevY = s.Y;
                _prevZ = s.Z;
                _prevLocalPos = p;
                _prevT = nowT;
            }

            if (_ordnance && _visible && !_ordAudioOn) IgniteMissileFx();
            if (_ordnance) LateOrdnanceAudio(s);

            Velocities[GetInstanceID()] = _velMs;
            if (_air && gameObject == Plugin._ghost)
                CheckCameraReposition(_velMs);
            for (int i = 0; i < _rbs.Length; i++)
            {
                var rb = _rbs[i];
                if (rb == null || IsIgnored(rb.transform)) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.velocity = _velMs;
                rb.angularVelocity = Vector3.zero;
            }

            if (_air && !_neutral)
            {
                ApplyFx(s);
            }
            else
            {
                WriteUnitMotion(_spdMs, _velMs);
                if (_ship && !_neutral)
                    DriveShipFx(_spdMs, _velMs);
                if (NearMe(90f))
                {
                    if (_ship) DriveShipAudio(_spdMs);
                    else if (!_ordnance) DriveVehicleAudio(_spdMs);
                }
            }
            if (!_ordnance && _visible)
                TickRadars();
            if (!_ordnance)
            {
                TickStructural(true);
                TickAmmo();
                ReapplyInactiveOrdnance();
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
            ReapplyReplayHides();
            if (!_air && !_ship && !_ordnance)
                Plugin.KillLaserBeams(gameObject);
        }

        void SetVisible(bool on)
        {
            if (_visible == on) return;
            _visible = on;
            if (!on)
                StopSurfaceFx(false);
            for (int i = 0; i < _rend.Length; i++)
            {
                var r = _rend[i];
                if (r == null) continue;
                if (on && (RendererForcedOff(r) || _disabledInPrefab.Contains(r))) continue;
                r.enabled = on;
            }
            if (on) ReapplyInactiveOrdnance();
        }

        void BindMotion()
        {
            Bind();
            var list = new List<SpeedWrite>();
            var units = new List<Component>();
            var behs = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behs.Length; i++)
            {
                var c = behs[i];
                if (c == null) continue;
                var ct = c.GetType();
                var f = FindInstField(ct, "speed");
                if (f != null && f.FieldType == typeof(float))
                    list.Add(new SpeedWrite { Target = c, Field = f });
                if (_unitType != null && _unitType.IsAssignableFrom(ct))
                    units.Add(c);
                else if (ct.Name == "Aircraft" || ct.Name == "Ship" || ct.Name == "GroundVehicle")
                    units.Add(c);
                if (_aircraftComp == null && ct.Name == "Aircraft")
                    _aircraftComp = c;
            }
            _speeds = list.ToArray();
            _unitComps = units.ToArray();
            _motionRbs = GetComponentsInChildren<Rigidbody>(true);
            _motionBound = true;
        }

        internal void InvalidatePlaybackCaches()
        {
            _motionBound = false;
            _engineListReady = false;
            _nozzlesBound = false;
            _ignitionBound = false;
            _throttleBound = false;
        }

        void WriteUnitMotion(float spdMs, Vector3 vel)
        {
            if (!_motionBound) BindMotion();
            for (int i = 0; i < _speeds.Length; i++)
            {
                try { _speeds[i].Field.SetValue(_speeds[i].Target, spdMs); } catch { }
            }
            for (int i = 0; i < _motionRbs.Length; i++)
            {
                var rb = _motionRbs[i];
                if (rb == null) continue;
                try
                {
                    rb.velocity = vel;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }

            if (_unitComps.Length > 0)
            {
                bool active = _visible && !_neutral && IsTrackAlive();
                float detail = 0f;
                if (active)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        float dist = Vector3.Distance(transform.position, cam.transform.position);
                        detail = Mathf.Max(2f, 2500f / Mathf.Max(dist, 1f));
                    }
                    else
                    {
                        detail = 100f;
                    }
                }

                for (int i = 0; i < _unitComps.Length; i++)
                {
                    var u = _unitComps[i];
                    if (u == null) continue;
                    try { _fUnitDisplayDetail?.SetValue(u, detail); } catch { }
                    try { _fUnitAirDensity?.SetValue(u, 1f); } catch { }
                }

                if (_air && active && Time.unscaledTime >= _nextRadarAltAt)
                {
                    _nextRadarAltAt = Time.unscaledTime + 0.05f;
                    bool updatedAlt = false;
                    if (_aircraftComp != null && _mCheckRadarAlt != null)
                    {
                        try
                        {
                            _mCheckRadarAlt.Invoke(_aircraftComp, null);
                            updatedAlt = true;
                        }
                        catch { }
                    }
                    if (_fUnitRadarAlt != null)
                    {
                        const int surfaceMask = (1 << 6) | (1 << 13); // StaticsMask | ShipsMask
                        if (Physics.Raycast(transform.position, Vector3.down, out var hit, 5000f, surfaceMask, QueryTriggerInteraction.Ignore))
                        {
                            for (int i = 0; i < _unitComps.Length; i++)
                            {
                                var u = _unitComps[i];
                                if (u == null) continue;
                                try
                                {
                                    float cur = updatedAlt && _fUnitRadarAlt.GetValue(u) is float ca ? ca : float.MaxValue;
                                    if (hit.distance < cur || !updatedAlt)
                                        _fUnitRadarAlt.SetValue(u, hit.distance);
                                }
                                catch { }
                            }
                        }
                        else if (!updatedAlt)
                        {
                            float fallbackAlt = ReadAgl();
                            for (int i = 0; i < _unitComps.Length; i++)
                            {
                                var u = _unitComps[i];
                                if (u == null) continue;
                                try { _fUnitRadarAlt.SetValue(u, fallbackAlt); } catch { }
                            }
                        }
                    }
                }
            }
        }

        static MethodInfo? _mSetGearBool;
        static Type? _aircraftType;
    }
}