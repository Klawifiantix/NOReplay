using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        readonly HashSet<int> _partHide = new HashSet<int>();
        readonly HashSet<int> _ammoHide = new HashSet<int>();
        readonly HashSet<int> _detachedPartIds = new HashSet<int>();
        readonly List<GameObject> _ammoInactive = new List<GameObject>();
        readonly HashSet<int> _ammoInactiveIds = new HashSet<int>();
        readonly List<Renderer> _partTouched = new List<Renderer>();
        readonly List<FallingBit> _bits = new List<FallingBit>();
        readonly List<FallingBit> _fxBits = new List<FallingBit>();

        Dictionary<byte, UnitPart>? _partById;
        SwashRotor[]? _rotors;
        Unit? _structUnit;
        bool[] _eventSpawned = Array.Empty<bool>();
        int _partVisCount = int.MinValue;
        int _structEpoch;
        float _structPrev = -1f;
        int _partMissLog;

        const float DebrisLife = 14f;
        const float FxLife = 8f;
        const float PartFxLife = 30f;
        const float BreakFxRange = 5000f;
        const float PartJump = 2f;
        const int DebrisCap = 24;

        static FieldInfo? _disintegrate;
        static FieldInfo? _disintegrateFx;
        static FieldInfo? _fireDamage;
        static FieldInfo? _rotorSegments;
        static FieldInfo? _segTransform;
        static FieldInfo? _segRenderer;
        static FieldInfo? _breakEffects;
        static FieldInfo? _rotorDamageIndex;
        static FieldInfo? _damageFxList;
        static FieldInfo? _fuelFire;
        static FieldInfo? _fuelLeak;
        static FieldInfo? _fuelBall;
        static FieldInfo? _fuelPart;
        static FieldInfo? _turbineFail;
        static FieldInfo? _turbineFailXf;
        static FieldInfo? _turbineCrit;
        static FieldInfo? _jetNozzles;
        static FieldInfo? _jetCrit;
        static FieldInfo? _nozzleFail;
        static FieldInfo? _nozzleThrust;
        static FieldInfo? _nozzlePart;
        static FieldInfo? _attachInfo;
        static FieldInfo? _attachParent;
        static FieldInfo? _fStopCulled;
        static bool _fxFields;

        sealed class FallingBit
        {
            public GameObject Go = null!;
            public float Time;
            public float Life;
            public bool IsFx;
            public bool Grounded;
            public bool ParticlesPaused;
            public bool Frozen;
            public GameObject? Host;
            public Transform? Anchor;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
            public Rigidbody? Drive;
            public Vector3 HeldVel;
            public Vector3 HeldAng;
            public float Born;
        }

        bool RendererForcedOff(Renderer r)
        {
            int id = r.GetInstanceID();
            return _partHide.Contains(id) || _ammoHide.Contains(id);
        }

        bool IsAmmoInactive(GameObject go)
        {
            return go != null && _ammoInactiveIds.Contains(go.GetInstanceID());
        }

        void ApplyReplayRenderer(Renderer r)
        {
            if (r == null) return;
            if (RendererForcedOff(r) || _disabledInPrefab.Contains(r) || !_visible)
                r.enabled = false;
            else
                r.enabled = true;
        }

        void ReapplyReplayHides()
        {
            if ((_partHide.Count > 0 || _ammoHide.Count > 0) && _rend != null)
            {
                for (int i = 0; i < _rend.Length; i++)
                {
                    var r = _rend[i];
                    if (r != null && RendererForcedOff(r))
                        r.enabled = false;
                }
            }
            ReapplyInactiveOrdnance();
        }

        void ReapplyInactiveOrdnance()
        {
            for (int i = 0; i < _ammoInactive.Count; i++)
            {
                var go = _ammoInactive[i];
                if (go != null && go.activeSelf)
                    go.SetActive(false);
            }
        }

        internal bool TransformDetached(Transform t)
        {
            while (t != null && t != transform)
            {
                var part = t.GetComponent<UnitPart>();
                if (part != null && _detachedPartIds.Contains(part.GetInstanceID()))
                    return true;
                t = t.parent;
            }
            return false;
        }

        internal void InvalidateStructure()
        {
            var unit = StructureUnit();
            bool sameWeapons = _stations != null && unit != null && AmmoStamp(unit) == _ammoStamp;
            if (!sameWeapons) _stations = null;
            _partById = null;
            _rotors = null;
            _partVisCount = int.MinValue;
        }

        internal void ClearStructural()
        {
            ClearBits(_bits);
            ClearBits(_fxBits);
            _eventSpawned = Array.Empty<bool>();
            _partVisCount = int.MinValue;
            _structPrev = -1f;
        }

        void TickStructural(bool allowSpawn)
        {
            QuietLooseGear();
            if (!_air && !_ship && !_ordnance)
                TickWheels();
            if (Track == null)
            {
                if (AirframeFx()) SyncBreakFx();
                return;
            }
            TrimBits(_bits);
            TrimBits(_fxBits);
            var events = Track.PartBreaks;
            float now = Plugin.Clock.Time;
            if (events == null || events.Length == 0)
            {
                if (_partVisCount > 0)
                {
                    ApplyPartVisibility(Array.Empty<ReplayObject>(), 0);
                    _partVisCount = 0;
                    _structEpoch++;
                }
                _structPrev = now;
                if (AirframeFx()) SyncBreakFx();
                return;
            }
            int count = ReplayDocument.CountAtOrBefore(events, now);
            bool baseline = _structPrev < 0f;
            bool jumped = !baseline && now > _structPrev + PartJump;
            bool rewound = !baseline && now + 0.02f < _structPrev;
            bool crossing = allowSpawn && !baseline && !jumped && !rewound && now > _structPrev;
            if (jumped || rewound)
                ClearDebris();
            else
                ClearDebrisAfter(now);
            if (crossing)
                SpawnCrossed(events, _structPrev, now);
            if (baseline || jumped || rewound || count != _partVisCount)
            {
                ApplyPartVisibility(events, count);
                _partVisCount = count;
                _structEpoch++;
            }
            _structPrev = now;
            if (AirframeFx()) SyncBreakFx();
        }

        bool _airframeKnown;
        bool _airframe;

        // Part-break fire that follows a falling piece is only for Aircraft.
        // Ground vehicles, ships and buildings keep the old world-space burst.
        bool AirframeFx()
        {
            if (_ordnance) return false;
            if (_airframeKnown) return _airframe;
            var unit = StructureUnit();
            if (unit == null) return false;
            _airframeKnown = true;
            _airframe = unit is Aircraft;
            return _airframe;
        }

        void ApplyPartVisibility(ReplayObject[] events, int count)
        {
            var nextHide = new HashSet<int>();
            var nextParts = new HashSet<int>();
            var touched = new List<Renderer>();
            var unit = StructureUnit();
            int n = Mathf.Min(count, events.Length);
            for (int i = 0; i < n; i++)
            {
                var ev = events[i];
                if (ev == null) continue;
                if (IsRotorEvent(ev))
                {
                    if (unit != null && TrySegment(unit, ev, out var segTr, out var segRend))
                    {
                        if (segTr != null)
                            CollectRenderers(segTr, nextHide, touched);
                        else if (segRend != null)
                        {
                            nextHide.Add(segRend.GetInstanceID());
                            touched.Add(segRend);
                        }
                    }
                    continue;
                }
                if (unit == null) continue;
                var part = ResolvePart(unit, ev);
                if (part == null)
                {
                    if (_partMissLog < 4)
                    {
                        _partMissLog++;
                        Plugin.Log.LogInfo("Teil nicht gefunden id=" + ev.PartId + " " + (ev.PartPath ?? "")
                            + " " + (Track?.Name ?? name));
                    }
                    continue;
                }
                var nested = part.GetComponentsInChildren<UnitPart>(true);
                for (int p = 0; p < nested.Length; p++)
                    if (nested[p] != null) nextParts.Add(nested[p].GetInstanceID());
                nextParts.Add(part.GetInstanceID());
                CollectRenderers(part.transform, nextHide, touched);
            }
            _partHide.Clear();
            foreach (var id in nextHide)
                _partHide.Add(id);
            _detachedPartIds.Clear();
            foreach (var id in nextParts)
                _detachedPartIds.Add(id);
            for (int i = 0; i < _partTouched.Count; i++)
            {
                var r = _partTouched[i];
                if (r != null) ApplyReplayRenderer(r);
            }
            for (int i = 0; i < touched.Count; i++)
            {
                var r = touched[i];
                if (r != null) ApplyReplayRenderer(r);
            }
            _partTouched.Clear();
            _partTouched.AddRange(touched);
        }

        static void CollectRenderers(Transform root, HashSet<int> ids, List<Renderer> touched)
        {
            if (root == null) return;
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                ids.Add(r.GetInstanceID());
                touched.Add(r);
            }
        }

        void SpawnCrossed(ReplayObject[] events, float prev, float now)
        {
            var unit = StructureUnit();
            if (unit == null) return;
            EnsureFlags(events.Length);
            for (int i = 0; i < events.Length; i++)
            {
                var ev = events[i];
                if (ev == null || _eventSpawned[i]) continue;
                float when = ev.EventTime();
                if (when <= prev || when > now) continue;
                _eventSpawned[i] = true;
                if (IsRotorEvent(ev))
                {
                    if (TrySegment(unit, ev, out var segTr, out var segRend))
                    {
                        Vector3 pos = segTr != null ? segTr.position : transform.position;
                        Quaternion rot = segTr != null ? segTr.rotation : transform.rotation;
                        SpawnBreakEffects(unit, ev, pos, rot, when);
                        SpawnMeshDebris(segTr, segRend, when, BreakVelocity(ev, pos), 8f);
                    }
                    continue;
                }
                var part = ResolvePart(unit, ev);
                if (part == null) continue;
                Vector3 p = part.transform.position;
                Quaternion q = part.transform.rotation;
                if (!AirframeFx())
                {
                    SpawnLegacyPartEffects(part, p, q, when);
                    if (!IsFragment(ev))
                        SpawnPartDebris(part, when, BreakVelocity(ev, p));
                    continue;
                }
                GameObject? debris = null;
                if (!IsFragment(ev))
                    debris = SpawnPartDebris(part, when, BreakVelocity(ev, p));
                SpawnPartEffects(part, p, q, when, debris, !IsFragment(ev));
            }
        }

        Vector3 BreakVelocity(ReplayObject ev, Vector3 worldPos)
        {
            var vel = new Vector3(ev.PartVx, ev.PartVy, ev.PartVz);
            if (vel.sqrMagnitude < 1f)
                vel = _velMs;
            Vector3 away = worldPos - transform.position;
            if (away.sqrMagnitude > 0.25f)
                vel += away.normalized * 6f;
            vel += UnityEngine.Random.insideUnitSphere * 1.5f;
            return vel;
        }

        void SpawnLegacyPartEffects(UnitPart part, Vector3 pos, Quaternion rot, float when)
        {
            BindFxFields();
            if (_disintegrateFx?.GetValue(part) is not GameObject[] fx) return;
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] != null)
                    SpawnFx(fx[i], pos, rot, when);
            }
        }

        void SpawnPartEffects(UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, bool detached)
        {
            if (!BreakFxNear(pos)) return;
            BindFxFields();
            var seen = new HashSet<int>();
            if (_disintegrateFx?.GetValue(part) is GameObject[] fx)
            {
                for (int i = 0; i < fx.Length; i++)
                    PlaceBreakFx(fx[i], part, pos, rot, when, debris, seen);
            }
            PlaceDamageEffects(part, pos, rot, when, debris, seen);
            if (!detached) return;
            PlaceFuelEffects(part, pos, rot, when, debris, seen);
            PlaceEngineFailures(part, pos, rot, when, debris, seen);
        }

        void PlaceBreakFx(GameObject? prefab, UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            if (prefab == null || !seen.Add(prefab.GetInstanceID())) return;
            if (PrefabRidesPart(prefab))
            {
                if (debris != null)
                {
                    SpawnFallFx(prefab, pos, rot, when, debris);
                    var stump = StumpAnchor(part);
                    if (stump != null)
                        SpawnHeldFx(prefab, pos, rot, when, stump, PartFxLife);
                }
                else
                    SpawnHeldFx(prefab, pos, rot, when, part.transform, PartFxLife);
                return;
            }
            // Explosion flash and smoke columns stay in the world. The game only
            // leaves a prefab on the falling part when its root is DamageParticles.
            SpawnFx(prefab, pos, rot, when);
        }

        void PlaceDamageEffects(UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            if (_damageFxList?.GetValue(part) is not System.Collections.IEnumerable effects) return;
            foreach (var item in effects)
            {
                if (item is DamageEffect effect)
                    PlaceBreakFx(effect.prefab, part, pos, rot, when, debris, seen);
            }
        }

        void PlaceFuelEffects(UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            var tanks = part.GetComponentsInChildren<FuelTank>(true);
            for (int i = 0; i < tanks.Length; i++)
            {
                var tank = tanks[i];
                if (tank == null || !FuelTankOnPart(tank, part)) continue;
                // Detach always ruptures the tank. leakEffect is the spray, fireEffect the flame.
                // Neither is guaranteed to carry DamageParticles, so both are parented by hand.
                PlaceRidingFx(FieldGo(_fuelLeak, tank), part, pos, rot, when, debris, seen);
                PlaceRidingFx(FieldGo(_fuelFire, tank), part, pos, rot, when, debris, seen);
                PlaceBreakFx(FieldGo(_fuelBall, tank), part, pos, rot, when, debris, seen);
            }
        }

        void PlaceRidingFx(GameObject? prefab, UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            if (prefab == null || !seen.Add(prefab.GetInstanceID())) return;
            if (debris != null)
            {
                SpawnFallFx(prefab, pos, rot, when, debris);
                var stump = StumpAnchor(part);
                if (stump != null)
                    SpawnHeldFx(prefab, pos, rot, when, stump, PartFxLife);
            }
            else
                SpawnHeldFx(prefab, pos, rot, when, part.transform, PartFxLife);
        }

        void PlaceEngineFailures(UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            var turbines = GetComponentsInChildren<TurbineEngine>(true);
            for (int i = 0; i < turbines.Length; i++)
            {
                var eng = turbines[i];
                if (eng == null || !PartListed(_turbineCrit, eng, part)) continue;
                var prefab = FieldGo(_turbineFail, eng);
                var anchor = _turbineFailXf?.GetValue(eng) as Transform;
                PlaceEngineFx(prefab, anchor, part, pos, rot, when, debris, seen);
            }
            var jets = GetComponentsInChildren<Turbojet>(true);
            var nozzles = GetComponentsInChildren<JetNozzle>(true);
            for (int n = 0; n < nozzles.Length; n++)
            {
                var nozzle = nozzles[n];
                if (nozzle == null) continue;
                bool own = _nozzlePart?.GetValue(nozzle) is UnitPart up && PartHits(up, part);
                bool owned = own || JetOwnsBroken(jets, nozzle, part);
                if (!owned) continue;
                var prefab = FieldGo(_nozzleFail, nozzle);
                var anchor = _nozzleThrust?.GetValue(nozzle) as Transform;
                PlaceEngineFx(prefab, anchor, part, pos, rot, when, debris, seen);
            }
        }

        void PlaceEngineFx(GameObject? prefab, Transform? anchor, UnitPart part, Vector3 pos, Quaternion rot, float when, GameObject? debris, HashSet<int> seen)
        {
            if (prefab == null || !seen.Add(prefab.GetInstanceID())) return;
            bool fell = anchor != null && (anchor == part.transform || anchor.IsChildOf(part.transform));
            Vector3 at = anchor != null ? anchor.position : pos;
            Quaternion facing = anchor != null ? anchor.rotation : rot;
            if (fell && debris != null)
            {
                SpawnFallFx(prefab, at, facing, when, debris);
                var stump = StumpAnchor(part);
                if (stump != null && stump != anchor)
                    SpawnHeldFx(prefab, pos, rot, when, stump, PartFxLife);
            }
            else if (anchor != null)
                SpawnHeldFx(prefab, at, facing, when, anchor, PartFxLife);
            else if (debris != null)
                SpawnFallFx(prefab, pos, rot, when, debris);
            else
                SpawnFx(prefab, pos, rot, when);
        }

        bool JetOwnsBroken(Turbojet[] jets, JetNozzle nozzle, UnitPart part)
        {
            for (int i = 0; i < jets.Length; i++)
            {
                var jet = jets[i];
                if (jet == null || !PartListed(_jetCrit, jet, part)) continue;
                if (_jetNozzles?.GetValue(jet) is not Array nozzles) continue;
                for (int n = 0; n < nozzles.Length; n++)
                {
                    if (ReferenceEquals(nozzles.GetValue(n), nozzle))
                        return true;
                }
            }
            return false;
        }

        static bool PartListed(FieldInfo? field, Component host, UnitPart part)
        {
            if (field?.GetValue(host) is not Array arr) return false;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr.GetValue(i) is UnitPart up && PartHits(up, part))
                    return true;
            }
            return false;
        }

        static bool PartHits(UnitPart candidate, UnitPart broken)
        {
            if (candidate == null || broken == null) return false;
            if (candidate == broken) return true;
            // The listed part rode out on the piece that broke off.
            return candidate.transform.IsChildOf(broken.transform);
        }

        static bool FuelTankOnPart(FuelTank tank, UnitPart part)
        {
            BindFxFields();
            if (_fuelPart?.GetValue(tank) is UnitPart up && up != null)
                return PartHits(up, part);
            return tank.transform == part.transform || tank.transform.IsChildOf(part.transform);
        }

        static GameObject? FieldGo(FieldInfo? field, Component host)
        {
            try { return field?.GetValue(host) as GameObject; }
            catch { return null; }
        }

        static bool PrefabRidesPart(GameObject prefab)
        {
            // UnitPart.SpawnFragments uses TryGetComponent on the instance root.
            // A DamageParticles child does not keep an explosion parented to the part.
            return prefab.GetComponent<DamageParticles>() != null;
        }

        Transform? StumpAnchor(UnitPart part)
        {
            var parent = ReadParentPart(part);
            if (parent != null && parent.transform != part.transform)
                return parent.transform;
            var up = part.transform.parent;
            if (up != null && up != transform && up != part.transform)
                return up;
            return null;
        }

        static UnitPart? ReadParentPart(UnitPart part)
        {
            BindFxFields();
            if (_attachInfo == null) return null;
            object? info;
            try { info = _attachInfo.GetValue(part); }
            catch { return null; }
            if (info == null) return null;
            if (_attachParent == null)
                _attachParent = info.GetType().GetField("parentPart",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            try { return _attachParent?.GetValue(info) as UnitPart; }
            catch { return null; }
        }

        bool BreakFxNear(Vector3 pos)
        {
            var cam = Plugin.MainCamera();
            if (cam == null) return true;
            return (pos - cam.transform.position).sqrMagnitude <= BreakFxRange * BreakFxRange;
        }

        void SpawnFallFx(GameObject prefab, Vector3 pos, Quaternion rot, float when, GameObject debris)
        {
            var body = debris.GetComponent<Rigidbody>();
            var go = SpawnInactive(prefab);
            if (go == null) return;
            go.name = "NOReplay_PartFx";
            // Parent while the debris body is still dynamic, so DamageParticles.Awake
            // follows that body instead of starting the 1.5s cull timer.
            go.transform.SetParent(debris.transform, false);
            go.SetActive(true);
            go.transform.SetPositionAndRotation(pos, rot);
            NeutralizeFx(go);
            PlayParticles(go);
            ArmTrail(go, body);
            _fxBits.Add(new FallingBit
            {
                Go = go,
                Time = when,
                Life = PartFxLife,
                IsFx = true,
                Host = debris,
                Born = Time.time
            });
        }

        void SpawnHeldFx(GameObject prefab, Vector3 pos, Quaternion rot, float when, Transform? anchor, float life)
        {
            var go = SpawnInactive(prefab);
            if (go == null) return;
            var holder = new GameObject(anchor != null ? "NOReplay_StumpFx" : "NOReplay_BreakFx");
            holder.transform.SetPositionAndRotation(pos, rot);
            var rb = holder.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 1f;
            rb.isKinematic = false;
            rb.detectCollisions = false;
            go.name = "NOReplay_PartFx";
            go.transform.SetParent(holder.transform, false);
            go.SetActive(true);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            rb.isKinematic = true;
            if (anchor != null)
                holder.transform.SetParent(anchor, true);
            NeutralizeFx(holder);
            PlayParticles(holder);
            var bit = new FallingBit
            {
                Go = holder,
                Time = when,
                Life = life,
                IsFx = true,
                Born = Time.time
            };
            if (anchor != null)
            {
                bit.Anchor = anchor;
                bit.LocalPos = anchor.InverseTransformPoint(holder.transform.position);
                bit.LocalRot = Quaternion.Inverse(anchor.rotation) * holder.transform.rotation;
            }
            _fxBits.Add(bit);
        }

        static GameObject? SpawnInactive(GameObject prefab)
        {
            var dormant = new GameObject("NOReplay_Dormant");
            dormant.SetActive(false);
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, dormant.transform); }
            catch
            {
                UnityEngine.Object.Destroy(dormant);
                return null;
            }
            // A kinematic body on the prefab would hide the debris body from Awake.
            var bodies = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = bodies.Length - 1; i >= 0; i--)
            {
                if (bodies[i] == null) continue;
                try { UnityEngine.Object.DestroyImmediate(bodies[i]); }
                catch { }
            }
            go.SetActive(false);
            go.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(dormant);
            return go;
        }

        void SpawnBreakEffects(Unit unit, ReplayObject ev, Vector3 pos, Quaternion rot, float when)
        {
            var rotor = ResolveRotor(unit, ev);
            if (rotor == null) return;
            BindFxFields();
            _breakEffects ??= typeof(SwashRotor).GetField("breakEffects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_breakEffects?.GetValue(rotor) is not GameObject[] fx) return;
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] != null)
                    SpawnFx(fx[i], pos, rot, when);
            }
        }

        void SpawnFx(GameObject prefab, Vector3 pos, Quaternion rot, float when)
        {
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, pos, rot); }
            catch { return; }
            go.name = "NOReplay_PartFx";
            go.transform.SetParent(null, true);
            NeutralizeFx(go);
            PlayParticles(go);
            UnityEngine.Object.Destroy(go, FxLife);
            _fxBits.Add(new FallingBit { Go = go, Time = when });
        }

        static void ArmTrail(GameObject go, Rigidbody? body)
        {
            if (body == null) return;
            var trails = go.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                var trail = trails[i];
                if (trail == null) continue;
                trail.rb = body;
                trail.StartTrail();
            }
        }

        void SyncBreakFx()
        {
            bool paused = Plugin.Clock != null && Plugin.Clock.Paused;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            float sea = SeaY();
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var bit = _bits[i];
                if (bit.Go == null)
                {
                    _bits.RemoveAt(i);
                    continue;
                }
                if (bit.Life > 0f && now > bit.Time + bit.Life)
                {
                    ReleaseHost(bit.Go);
                    UnityEngine.Object.Destroy(bit.Go);
                    _bits.RemoveAt(i);
                    continue;
                }
                if (bit.Drive == null) continue;
                if (paused)
                {
                    if (!bit.Frozen && !bit.Drive.isKinematic)
                    {
                        bit.HeldVel = bit.Drive.velocity;
                        bit.HeldAng = bit.Drive.angularVelocity;
                        bit.Drive.velocity = Vector3.zero;
                        bit.Drive.angularVelocity = Vector3.zero;
                        bit.Drive.isKinematic = true;
                        bit.Frozen = true;
                    }
                }
                else if (bit.Frozen)
                {
                    bit.Drive.isKinematic = false;
                    bit.Drive.velocity = bit.HeldVel;
                    bit.Drive.angularVelocity = bit.HeldAng;
                    bit.Frozen = false;
                }
            }
            for (int i = _fxBits.Count - 1; i >= 0; i--)
            {
                var bit = _fxBits[i];
                if (bit.Go == null)
                {
                    _fxBits.RemoveAt(i);
                    continue;
                }
                float life = bit.Life > 0f ? bit.Life : FxLife;
                if (now > bit.Time + life)
                {
                    UnityEngine.Object.Destroy(bit.Go);
                    _fxBits.RemoveAt(i);
                    continue;
                }
                if (!bit.Grounded && ShouldGroundFx(bit, sea))
                    GroundFx(bit);
                if (!bit.Grounded && bit.Anchor != null)
                    StickToAnchor(bit);
                if (!paused && now > bit.Time + 2f && FxSpent(bit.Go))
                {
                    UnityEngine.Object.Destroy(bit.Go);
                    _fxBits.RemoveAt(i);
                    continue;
                }
                if (paused)
                {
                    if (!bit.ParticlesPaused)
                    {
                        SetFxPaused(bit.Go, true);
                        bit.ParticlesPaused = true;
                    }
                }
                else if (bit.ParticlesPaused && !bit.Grounded)
                {
                    SetFxPaused(bit.Go, false);
                    bit.ParticlesPaused = false;
                }
            }
        }

        void ReleaseHost(GameObject host)
        {
            for (int i = 0; i < _fxBits.Count; i++)
            {
                var fx = _fxBits[i];
                if (fx.Host != host || fx.Go == null) continue;
                if (!fx.Grounded)
                    GroundFx(fx);
                fx.Host = null;
            }
        }

        static void StickToAnchor(FallingBit bit)
        {
            if (bit.Go == null || bit.Anchor == null) return;
            bit.Go.transform.SetPositionAndRotation(
                bit.Anchor.TransformPoint(bit.LocalPos),
                bit.Anchor.rotation * bit.LocalRot);
        }

        static PropertyInfo? _pParticleCount;

        static bool FxSpent(GameObject go)
        {
            var pt = ParticleType();
            if (pt == null) return false;
            _pParticleCount ??= pt.GetProperty("particleCount");
            BindParticleRefl();
            var raw = go.GetComponentsInChildren(pt, true);
            for (int i = 0; i < raw.Length; i++)
            {
                var ps = raw[i];
                if (ps == null) continue;
                try
                {
                    if (_pPsIsPlaying?.GetValue(ps) is bool playing && playing) return false;
                    if (_pParticleCount?.GetValue(ps) is int n && n > 0) return false;
                }
                catch { return false; }
            }
            var ribbons = go.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < ribbons.Length; i++)
            {
                if (ribbons[i] != null && ribbons[i].positionCount > 0) return false;
            }
            var emitters = go.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] != null && emitters[i].enabled && emitters[i].rb != null)
                    return false;
            }
            return raw.Length > 0 || ribbons.Length > 0;
        }

        static float SeaY()
        {
            try { return Datum.LocalSeaY; }
            catch { return 0f; }
        }

        static bool ShouldGroundFx(FallingBit bit, float sea)
        {
            if (bit.Host == null || Time.time < bit.Born + 0.15f) return false;
            var sink = bit.Host.GetComponent<ReplayDebrisSink>();
            if (sink != null && sink.Hit) return true;
            return bit.Go != null && bit.Go.transform.position.y < sea - 0.5f;
        }

        void GroundFx(FallingBit bit)
        {
            bit.Grounded = true;
            bit.Anchor = null;
            if (bit.Go == null) return;
            // Stay parented to our own holder. DamageParticles.ParentObjectCulled
            // reparents the effect to the world and we would lose the reference.
            bit.Go.transform.SetParent(null, true);
            bool wet = bit.Go.transform.position.y < SeaY() - 0.5f;
            bool stop = wet;
            if (!wet && bit.Host != null)
            {
                var sink = bit.Host.GetComponent<ReplayDebrisSink>();
                if (sink != null && sink.Hit)
                    bit.Go.transform.position = sink.Point + Vector3.up * 0.15f;
            }
            BindFxFields();
            var dps = bit.Go.GetComponentsInChildren<DamageParticles>(true);
            for (int i = 0; i < dps.Length; i++)
            {
                if (dps[i] == null) continue;
                try
                {
                    if (_fStopCulled?.GetValue(dps[i]) is bool flag && flag)
                        stop = true;
                }
                catch { }
            }
            var emitters = bit.Go.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] == null) continue;
                try { emitters[i].StopTrail(); }
                catch { }
            }
            var ribbons = bit.Go.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < ribbons.Length; i++)
            {
                if (ribbons[i] != null) ribbons[i].emitting = false;
            }
            if (stop)
                StopFxParticles(bit.Go);
        }

        static void SetFxPaused(GameObject go, bool paused)
        {
            var pt = ParticleType();
            if (pt != null)
            {
                var raw = go.GetComponentsInChildren(pt, true);
                var arr = new Component[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    arr[i] = raw[i];
                if (paused)
                    PauseParticleSystems(arr);
                else
                {
                    for (int i = 0; i < arr.Length; i++)
                        PlayParticleSystem(arr[i]);
                }
            }
            var emitters = go.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < emitters.Length; i++)
            {
                var emitter = emitters[i];
                if (emitter == null) continue;
                if (!paused && emitter.rb == null) continue;
                emitter.enabled = !paused;
            }
            var sources = go.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (src == null) continue;
                if (paused) src.Pause();
                else src.UnPause();
            }
        }

        static void StopFxParticles(GameObject go)
        {
            var pt = ParticleType();
            if (pt == null) return;
            var raw = go.GetComponentsInChildren(pt, true);
            for (int i = 0; i < raw.Length; i++)
                StopParticleSystem(raw[i], false);
        }

        static void NeutralizeFx(GameObject go)
        {
            BindFxFields();
            var dps = go.GetComponentsInChildren<DamageParticles>(true);
            for (int i = 0; i < dps.Length; i++)
            {
                if (dps[i] == null) continue;
                try { _fireDamage?.SetValue(dps[i], 0f); } catch { }
            }
            var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < mbs.Length; i++)
            {
                var mb = mbs[i];
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "Shockwave" || n == "Explosion")
                    mb.enabled = false;
            }
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) cols[i].enabled = false;
        }

        GameObject? SpawnPartDebris(UnitPart part, float when, Vector3 vel)
        {
            if (_bits.Count >= DebrisCap) return null;
            var skip = DisintegrateIds(part);
            var root = new GameObject("NOReplay_Debris");
            root.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation);
            root.transform.localScale = SanitizedScale(part.transform, "part " + part.name);
            int copied = CopyMeshes(part.transform, root.transform, skip, 80);
            if (copied == 0)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }
            return FinishDebris(root, when, vel, part.mass > 0.1f ? part.mass : 40f);
        }

        void SpawnMeshDebris(Transform? seg, Renderer? rend, float when, Vector3 vel, float mass)
        {
            if (_bits.Count >= DebrisCap || seg == null) return;
            var root = new GameObject("NOReplay_Debris");
            root.transform.SetPositionAndRotation(seg.position, seg.rotation);
            root.transform.localScale = SanitizedScale(seg, "segment " + seg.name);
            int copied = CopyMeshes(seg, root.transform, null, 8);
            // CopyMeshes skips a renderer that is off. This fallback still copies
            // that one mesh. seg is the source root, the debris is only the destination.
            if (copied == 0 && rend != null)
                copied = CopyOne(rend, seg, root.transform, new Dictionary<int, Transform>()) ? 1 : 0;
            if (copied == 0)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            FinishDebris(root, when, vel, mass);
        }

        GameObject FinishDebris(GameObject root, float when, Vector3 vel, float mass)
        {
            bool air = AirframeFx();
            if (air)
                root.AddComponent<ReplayDebrisSink>();
            var sphere = root.AddComponent<SphereCollider>();
            var rends = root.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                Bounds box = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++)
                    if (rends[i] != null) box.Encapsulate(rends[i].bounds);
                sphere.center = root.transform.InverseTransformPoint(box.center);
                sphere.radius = Mathf.Clamp(box.extents.magnitude, 0.15f, 30f);
            }
            else sphere.radius = 0.4f;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(mass, 1f);
            rb.useGravity = true;
            rb.drag = 0.15f;
            rb.angularDrag = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = vel;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 2.5f;
            var life = root.AddComponent<ReplayDebrisLife>();
            life.Born = when;
            life.Life = DebrisLife;
            if (!air)
            {
                _bits.Add(new FallingBit { Go = root, Time = when });
                return root;
            }
            _bits.Add(new FallingBit
            {
                Go = root,
                Time = when,
                Life = DebrisLife,
                Drive = rb,
                Born = Time.time
            });
            return root;
        }

        static int CopyMeshes(Transform src, Transform dst, HashSet<int>? skip, int cap)
        {
            var map = new Dictionary<int, Transform>();
            var rends = src.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            for (int i = 0; i < rends.Length && n < cap; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (skip != null && skip.Contains(r.GetInstanceID())) continue;
                string tn = r.GetType().Name;
                if (tn == "ParticleSystemRenderer" || tn == "TrailRenderer" || tn == "LineRenderer") continue;
                if (CopyOne(r, src, dst, map)) n++;
            }
            return n;
        }

        static int _scaleLogs;
        static FieldInfo? _gearHinge;
        readonly HashSet<int> _gearNoted = new HashSet<int>();

        static Vector3 SanitizedScale(Transform src, string label)
        {
            var s = src.localScale;
            if (!GearScale.Absurd(s.x, s.y, s.z)) return s;
            if (_scaleLogs < 6)
            {
                _scaleLogs++;
                var w = src.lossyScale;
                Plugin.Log?.LogInfo("Debris-scale " + label
                    + " local=" + s.ToString("0.##")
                    + " lossy=" + w.ToString("0.##")
                    + " parent=" + (src.parent != null ? src.parent.name : "-"));
            }
            return Vector3.one;
        }

        void QuietLooseGear()
        {
            var t = Plugin.FindGameType("LandingGear");
            if (t == null) return;
            if (_gearHinge == null)
                _gearHinge = t.GetField("gearHinge", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_gearHinge == null) return;
            var gears = GetComponentsInChildren(t, true);
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            for (int i = 0; i < gears.Length; i++)
            {
                var gear = gears[i] as Component;
                if (gear == null) continue;
                var hinge = _gearHinge.GetValue(gear) as Transform;
                if (hinge == null) continue;
                int id = hinge.GetInstanceID();
                var rb = hinge.GetComponent<Rigidbody>();
                var sc = hinge.lossyScale;
                bool loose = hinge.parent == null || rb != null;
                bool absurd = GearScale.Absurd(sc.x, sc.y, sc.z);
                if (!loose && !absurd) continue;
                if (!_gearNoted.Add(id)) continue;
                Plugin.Log?.LogInfo("Gear-loose " + hinge.name
                    + " parent=" + (hinge.parent != null ? hinge.parent.name : "-")
                    + " body=" + (rb != null)
                    + " lossy=" + sc.ToString("0.##"));
                if (absurd) hinge.localScale = Vector3.one;
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                var cols = hinge.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                    if (cols[c] != null) cols[c].enabled = false;
                if (loose && hinge.GetComponent<ReplayDebrisLife>() == null)
                {
                    var life = hinge.gameObject.AddComponent<ReplayDebrisLife>();
                    life.Born = now;
                    life.Life = DebrisLife;
                }
            }
        }

        static Transform MirrorChain(Transform src, Transform srcRoot, Transform dstRoot, Dictionary<int, Transform> map)
        {
            if (src == srcRoot) return dstRoot;
            int id = src.GetInstanceID();
            if (map.TryGetValue(id, out var have) && have != null) return have;
            Transform parentSrc = src.parent != null ? src.parent : srcRoot;
            Transform parentDst = MirrorChain(parentSrc, srcRoot, dstRoot, map);
            var go = new GameObject(src.name);
            go.transform.SetParent(parentDst, false);
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = SanitizedScale(src, src.name);
            map[id] = go.transform;
            return go.transform;
        }

        static bool CopyOne(Renderer r, Transform srcRoot, Transform dstRoot, Dictionary<int, Transform> map)
        {
            Mesh? mesh = null;
            var filter = r.GetComponent<MeshFilter>();
            if (filter != null) mesh = filter.sharedMesh;
            if (mesh == null && r.GetType().Name == "SkinnedMeshRenderer")
            {
                var prop = r.GetType().GetProperty("sharedMesh");
                if (prop != null) mesh = prop.GetValue(r) as Mesh;
            }
            if (mesh == null) return false;
            Material[]? mats = r.sharedMaterials;
            Transform leaf = MirrorChain(r.transform, srcRoot, dstRoot, map);
            if (leaf.GetComponent<MeshFilter>() != null)
            {
                var extra = new GameObject(r.name);
                extra.transform.SetParent(leaf, false);
                leaf = extra.transform;
            }
            var mf = leaf.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = leaf.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            return true;
        }

        static HashSet<int>? DisintegrateIds(UnitPart part)
        {
            BindFxFields();
            if (_disintegrate?.GetValue(part) is not GameObject[] objs || objs.Length == 0)
                return null;
            var ids = new HashSet<int>();
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                var rends = objs[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                    if (rends[r] != null) ids.Add(rends[r].GetInstanceID());
            }
            return ids.Count > 0 ? ids : null;
        }

        Unit? StructureUnit()
        {
            if (_structUnit != null) return _structUnit;
            _structUnit = GetComponent<Unit>() ?? GetComponentInChildren<Unit>(true);
            return _structUnit;
        }

        void EnsurePartMap(Unit unit)
        {
            if (_partById != null) return;
            _partById = new Dictionary<byte, UnitPart>();
            var list = unit.damageables;
            if (list != null)
            {
                int n = list.Count;
                if (n > 256) n = 256;
                for (int i = 0; i < n; i++)
                {
                    if (list[i].Damageable is UnitPart up && up != null)
                        _partById[(byte)i] = up;
                }
            }
            var all = unit.GetComponentsInChildren<UnitPart>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p == null) continue;
                if (!_partById.ContainsKey(p.id))
                    _partById[p.id] = p;
            }
        }

        UnitPart? ResolvePart(Unit unit, ReplayObject ev)
        {
            EnsurePartMap(unit);
            UnitPart? byId = null;
            if (ev.HasPart && _partById != null && _partById.TryGetValue(ev.PartId, out var found))
                byId = found;
            if (!string.IsNullOrEmpty(ev.PartPath))
            {
                bool idOk = byId != null && PartPathEquals(unit, byId.transform, ev.PartPath);
                if (!idOk)
                {
                    var byPath = FindPartByPath(unit, ev.PartPath);
                    if (byPath != null) return byPath;
                }
            }
            return byId;
        }

        UnitPart? FindPartByPath(Unit unit, string path)
        {
            var all = unit.GetComponentsInChildren<UnitPart>(true);
            UnitPart? suffix = null;
            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p == null) continue;
                string have = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, p.transform));
                if (have.Equals(path, StringComparison.OrdinalIgnoreCase)) return p;
                if (suffix == null && (have.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("/" + have, StringComparison.OrdinalIgnoreCase)))
                    suffix = p;
            }
            return suffix;
        }

        static bool PartPathEquals(Unit unit, Transform part, string path)
        {
            string have = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, part));
            if (have.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
            return have.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/" + have, StringComparison.OrdinalIgnoreCase);
        }

        SwashRotor[] RotorsOf(Unit unit)
        {
            if (_rotors != null) return _rotors;
            _rotors = unit.GetComponentsInChildren<SwashRotor>(true);
            return _rotors;
        }

        SwashRotor? ResolveRotor(Unit unit, ReplayObject ev)
        {
            var all = RotorsOf(unit);
            if (!string.IsNullOrEmpty(ev.PartPath))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var r = all[i];
                    if (r == null) continue;
                    if (PartPathEquals(unit, r.transform, ev.PartPath)) return r;
                }
            }
            if (ev.HasPart && ev.PartId != 0)
            {
                SwashRotor? hit = null;
                int matches = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var r = all[i];
                    if (r == null) continue;
                    if (ReadRotorIndex(r) != ev.PartId) continue;
                    hit = r;
                    matches++;
                }
                if (matches == 1 && hit != null) return hit;
            }
            if (ev.PartRotor >= 0 && ev.PartRotor < all.Length)
                return all[ev.PartRotor];
            return null;
        }

        static byte ReadRotorIndex(SwashRotor rotor)
        {
            _rotorDamageIndex ??= typeof(SwashRotor).GetField("damageableIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                if (_rotorDamageIndex?.GetValue(rotor) is byte b) return b;
            }
            catch { }
            return 0;
        }

        bool TrySegment(Unit unit, ReplayObject ev, out Transform? segTr, out Renderer? rend)
        {
            segTr = null;
            rend = null;
            if (ev.PartSeg < 0) return false;
            var rotor = ResolveRotor(unit, ev);
            if (rotor == null) return false;
            BindFxFields();
            _rotorSegments ??= typeof(SwashRotor).GetField("segments",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_rotorSegments?.GetValue(rotor) is not Array segs) return false;
            if (ev.PartSeg >= segs.Length) return false;
            var item = segs.GetValue(ev.PartSeg);
            if (item == null) return false;
            var t = item.GetType();
            _segTransform ??= t.GetField("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _segRenderer ??= t.GetField("meshRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
            segTr = _segTransform?.GetValue(item) as Transform;
            rend = _segRenderer?.GetValue(item) as Renderer;
            return segTr != null || rend != null;
        }

        static bool IsRotorEvent(ReplayObject ev)
        {
            if (ev.PartSeg >= 0) return true;
            return string.Equals(ev.PartKind, "R", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsFragment(ReplayObject ev)
        {
            return string.Equals(ev.PartKind, "F", StringComparison.OrdinalIgnoreCase);
        }

        void EnsureFlags(int n)
        {
            if (_eventSpawned.Length == n) return;
            var next = new bool[n];
            int m = Mathf.Min(n, _eventSpawned.Length);
            for (int i = 0; i < m; i++) next[i] = _eventSpawned[i];
            _eventSpawned = next;
        }

        void ClearDebris()
        {
            ClearBits(_bits);
            ClearBits(_fxBits);
            for (int i = 0; i < _eventSpawned.Length; i++)
                _eventSpawned[i] = false;
        }

        void ClearDebrisAfter(float now)
        {
            ClearListAfter(_bits, now);
            ClearListAfter(_fxBits, now);
            var events = Track?.PartBreaks;
            if (events == null) return;
            int n = Mathf.Min(events.Length, _eventSpawned.Length);
            for (int i = 0; i < n; i++)
            {
                if (events[i] != null && events[i].EventTime() > now)
                    _eventSpawned[i] = false;
            }
        }

        static void ClearBits(List<FallingBit> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Go != null)
                    UnityEngine.Object.Destroy(list[i].Go);
            }
            list.Clear();
        }

        static void ClearListAfter(List<FallingBit> list, float now)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Time <= now && list[i].Go != null) continue;
                if (list[i].Go != null)
                    UnityEngine.Object.Destroy(list[i].Go);
                list.RemoveAt(i);
            }
        }

        static void TrimBits(List<FallingBit> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Go == null)
                    list.RemoveAt(i);
            }
        }

        static void BindFxFields()
        {
            if (_fxFields) return;
            _fxFields = true;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _disintegrate = typeof(UnitPart).GetField("disintegrateObjects", flags);
            _disintegrateFx = typeof(UnitPart).GetField("disintegrationEffects", flags);
            _damageFxList = typeof(UnitPart).GetField("damageEffects", flags);
            _fireDamage = typeof(DamageParticles).GetField("fireDamage", flags);
            _fStopCulled = typeof(DamageParticles).GetField("stopWhenParentCulled", flags);
            _attachInfo = typeof(UnitPart).GetField("attachInfo", flags);
            var fuel = typeof(FuelTank);
            _fuelFire = fuel.GetField("fireEffect", flags);
            _fuelLeak = fuel.GetField("leakEffect", flags);
            _fuelBall = fuel.GetField("fireball", flags);
            _fuelPart = fuel.GetField("part", flags);
            var turbine = typeof(TurbineEngine);
            _turbineFail = turbine.GetField("failureEffect", flags);
            _turbineFailXf = turbine.GetField("failureTransform", flags);
            _turbineCrit = turbine.GetField("criticalParts", flags);
            var jet = typeof(Turbojet);
            _jetNozzles = jet.GetField("nozzles", flags);
            _jetCrit = jet.GetField("criticalParts", flags);
            var nozzle = typeof(JetNozzle);
            _nozzleFail = nozzle.GetField("failureEffect", flags);
            _nozzleThrust = nozzle.GetField("thrustTransform", flags);
            _nozzlePart = nozzle.GetField("part", flags);
        }
    }
}
