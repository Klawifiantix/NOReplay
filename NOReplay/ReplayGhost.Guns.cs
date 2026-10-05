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

        internal void AimGun(float yawDeg, float pitchDeg)
        {
            BindGuns();
            _gunAimUntil = Time.unscaledTime + 0.2f;
            var yaw = Quaternion.Euler(0f, yawDeg, 0f);
            var pitch = Quaternion.Euler(-pitchDeg, 0f, 0f);
            for (int i = 0; i < _turrets.Length; i++)
            {
                if (_turrets[i] != null) _turrets[i].localRotation = yaw;
                if (_elev[i] != null) _elev[i].localRotation = pitch;
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
                        st.GetProperty("spatialBlend")?.SetValue(src, 1f);
                        st.GetProperty("volume")?.SetValue(src, 1f);
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
            st.GetProperty("volume")?.SetValue(_loopSrc, near ? 1f : 0f);
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

    static class ShellReplay
    {
        static ReplayObject[] _shells = Array.Empty<ReplayObject>();
        static ReplayDocument? _doc;
        static readonly List<GameObject> _pool = new List<GameObject>();
        static readonly HashSet<string> _boomed = new HashSet<string>();
        static readonly Dictionary<int, string> _burst = new Dictionary<int, string>();
        static float _lastTime;
        static bool _shown;
        static bool _missLogged;

        static ReplayGhost[] _ghosts = Array.Empty<ReplayGhost>();
        static readonly string[] _slotId = new string[36];
        static int _ghostFrame;

        public static void Tick(ReplayDocument? doc, float time)
        {
            if (doc == null) return;
            if (!ReferenceEquals(doc, _doc))
            {
                _doc = doc;
                _shells = Collect(doc);
                _boomed.Clear();
                _burst.Clear();
                Plugin.Log.LogInfo("Shells in ACMI: " + _shells.Length);
            }
            BurstReplay.Tick(doc, time);
            if (time + 0.05f < _lastTime) { _boomed.Clear(); _burst.Clear(); }
            _lastTime = time;
            if ((Time.frameCount - _ghostFrame) > 30)
            {
                _ghostFrame = Time.frameCount;
                _ghosts = UnityEngine.Object.FindObjectsOfType<ReplayGhost>();
            }
            int slot = 0;
            Vector3 listen = ListenAt();
            bool haveListen = listen.sqrMagnitude > 500f * 500f;
            for (int i = 0; i < _shells.Length && slot < 36; i++)
            {
                var o = _shells[i];
                if (o.SpawnAt > time) continue;
                if (o.RemovedAt >= 0f && time > o.RemovedAt + 1.2f) continue;
                if (o.Samples.Count < 2) continue;
                var a = o.Samples[0];
                int i1 = 1;
                while (i1 < o.Samples.Count - 1 && o.Samples[i1].Time - a.Time < 0.05f) i1++;
                var p1 = o.Samples[i1];
                var origin = Plugin.GlobalToLocal(a.X, a.Y, a.Z);
                var hit = Plugin.GlobalToLocal(p1.X, p1.Y, p1.Z);
                var dir = hit - origin;
                if (dir.sqrMagnitude < 4f) continue;
                float span = Mathf.Max(0.2f, p1.Time - a.Time);
                float speed = Mathf.Clamp(dir.magnitude / span, 180f, 900f);
                float age = time - a.Time;
                if (age < 0f || age > span + 0.15f) continue;
                int bucket = (int)(a.Time * 8f);
                int key = ((int)a.X / 20) * 73856093 ^ ((int)a.Z / 20) * 19349663 ^ bucket;
                if (_burst.TryGetValue(key, out var owner) && owner != o.Id) continue;
                _burst[key] = o.Id;
                var pos = origin + dir.normalized * Mathf.Min(speed * age, dir.magnitude);
                if (haveListen && (pos - listen).sqrMagnitude > 2000f * 2000f) continue;
                var go = Take(slot);
                var trail = go.GetComponent<TrailRenderer>();
                if (trail != null && _slotId[slot] != o.Id)
                {
                    trail.emitting = false;
                    trail.time = 0f;
                    trail.Clear();
                    go.SetActive(false);
                    go.transform.position = pos;
                    go.SetActive(true);
                    trail.Clear();
                    trail.time = 0.16f;
                    trail.emitting = true;
                    _slotId[slot] = o.Id;
                }
                slot++;
                go.transform.position = pos;
                go.SetActive(true);
                if (!_shown)
                {
                    _shown = true;
                    Plugin.Log.LogInfo("Shell fliegt " + o.Id + " @ " + pos + " v=" + speed.ToString("0"));
                }
                if (_boomed.Add(o.Id))
                    PulseNear(a, _ghosts);
            }
            if (!_shown && time > 7f && !_missLogged)
            {
                _missLogged = true;
                Plugin.Log.LogInfo("Shell unsichtbar t=" + time.ToString("0.0") + " listen=" + haveListen + " " + listen);
            }
            for (int i = slot; i < _pool.Count; i++)
            {
                var dead = _pool[i];
                if (dead != null && dead.activeSelf)
                {
                    var trail = dead.GetComponent<TrailRenderer>();
                    if (trail != null)
                    {
                        trail.emitting = false;
                        trail.time = 0f;
                        trail.Clear();
                    }
                    dead.SetActive(false);
                }
                if (i < _slotId.Length) _slotId[i] = "";
            }
        }

        static Vector3 ListenAt()
        {
            var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null) continue;
                if (c.GetComponent("AudioListener") == null) continue;
                if (c.transform.position.sqrMagnitude > 500f * 500f) return c.transform.position;
            }
            for (int i = 0; i < _ghosts.Length; i++)
            {
                var rg = _ghosts[i];
                if (rg == null || !rg.gameObject.activeInHierarchy) continue;
                if (Plugin.IsFollowed(rg.gameObject)) return rg.transform.position;
            }
            return Vector3.zero;
        }

        static bool NearGhost(Vector3 pos)
        {
            if (_ghosts.Length == 0) return true;
            float lim = 1800f * 1800f;
            for (int i = 0; i < _ghosts.Length; i++)
            {
                var rg = _ghosts[i];
                if (rg == null || !rg.gameObject.activeInHierarchy) continue;
                if ((rg.transform.position - pos).sqrMagnitude < lim) return true;
            }
            return false;
        }

        static void PulseNear(ReplaySample s, ReplayGhost[] ghosts)
        {
            var pos = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
            ReplayGhost? best = null;
            float bestD = 700f * 700f;
            for (int i = 0; i < ghosts.Length; i++)
            {
                var rg = ghosts[i];
                if (rg == null) continue;
                float d = (rg.transform.position - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = rg;
                }
            }
            best?.PulseAt(pos);
        }

        static ReplayObject[] Collect(ReplayDocument doc)
        {
            var list = new List<ReplayObject>();
            foreach (var o in doc.Objects)
            {
                string t = o.Type ?? "";
                if (t.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (o.Samples.Count == 0) continue;
                list.Add(o);
            }
            return list.ToArray();
        }

        static GameObject Take(int slot)
        {
            while (_pool.Count <= slot)
            {
                var go = MakeTracer();
                go.name = "NOReplay_Shell";
                go.SetActive(false);
                _pool.Add(go);
            }
            return _pool[slot];
        }

        static GameObject MakeTracer()
        {
            var go = new GameObject("NOReplay_Shell");
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 1.6f;
            trail.endWidth = 0.35f;
            trail.minVertexDistance = 0.15f;
            trail.numCapVertices = 4;
            trail.emitting = true;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var yellow = new Color(1f, 0.92f, 0.15f, 1f);
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = yellow;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellow * 6f);
                if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", yellow * 6f);
                if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", yellow * 12f);
                mat.EnableKeyword("_EMISSION");
                trail.material = mat;
            }
            trail.startColor = yellow;
            trail.endColor = new Color(1f, 0.85f, 0.1f, 0.35f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "head";
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = Vector3.one * 1.8f;
            var col = head.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var hr = head.GetComponent<Renderer>();
            if (hr != null && shader != null)
            {
                var hm = new Material(shader);
                hm.color = yellow;
                if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", yellow * 8f);
                if (hm.HasProperty("_UnlitColor")) hm.SetColor("_UnlitColor", yellow * 8f);
                if (hm.HasProperty("_EmissiveColor")) hm.SetColor("_EmissiveColor", yellow * 16f);
                hm.EnableKeyword("_EMISSION");
                hr.sharedMaterial = hm;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }
    }

    static class BurstReplay
    {
        static ReplayObject[] _bursts = Array.Empty<ReplayObject>();
        static ReplayDocument? _doc;
        static readonly List<GameObject> _pool = new List<GameObject>();
        static readonly string[] _slotId = new string[48];
        static bool _shown;

        static readonly HashSet<string> _heard = new HashSet<string>();

        public static void Tick(ReplayDocument doc, float time)
        {
            if (!ReferenceEquals(doc, _doc))
            {
                _doc = doc;
                var list = new List<ReplayObject>();
                foreach (var o in doc.Objects)
                    if ((o.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0 && o.Samples.Count > 0)
                        list.Add(o);
                _bursts = list.ToArray();
                _heard.Clear();
                Plugin.Log.LogInfo("Kanonenstöße im Replay: " + _bursts.Length);
            }
            int slot = 0;
            for (int i = 0; i < _bursts.Length && slot < 48; i++)
            {
                var o = _bursts[i];
                float start = o.SpawnAt >= 0f ? o.SpawnAt : o.Samples[0].Time;
                float end = o.RemovedAt >= 0f ? o.RemovedAt : o.Samples[o.Samples.Count - 1].Time;
                var shooter = FindParent(o);
                var ghost = GhostFor(shooter);
                if (time > end && _heard.Contains(o.Id))
                {
                    _heard.Remove(o.Id);
                    ghost?.EndBurstAudio();
                }
                if (time < start || time > end + 1.6f) continue;
                float rpm = o.Rpm > 1f ? o.Rpm : 750f;
                float speed = o.Muzzle > 50f ? o.Muzzle : 900f;
                float grav = o.Grav > 0f ? o.Grav : 1f;
                float step = Mathf.Max(0.04f, 60f / rpm);
                bool pulsed = false;
                for (float fired = start; fired <= end && slot < 48; fired += step)
                {
                    float age = time - fired;
                    if (age < 0f || age > 1.6f) continue;
                    if (!o.TrySample(fired, out var aim)) continue;
                    Vector3 origin;
                    if (shooter != null && shooter.TrySample(fired, out var src))
                        origin = Plugin.GlobalToLocal(src.X, src.Y, src.Z);
                    else if (Mathf.Abs(aim.X) < 4000f && Mathf.Abs(aim.Z) < 4000f)
                        origin = new Vector3(aim.X, aim.Y, aim.Z);
                    else
                        origin = Plugin.GlobalToLocal(aim.X, aim.Y, aim.Z);
                    if (ghost != null)
                    {
                        origin = ghost.MuzzlePos();
                        ghost.AimGun(aim.Yaw, aim.Pitch);
                    }
                    float yaw = aim.Yaw * Mathf.Deg2Rad;
                    float pitch = -aim.Pitch * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                    if (dir.sqrMagnitude < 0.01f) continue;
                    float drop = 0.5f * 9.81f * grav * age * age;
                    var pos = origin + dir.normalized * (speed * age) + Vector3.down * drop;
                    var go = Take(slot);
                    var trail = go.GetComponent<TrailRenderer>();
                    string id = o.Id + ":" + fired.ToString("0.00");
                    if (trail != null && _slotId[slot] != id)
                    {
                        trail.emitting = false;
                        trail.time = 0f;
                        trail.Clear();
                        go.SetActive(false);
                        go.transform.position = pos;
                        go.SetActive(true);
                        trail.Clear();
                        trail.time = 0.12f;
                        trail.emitting = true;
                        _slotId[slot] = id;
                    }
                    go.transform.position = pos;
                    go.SetActive(true);
                    slot++;
                    if (!pulsed && ghost != null && time <= end)
                    {
                        pulsed = true;
                        ghost.HoldBurstAudio();
                        if (Plugin.AudioNear(ghost.gameObject, 550f) && _heard.Add(o.Id))
                            ghost.BeginBurstAudio(o.Weapon);
                    }
                    if (!_shown)
                    {
                        _shown = true;
                        Plugin.Log.LogInfo("Kanonenstoß " + o.Id + " rpm=" + rpm.ToString("0") + " v=" + speed.ToString("0") + " @ " + origin);
                    }
                }
            }
            for (int i = slot; i < _pool.Count; i++)
            {
                var dead = _pool[i];
                if (dead != null && dead.activeSelf)
                {
                    var trail = dead.GetComponent<TrailRenderer>();
                    if (trail != null)
                    {
                        trail.emitting = false;
                        trail.time = 0f;
                        trail.Clear();
                    }
                    dead.SetActive(false);
                }
                if (i < _slotId.Length) _slotId[i] = "";
            }
        }

        static ReplayGhost[] _burstGhosts = Array.Empty<ReplayGhost>();
        static int _burstGhostFrame = -999;

        static ReplayGhost? GhostFor(ReplayObject? shooter)
        {
            if (shooter == null) return null;
            if (Time.frameCount - _burstGhostFrame > 30)
            {
                _burstGhostFrame = Time.frameCount;
                _burstGhosts = UnityEngine.Object.FindObjectsOfType<ReplayGhost>();
            }
            for (int i = 0; i < _burstGhosts.Length; i++)
            {
                var g = _burstGhosts[i];
                if (g != null && g.Track != null && g.Track.Id == shooter.Id)
                    return g;
            }
            return null;
        }

        static ReplayObject? FindParent(ReplayObject burst)
        {
            if (_doc == null || string.IsNullOrEmpty(burst.ParentId)) return null;
            for (int i = 0; i < _doc.Objects.Count; i++)
            {
                var o = _doc.Objects[i];
                if (o.Id.Equals(burst.ParentId, StringComparison.OrdinalIgnoreCase)) return o;
            }
            return null;
        }

        static GameObject Take(int slot)
        {
            while (_pool.Count <= slot)
            {
                var go = new GameObject("NOReplay_Burst");
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.12f;
                trail.startWidth = 0.45f;
                trail.endWidth = 0.05f;
                trail.minVertexDistance = 0.4f;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var yellow = new Color(1f, 0.9f, 0.2f, 1f);
                var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellow * 6f);
                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", yellow * 12f);
                    mat.EnableKeyword("_EMISSION");
                    trail.material = mat;
                }
                trail.startColor = yellow;
                trail.endColor = new Color(1f, 0.8f, 0.1f, 0.2f);
                go.SetActive(false);
                _pool.Add(go);
            }
            return _pool[slot];
        }
    }
}