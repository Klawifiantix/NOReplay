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
        static bool _fxFields;

        sealed class FallingBit
        {
            public GameObject Go = null!;
            public float Time;
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
            _stations = null;
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
            if (Track == null) return;
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
                SpawnPartEffects(part, p, q, when);
                if (!IsFragment(ev))
                    SpawnPartDebris(part, when, BreakVelocity(ev, p));
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

        void SpawnPartEffects(UnitPart part, Vector3 pos, Quaternion rot, float when)
        {
            BindFxFields();
            if (_disintegrateFx?.GetValue(part) is not GameObject[] fx) return;
            for (int i = 0; i < fx.Length; i++)
            {
                if (fx[i] != null)
                    SpawnFx(fx[i], pos, rot, when);
            }
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

        void SpawnPartDebris(UnitPart part, float when, Vector3 vel)
        {
            if (_bits.Count >= DebrisCap) return;
            var skip = DisintegrateIds(part);
            var root = new GameObject("NOReplay_Debris");
            root.transform.SetPositionAndRotation(part.transform.position, part.transform.rotation);
            int copied = CopyMeshes(part.transform, root.transform, skip, 80);
            if (copied == 0)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            FinishDebris(root, when, vel, part.mass > 0.1f ? part.mass : 40f);
        }

        void SpawnMeshDebris(Transform? seg, Renderer? rend, float when, Vector3 vel, float mass)
        {
            if (_bits.Count >= DebrisCap || seg == null) return;
            var root = new GameObject("NOReplay_Debris");
            root.transform.SetPositionAndRotation(seg.position, seg.rotation);
            int copied = CopyMeshes(seg, root.transform, null, 8);
            if (copied == 0 && rend != null)
                copied = CopyOne(rend, root.transform) ? 1 : 0;
            if (copied == 0)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            FinishDebris(root, when, vel, mass);
        }

        void FinishDebris(GameObject root, float when, Vector3 vel, float mass)
        {
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
            UnityEngine.Object.Destroy(root, DebrisLife);
            _bits.Add(new FallingBit { Go = root, Time = when });
        }

        static int CopyMeshes(Transform src, Transform dst, HashSet<int>? skip, int cap)
        {
            var rends = src.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            for (int i = 0; i < rends.Length && n < cap; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (skip != null && skip.Contains(r.GetInstanceID())) continue;
                string tn = r.GetType().Name;
                if (tn == "ParticleSystemRenderer" || tn == "TrailRenderer" || tn == "LineRenderer") continue;
                if (CopyOne(r, dst)) n++;
            }
            return n;
        }

        static bool CopyOne(Renderer r, Transform dst)
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
            var child = new GameObject(r.name);
            child.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            child.transform.localScale = r.transform.lossyScale;
            child.transform.SetParent(dst, true);
            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = child.AddComponent<MeshRenderer>();
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
            _fireDamage = typeof(DamageParticles).GetField("fireDamage", flags);
        }
    }
}
