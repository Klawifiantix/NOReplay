using System;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        Vector3[] _trailRaw = Array.Empty<Vector3>();
        Vector3[] _trailPts = Array.Empty<Vector3>();
        Vector3[] _trailShown = Array.Empty<Vector3>();
        int _trailShownN;
        const float AirTrailGap = 15f;
        const float OrdnanceTrailGap = 15f;

        static void GrowTrail(ref Vector3[] buf, int need)
        {
            if (buf.Length >= need) return;
            int n = buf.Length == 0 ? 64 : buf.Length;
            while (n < need) n *= 2;
            buf = new Vector3[n];
        }

        static int LowerBoundTime(System.Collections.Generic.List<ReplaySample> samples, float from)
        {
            int lo = 0;
            int hi = samples.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (samples[mid].Time < from) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        internal void UpdateTrail(float t, float lengthSec)
        {
            if (!_air && !_ordnance)
            {
                HideTrail();
                return;
            }
            if (Track == null || Track.Samples.Count == 0 || !gameObject.activeSelf)
            {
                HideTrail();
                return;
            }
            float born = Track.Samples[0].Time;
            if (Track.SpawnAt > born) born = Track.SpawnAt;
            if (t < born)
            {
                HideTrail();
                return;
            }
            bool dead = Track.RemovedAt >= 0f && t >= Track.RemovedAt;
            if (lengthSec <= 0.5f || dead)
            {
                HideTrail();
                return;
            }
            EnsureTrail();
            if (_trail == null) return;
            float from = t - lengthSec;
            var samples = Track.Samples;
            GrowTrail(ref _trailRaw, samples.Count + 1);
            int rawN = 0;
            for (int i = LowerBoundTime(samples, from); i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.Time > t) break;
                var p = new Vector3(s.X, s.Y, s.Z);
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) continue;
                _trailRaw[rawN++] = p;
            }
            bool haveHere = false;
            Vector3 here = default;
            if (Track.TrySample(t, out var now))
            {
                var p = new Vector3(now.X, now.Y, now.Z);
                if (!float.IsNaN(p.x))
                {
                    haveHere = true;
                    here = p;
                    _trailRaw[rawN++] = p;
                }
            }
            GrowTrail(ref _trailPts, rawN > 0 ? rawN : 1);
            int pn = 0;
            const float min2 = 0.25f;
            const float max2 = 2500f * 2500f;
            for (int i = 0; i < rawN; i++)
            {
                var p = _trailRaw[i];
                if (pn == 0)
                {
                    _trailPts[pn++] = p;
                    continue;
                }
                var d = _trailPts[pn - 1] - p;
                float d2 = d.sqrMagnitude;
                if (d2 < min2) continue;
                if (d2 > max2) continue;
                _trailPts[pn++] = p;
            }
            if (haveHere)
                pn = TrimAirTrail(_trailPts, pn, here, _air ? AirTrailGap : OrdnanceTrailGap);
            if (pn < 2)
            {
                HideTrail();
                return;
            }
            if (!TrailUnchanged(pn))
            {
                if (_trail.positionCount != pn)
                    _trail.positionCount = pn;
                for (int i = 0; i < pn; i++)
                    _trail.SetPosition(i, _trailPts[i]);
                GrowTrail(ref _trailShown, pn);
                Array.Copy(_trailPts, _trailShown, pn);
                _trailShownN = pn;
            }
            _trail.enabled = true;
        }

        static int TrimAirTrail(Vector3[] pts, int n, Vector3 here, float gap)
        {
            if (n <= 0) return 0;
            if (gap <= 0f) return n;
            float gap2 = gap * gap;
            int last = n - 1;
            while (last > 0 && (pts[last] - here).sqrMagnitude < gap2)
                last--;
            if ((pts[last] - here).sqrMagnitude < gap2)
                return 0;
            if (last >= n - 1)
                return n;
            var cut = CutAtRadius(pts[last], pts[last + 1], here, gap);
            if ((cut - pts[last]).sqrMagnitude <= 0.0001f)
                return last + 1;
            pts[last + 1] = cut;
            return last + 2;
        }

        static Vector3 CutAtRadius(Vector3 outside, Vector3 inside, Vector3 center, float radius)
        {
            Vector3 d = inside - outside;
            Vector3 f = outside - center;
            float a = Vector3.Dot(d, d);
            float b = 2f * Vector3.Dot(f, d);
            float c = Vector3.Dot(f, f) - radius * radius;
            float disc = b * b - 4f * a * c;
            if (a < 1e-6f || disc < 0f) return outside;
            float s = Mathf.Sqrt(disc);
            float t = (-b - s) / (2f * a);
            if (t < 0f || t > 1f) t = (-b + s) / (2f * a);
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return outside + d * t;
        }

        bool TrailUnchanged(int n)
        {
            if (n != _trailShownN || _trail == null || !_trail.enabled) return false;
            for (int i = 0; i < n; i++)
            {
                var a = _trailPts[i];
                var b = _trailShown[i];
                if (a.x != b.x || a.y != b.y || a.z != b.z) return false;
            }
            return true;
        }

        void EnsureTrail()
        {
            if (_trail != null) return;
            var go = new GameObject("NOReplay_Trail");
            var datum = Plugin.Datum;
            go.transform.SetParent(datum != null ? datum : null, false);
            _trail = go.AddComponent<LineRenderer>();
            _trail.useWorldSpace = false;
            _trail.widthMultiplier = _ordnance ? 2f : 8f;
            _trail.numCornerVertices = 4;
            _trail.numCapVertices = 4;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.alignment = LineAlignment.View;
            _trail.textureMode = LineTextureMode.Stretch;
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Hidden/Internal-Colored")
                     ?? Shader.Find("Sprites/Default");
            if (sh != null && sh.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                sh = Shader.Find("Sprites/Default") ?? sh;
            if (sh != null)
            {
                var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                mat.color = Color.white;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    mat.SetColor("_EmissionColor", Color.white * 2f);
                }
                _trail.material = mat;
                // Plugin.Log?.LogInfo("Trail-Shader " + sh.name);
            }
            else Plugin.Log?.LogWarning("Trail: kein Shader");
            _trail.startWidth = 8f;
            _trail.endWidth = 6f;
            _trail.widthMultiplier = _ordnance ? 2f : 8f;
            _trail.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            var col = FactionTrailColor(Track?.Coalition);
            _trail.startColor = col;
            _trail.endColor = col;
            ApplyTrailColor(col);
        }

        void ApplyTrailColor(Color col)
        {
            if (_trail == null || _trail.material == null) return;
            var mat = _trail.material;
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", col * 2.2f);
        }

        internal void ForceHideTrail()
        {
            _trailShownN = 0;
            if (_trail != null)
            {
                _trail.positionCount = 0;
                _trail.enabled = false;
            }
        }

        void HideTrail()
        {
            ForceHideTrail();
        }

        static Color FactionTrailColor(string? coal)
        {
            string c = coal ?? "";
            if (c.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(1f, 0.48f, 0.05f, 1f);
            if (c.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.2f, 0.55f, 1f, 1f);
            return new Color(0.85f, 0.85f, 0.85f, 1f);
        }
    }
}
