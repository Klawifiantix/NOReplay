using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        Transform[] _gunMuzzles = Array.Empty<Transform>();
        Component[] _guns = Array.Empty<Component>();
        Component[] _turretComps = Array.Empty<Component>();
        Transform[] _turrets = Array.Empty<Transform>();
        Transform[] _elev = Array.Empty<Transform>();
        bool[] _turretForward = Array.Empty<bool>();
        bool _gunsBound;
        float _nextShot;
        int _tracerSeed;
        static bool _shotLogged;
        static FieldInfo? _fTurretTarget;

        void AimGuns()
        {
            if (_ordnance || _neutral) return;
            BindGuns();
            if (_gunAimUntil > Time.unscaledTime) return;
            for (int i = 0; i < _turrets.Length; i++)
            {
                var tr = _turrets[i];
                if (tr != null) tr.localRotation = Quaternion.identity;
                var elev = _elev[i];
                if (elev != null) elev.localRotation = Quaternion.identity;
            }
        }

        float _gunAimUntil;

        internal void AimGun(Vector3 worldDir)
        {
            BindGuns();
            if (worldDir.sqrMagnitude < 0.01f) return;
            _gunAimUntil = Time.unscaledTime + 0.15f;
            var localDir = Drive.InverseTransformDirection(worldDir.normalized);
            float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(localDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            var yawQ = Quaternion.Euler(0f, yaw, 0f);
            var pitchQ = Quaternion.Euler(pitch, 0f, 0f);
            for (int i = 0; i < _turrets.Length; i++)
            {
                if (_turrets[i] != null) _turrets[i].localRotation = yawQ;
                if (_elev[i] != null) _elev[i].localRotation = pitchQ;
            }
        }

        void BindGuns()
        {
            if (_gunsBound) return;
            _gunsBound = true;
            var gt = Plugin.FindGameType("Gun");
            var tt = Plugin.FindGameType("Turret");
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (gt != null)
            {
                var raw = GetComponentsInChildren(gt, true);
                _guns = new Component[raw.Length];
                var muzzles = new System.Collections.Generic.List<Transform>();
                for (int i = 0; i < raw.Length; i++)
                {
                    var c = raw[i] as Component;
                    _guns[i] = c;
                    if (c == null) continue;
                    if (c is Behaviour b) b.enabled = false;
                    var arr = gt.GetField("muzzles", flags)?.GetValue(c) as Array;
                    if (arr == null) continue;
                    foreach (var m in arr)
                        if (m is Transform tr) muzzles.Add(tr);
                }
                _gunMuzzles = muzzles.ToArray();
            }
            if (tt != null)
            {
                var raw = GetComponentsInChildren(tt, true);
                _turrets = new Transform[raw.Length];
                _elev = new Transform[raw.Length];
                _turretComps = new Component[raw.Length];
                _turretForward = new bool[raw.Length];
                if (_fTurretTarget == null)
                    _fTurretTarget = tt.GetField("target", flags);
                for (int i = 0; i < raw.Length; i++)
                {
                    var c = raw[i] as Component;
                    if (c == null) continue;
                    _turretComps[i] = c;
                    _turrets[i] = c.transform;
                    _elev[i] = tt.GetField("elevationTransform", flags)?.GetValue(c) as Transform;
                    if (c is Behaviour tb) tb.enabled = false;
                }
            }
        }

        void PulseGun()
        {
            if (_guns.Length == 0) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var gun = _guns[0];
            if (gun == null) return;
            var gt = gun.GetType();
            try
            {
                var parts = gt.GetField("muzzleParticles", flags)?.GetValue(gun) as Array;
                if (parts != null)
                    foreach (var p in parts) PlayPs(p);
            }
            catch { }
            if (Plugin.AudioNear(gameObject, 450f))
            {
                try
                {
                    var clips = gt.GetField("fireSounds", flags)?.GetValue(gun) as Array;
                    var sources = gt.GetField("sources", flags)?.GetValue(gun) as Array;
                    if (clips != null && clips.Length > 0 && sources != null && sources.Length > 0 && sources.GetValue(0) != null)
                    {
                        var src = sources.GetValue(0);
                        var st = src.GetType();
                        st.GetProperty("mute")?.SetValue(src, false);
                        var play = st.GetMethod("PlayOneShot", new[] { clips.GetValue(0).GetType() });
                        play?.Invoke(src, new[] { clips.GetValue(0) });
                    }
                }
                catch { }
            }
            _tracerSeed++;
            int ratio = 6;
            try
            {
                if (gt.GetField("tracerRatio", flags)?.GetValue(gun) is int r && r > 0) ratio = r;
            }
            catch { }
            if (_tracerSeed < ratio) return;
            _tracerSeed = 0;
        }

        internal void HoldBurstAudio()
        {
            if (!_burstAudio || _loopSrc == null) return;
            bool near = Plugin.AudioNear(gameObject, 550f);
            var st = _loopSrc.GetType();
            var playing = st.GetProperty("isPlaying")?.GetValue(_loopSrc) as bool? ?? false;
            st.GetProperty("mute")?.SetValue(_loopSrc, !near);
            if (!near) st.GetMethod("Stop", Type.EmptyTypes)?.Invoke(_loopSrc, null);
            else if (!playing) st.GetMethod("Play", Type.EmptyTypes)?.Invoke(_loopSrc, null);
        }

        internal void BeginBurstAudio(string? weapon)
        {
            BindGuns();
            if (_guns.Length == 0) return;
            if (!Plugin.AudioNear(gameObject, 550f)) return;
            if (_burstAudio && string.Equals(_burstWeapon, weapon ?? "", StringComparison.Ordinal)) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var gun = PickGun(weapon);
            if (gun == null) return;
            var loop = gun.GetType().GetField("fireSustained", flags)?.GetValue(gun);
            if (loop == null) return;
            for (int i = 0; i < _guns.Length; i++)
                if (_guns[i] != null) MuteGunSources(_guns[i], true, true);
            if (_loopSrc == null)
            {
                var host = new GameObject("NOReplay_GunLoop");
                host.transform.SetParent(transform, false);
                var srcType = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
                if (srcType == null) return;
                _loopSrc = host.AddComponent(srcType);
            }
            var st = _loopSrc.GetType();
            st.GetProperty("spatialBlend")?.SetValue(_loopSrc, 1f);
            st.GetProperty("minDistance")?.SetValue(_loopSrc, 25f);
            st.GetProperty("maxDistance")?.SetValue(_loopSrc, 700f);
            st.GetProperty("loop")?.SetValue(_loopSrc, true);
            st.GetProperty("playOnAwake")?.SetValue(_loopSrc, false);
            st.GetProperty("clip")?.SetValue(_loopSrc, loop);
            st.GetProperty("volume")?.SetValue(_loopSrc, 1f);
            st.GetProperty("mute")?.SetValue(_loopSrc, false);
            st.GetMethod("Stop", Type.EmptyTypes)?.Invoke(_loopSrc, null);
            st.GetMethod("Play", Type.EmptyTypes)?.Invoke(_loopSrc, null);
            _burstAudio = true;
            _burstWeapon = weapon ?? "";
        }

        Component? PickGun(string? weapon)
        {
            if (!string.IsNullOrEmpty(weapon))
            {
                for (int i = 0; i < _guns.Length; i++)
                {
                    var g = _guns[i];
                    if (g == null) continue;
                    if (GunPath(transform, g.transform).Equals(weapon, StringComparison.OrdinalIgnoreCase)) return g;
                }
                string leaf = weapon;
                int slash = weapon.LastIndexOf('/');
                if (slash >= 0 && slash < weapon.Length - 1) leaf = weapon.Substring(slash + 1);
                for (int i = 0; i < _guns.Length; i++)
                {
                    var g = _guns[i];
                    if (g != null && g.name.Equals(leaf, StringComparison.OrdinalIgnoreCase)) return g;
                }
            }
            return _guns.Length > 0 ? _guns[0] : null;
        }

        static string GunPath(Transform root, Transform gun)
        {
            var parts = new List<string>();
            var t = gun;
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        internal void EndBurstAudio()
        {
            if (!_burstAudio) return;
            _burstAudio = false;
            _burstWeapon = "";
            if (_loopSrc != null)
            {
                var st = _loopSrc.GetType();
                st.GetMethod("Stop", Type.EmptyTypes)?.Invoke(_loopSrc, null);
                st.GetProperty("volume")?.SetValue(_loopSrc, 0f);
            }
            if (_guns.Length > 0 && _guns[0] != null) MuteGunSources(_guns[0], true, true);
        }

        static void MuteGunSources(Component gun, bool mute, bool stop)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sources = gun.GetType().GetField("sources", flags)?.GetValue(gun) as Array;
            if (sources == null) return;
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources.GetValue(i);
                if (src == null) continue;
                src.GetType().GetProperty("mute")?.SetValue(src, mute);
                if (stop) src.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(src, null);
            }
        }

        internal Vector3 MuzzlePos()
        {
            BindGuns();
            if (_gunMuzzles.Length > 0 && _gunMuzzles[0] != null) return _gunMuzzles[0].position;
            return transform.position;
        }

        static void Arm(object? src)
        {
            if (src == null) return;
            var st = src.GetType();
            st.GetProperty("mute")?.SetValue(src, false);
            st.GetProperty("spatialBlend")?.SetValue(src, 1f);
            st.GetProperty("volume")?.SetValue(src, 1f);
            st.GetProperty("minDistance")?.SetValue(src, 25f);
            st.GetProperty("maxDistance")?.SetValue(src, 900f);
        }

        bool _burstAudio;
        string _burstWeapon = "";
        Component? _loopSrc;

        internal void PulseAt(Vector3 pos)
        {
            BindGuns();
            if (_guns.Length == 0 || _gunMuzzles.Length == 0) return;
            Transform muzzle = _gunMuzzles[0];
            float best = 80f * 80f;
            for (int i = 0; i < _gunMuzzles.Length; i++)
            {
                var m = _gunMuzzles[i];
                if (m == null) continue;
                float d = (m.position - pos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    muzzle = m;
                }
            }
            if (best > 80f * 80f) return;
            var gun = _guns[0];
            if (gun == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var parts = gun.GetType().GetField("muzzleParticles", flags)?.GetValue(gun) as Array;
                if (parts != null)
                    foreach (var p in parts) PlayPs(p);
            }
            catch { }
            try
            {
                var clips = gun.GetType().GetField("fireSounds", flags)?.GetValue(gun) as Array;
                var sources = gun.GetType().GetField("sources", flags)?.GetValue(gun) as Array;
                object? clip = clips != null && clips.Length > 0 ? clips.GetValue(0) : null;
                object? src = sources != null && sources.Length > 0 ? sources.GetValue(0) : null;
                if (clip == null || src == null)
                {
                    var comps = gun.GetComponentsInChildren<Component>(true);
                    for (int i = 0; i < comps.Length; i++)
                    {
                        var c = comps[i];
                        if (c == null) continue;
                        if (src == null && c.GetType().Name == "AudioSource") src = c;
                        if (clip != null && src != null) break;
                        foreach (var f in c.GetType().GetFields(flags))
                        {
                            object? v = null;
                            try { v = f.GetValue(c); } catch { }
                            if (v == null) continue;
                            if (clip == null && v.GetType().Name == "AudioClip") clip = v;
                            if (v is Array arr && arr.Length > 0 && clip == null && arr.GetValue(0)?.GetType().Name == "AudioClip")
                                clip = arr.GetValue(0);
                        }
                    }
                }
                if (clip == null || src == null)
                {
                    if (!_shotLogged)
                    {
                        _shotLogged = true;
                        Plugin.Log.LogInfo("GunShot ohne Clip typ=" + gun.GetType().Name);
                    }
                    return;
                }
                var shot = transform.Find("NOReplay_GunShot");
                if (shot == null)
                {
                    var host = new GameObject("NOReplay_GunShot");
                    host.transform.SetParent(transform, false);
                    shot = host.transform;
                    var added = host.AddComponent(src.GetType());
                    src = added;
                }
                else src = shot.GetComponent(src.GetType());
                if (src == null) return;
                var st = src.GetType();
                st.GetProperty("mute")?.SetValue(src, false);
                st.GetProperty("spatialBlend")?.SetValue(src, 1f);
                st.GetProperty("volume")?.SetValue(src, 1f);
                st.GetProperty("minDistance")?.SetValue(src, 30f);
                st.GetProperty("maxDistance")?.SetValue(src, 1200f);
                st.GetProperty("clip")?.SetValue(src, clip);
                st.GetProperty("loop")?.SetValue(src, false);
                st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
                if (!_shotLogged)
                {
                    _shotLogged = true;
                    Plugin.Log.LogInfo("GunShot Play " + clip);
                }
            }
            catch (Exception ex)
            {
                if (!_shotLogged) { _shotLogged = true; Plugin.Log.LogInfo("GunShot " + ex.Message); }
            }
        }
    }
}
