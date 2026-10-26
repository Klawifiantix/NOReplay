using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static Type? _downwashType;
        static Type? _downwashEffectType;
        static Type? _unitType;
        static FieldInfo? _fUnitRadarAlt;
        static FieldInfo? _fUnitDisplayDetail;
        static FieldInfo? _fUnitAirDensity;
        static MethodInfo? _mCheckRadarAlt;
        static FieldInfo? _fDwSources;
        static FieldInfo? _fDwDustTr;
        static FieldInfo? _fDwWaterTr;
        static FieldInfo? _fDwDustSys;
        static FieldInfo? _fDwWaterSys;
        static MethodInfo? _mDwUpdateSystems;
        static MethodInfo? _mDwCheckSurface;
        static FieldInfo? _fDwSrcObj;
        static FieldInfo? _fDwSrcThrust;
        static FieldInfo? _fDwSrcMaxThrust;
        static MethodInfo? _mDwSrcInit;
        static FieldInfo? _fDweLand;
        static FieldInfo? _fDweWater;
        static MethodInfo? _mDweStop;
        static MethodInfo? _mDweSlow;
        static FieldInfo? _fShaftThrust;
        static FieldInfo? _fFanThrust;
        static FieldInfo? _fFanMaxRpm;
        static FieldInfo? _fTfanThrust;
        static FieldInfo? _fTjetThrust;
        static FieldInfo? _fCspThrust;
        static FieldInfo? _fPropThrust;

        static MethodInfo? _mPsPlay;
        static MethodInfo? _mPsStop;
        static MethodInfo? _mPsPause;
        static MethodInfo? _mPsClearBool;
        static MethodInfo? _mPsClear;
        static PropertyInfo? _pPsIsPlaying;
        static PropertyInfo? _pPsIsPaused;
        static PropertyInfo? _pPsEnableEmission;
        static PropertyInfo? _pPsEmission;
        static PropertyInfo? _pEmEnabled;
        static bool _psReflBound;

        struct DownwashThrustTarget
        {
            public object? SourceObj;
            public Component ThrustComp;
            public FieldInfo ThrustField;
            public float MaxThrust;
        }

        Component[] _downwashes = Array.Empty<Component>();
        Component[] _downwashEffects = Array.Empty<Component>();
        DownwashThrustTarget[] _dwTargets = Array.Empty<DownwashThrustTarget>();
        Component[] _dwParticleSystems = Array.Empty<Component>();
        readonly List<Transform> _spawnedDownwashTransforms = new();
        bool _dwPaused;

        static void BindParticleRefl()
        {
            if (_psReflBound) return;
            _psReflBound = true;
            var pt = ParticleType();
            if (pt == null) return;
            _mPsPlay = pt.GetMethod("Play", Type.EmptyTypes) ?? pt.GetMethod("Play", new[] { typeof(bool) });
            _mPsStop = pt.GetMethod("Stop", Type.EmptyTypes);
            _mPsPause = pt.GetMethod("Pause", Type.EmptyTypes);
            _mPsClearBool = pt.GetMethod("Clear", new[] { typeof(bool) });
            _mPsClear = pt.GetMethod("Clear", Type.EmptyTypes);
            _pPsIsPlaying = pt.GetProperty("isPlaying");
            _pPsIsPaused = pt.GetProperty("isPaused");
            _pPsEnableEmission = pt.GetProperty("enableEmission");
            _pPsEmission = pt.GetProperty("emission");
            if (_pPsEmission != null)
                _pEmEnabled = _pPsEmission.PropertyType.GetProperty("enabled");
        }

        internal static void EnableParticleEmission(Component? ps)
        {
            if (ps == null) return;
            BindParticleRefl();
            try
            {
                _pPsEnableEmission?.SetValue(ps, true);
                if (_pPsEmission != null && _pEmEnabled != null)
                {
                    var em = _pPsEmission.GetValue(ps);
                    if (em != null) _pEmEnabled.SetValue(em, true);
                }
            }
            catch { }
        }

        internal static void PlayParticleSystem(Component? ps)
        {
            if (ps == null) return;
            BindParticleRefl();
            try
            {
                if (_pPsIsPlaying?.GetValue(ps) is bool playing && playing) return;
                if (_mPsPlay != null)
                {
                    if (_mPsPlay.GetParameters().Length == 1)
                        _mPsPlay.Invoke(ps, new object[] { true });
                    else
                        _mPsPlay.Invoke(ps, null);
                }
            }
            catch { }
        }

        internal static void StopParticleSystem(Component? ps, bool clear)
        {
            if (ps == null) return;
            BindParticleRefl();
            try
            {
                if (clear || (_pPsIsPlaying?.GetValue(ps) is bool playing && playing)
                          || (_pPsIsPaused?.GetValue(ps) is bool paused && paused))
                {
                    _mPsStop?.Invoke(ps, null);
                }
                if (clear)
                {
                    if (_mPsClearBool != null)
                        _mPsClearBool.Invoke(ps, new object[] { true });
                    else
                        _mPsClear?.Invoke(ps, null);
                }
            }
            catch { }
        }

        internal static void PauseParticleSystems(Component[] systems)
        {
            if (systems == null || systems.Length == 0) return;
            BindParticleRefl();
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps == null) continue;
                try
                {
                    if (_pPsIsPlaying?.GetValue(ps) is bool playing && playing)
                        _mPsPause?.Invoke(ps, null);
                }
                catch { }
            }
        }

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
                    || n == "JetNozzle" || n == "Downwash" || n == "DownwashEffect"
                    || n == "VaporEffect" || n == "VaporEmitter") continue;
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

        void HealParts()
        {
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
                if (r == null || _disabledInPrefab.Contains(r)) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.HasProperty("_HitPoints"))
                        m.SetFloat("_HitPoints", 100f);
                }
            }
        }

        static void AddPsFromObject(object? obj, Type pt, HashSet<Component> keep)
        {
            if (obj == null) return;
            if (obj is Component c)
            {
                if (pt.IsInstanceOfType(c))
                {
                    keep.Add(c);
                    return;
                }
                foreach (var child in c.GetComponentsInChildren(pt, true))
                    if (child != null) keep.Add(child);
                return;
            }
            if (obj is GameObject go)
            {
                foreach (var child in go.GetComponentsInChildren(pt, true))
                    if (child != null) keep.Add(child);
                return;
            }
            if (obj is Array arr)
            {
                for (int i = 0; i < arr.Length; i++)
                    AddPsFromObject(arr.GetValue(i), pt, keep);
            }
        }

        void CollectProtectedParticles(Type pt, HashSet<Component> keep)
        {
            Bind();
            BindShipRefl();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null) continue;
                var bt = beh.GetType();
                string tn = bt.Name;
                if (tn == "Ship")
                {
                    if (_fShipWakeParticles?.GetValue(beh) is Array wArr && _fWakeSystem != null)
                    {
                        for (int i = 0; i < wArr.Length; i++)
                        {
                            var elem = wArr.GetValue(i);
                            if (elem != null && _fWakeSystem.GetValue(elem) is Component ps && ps != null)
                                keep.Add(ps);
                        }
                    }
                }
                else if (tn == "ShipPropulsion")
                {
                    AddPsFromObject(_fShipPropParticles?.GetValue(beh), pt, keep);
                }
                else if (tn == "Downwash")
                {
                    AddPsFromObject(_fDwDustSys?.GetValue(beh), pt, keep);
                    AddPsFromObject(_fDwWaterSys?.GetValue(beh), pt, keep);
                    AddPsFromObject(_fDwDustTr?.GetValue(beh), pt, keep);
                    AddPsFromObject(_fDwWaterTr?.GetValue(beh), pt, keep);
                }
                else if (tn == "DownwashEffect")
                {
                    AddPsFromObject(_fDweLand?.GetValue(beh), pt, keep);
                    AddPsFromObject(_fDweWater?.GetValue(beh), pt, keep);
                    AddPsFromObject(beh, pt, keep);
                }
                else if (tn == "JetNozzle")
                {
                    var haze = bt.GetField("heatHaze", flags)?.GetValue(beh);
                    if (haze != null)
                        AddPsFromObject(haze.GetType().GetField("system", flags)?.GetValue(haze), pt, keep);
                    AddPsFromObject(beh, pt, keep);
                }
                else if (tn == "TurbineEngine")
                {
                    AddPsFromObject(bt.GetField("heatHazeParticles", flags)?.GetValue(beh), pt, keep);
                }
                else if (tn == "VaporEffect" || tn == "VaporEmitter" || tn == "TrailEmitter")
                {
                    AddPsFromObject(bt.GetField("particles", flags)?.GetValue(beh), pt, keep);
                    AddPsFromObject(beh, pt, keep);
                }
                else if (tn == "LandingGear")
                {
                    AddPsFromObject(bt.GetField("tireSmokeParticles", flags)?.GetValue(beh), pt, keep);
                }
                else if (tn == "NavLights")
                {
                    AddPsFromObject(beh, pt, keep);
                }
                else if (tn == "Gun")
                {
                    AddPsFromObject(bt.GetField("muzzleParticles", flags)?.GetValue(beh), pt, keep);
                }
            }
        }

        void SuppressDamageFx()
        {
            if (_ordnance) return;
            HealParts();
            var pt = ParticleType();
            if (pt == null) return;
            BindParticleRefl();
            if (!_killPsBound)
            {
                _killPsBound = true;
                BindGuns();
                var protectedPs = new HashSet<Component>();
                CollectProtectedParticles(pt, protectedPs);
                foreach (var keepPs in protectedPs)
                    EnableParticleEmission(keepPs);

                var list = new List<Component>();
                foreach (var c in GetComponentsInChildren(pt, true))
                {
                    if (c == null) continue;
                    if (protectedPs.Contains(c) || IsMuzzleParticle(c))
                    {
                        EnableParticleEmission(c);
                        continue;
                    }
                    var tr = c.transform;
                    string path = tr != null ? tr.name : "";
                    if (tr != null && tr.parent != null) path = tr.parent.name + "/" + path;
                    if (KeepFxName(path.ToLowerInvariant()))
                    {
                        EnableParticleEmission(c);
                        continue;
                    }
                    list.Add(c);
                }
                _killPs = list.ToArray();
            }
            for (int i = 0; i < _killPs.Length; i++)
            {
                var c = _killPs[i];
                if (c == null || IsMuzzleParticle(c)) continue;
                try
                {
                    _pPsEnableEmission?.SetValue(c, false);
                    _mPsStop?.Invoke(c, null);
                    _mPsClear?.Invoke(c, null);
                }
                catch { }
            }
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
            _downwashes = CollectFx(_downwashType);
            _downwashEffects = CollectFx(_downwashEffectType);
            BindDownwashSources();
            BindNozzles();
            BindMotion();
        }

        void BindDownwashSources()
        {
            var targets = new List<DownwashThrustTarget>(8);
            var psList = new List<Component>(16);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int i = 0; i < _downwashes.Length; i++)
            {
                var dw = _downwashes[i];
                if (dw == null) continue;
                if (_fDwDustTr?.GetValue(dw) is Transform dTr && dTr != null && !_spawnedDownwashTransforms.Contains(dTr))
                    _spawnedDownwashTransforms.Add(dTr);
                if (_fDwWaterTr?.GetValue(dw) is Transform wTr && wTr != null && !_spawnedDownwashTransforms.Contains(wTr))
                    _spawnedDownwashTransforms.Add(wTr);

                if (_fDwDustSys?.GetValue(dw) is Array dSys)
                {
                    for (int k = 0; k < dSys.Length; k++)
                    {
                        if (dSys.GetValue(k) is Component ps && ps != null)
                        {
                            EnableParticleEmission(ps);
                            psList.Add(ps);
                        }
                    }
                }
                if (_fDwWaterSys?.GetValue(dw) is Array wSys)
                {
                    for (int k = 0; k < wSys.Length; k++)
                    {
                        if (wSys.GetValue(k) is Component ps && ps != null)
                        {
                            EnableParticleEmission(ps);
                            psList.Add(ps);
                        }
                    }
                }

                if (_fDwSources?.GetValue(dw) is Array srcArr)
                {
                    for (int s = 0; s < srcArr.Length; s++)
                    {
                        var src = srcArr.GetValue(s);
                        if (src == null) continue;
                        try { _mDwSrcInit?.Invoke(src, null); } catch { }

                        Component? thrustComp = _fDwSrcThrust?.GetValue(src) as Component;
                        if (thrustComp == null && _fDwSrcObj?.GetValue(src) is GameObject tGo && tGo != null)
                        {
                            foreach (var mb in tGo.GetComponents<MonoBehaviour>())
                            {
                                if (mb == null) continue;
                                if (FindInstField(mb.GetType(), "currentThrust") != null
                                    || FindInstField(mb.GetType(), "thrust") != null)
                                {
                                    thrustComp = mb;
                                    break;
                                }
                            }
                        }
                        if (thrustComp == null) continue;

                        var tf = FindInstField(thrustComp.GetType(), "currentThrust")
                              ?? FindInstField(thrustComp.GetType(), "thrust");
                        if (tf == null || tf.FieldType != typeof(float)) continue;

                        float maxT = 0f;
                        if (_fDwSrcMaxThrust?.GetValue(src) is float mf) maxT = mf;
                        if (maxT <= 0.01f)
                        {
                            try
                            {
                                var mGetMax = thrustComp.GetType().GetMethod("GetMaxThrust", flags);
                                if (mGetMax?.Invoke(thrustComp, null) is float gm && gm > 0.01f)
                                    maxT = gm;
                            }
                            catch { }
                            if (maxT <= 0.01f) maxT = 20000f;
                            try { _fDwSrcMaxThrust?.SetValue(src, maxT); } catch { }
                        }

                        targets.Add(new DownwashThrustTarget
                        {
                            SourceObj = src,
                            ThrustComp = thrustComp,
                            ThrustField = tf,
                            MaxThrust = maxT
                        });
                    }
                }
            }

            for (int i = 0; i < _downwashEffects.Length; i++)
            {
                var dwe = _downwashEffects[i];
                if (dwe == null) continue;
                if (_fDweLand?.GetValue(dwe) is Array lArr)
                {
                    for (int k = 0; k < lArr.Length; k++)
                    {
                        if (lArr.GetValue(k) is Component ps && ps != null)
                        {
                            EnableParticleEmission(ps);
                            psList.Add(ps);
                        }
                    }
                }
                if (_fDweWater?.GetValue(dwe) is Array wArr)
                {
                    for (int k = 0; k < wArr.Length; k++)
                    {
                        if (wArr.GetValue(k) is Component ps && ps != null)
                        {
                            EnableParticleEmission(ps);
                            psList.Add(ps);
                        }
                    }
                }
            }

            _dwTargets = targets.ToArray();
            _dwParticleSystems = psList.ToArray();
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

        internal void PaintMissingLivery()
        {
            Texture? tex = null;
            float gloss = 0.2f;
            bool haveGloss = false;
            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || _disabledInPrefab.Contains(r)) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    string sh = mat.shader != null ? mat.shader.name : "";
                    if (sh.IndexOf("AircraftSkin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (mat.HasProperty("_Livery"))
                    {
                        var t = mat.GetTexture("_Livery");
                        if (t != null)
                        {
                            tex = t;
                            if (mat.HasProperty("_Glossiness"))
                            {
                                gloss = mat.GetFloat("_Glossiness");
                                haveGloss = true;
                            }
                            break;
                        }
                    }
                }
                if (tex != null) break;
            }
            if (tex == null) return;
            _liveryPainted = true;
            var err = Shader.Find("Hidden/InternalErrorShader");
            var fix = Shader.Find("Shader Graphs/AircraftSkin")
                     ?? Shader.Find("Universal Render Pipeline/Lit");
            if (err != null && fix != null)
            {
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || _disabledInPrefab.Contains(r)) continue;
                    var ms = r.sharedMaterials;
                    if (ms == null) continue;
                    bool hasErr = false;
                    for (int m = 0; m < ms.Length; m++)
                    {
                        if (ms[m] != null && ms[m].shader == err) { hasErr = true; break; }
                    }
                    if (!hasErr) continue;
                    var mats = r.materials;
                    bool dirty = false;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        var mat = mats[m];
                        if (mat == null || mat.shader != err) continue;
                        mat.shader = fix;
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                        if (mat.HasProperty("_Livery")) mat.SetTexture("_Livery", tex);
                        if (haveGloss && mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", gloss);
                        dirty = true;
                    }
                    if (dirty) r.materials = mats;
                }
            }
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || _disabledInPrefab.Contains(r)) continue;
                var sm = r.sharedMaterials;
                if (sm == null) continue;
                bool needFix = false;
                for (int m = 0; m < sm.Length; m++)
                {
                    var mat = sm[m];
                    if (mat == null) continue;
                    string sh = mat.shader != null ? mat.shader.name : "";
                    if (sh.IndexOf("AircraftSkin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (mat.HasProperty("_Livery") && mat.GetTexture("_Livery") != tex)
                    {
                        needFix = true;
                        break;
                    }
                }
                if (!needFix) continue;
                var mats = r.materials;
                if (mats == null) continue;
                bool dirty = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    string sh = mat.shader != null ? mat.shader.name : "";
                    if (sh.IndexOf("AircraftSkin", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (mat.HasProperty("_Livery") && mat.GetTexture("_Livery") != tex)
                    {
                        mat.SetTexture("_Livery", tex);
                        if (haveGloss && mat.HasProperty("_Glossiness"))
                            mat.SetFloat("_Glossiness", gloss);
                        dirty = true;
                    }
                }
                if (dirty) r.materials = mats;
            }
        }

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
            _unitType = Plugin.FindGameType("Unit");
            _engType = Plugin.FindGameType("TurbineEngine");
            _downwashType = Plugin.FindGameType("Downwash");
            _downwashEffectType = Plugin.FindGameType("DownwashEffect");
            if (_acType != null)
            {
                _fAcEngines = _acType.GetField("engines", flags);
                _mCheckRadarAlt = _acType.GetMethod("CheckRadarAlt", flags);
                _fUnitRadarAlt = FindInstField(_acType, "radarAlt");
                _fUnitDisplayDetail = FindInstField(_acType, "displayDetail");
                _fUnitAirDensity = FindInstField(_acType, "airDensity");
            }
            else if (_unitType != null)
            {
                _fUnitRadarAlt = FindInstField(_unitType, "radarAlt");
                _fUnitDisplayDetail = FindInstField(_unitType, "displayDetail");
                _fUnitAirDensity = FindInstField(_unitType, "airDensity");
            }
            if (_downwashType != null)
            {
                _fDwSources = _downwashType.GetField("downwashSources", flags);
                _fDwDustTr = _downwashType.GetField("dustTransform", flags);
                _fDwWaterTr = _downwashType.GetField("waterSprayTransform", flags);
                _fDwDustSys = _downwashType.GetField("dustSystems", flags);
                _fDwWaterSys = _downwashType.GetField("waterSystems", flags);
                _mDwUpdateSystems = _downwashType.GetMethod("UpdateSystems", flags);
                _mDwCheckSurface = _downwashType.GetMethod("CheckSurface", flags);
                var dwSrcType = _downwashType.GetNestedType("DownwashSource", flags);
                if (dwSrcType != null)
                {
                    _fDwSrcObj = dwSrcType.GetField("thrustSourceObject", flags);
                    _fDwSrcThrust = dwSrcType.GetField("thrustSource", flags);
                    _fDwSrcMaxThrust = dwSrcType.GetField("maxThrust", flags);
                    _mDwSrcInit = dwSrcType.GetMethod("Initialize", flags);
                }
            }
            if (_downwashEffectType != null)
            {
                _fDweLand = _downwashEffectType.GetField("landEffects", flags);
                _fDweWater = _downwashEffectType.GetField("waterEffects", flags);
                _mDweStop = _downwashEffectType.GetMethod("StopEffects", flags);
                _mDweSlow = _downwashEffectType.GetMethod("SlowUpdate", flags);
            }
            if (_tjetType != null)
            {
                _fTjetSrc = _tjetType.GetField("turbineAudio", flags);
                _fTjetRpm = _tjetType.GetField("rpm", flags);
                _fTjetMax = _tjetType.GetField("maxRPM", flags);
                _fTjetThrust = _tjetType.GetField("thrust", flags);
            }
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
                _fShaftThrust = _shaftType.GetField("currentThrust", flags);
                _mAnimate = _shaftType.GetMethod("AnimateRotor", flags);
            }
            if (_tfanType != null)
            {
                _fTfanSrc = _tfanType.GetField("turbineAudio", flags);
                _fTfanRpm = _tfanType.GetField("currentRPM", flags);
                _fTfanMax = _tfanType.GetField("maxRPM", flags);
                _fTfanThrust = _tfanType.GetField("currentThrust", flags);
            }
            if (_propType != null)
            {
                _fPropRpm = _propType.GetField("currentRPM", flags);
                _fPropRatio = _propType.GetField("rpmRatio", flags);
                _fPropNom = _propType.GetField("nominalRPM", flags);
                _fPropThrust = _propType.GetField("thrust", flags);
                _fPfanSrc = _propType.GetField("source", flags);
            }
            if (_cspType != null)
            {
                _fCspRpm = _cspType.GetField("RPM", flags);
                _fCspHub = _cspType.GetField("hubVisible", flags);
                _fCspDir = _cspType.GetField("turnDirection", flags);
                _fCspAudio = _cspType.GetField("propAudio", flags);
                _fCspExt = _cspType.GetField("exteriorSound", flags);
                _fCspVol = _cspType.GetField("volumeBase", flags);
                _fCspPitch = _cspType.GetField("pitchBase", flags);
                _fCspThrust = _cspType.GetField("currentThrust", flags);
            }
            if (_fanType != null)
            {
                _fFanRpm = _fanType.GetField("rpm", flags);
                _fFanMaxRpm = _fanType.GetField("maxRPM", flags);
                _fFanSrc = _fanType.GetField("fanSource", flags);
                _fFanThrust = _fanType.GetField("currentThrust", flags);
            }
            if (_engType != null)
            {
                _fThrottle = _engType.GetField("throttle", flags);
                _fRpm = _engType.GetField("currentRPM", flags);
                _fStarted = _engType.GetField("startedAmount", flags);
                _fOperable = _engType.GetField("operable", flags);
                _fHasFuel = _engType.GetField("hasFuel", flags);
                _fEngMaxPublic = _engType.GetField("maxRPM", BindingFlags.Instance | BindingFlags.Public);
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
                _fStrut = _gearType.GetField("strutRotationTransform", flags);
                _fStrutRot = _gearType.GetField("strutRotation", flags);
                _mSetGear = _gearType.GetMethod("LandingGear_OnSetGear", flags);
                _mMoveGear = _gearType.GetMethod("MoveGear", flags);
                _mUpdateParts = _gearType.GetMethod("UpdateMovingParts", flags);
            }
        }
    }
}