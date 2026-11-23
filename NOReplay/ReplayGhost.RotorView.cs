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
        string _rotorViewSig = "";
        bool _rotorYawReady;
        readonly Dictionary<int, float> _rotorPrevYaw = new Dictionary<int, float>();
        readonly Dictionary<int, float> _rotorMoved = new Dictionary<int, float>();

        void NoteRotorView()
        {
            if (!_air || !IsChicaneRotor()) return;
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
                Plugin.Log?.LogInfo("Rotor tree " + RotorWho() + " root=none");
                _rotorTreeLogged = true;
                return;
            }
            string who = RotorWho();
            for (int r = 0; r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                var all = root.GetComponentsInChildren<Transform>(true);
                Plugin.Log?.LogInfo("Rotor tree " + who + " root=" + root.name + " n=" + all.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null) continue;
                    Plugin.Log?.LogInfo("Rotor tree " + who + " " + RotorPath(t) + " [" + RotorCompNames(t) + "]");
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
            Plugin.Log?.LogInfo("Rotor view " + who
                + " dot=" + dot
                + " side=" + side
                + " pos=" + pos);

            var renderers = GetComponentsInChildren<Renderer>(true);
            int shown = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string path = RotorPath(r.transform);
                if (!RotorMeshPath(path)) continue;
                shown++;
                Plugin.Log?.LogInfo("Rotor mesh " + who
                    + " en=" + (r.enabled ? "true" : "false")
                    + " vis=" + (r.isVisible ? "true" : "false")
                    + " underHub=" + (UnderHub(r.transform) ? "yes" : "no")
                    + " lod=" + RotorLodText(r, cam)
                    + " mat=" + RotorMatText(r)
                    + " path=" + path);
            }
            if (shown == 0)
                Plugin.Log?.LogInfo("Rotor mesh " + who + " n=0");
            if (withYaw) LogRotorYaw(who, cam);
        }

        void LogRotorYaw(string who, Camera? cam)
        {
            Vector3 axis = RotorSpinAxis();
            var parts = RotorSpinParts();
            if (parts.Count == 0)
            {
                Plugin.Log?.LogInfo("Rotor yaw " + who + " n=0");
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
                Plugin.Log?.LogInfo("Rotor yaw " + who
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
    }
}
