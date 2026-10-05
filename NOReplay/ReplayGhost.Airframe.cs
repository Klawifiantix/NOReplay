using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        void SilenceAirframe()
        {
            if (_ordnance) return;
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null || beh is ReplayGhost) continue;
                if (IsIgnored(beh.transform)) continue;
                string n = beh.GetType().Name;
                if (n.IndexOf("Render", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Orbit", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n == "RotorShaft" || n == "TurbineEngine" || n == "LandingGear"
                    || n == "SoftBodyRotor" || n == "SwashRotor" || n == "PropFan"
                    || n == "DuctedFan" || n == "Turbofan" || n == "Turbojet"
                    || n == "ConstantSpeedProp" || n == "Aircraft"
                    || n == "JetNozzle") continue;
                if (n.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Counter", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Emitter", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n == "NavLights") continue;
                beh.enabled = false;
            }
            MuteAllSources();
        }

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
                || p.IndexOf("prop", StringComparison.Ordinal) >= 0;
        }

        void HealParts()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var c in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n.IndexOf("Part", StringComparison.Ordinal) >= 0
                    || n == "Unit" || n == "Aircraft" || n == "Ship" || n == "GroundVehicle")
                {
                    try
                    {
                        var hp = FindInstField(c.GetType(), "hitPoints");
                        if (hp != null && hp.FieldType == typeof(float))
                            hp.SetValue(c, 100f);
                    }
                    catch { }
                    try
                    {
                        var dis = FindInstField(c.GetType(), "disabled");
                        if (dis != null && dis.FieldType == typeof(bool))
                            dis.SetValue(c, false);
                    }
                    catch { }
                }
                if (n.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Leak", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("FireFx", StringComparison.OrdinalIgnoreCase) >= 0
                    || n == "SpecialSmokeEjector")
                {
                    if (c is Behaviour b) b.enabled = false;
                }
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.HasProperty("_HitPoints"))
                        m.SetFloat("_HitPoints", 100f);
                }
            }
        }

        void SuppressDamageFx()
        {
            if (_ordnance) return;
            HealParts();
            var pt = ParticleType();
            if (pt == null) return;
            if (!_killPsBound)
            {
                _killPsBound = true;
                var list = new System.Collections.Generic.List<Component>();
                foreach (var c in GetComponentsInChildren(pt, true))
                {
                    if (c == null) continue;
                    var tr = (c as Component)?.transform;
                    string path = tr != null ? tr.name : "";
                    if (tr != null && tr.parent != null) path = tr.parent.name + "/" + path;
                    if (KeepFxName(path.ToLowerInvariant())) continue;
                    list.Add(c);
                }
                _killPs = list.ToArray();
            }
            var stop = pt.GetMethod("Stop", Type.EmptyTypes);
            var clear = pt.GetMethod("Clear", Type.EmptyTypes);
            var emBool = pt.GetProperty("enableEmission");
            for (int i = 0; i < _killPs.Length; i++)
            {
                var c = _killPs[i];
                if (c == null) continue;
                try
                {
                    emBool?.SetValue(c, false);
                    stop?.Invoke(c, null);
                    clear?.Invoke(c, null);
                }
                catch { }
            }
        }


        void AdoptOrphanFx()
        {
            AdoptType(_shaftType, ref _shafts);
            AdoptType(_cspType, ref _props);
            AdoptType(_propType, ref _props);
            AdoptType(_fanType, ref _props);
            AdoptType(_tfanType, ref _tfans);
            AdoptType(_gearType, ref _gears);
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

        void CacheFx()
        {
            Bind();
            _shafts = CollectFx(_shaftType);
            var p1 = CollectFx(_propType);
            var p2 = CollectFx(_cspType);
            var p3 = CollectFx(_fanType);
            _props = new Component[p1.Length + p2.Length + p3.Length];
            p1.CopyTo(_props, 0);
            p2.CopyTo(_props, p1.Length);
            p3.CopyTo(_props, p1.Length + p2.Length);
            _engs = CollectFx(_engType);
            _tfans = CollectFx(_tfanType);
            _gears = CollectFx(_gearType);
        }


        void DriveCspAudio(Component c, float rpm)
        {
            DriveEngineSrc(c, _fCspAudio?.GetValue(c) ?? FieldOn(c, "propAudio"),
                FieldOn(c, "exteriorSound"), rpm > 1f ? 0.55f : 0f, 0.95f);
        }

        void DriveRotorAudio(Component c, float ratio)
        {
            DriveEngineSrc(c, _fRotorSrc?.GetValue(c) ?? FieldOn(c, "rotorSource"),
                FieldOn(c, "exteriorSound"), ratio * 0.7f, 0.9f + 0.15f * ratio);
        }

        void DriveTurbofanAudio(Component c, float ratio)
        {
            DriveEngineSrc(c, _fTfanSrc?.GetValue(c) ?? FieldOn(c, "turbineAudio"),
                FieldOn(c, "exteriorSound") ?? FieldOn(c, "clip"),
                ratio > 0.05f ? 0.5f : 0f, 0.7f + 0.4f * ratio);
        }

        void DriveFanAudio(Component c, float ratio)
        {
            object? src = _fFanSrc?.GetValue(c) ?? _fPfanSrc?.GetValue(c)
                ?? FieldOn(c, "fanSource") ?? FieldOn(c, "source");
            DriveEngineSrc(c, src, FieldOn(c, "exteriorSound"),
                ratio > 0.05f ? 0.45f : 0f, 0.85f + 0.2f * ratio);
        }

        void DriveJetAudio(Component c, float ratio)
        {
            if (c is Behaviour jb) jb.enabled = false;
            var rpmF = c.GetType().GetField("rpm", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? c.GetType().GetField("currentRPM", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var maxF = c.GetType().GetField("maxRPM", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            float max = 10000f;
            if (maxF?.GetValue(c) is float mx && mx > 1f) max = mx;
            rpmF?.SetValue(c, max * Mathf.Clamp01(ratio));
            object? src = FieldOn(c, "turbineAudio");
            DriveEngineSrc(c, src, null, ratio > 0.05f ? 0.55f : 0f, 0.75f + 0.35f * ratio);
        }

        void DriveOneEngine(Component c, float ratio)
        {
            if (c == null) return;
            switch (c.GetType().Name)
            {
                case "RotorShaft":
                    DriveRotorAudio(c, ratio);
                    break;
                case "ConstantSpeedProp":
                    DriveCspAudio(c, 1800f);
                    break;
                case "Turbojet":
                case "Turbofan":
                case "TurbineEngine":
                    DriveJetAudio(c, ratio);
                    break;
                case "DuctedFan":
                case "PropFan":
                    DriveFanAudio(c, ratio);
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
                return;
            }
            Bind();
            float dt = Time.unscaledDeltaTime * Mathf.Max(Plugin.Clock.Speed, 0.01f);
            if (float.IsNaN(_spawnY)) _spawnY = s.Y;
            float agl = ReadAgl();
            float spd = _spdMs;
            if (_prevT >= 0f && Plugin.Clock.Time > _prevT)
            {
                float dtS = Mathf.Max(Plugin.Clock.Time - _prevT, 0.001f);
                float dx = s.X - _prevX;
                float dy = s.Y - _prevY;
                float dz = s.Z - _prevZ;
                _velMs = new Vector3(dx, dy, dz) / dtS;
                spd = _velMs.magnitude;
                _spdMs = spd;
            }
            _prevX = s.X; _prevY = s.Y; _prevZ = s.Z; _prevT = Plugin.Clock.Time;
            if (s.HasTas && s.Tas > 0.05f)
            {
                float tas = s.Tas;
                if (tas > 15f && spd > 1f && tas > spd * 1.6f)
                    tas = tas / 1.94384f;
                spd = tas;
                _spdMs = tas;
            }
            if (spd > 0.5f)
                _velMs = transform.forward * spd;
            WriteUnitMotion(spd, _velMs);
            DisableFlightAssist();
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
                _startedAir = ground.distance > 18f;
                if (_startedAir) _fold = 1f;
            }
            if (s.HasGear)
            {
                _haveGearAcmi = true;
                _gearFromAcmi = s.Gear > 0.5f;
            }
            bool gearDown = _haveGearAcmi ? _gearFromAcmi : !airborne;
            if (_startedAir && gearDown && Physics.Raycast(transform.position + Vector3.up * 4f, Vector3.down, out var still, 1200f, ~0, QueryTriggerInteraction.Ignore) && still.distance > 18f)
                gearDown = false;
            if (_startedAir && !gearDown) _fold = 1f;
            ApplyReplayGear(gearDown);
            foreach (var beh in GetComponents<MonoBehaviour>())
            {
                if (beh == null || beh.GetType().Name != "Aircraft") continue;
                if (_fIgnition == null)
                    _fIgnition = beh.GetType().GetField("Ignition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fIgnition?.SetValue(beh, true);
            }
            if (_shafts.Length > 0)
            {
                for (int i = 0; i < _shafts.Length; i++)
                {
                    var c = _shafts[i];
                    if (c == null) continue;
                    if (c is Behaviour sb) sb.enabled = false;
                    float nom = 300f;
                    if (_fNomRpm != null)
                    {
                        object? v = _fNomRpm.GetValue(c);
                        if (v is float f) nom = f;
                    }
                    _fStartup?.SetValue(c, 1f);
                    _fRotorUnfold?.SetValue(c, true);
                    float rad = Plugin.Clock.Paused ? 0f : nom * 0.1047f;
                    _fAngSpeed?.SetValue(c, rad);
                    if (!Plugin.Clock.Paused)
                    {
                        var hub = _fRotorHub?.GetValue(c) as Transform;
                        float dir = 1f;
                        if (_fRotorDir != null)
                        {
                            object? dv = _fRotorDir.GetValue(c);
                            if (dv != null) dir = Convert.ToSingle(dv);
                        }
                        if (hub != null)
                            hub.Rotate(0f, nom * 6f * dir * dt, 0f, Space.Self);
                    }
                    DriveRotorAudio(c, Plugin.Clock.Paused ? 0f : 1f);
                }
            }
            if (_props.Length > 0 && !Plugin.Clock.Paused)
            {
                for (int i = 0; i < _props.Length; i++)
                {
                    var c = _props[i];
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    float rpm = airborne ? 2000f : 900f;
                    if (tn == "ConstantSpeedProp")
                    {
                        _fCspRpm?.SetValue(c, rpm);
                        var hubGo = _fCspHub?.GetValue(c) as GameObject;
                        float dir = 1f;
                        if (_fCspDir != null)
                        {
                            object? dv = _fCspDir.GetValue(c);
                            if (dv != null) dir = Convert.ToSingle(dv);
                        }
                        if (hubGo != null)
                            hubGo.transform.Rotate(0f, 0f, rpm * -6f * dir * dt, Space.Self);
                        DriveCspAudio(c, rpm);
                    }
                    else if (tn == "DuctedFan" || tn == "Turbofan" || tn == "PropFan")
                    {
                        _fFanRpm?.SetValue(c, rpm);
                        _fPropRpm?.SetValue(c, rpm);
                        DriveFanAudio(c, airborne ? 0.9f : 0.45f);
                    }
                    else
                    {
                        float nom = 2200f;
                        if (_fPropNom != null && _fPropNom.GetValue(c) is float n) nom = n;
                        rpm = nom * (airborne ? 0.95f : 0.45f);
                        _fPropRpm?.SetValue(c, rpm);
                        _fPropRatio?.SetValue(c, rpm / Mathf.Max(nom, 1f));
                        DriveFanAudio(c, airborne ? 0.9f : 0.45f);
                    }
                }
            }
            if (_engs.Length > 0)
            {
                for (int i = 0; i < _engs.Length; i++)
                {
                    var c = _engs[i];
                    if (c == null) continue;
                    _fOperable?.SetValue(c, true);
                    _fHasFuel?.SetValue(c, true);
                    _fStarted?.SetValue(c, 1f);
                    _fThrottle?.SetValue(c, airborne ? 0.7f : 0.2f);
                    float rpm = 0.3f;
                    if (_engType != null)
                    {
                        var fMax = _engType.GetField("maxRPM", BindingFlags.Instance | BindingFlags.Public);
                        float max = 10000f;
                        if (fMax != null && fMax.GetValue(c) is float mx) max = mx;
                        rpm = max * (airborne ? 0.8f : 0.35f);
                    }
                    _fRpm?.SetValue(c, rpm);
                    DriveTurbofanAudio(c, airborne ? 0.8f : 0.35f);
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
                    if (_fTfanMax != null && _fTfanMax.GetValue(c) is float mx) max = mx;
                    float rpm = max * (airborne ? 0.8f : 0.35f);
                    _fTfanRpm?.SetValue(c, rpm);
                    DriveTurbofanAudio(c, airborne ? 0.8f : 0.35f);
                }
            }
            DriveAircraftEngines(airborne ? 0.8f : 0.4f);
            if (s.HasThrottle)
            {
                _haveThr = true;
                _thr = s.Throttle;
            }
            float thr = _haveThr ? _thr : 0.45f;
            WriteThrottle(thr);
            CloseAirbrakes();
            DriveJetExhaust(true, thr);
            if (_gears.Length > 0)
            {
                float target = gearDown ? 0f : 1f;
                _fold = Mathf.MoveTowards(_fold, target, 0.6f * dt);
                for (int i = 0; i < _gears.Length; i++)
                {
                    var c = _gears[i];
                    if (c == null) continue;
                    _fFold?.SetValue(c, _fold);
                    var hinge = _fGearHinge?.GetValue(c) as Transform;
                    if (hinge != null)
                    {
                        Vector3 baseAng = _fHingeBase != null && _fHingeBase.GetValue(c) is Vector3 ba ? ba : hinge.localEulerAngles;
                        Vector3 basePos = _fHingePos != null && _fHingePos.GetValue(c) is Vector3 bp ? bp : hinge.localPosition;
                        Vector3 motion = _fHingeMotion != null && _fHingeMotion.GetValue(c) is Vector3 mo ? mo : Vector3.zero;
                        float deg = _fFoldDeg != null && _fFoldDeg.GetValue(c) is float fd ? fd : 90f;
                        hinge.localEulerAngles = baseAng + new Vector3(deg * _fold, 0f, 0f);
                        hinge.localPosition = Vector3.Lerp(basePos, basePos + motion, _fold);
                    }
                    var doors = _fGearDoors?.GetValue(c) as System.Collections.IEnumerable;
                    if (doors != null)
                    {
                        foreach (var d in doors)
                        {
                            if (d == null) continue;
                            var am = d.GetType().GetMethod("Animate");
                            am?.Invoke(d, new object[] { 1f - _fold });
                        }
                    }
                    // nicht MuteOn: LandingGear sitzt oft am Root und killt Rotor/Jet-Sources
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

        void CaptureSurfaceRest()
        {
            if (!_air || _surfBound) return;
            BindSurfaceRefl();
            if (_surfType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fJobs = _surfType.GetField("JobFields", flags);
            var fUp = _surfType.GetField("splitUpper", flags);
            var fLo = _surfType.GetField("splitLower", flags);
            var fMax = _surfType.GetField("maxSplit", flags);
            var fSplit = _surfType.GetField("splitAmount", flags);
            var meshes = new System.Collections.Generic.List<Transform>();
            var rests = new System.Collections.Generic.List<Quaternion>();
            int pending = 0;
            int splits = 0;
            foreach (var c in GetComponentsInChildren(_surfType, true))
            {
                if (c == null) continue;
                if (_fFlap?.GetValue(c) is bool flap && flap) continue;
                object mv = fMax?.GetValue(c);
                float max = mv is float f ? f : 0f;
                if (max <= 0f) continue;
                splits++;
                Quaternion restSplit = Quaternion.identity;
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
                            object rs = fields.GetType().GetField("restingSplitRotation")?.GetValue(fields);
                            if (rs is Quaternion qs) { restSplit = qs; have = true; }
                        }
                    }
                }
                if (!have)
                {
                    pending++;
                    continue;
                }
                fSplit?.SetValue(c, 0f);
                var up = fUp?.GetValue(c) as Transform;
                var lo = fLo?.GetValue(c) as Transform;
                if (up != null)
                {
                    up.localRotation = restSplit;
                    RememberBrake(up, restSplit);
                    meshes.Add(up);
                    rests.Add(restSplit);
                }
                if (lo != null)
                {
                    lo.localRotation = restSplit;
                    RememberBrake(lo, restSplit);
                    meshes.Add(lo);
                    rests.Add(restSplit);
                }
                try { UnityEngine.Object.Destroy(c); } catch { }
            }
            if (splits == 0 || pending > 0)
                return;
            _surfMesh = meshes.ToArray();
            _surfRest = rests.ToArray();
            _surfBound = true;
            Plugin.Log.LogInfo("Surface-Rest " + name + " splits=" + splits);
        }

        void DisableFlightAssist()
        {
            if (!_air) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in new[] { "ControlsFilter", "HeloControlsFilter", "RelaxedStabilityController", "MagicTorqueController", "TiltWingController", "SwingWingController", "CompoundHeloController", "PilotPlayerState" })
            {
                var t = Plugin.FindGameType(name);
                if (t == null) continue;
                foreach (var c in GetComponentsInChildren(t, true))
                {
                    if (c is Behaviour b) b.enabled = false;
                }
            }
            var act = Plugin.FindGameType("Aircraft");
            if (act == null) return;
            var ac = GetComponent(act) ?? GetComponentInChildren(act, true);
            if (ac == null) return;
            try { act.GetMethod("SetFlightAssist", flags)?.Invoke(ac, new object[] { false }); } catch { }
            try
            {
                var gf = act.GetMethod("GetControlsFilter", flags);
                var filter = gf?.Invoke(ac, null);
                filter?.GetType().GetMethod("SetFlyByWireParameters", flags)
                    ?.Invoke(filter, new object[] { false, Array.Empty<float>() });
                filter?.GetType().GetMethod("SetAutoHover", flags)
                    ?.Invoke(filter, new object[] { false });
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
                        _gsLockedUp = Enum.Parse(_gearStateEnum, "LockedRetracted");
                        _gsLockedDown = Enum.Parse(_gearStateEnum, "LockedExtended");
                        _mSetGearState = act.GetMethod("SetGear", new[] { _gearStateEnum });
                    }
                }
                if (_mSetGearState != null)
                    _mSetGearState.Invoke(ac, new[] { gearDown ? _gsLockedDown : _gsLockedUp });
            }
            catch { }
        }

        void NeutralizeSurfaces()
        {
            if (!_air) return;
            if (_surfDone) return;
            BindSurfaceRefl();
            CaptureSurfaceRest();
            var act = Plugin.FindGameType("Aircraft");
            if (act != null)
            {
                var ac = GetComponent(act) ?? GetComponentInChildren(act, true);
                if (ac != null)
                {
                    try
                    {
                        var inputs = _mGetInputs?.Invoke(ac, null);
                        if (inputs != null)
                        {
                            var t = inputs.GetType();
                            t.GetField("pitch")?.SetValue(inputs, 0f);
                            t.GetField("roll")?.SetValue(inputs, 0f);
                            t.GetField("yaw")?.SetValue(inputs, 0f);
                            t.GetField("brake")?.SetValue(inputs, 0f);
                        }
                    }
                    catch { }
                }
            }
            if (_surfType != null)
            {
                foreach (var c in GetComponentsInChildren(_surfType, true))
                {
                    if (c == null) continue;
                    try { _mSetLocked?.Invoke(c, new object[] { true }); } catch { }
                    if (c is Behaviour b) b.enabled = false;
                }
            }
            for (int i = 0; i < _surfMesh.Length; i++)
            {
                if (_surfMesh[i] == null) continue;
                _surfMesh[i].localRotation = _surfRest[i];
            }
            _surfDone = true;
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
            Invoke(nameof(KillJetFlames), 0.5f);
            Invoke(nameof(KillJetFlames), 2f);
            MuteAllSources();
            _shafts = Array.Empty<Component>();
            _props = Array.Empty<Component>();
            _engs = Array.Empty<Component>();
            _tfans = Array.Empty<Component>();
            _gears = Array.Empty<Component>();
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

        void WriteThrottle(float throttle)
        {
            var act = Plugin.FindGameType("Aircraft");
            if (act == null) return;
            var ac = GetComponent(act) ?? GetComponentInChildren(act, true);
            if (ac == null) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var inputs = act.GetMethod("GetInputs", flags)?.Invoke(ac, null);
                if (inputs == null) return;
                var it = inputs.GetType();
                it.GetField("throttle")?.SetValue(inputs, Mathf.Clamp01(throttle));
                it.GetField("brake")?.SetValue(inputs, 0f);
            }
            catch { }
        }

        void DriveJetExhaust(bool on, float throttle)
        {
            var jn = Plugin.FindGameType("JetNozzle");
            if (jn == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            throttle = on ? Mathf.Clamp01(throttle) : 0f;
            float rpmRatio = on ? Mathf.Lerp(0.25f, 1f, throttle) : 0f;
            float thrustRatio = throttle;
            bool allowAb = on && throttle > 0.92f;
            var mThrust = jn.GetMethod("Thrust", flags);
            foreach (var c in GetComponentsInChildren(jn, true))
            {
                if (c == null) continue;
                try
                {
                    mThrust?.Invoke(c, new object[] { 0f, rpmRatio, thrustRatio, throttle, allowAb });
                    var haze = jn.GetField("heatHaze", flags)?.GetValue(c);
                    if (haze != null)
                    {
                        var ht = haze.GetType();
                        ht.GetMethod("UpdateParticles")?.Invoke(haze, new object[] { thrustRatio, rpmRatio, _spdMs });
                    }
                    var abs = jn.GetField("afterburners", flags)?.GetValue(c) as Array;
                    if (abs == null) continue;
                    float abAmt = allowAb ? Mathf.Clamp01((throttle - 0.92f) / 0.08f) : 0f;
                    foreach (var ab in abs)
                    {
                        if (ab == null) continue;
                        var at = ab.GetType();
                        at.GetField("afterburnerAmount", flags)?.SetValue(ab, abAmt);
                        var flame = at.GetField("flameRenderer", flags)?.GetValue(ab) as Renderer;
                        if (flame != null)
                        {
                            flame.enabled = abAmt > 0.01f;
                            if (flame.material != null)
                            {
                                flame.material.SetFloat("_Brightness", abAmt);
                                flame.material.SetFloat("_Temperature", abAmt);
                            }
                        }
                        var glowR = at.GetField("nozzleGlowRenderer", flags)?.GetValue(ab) as Renderer;
                        if (glowR != null) glowR.enabled = abAmt > 0.01f;
                    }
                }
                catch { }
            }
        }

        internal void PaintMissingLivery()
        {
            Texture? tex = null;
            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty("_Livery"))
                    {
                        var t = mat.GetTexture("_Livery");
                        if (t != null) { tex = t; break; }
                    }
                    if (tex == null && mat.HasProperty("_BaseMap"))
                        tex = mat.GetTexture("_BaseMap");
                    if (tex == null && mat.HasProperty("_MainTex"))
                        tex = mat.GetTexture("_MainTex");
                }
                if (tex != null) break;
            }
            if (tex == null) return;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                var mats = r.materials;
                if (mats == null) continue;
                bool dirty = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty("_Livery") && mat.GetTexture("_Livery") == null && tex != null)
                    {
                        mat.SetTexture("_Livery", tex);
                        dirty = true;
                    }
                    if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null && tex != null)
                    {
                        mat.SetTexture("_BaseMap", tex);
                        dirty = true;
                    }
                    if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == null && tex != null)
                    {
                        mat.SetTexture("_MainTex", tex);
                        dirty = true;
                    }
                }
                if (dirty) r.materials = mats;
            }
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


        void DumpFxNames()
        {
            if (_fxDumpLeft <= 0) return;
            _fxDumpLeft--;
            var names = new System.Collections.Generic.List<string>();
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? Plugin.FindGameType("ParticleSystem");
            if (pt != null)
            {
                foreach (var c in GetComponentsInChildren(pt, true))
                {
                    if (c == null) continue;
                    var tr = (c as Component)?.transform;
                    string path = tr != null ? tr.name : c.name;
                    if (tr != null && tr.parent != null) path = tr.parent.name + "/" + tr.name;
                    names.Add("PS:" + path);
                }
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name ?? "";
                if (n.IndexOf("flare", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("chaff", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("counter", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("decoy", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("cm", StringComparison.OrdinalIgnoreCase) >= 0)
                    names.Add("GO:" + n);
            }
            Plugin.Log.LogInfo("FX-Dump " + gameObject.name + " n=" + names.Count
                + " " + string.Join(" | ", names.ToArray()));
        }


        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _shaftType = Plugin.FindGameType("RotorShaft");
            _propType = Plugin.FindGameType("PropFan");
            _cspType = Plugin.FindGameType("ConstantSpeedProp");
            _fanType = Plugin.FindGameType("DuctedFan");
            _tfanType = Plugin.FindGameType("Turbofan");
            _tjetType = Plugin.FindGameType("Turbojet");
            _acType = Plugin.FindGameType("Aircraft");
            _engType = Plugin.FindGameType("TurbineEngine");
            if (_tjetType != null)
            {
                _fTjetSrc = _tjetType.GetField("turbineAudio", flags);
                _fTjetRpm = _tjetType.GetField("rpm", flags);
                _fTjetMax = _tjetType.GetField("maxRPM", flags);
            }
            if (_acType != null)
                _fAcEngines = _acType.GetField("engines", flags);
            _gearType = Plugin.FindGameType("LandingGear");
            if (_shaftType != null)
            {
                _fAngSpeed = _shaftType.GetField("angularSpeed", flags);
                _fAngPos = _shaftType.GetField("angularPosition", flags);
                _fNomRpm = _shaftType.GetField("nominalRPM", flags);
                _fStartup = _shaftType.GetField("startupProgress", flags);
                _fRotorSrc = _shaftType.GetField("rotorSource", flags);
                _fRotorExt = _shaftType.GetField("exteriorSound", flags);
                _fRotorHub = _shaftType.GetField("hubRotator", flags);
                _fRotorDir = _shaftType.GetField("directionMult", flags);
                _fRotorUnfold = _shaftType.GetField("unfolded", flags);
                _mAnimate = _shaftType.GetMethod("AnimateRotor", flags);
            }
            if (_tfanType != null)
            {
                _fTfanSrc = _tfanType.GetField("turbineAudio", flags);
                _fTfanRpm = _tfanType.GetField("currentRPM", flags);
                _fTfanMax = _tfanType.GetField("maxRPM", flags);
            }
            if (_propType != null)
            {
                _fPropRpm = _propType.GetField("currentRPM", flags);
                _fPropRatio = _propType.GetField("rpmRatio", flags);
                _fPropNom = _propType.GetField("nominalRPM", flags);
            }
            if (_cspType != null)
            {
                _fCspRpm = _cspType.GetField("RPM", flags);
                _fCspHub = _cspType.GetField("hubVisible", flags);
                _fCspDir = _cspType.GetField("turnDirection", flags);
                _fCspAudio = _cspType.GetField("propAudio", flags);
                _fCspVol = _cspType.GetField("volumeBase", flags);
                _fCspPitch = _cspType.GetField("pitchBase", flags);
            }
            if (_fanType != null)
            {
                _fFanRpm = _fanType.GetField("rpm", flags);
                _fFanSrc = _fanType.GetField("fanSource", flags);
            }
            if (_propType != null)
                _fPfanSrc = _propType.GetField("source", flags);
            if (_engType != null)
            {
                _fThrottle = _engType.GetField("throttle", flags);
                _fRpm = _engType.GetField("currentRPM", flags);
                _fStarted = _engType.GetField("startedAmount", flags);
                _fOperable = _engType.GetField("operable", flags);
                _fHasFuel = _engType.GetField("hasFuel", flags);
            }
            if (_gearType != null)
            {
                _fFold = _gearType.GetField("foldAmount", flags);
                _fGearDoors = _gearType.GetField("gearDoors", flags);
                _fGearHinge = _gearType.GetField("gearHinge", flags);
                _fHingeBase = _gearType.GetField("hingeBaseAngles", flags);
                _fFoldDeg = _gearType.GetField("foldDegrees", flags);
                _fHingePos = _gearType.GetField("hingeBasePos", flags);
                _fHingeMotion = _gearType.GetField("hingeFoldMotion", flags);
                _mSetGear = _gearType.GetMethod("LandingGear_OnSetGear", flags);
                _mMoveGear = _gearType.GetMethod("MoveGear", flags);
                _mUpdateParts = _gearType.GetMethod("UpdateMovingParts", flags);
            }
        }


    }
}