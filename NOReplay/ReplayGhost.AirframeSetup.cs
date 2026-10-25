using System;
using System.Collections.Generic;
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
            BindNozzles();
            BindMotion();
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
            if (name.IndexOf("Vagrant", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("VT-7", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var rs = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null) continue;
                    var ms = r.sharedMaterials;
                    if (ms == null) continue;
                    for (int m = 0; m < ms.Length; m++)
                    {
                        var mat = ms[m];
                        if (mat == null) continue;
                        string sh = mat.shader != null ? mat.shader.name : "null";
                        bool hasL = mat.HasProperty("_Livery") && mat.GetTexture("_Livery") != null;
                        bool hasB = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
                        // Plugin.Log.LogInfo("Vagrant-Mat " + r.name + " sh=" + sh + " L=" + hasL + " B=" + hasB);
                    }
                }
            }
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
            var err = Shader.Find("Hidden/InternalErrorShader");
            var fix = Shader.Find("Shader Graphs/AircraftSkin")
                     ?? Shader.Find("Universal Render Pipeline/Lit");
            if (err != null && fix != null && tex != null)
            {
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null) continue;
                    var ms = r.materials;
                    if (ms == null) continue;
                    bool dirty = false;
                    for (int m = 0; m < ms.Length; m++)
                    {
                        var mat = ms[m];
                        if (mat == null || mat.shader != err) continue;
                        // Replace error shader with a working one and assign the found texture.
                        mat.shader = fix;
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                        if (mat.HasProperty("_Livery")) mat.SetTexture("_Livery", tex);
                        dirty = true;
                    }
                    if (dirty) r.materials = ms;
                }
            }
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
                _fCspExt = _cspType.GetField("exteriorSound", flags);
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
