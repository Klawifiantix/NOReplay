using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        bool _rotorTreeLogged;
        float _rotorTreeWait;
        float _rotorViewLogAt = -999f;
        float _rotorRenderAt = -999f;
        string _rotorViewSig = "";
        bool _rotorYawReady;
        readonly Dictionary<int, float> _rotorPrevYaw = new Dictionary<int, float>();
        readonly Dictionary<int, float> _rotorMoved = new Dictionary<int, float>();

        Transform? _spinHub;
        Transform? _spinSeg;
        int _postFrame = -1;
        float _postHub;
        float _postSeg;
        bool _rear;
        float _rearAt;
        static bool _rotorCamHook;
        static ReplayGhost? _rotorWatch;

        struct RotorFrame
        {
            public float T;
            public int F;
            public float Post;
            public float Cull;
            public float Rend;
            public float DRend;
            public float Dt;
            public float Udt;
            public float Behind;
            public Vector3 Rel;
        }

        readonly RotorFrame[] _rotorRing = new RotorFrame[40];
        int _rotorRingAt;
        int _rotorRingN;
        int _rotorFast;
        int _rotorSlow;
        int _rotorBurst;
        bool _rotorStopped;
        float _rendPrev;
        bool _rendPrevOk;
        float _cullHub;
        int _cullFrame = -1;

        void NoteRotorView()
        {
            if (Plugin.CurrentDoc == null || !_air || !IsChicaneRotor()) return;
            EnsureRotorCamHook();
            _rotorWatch = this;
            TrackBehind();
            DumpRotorTreeOnce();
            StepRotorYaw();
            bool sideChanged = false;
            string sig = RotorViewSignature(out sideChanged);
            bool due = Time.unscaledTime - _rotorViewLogAt >= 1f;
            if (!due && !sideChanged && sig == _rotorViewSig) return;
            _rotorViewLogAt = Time.unscaledTime;
            _rotorViewSig = sig;
            LogRotorView(sideChanged || due);
        }

        bool IsChicaneRotor()
        {
            string n = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
            return n.IndexOf("Chicane", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("SAH-46", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string RotorWho()
        {
            return Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
        }

        void DumpRotorTreeOnce()
        {
            if (_rotorTreeLogged) return;
            var roots = RotorRoots();
            if (roots.Count == 0)
            {
                if (transform.childCount == 0) return;
                if (_rotorTreeWait <= 0f) _rotorTreeWait = Time.unscaledTime + 2f;
                if (Time.unscaledTime < _rotorTreeWait) return;
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor tree " + RotorWho() + " root=none");
                _rotorTreeLogged = true;
                return;
            }
            string who = RotorWho();
            for (int r = 0; r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                var all = root.GetComponentsInChildren<Transform>(true);
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor tree " + who + " root=" + root.name + " n=" + all.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    Plugin.Log?.LogInfo(RotorStamp() + "Rotor tree " + who + " " + RotorPath(t) + " [" + RotorCompNames(t) + "]");
                }
            }
            _rotorTreeLogged = true;
        }

        List<Transform> RotorRoots()
        {
            var list = new List<Transform>();
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || !RotorRootName(t.name)) continue;
                if (t.parent != null && RotorRootName(t.parent.name)) continue;
                list.Add(t);
            }
            return list;
        }

        static bool RotorRootName(string? n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("rotor", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("transmission", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string RotorCompNames(Transform t)
        {
            var comps = t.GetComponents<Component>();
            var sb = new StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(comps[i].GetType().Name);
            }
            return sb.ToString();
        }

        void StepRotorYaw()
        {
            Vector3 axis = RotorSpinAxis();
            var parts = RotorSpinParts();
            var live = new HashSet<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                var t = parts[i];
                if (t == null) continue;
                int id = t.GetInstanceID();
                live.Add(id);
                float yaw = AngleAbout(t, axis);
                if (_rotorPrevYaw.TryGetValue(id, out float prev))
                {
                    float step = Mathf.Abs(Mathf.DeltaAngle(prev, yaw));
                    _rotorMoved.TryGetValue(id, out float sum);
                    _rotorMoved[id] = sum + step;
                }
                _rotorPrevYaw[id] = yaw;
            }
            if (_rotorYawReady)
            {
                var drop = new List<int>();
                foreach (var kv in _rotorPrevYaw)
                    if (!live.Contains(kv.Key)) drop.Add(kv.Key);
                for (int i = 0; i < drop.Count; i++)
                {
                    _rotorPrevYaw.Remove(drop[i]);
                    _rotorMoved.Remove(drop[i]);
                }
            }
            _rotorYawReady = true;
        }

        void LogRotorView(bool withYaw)
        {
            string who = RotorWho();
            var cam = Plugin.MainCamera();
            Vector3 fwd = Flat(Drive.forward);
            string dot = "none";
            string pos = "none";
            string side = "-";
            if (cam != null && fwd.sqrMagnitude > 1e-6f)
            {
                Vector3 look = Flat(cam.transform.forward);
                Vector3 to = Flat(cam.transform.position - Drive.position);
                if (look.sqrMagnitude > 1e-6f)
                    dot = Vector3.Dot(fwd, look).ToString("0.00");
                if (to.sqrMagnitude > 1e-6f)
                {
                    float p = Vector3.Dot(fwd, to);
                    pos = p.ToString("0.00");
                    side = p < 0f ? "rear" : "front";
                }
            }
            Plugin.Log?.LogInfo(RotorStamp() + "Rotor view " + who
                + " dot=" + dot
                + " side=" + side
                + " pos=" + pos
                + " behind=" + BehindAge().ToString("0.00"));

            var renderers = GetComponentsInChildren<Renderer>(true);
            int shown = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string path = RotorPath(r.transform);
                if (!RotorMeshPath(path)) continue;
                shown++;
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor mesh " + who);
                    + " en=" + (r.enabled ? "true" : "false")
                    + " vis=" + (r.isVisible ? "true" : "false")
                    + " underHub=" + (UnderHub(r.transform) ? "yes" : "no")
                    + " lod=" + RotorLodText(r, cam)
                    + " mat=" + RotorMatText(r)
                    + " path=" + path);
            }
            if (shown == 0)
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor mesh " + who + " n=0");
            if (withYaw) LogRotorYaw(who, cam);
        }

        void LogRotorYaw(string who, Camera? cam)
        {
            Vector3 axis = RotorSpinAxis();
            var parts = RotorSpinParts();
            if (parts.Count == 0)
            {
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor yaw " + who + " n=0");
                return;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var t = parts[i];
                if (t == null) continue;
                int id = t.GetInstanceID();
                float yaw = AngleAbout(t, axis);
                _rotorMoved.TryGetValue(id, out float moved);
                _rotorMoved[id] = 0f;
                string rel = "none";
                if (cam != null)
                {
                    Vector3 look = Vector3.ProjectOnPlane(cam.transform.forward, axis);
                    Vector3 spoke = Spoke(t, axis);
                    if (look.sqrMagnitude > 1e-6f && spoke.sqrMagnitude > 1e-6f)
                        rel = Vector3.SignedAngle(look, spoke, axis).ToString("0.0");
                }
                string kind = t.name.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.name.IndexOf("rotorhub", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "hub" : "seg";
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor yaw " + who);
                    + " " + kind
                    + " yaw=" + yaw.ToString("0.0")
                    + " moved=" + moved.ToString("0.0")
                    + " rel=" + rel
                    + " path=" + RotorPath(t));
            }
        }

        string RotorViewSignature(out bool sideChanged)
        {
            sideChanged = false;
            var cam = Plugin.MainCamera();
            string side = "-";
            Vector3 fwd = Flat(Drive.forward);
            if (cam != null && fwd.sqrMagnitude > 1e-6f)
            {
                Vector3 to = Flat(cam.transform.position - Drive.position);
                if (to.sqrMagnitude > 1e-6f)
                    side = Vector3.Dot(fwd, to) < 0f ? "rear" : "front";
            }
            if (_rotorViewSig.Length > 0)
            {
                string prev = _rotorViewSig.Length >= 5 ? _rotorViewSig.Substring(0, _rotorViewSig.IndexOf('|')) : "";
                sideChanged = prev.Length > 0 && prev != side;
            }
            var sb = new StringBuilder(side);
            sb.Append('|');
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string path = RotorPath(r.transform);
                if (!RotorMeshPath(path)) continue;
                sb.Append(path).Append('=');
                sb.Append(r.enabled ? '1' : '0');
                sb.Append(r.isVisible ? '1' : '0');
                sb.Append(UnderHub(r.transform) ? '1' : '0');
                sb.Append(RotorLodText(r, cam)).Append(';');
            }
            return sb.ToString();
        }

        List<Transform> RotorSpinParts()
        {
            var list = new List<Transform>();
            var all = GetComponentsInChildren<Transform>(true);
            Transform? hub = null;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (string.Equals(t.name, "rotorhub_visible", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.name, "rotorhub", StringComparison.OrdinalIgnoreCase))
                {
                    if (hub == null || t.name.IndexOf("visible", StringComparison.OrdinalIgnoreCase) >= 0)
                        hub = t;
                }
            }
            if (hub != null) list.Add(hub);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == hub) continue;
                string n = t.name ?? "";
                if (n.IndexOf("blade", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("hinge", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("segment", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!UnderNamed(t, "rotor") && !UnderNamed(t, "transmission")) continue;
                list.Add(t);
            }
            return list;
        }

        Vector3 RotorSpinAxis()
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t != null && string.Equals(t.name, "rotorhub_visible", StringComparison.OrdinalIgnoreCase))
                    return t.up;
            }
            return Drive.up;
        }

        static float AngleAbout(Transform t, Vector3 axis)
        {
            Vector3 spoke = Spoke(t, axis);
            Vector3 reference = Vector3.ProjectOnPlane(Vector3.forward, axis);
            if (reference.sqrMagnitude < 1e-6f) reference = Vector3.ProjectOnPlane(Vector3.right, axis);
            if (spoke.sqrMagnitude < 1e-6f || reference.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(reference, spoke, axis);
        }

        static Vector3 Spoke(Transform t, Vector3 axis)
        {
            Vector3 spoke = Vector3.ProjectOnPlane(t.forward, axis);
            if (spoke.sqrMagnitude < 1e-6f) spoke = Vector3.ProjectOnPlane(t.right, axis);
            return spoke;
        }

        static bool UnderHub(Transform t)
        {
            return UnderNamed(t, "rotorhub_visible");
        }

        static bool UnderNamed(Transform t, string token)
        {
            var p = t.parent;
            int guard = 0;
            while (p != null && guard < 24)
            {
                if ((p.name ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                p = p.parent;
                guard++;
            }
            return false;
        }

        static bool RotorMeshPath(string path)
        {
            return path.IndexOf("rotor", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("blade", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("hub", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("disc", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("transmission", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string RotorLodText(Renderer r, Camera? cam)
        {
            var groups = r.GetComponentsInParent<LODGroup>(true);
            if (groups == null || groups.Length == 0) return "-";
            var sb = new StringBuilder();
            for (int g = 0; g < groups.Length; g++)
            {
                var group = groups[g];
                if (group == null) continue;
                var lods = group.GetLODs();
                int slot = -1;
                for (int i = 0; i < lods.Length && slot < 0; i++)
                {
                    var rs = lods[i].renderers;
                    if (rs == null) continue;
                    for (int k = 0; k < rs.Length; k++)
                        if (rs[k] == r) { slot = i; break; }
                }
                if (slot < 0) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(group.name).Append(':').Append(slot).Append('/').Append(lods.Length);
                sb.Append(" now=").Append(CurrentLod(group, lods, cam));
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        static string CurrentLod(LODGroup group, LOD[] lods, Camera? cam)
        {
            if (cam == null || lods == null || lods.Length == 0) return "-";
            float bias = QualitySettings.lodBias;
            if (bias < 0.01f) bias = 1f;
            Vector3 focus = group.transform.TransformPoint(group.localReferencePoint);
            float dist = Vector3.Distance(cam.transform.position, focus);
            if (dist < 0.01f) dist = 0.01f;
            float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (half < 1e-4f) half = 1e-4f;
            float rel = group.size / (dist * 2f * half) * bias;
            for (int i = 0; i < lods.Length; i++)
                if (rel >= lods[i].screenRelativeTransitionHeight) return i.ToString();
            return "cull";
        }

        static string RotorMatText(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(m.name);
                if (m.shader != null) sb.Append('/').Append(m.shader.name);
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }

        static string RotorPath(Transform t)
        {
            string path = t.name;
            int guard = 0;
            while (t.parent != null && guard < 24)
            {
                t = t.parent;
                path = t.name + "/" + path;
                guard++;
            }
            return path;
        }

        static string RotorStamp()
        {
            float t = Plugin.Clock != null ? Plugin.Clock.Time : -1f;
            return "t=" + t.ToString("0.00") + " f=" + Time.frameCount + " ";
        }

        void TrackBehind()
        {
            var cam = Plugin.MainCamera();
            bool rear = false;
            if (cam != null)
            {
                Vector3 fwd = Flat(Drive.forward);
                Vector3 to = Flat(cam.transform.position - Drive.position);
                if (fwd.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    rear = Vector3.Dot(fwd, to) < 0f;
            }
            if (rear && !_rear)
            {
                _rearAt = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
                _rotorFast = 0;
                _rotorSlow = 0;
                _rotorStopped = false;
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor behind cam="
                    + (Plugin.MainCamera() != null ? Plugin.MainCamera().name : "-"));
            }
            _rear = rear;
        }

        float BehindAge()
        {
            if (!_rear || Plugin.Clock == null) return 0f;
            return Plugin.Clock.Time - _rearAt;
        }

        internal void NoteRotorSpun(Transform hub)
        {
            if (Plugin.CurrentDoc == null || hub == null || !IsChicaneRotor()) return;
            if (hub.name.IndexOf("visible", StringComparison.OrdinalIgnoreCase) < 0 && _spinHub != null)
                return;
            EnsureRotorCamHook();
            _rotorWatch = this;
            _spinHub = hub;
            if (_spinSeg == null) _spinSeg = FindBlade1();
            Vector3 axis = hub.up;
            _postHub = AngleAbout(hub, axis);
            _postSeg = _spinSeg != null ? AngleAbout(_spinSeg, axis) : _postHub;
            _postFrame = Time.frameCount;
        }

        Transform? FindBlade1()
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name != "segment1") continue;
                if (t.parent != null && t.parent.name == "blade1") return t;
            }
            return null;
        }

        static void EnsureRotorCamHook()
        {
            if (_rotorCamHook) return;
            _rotorCamHook = true;
            Camera.onPreCull += OnRotorPreCull;
            Camera.onPreRender += OnRotorPreRender;
        }

        static bool RotorCam(Camera cam)
        {
            if (Plugin.CurrentDoc == null || cam == null) return false;
            return cam == Camera.main;
        }

        static void OnRotorPreCull(Camera cam)
        {
            if (!RotorCam(cam)) return;
            var g = _rotorWatch;
            if (g == null || g._spinHub == null) return;
            g._cullHub = AngleAbout(g._spinHub, g._spinHub.up);
            g._cullFrame = Time.frameCount;
        }

        static void OnRotorPreRender(Camera cam)
        {
            if (!RotorCam(cam)) return;
            var g = _rotorWatch;
            if (g == null || g._spinHub == null) return;
            g.SampleRotorRender(cam);
        }

        void SampleRotorRender(Camera cam)
        {
            if (_spinHub == null) return;
            Vector3 axis = _spinHub.up;
            float rend = AngleAbout(_spinHub, axis);
            float seg = _spinSeg != null ? AngleAbout(_spinSeg, axis) : rend;
            float dRend = 0f;
            if (_rendPrevOk) dRend = Mathf.DeltaAngle(_rendPrev, rend);
            _rendPrev = rend;
            _rendPrevOk = true;
            float behind = BehindAge();
            bool fresh = _postFrame == Time.frameCount;
            float post = fresh ? _postHub : rend;
            float cull = _cullFrame == Time.frameCount ? _cullHub : rend;
            var rel = cam.transform.position - _spinHub.position;
            var sample = new RotorFrame
            {
                T = Plugin.Clock != null ? Plugin.Clock.Time : -1f,
                F = Time.frameCount,
                Post = post,
                Cull = cull,
                Rend = rend,
                DRend = dRend,
                Dt = Time.deltaTime,
                Udt = Time.unscaledDeltaTime,
                Behind = behind,
                Rel = rel
            };
            _rotorRing[_rotorRingAt] = sample;
            _rotorRingAt = (_rotorRingAt + 1) % _rotorRing.Length;
            if (_rotorRingN < _rotorRing.Length) _rotorRingN++;

            bool fast = Mathf.Abs(dRend) > 3f;
            if (fast) { _rotorFast++; _rotorSlow = 0; }
            else if (_rendPrevOk) _rotorSlow++;
            if (_rear && _rotorFast > 8 && _rotorSlow == 2 && !_rotorStopped)
            {
                _rotorStopped = true;
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor freeze behind=" + behind.ToString("0.00")
                    + " post=" + post.ToString("0.0")
                    + " cull=" + cull.ToString("0.0")
                    + " rend=" + rend.ToString("0.0")
                    + " seg=" + seg.ToString("0.0")
                    + " dRend=" + dRend.ToString("0.00"));
                DumpRotorRing();
                _rotorBurst = 30;
            }
            if (_rotorStopped && fast)
            {
                _rotorStopped = false;
                _rotorFast = 0;
                Plugin.Log?.LogInfo(RotorStamp() + "Rotor resume behind=" + behind.ToString("0.00")
                    + " side=" + (_rear ? "rear" : "front")
                    + " dRend=" + dRend.ToString("0.00")
                    + " rend=" + rend.ToString("0.0"));
                _rotorBurst = 15;
            }
            if (_rotorBurst > 0)
            {
                _rotorBurst--;
                LogRotorFrame("Rotor frame", sample, cam, seg, fresh);
            }
            if (Time.unscaledTime - _rotorRenderAt < 1f) return;
            _rotorRenderAt = Time.unscaledTime;
            LogRotorFrame("Rotor render", sample, cam, seg, fresh);
        }

        void DumpRotorRing()
        {
            int n = _rotorRingN;
            int start = (_rotorRingAt - n + _rotorRing.Length) % _rotorRing.Length;
            for (int i = 0; i < n; i++)
            {
                var s = _rotorRing[(start + i) % _rotorRing.Length];
                Plugin.Log?.LogInfo("t=" + s.T.ToString("0.00")
                    + " f=" + s.F
                    + " Rotor hold behind=" + s.Behind.ToString("0.00")
                    + " post=" + s.Post.ToString("0.0")
                    + " cull=" + s.Cull.ToString("0.0")
                    + " rend=" + s.Rend.ToString("0.0")
                    + " gap=" + Mathf.DeltaAngle(s.Post, s.Rend).ToString("0.00")
                    + " dRend=" + s.DRend.ToString("0.00")
                    + " dt=" + s.Dt.ToString("0.000")
                    + " udt=" + s.Udt.ToString("0.000")
                    + " rel=" + s.Rel.x.ToString("0") + "," + s.Rel.y.ToString("0") + "," + s.Rel.z.ToString("0"));
            }
        }

        void LogRotorFrame(string tag, RotorFrame s, Camera cam, float seg, bool fresh)
        {
            Plugin.Log?.LogInfo("t=" + s.T.ToString("0.00")
                + " f=" + s.F
                + " " + tag
                + " behind=" + s.Behind.ToString("0.00")
                + " post=" + (fresh ? s.Post.ToString("0.0") : "hold")
                + " cull=" + s.Cull.ToString("0.0")
                + " rend=" + s.Rend.ToString("0.0")
                + " seg=" + seg.ToString("0.0")
                + " gap=" + (fresh ? Mathf.DeltaAngle(s.Post, s.Rend).ToString("0.00") : "hold")
                + " dRend=" + s.DRend.ToString("0.00")
                + " dt=" + s.Dt.ToString("0.000")
                + " udt=" + s.Udt.ToString("0.000")
                + " cam=" + cam.name
                + " comp=" + cam.GetType().Name
                + " rel=" + s.Rel.x.ToString("0") + "," + s.Rel.y.ToString("0") + "," + s.Rel.z.ToString("0"));
        }
    }
}
