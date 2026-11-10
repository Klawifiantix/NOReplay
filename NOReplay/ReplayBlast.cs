using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Grows a MushroomCloud and Shockwave from the replay clock.
    /// Shockwave.Update also applies blast damage, so that component stays off.
    /// MushroomCloud.Update reads the live wind singleton and then sets the
    /// scale, so a missing wind lookup leaves the cloud at its start size.
    /// </summary>
    internal class ReplayBlast : MonoBehaviour
    {
        public float Born;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly int IdSize = Shader.PropertyToID("_Size");
        static readonly int IdAlpha = Shader.PropertyToID("_ShockwaveAlpha");
        static readonly int IdSoft = Shader.PropertyToID("_ShockwaveSoftness");
        static readonly int IdExpand = Shader.PropertyToID("_shockwaveExpansion");
        static readonly int IdOpacity = Shader.PropertyToID("_opacity");
        static bool _logged;

        Component _wave;
        Component _cloud;
        GameObject _vapor;
        Renderer _vaporRend;
        Material _vaporMat;
        AnimationCurve _vaporAlpha;
        float _detail = 1f;
        float _yieldKt = 1f;
        GameObject _decal;
        Material _decalMat;
        bool _bound;
        float _prevAge = -1f;

        void LateUpdate()
        {
            if (Plugin.CurrentDoc == null || Plugin.Clock == null) return;
            if (!_bound) Bind();
            float age = Plugin.Clock.Time - Born;
            if (age < 0f) age = 0f;
            if (_prevAge >= 0f && age + 0.05f < _prevAge)
                Revive();
            _prevAge = age;
            DriveShock(age);
            DriveCloud(age);
        }

        void Revive()
        {
            if (_vapor != null && !_vapor.activeSelf) _vapor.SetActive(true);
            if (_cloud == null) return;
            var host = _cloud as Component;
            var rends = host != null ? host.GetComponentsInChildren<Renderer>(true) : null;
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++)
                if (rends[i] != null) rends[i].enabled = true;
        }

        void Bind()
        {
            _bound = true;
            var all = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var mb = all[i];
                if (mb == null || mb == this) continue;
                string n = mb.GetType().Name;
                if (n == "Shockwave")
                {
                    _wave = mb;
                    mb.enabled = false;
                }
                else if (n == "MushroomCloud")
                {
                    _cloud = mb;
                    mb.enabled = false;
                }
            }
            if (_wave != null)
            {
                var t = _wave.GetType();
                if (t.GetField("yieldKilotons", Flags)?.GetValue(_wave) is float y) _yieldKt = y;
                _vapor = t.GetField("vaporCloud", Flags)?.GetValue(_wave) as GameObject;
                _vaporAlpha = t.GetField("vaporCloudAlpha", Flags)?.GetValue(_wave) as AnimationCurve;
                if (t.GetField("vaporCloudDetailScale", Flags)?.GetValue(_wave) is float d && d > 0.001f) _detail = d;
                _decal = t.GetField("groundDecal", Flags)?.GetValue(_wave) as GameObject;
                if (_vapor != null)
                {
                    _vaporRend = _vapor.GetComponent<Renderer>();
                    if (_vaporRend != null) _vaporMat = _vaporRend.material;
                }
            }
        }

        void DriveShock(float age)
        {
            if (_vapor == null) return;
            float power = Mathf.Pow(_yieldKt * 1000000f, 0.3333f);
            float radius = power * 13f;
            float prop = power * 0.5f + 340f * age;
            _vapor.transform.localScale = Vector3.one * prop;
            var cam = Plugin.MainCamera();
            if (cam != null)
            {
                try { _vapor.transform.LookAt(cam.transform.position); } catch { }
            }
            if (_vaporMat != null)
            {
                _vaporMat.SetFloat(IdSize, prop / _detail);
                if (_vaporAlpha != null) _vaporMat.SetFloat(IdAlpha, _vaporAlpha.Evaluate(age));
                if (prop > 0.01f) _vaporMat.SetFloat(IdSoft, 4f / prop);
            }
            if (_decal != null)
            {
                if (_decalMat == null)
                {
                    var proj = _decal.GetComponent<Renderer>();
                    if (proj != null) _decalMat = proj.material;
                }
                if (_decalMat != null && prop > 0.01f)
                {
                    _decalMat.SetFloat(IdExpand, radius / prop);
                    if (prop > radius) _decalMat.SetFloat(IdOpacity, Mathf.Max(0f, 1f - (prop - radius) / (340f * 10f)));
                }
            }
        }

        void DriveCloud(float age)
        {
            if (_cloud == null) return;
            var t = _cloud.GetType();
            try { t.GetField("time", Flags)?.SetValue(_cloud, age); } catch { }
            var cloud = t.GetField("cloud", Flags)?.GetValue(_cloud);
            if (cloud != null) ScaleCap(cloud, age);
            InvokeUpdate(t.GetField("fireball", Flags)?.GetValue(_cloud), age);
            var rings = t.GetField("cloudRings", Flags)?.GetValue(_cloud) as Array;
            if (rings == null) return;
            for (int i = 0; i < rings.Length; i++)
                InvokeUpdate(rings.GetValue(i), age);
        }

        static void ScaleCap(object cloud, float age)
        {
            var t = cloud.GetType();
            var rend = t.GetField("meshRenderer", Flags)?.GetValue(cloud) as Renderer;
            var wide = t.GetField("horizontalSizeOverTime", Flags)?.GetValue(cloud) as AnimationCurve;
            var tall = t.GetField("relativeHeightOverTime", Flags)?.GetValue(cloud) as AnimationCurve;
            if (rend == null || wide == null) return;
            float w = wide.Evaluate(age);
            float h = tall != null ? tall.Evaluate(age) : 1f;
            rend.transform.localScale = new Vector3(w, w * h, w);
        }

        static void InvokeUpdate(object node, float age)
        {
            if (node == null) return;
            object target = node;
            var nt = node.GetType();
            if (nt.IsValueType && nt.Name.StartsWith("Optional", StringComparison.Ordinal))
            {
                var en = nt.GetProperty("Enabled", Flags);
                if (en != null && en.GetValue(node) is bool on && !on) return;
                var val = nt.GetProperty("Value", Flags);
                target = val?.GetValue(node);
                if (target == null) return;
            }
            var method = target.GetType().GetMethod("Update", Flags, null, new[] { typeof(float) }, null);
            if (method == null) return;
            try { method.Invoke(target, new object[] { age }); }
            catch (Exception ex)
            {
                if (_logged) return;
                _logged = true;
                Plugin.Log?.LogInfo("Blast step " + target.GetType().Name + " " + ex.GetType().Name);
            }
        }
    }
}
