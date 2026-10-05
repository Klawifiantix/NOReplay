using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        void ToggleOrdnanceFx(bool on)
        {
            if (_ordFx.Length == 0)
            {
                var list = new System.Collections.Generic.List<Component>();
                foreach (var c in GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0
                        || tn.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add(c);
                }
                _ordFx = list.ToArray();
            }
            for (int i = 0; i < _ordFx.Length; i++)
            {
                if (_ordFx[i] == null) continue;
                var p = _ordFx[i].GetType().GetProperty("enabled");
                try { p?.SetValue(_ordFx[i], on); } catch { }
            }
        }

        bool NearMe(float maxM) => Plugin.AudioNear(gameObject, maxM);

        static bool NearListener(Vector3 world, float maxM)
            => Plugin.AudioNearWorld(world, maxM);


        static int _missileVoices;
        object? _flightSrc;
        object? _detonateClip;

        void IgniteMissileFx()
        {
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (missile != null && mt != null)
            {
                ActivateMotorVisuals(missile, mt, flags);
                ApplyMissileAudio(missile, mt);
            }
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? Plugin.FindGameType("ParticleSystem");
            if (pt != null)
            {
                var clear = pt.GetMethod("Clear", new[] { typeof(bool) });
                var play = pt.GetMethod("Play", new[] { typeof(bool) });
                foreach (var ps in GetComponentsInChildren(pt, true))
                {
                    if (ps == null) continue;
                    var c = ps as Component;
                    if (c != null) c.gameObject.SetActive(true);
                    try { clear?.Invoke(ps, new object[] { true }); } catch { }
                    try { play?.Invoke(ps, new object[] { true }); } catch { }
                }
            }
            _ordAudioOn = true;
        }

        void LateOrdnanceAudio()
        {
            if (!_ordnance || _boomed) return;
            if (!Plugin.IsFollowed(gameObject) && !NearMe(1400f)) return;
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            if (missile == null || mt == null) return;
            DriveMissileAudio(missile, mt);
        }

        void DriveMissileAudio(object missile, Type mt)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            float spd = 0f;
            try
            {
                object? sv = mt.GetField("speed", flags)?.GetValue(missile);
                if (sv is float sf) spd = sf;
            }
            catch { }
            if (Track != null && Track.TrySample(Plugin.Clock.Time, out var sample))
            {
                float fromTrack = 0f;
                if (sample.HasTas && sample.Tas > 1f)
                    fromTrack = sample.Tas > 40f ? sample.Tas / 1.94384f : sample.Tas;
                if (fromTrack < 1f && Track.Samples.Count > 1)
                {
                    int i = 0;
                    var samples = Track.Samples;
                    while (i < samples.Count - 1 && samples[i + 1].Time < Plugin.Clock.Time) i++;
                    var a = samples[i];
                    var b = samples[Math.Min(i + 1, samples.Count - 1)];
                    float dt = b.Time - a.Time;
                    if (dt > 0.01f)
                    {
                        float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                        fromTrack = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
                    }
                }
                if (fromTrack > spd) spd = fromTrack;
            }
            try { mt.GetField("speed", flags)?.SetValue(missile, spd); } catch { }
            float maxPitch = 340f;
            float pitchRange = 1f;
            float basePitch = 0.5f;
            try
            {
                if (mt.GetField("maxPitchSpeed", flags)?.GetValue(missile) is float m) maxPitch = m;
                if (mt.GetField("pitchRange", flags)?.GetValue(missile) is float p) pitchRange = p;
                if (mt.GetField("basePitch", flags)?.GetValue(missile) is float b) basePitch = b;
            }
            catch { }
            float num = Mathf.Clamp01((-50f + spd) / Mathf.Max(maxPitch, 1f));
            object? flight = null;
            try { flight = mt.GetField("flightSound", flags)?.GetValue(missile); } catch { }
            if (flight != null)
            {
                var ft = flight.GetType();
                bool flightLoop = true;
                try { if (ft.GetProperty("loop")?.GetValue(flight) is bool lb) flightLoop = lb; } catch { }
                ft.GetProperty("mute")?.SetValue(flight, false);
                ft.GetProperty("loop")?.SetValue(flight, flightLoop);
                ft.GetProperty("spatialBlend")?.SetValue(flight, 1f);
                ft.GetProperty("dopplerLevel")?.SetValue(flight, 0f);
                ft.GetProperty("minDistance")?.SetValue(flight, 15f);
                ft.GetProperty("maxDistance")?.SetValue(flight, 1600f);
                float vol = 0f;
                try { if (ft.GetProperty("volume")?.GetValue(flight) is float v) vol = v; } catch { }
                ft.GetProperty("volume")?.SetValue(flight, vol < num ? Mathf.Min(num, vol + Time.deltaTime) : num);
                ft.GetProperty("pitch")?.SetValue(flight, pitchRange * num + basePitch);
                bool playing = false;
                try { playing = ft.GetProperty("isPlaying")?.GetValue(flight) is bool on && on; } catch { }
                if (!playing) ft.GetMethod("Play", Type.EmptyTypes)?.Invoke(flight, null);
            }
            try
            {
                var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                if (motors == null) return;
                foreach (var motor in motors)
                {
                    if (motor == null) continue;
                    var tt = motor.GetType();
                    var start = tt.GetField("startupSource", flags)?.GetValue(motor);
                    if (start != null && !_startupPlayed)
                    {
                        var st = start.GetType();
                        st.GetProperty("mute")?.SetValue(start, false);
                        st.GetProperty("loop")?.SetValue(start, false);
                        st.GetProperty("volume")?.SetValue(start, 1f);
                        bool sp = false;
                        try { sp = st.GetProperty("isPlaying")?.GetValue(start) is bool b && b; } catch { }
                        if (!sp) st.GetMethod("Play", Type.EmptyTypes)?.Invoke(start, null);
                        _startupPlayed = true;
                    }
                    if (!_startupPlayed) continue;
                    var srcs = tt.GetField("audioSources", flags)?.GetValue(motor) as Array;
                    if (srcs == null) continue;
                    foreach (var s in srcs)
                    {
                        if (s == null || ReferenceEquals(s, start)) continue;
                        var st = s.GetType();
                        bool loop = false;
                        try { if (st.GetProperty("loop")?.GetValue(s) is bool b) loop = b; } catch { }
                        st.GetProperty("mute")?.SetValue(s, false);
                        st.GetProperty("loop")?.SetValue(s, loop);
                        st.GetProperty("spatialBlend")?.SetValue(s, 1f);
                        st.GetProperty("volume")?.SetValue(s, 0.8f);
                        st.GetProperty("minDistance")?.SetValue(s, 15f);
                        st.GetProperty("maxDistance")?.SetValue(s, 1600f);
                        bool playing = false;
                        try { playing = st.GetProperty("isPlaying")?.GetValue(s) is bool pb && pb; } catch { }
                        if (!playing) st.GetMethod("Play", Type.EmptyTypes)?.Invoke(s, null);
                    }
                }
            }
            catch { }
        }

        bool _startupPlayed;

        static void PlayPs(object? ps)
        {
            if (ps == null) return;
            try
            {
                var t = ps.GetType();
                if (ps is Behaviour b) b.enabled = true;
                t.GetMethod("Clear", new[] { typeof(bool) })?.Invoke(ps, new object[] { true });
                var play = t.GetMethod("Play", new[] { typeof(bool) })
                           ?? t.GetMethod("Play", Type.EmptyTypes);
                if (play != null)
                    play.Invoke(ps, play.GetParameters().Length == 1 ? new object[] { true } : null);
            }
            catch { }
        }

        void ActivateMotorVisuals(object missile, Type mt, BindingFlags flags)
        {
            try
            {
                var fxTr = mt.GetField("effectsTransform", flags)?.GetValue(missile) as Transform;
                if (fxTr != null)
                {
                    fxTr.gameObject.SetActive(true);
                    PlayParticles(fxTr.gameObject);
                }
            }
            catch { }
            try
            {
                var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                if (motors == null) return;
                foreach (var motor in motors)
                {
                    if (motor == null) continue;
                    var tt = motor.GetType();
                    var parts = tt.GetField("particleSystems", flags)?.GetValue(motor) as Array;
                    if (parts != null)
                    {
                        foreach (var p in parts)
                            PlayPs(p);
                    }
                    var lights = tt.GetField("lights", flags)?.GetValue(motor) as Array;
                    if (lights != null)
                    {
                        foreach (var l in lights)
                        {
                            if (l is Behaviour lb) lb.enabled = true;
                            else
                            {
                                try { l?.GetType().GetProperty("enabled")?.SetValue(l, true); } catch { }
                            }
                        }
                    }
                    var trails = tt.GetField("trailEmitters", flags)?.GetValue(motor) as Array;
                    if (trails != null)
                    {
                        foreach (var tr in trails)
                        {
                            if (tr == null) continue;
                            try
                            {
                                if (tr is Behaviour tb) tb.enabled = true;
                                tr.GetType().GetMethod("StartTrail", flags)?.Invoke(tr, null);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        void ApplyMissileAudio(object missile, Type mt)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bool near = Plugin.IsFollowed(gameObject) || NearMe(900f);
            if (!near)
            {
                return;
            }
            if (_ordVoiceHeld) return;
            if (!Plugin.IsFollowed(gameObject) && _missileVoices >= 4) return;
            try { _flightSrc = mt.GetField("flightSound", flags)?.GetValue(missile); } catch { }
            try { _detonateClip = mt.GetField("nearbyDetonationClip", flags)?.GetValue(missile); } catch { }
            _missileVoices++;
            _ordVoiceHeld = true;
            try
            {
                var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                if (motors != null)
                {
                    foreach (var motor in motors)
                    {
                        if (motor == null) continue;
                        var tt = motor.GetType();
                        var start = tt.GetField("startupSource", flags)?.GetValue(motor);
                        if (start != null)
                            PlaySrc(start, 1f, loop: false, restart: true);
                        var srcs = tt.GetField("audioSources", flags)?.GetValue(motor) as Array;
                        if (srcs == null) continue;
                        foreach (var s in srcs)
                        {
                            if (s == null) continue;
                            bool loop = false;
                            try
                            {
                                var lp = s.GetType().GetProperty("loop")?.GetValue(s);
                                if (lp is bool b) loop = b;
                            }
                            catch { }
                            string tag = "";
                            try
                            {
                                object? cl = s.GetType().GetProperty("clip")?.GetValue(s);
                                tag = ((cl != null ? cl.ToString() : "") + " " + (s as Component)?.name).ToLowerInvariant();
                            }
                            catch { }
                            if (tag.IndexOf("launch") >= 0 || tag.IndexOf("fire") >= 0
                                || tag.IndexOf("shot") >= 0 || tag.IndexOf("ignit") >= 0
                                || tag.IndexOf("start") >= 0)
                                loop = false;
                            PlaySrc(s, 0.7f, loop, restart: !loop ? false : true);
                        }
                    }
                }
            }
            catch { }
            if (_flightSrc != null)
                PlaySrc(_flightSrc, 0.65f, loop: true, restart: true);
        }

        static void PlaySrc(object? src, float vol, bool loop, bool restart = true)
        {
            if (src == null) return;
            var t = src.GetType();
            try
            {
                t.GetProperty("enabled")?.SetValue(src, true);
                t.GetProperty("spatialBlend")?.SetValue(src, 1f);
                t.GetProperty("dopplerLevel")?.SetValue(src, 0f);
                t.GetProperty("minDistance")?.SetValue(src, 8f);
                t.GetProperty("maxDistance")?.SetValue(src, 1200f);
                t.GetProperty("rolloffMode")?.SetValue(src, 0);
                t.GetProperty("priority")?.SetValue(src, 200);
                t.GetProperty("volume")?.SetValue(src, vol);
                t.GetProperty("loop")?.SetValue(src, loop);
                bool playing = false;
                try { playing = t.GetProperty("isPlaying")?.GetValue(src) is bool b && b; } catch { }
                if (playing && !restart && loop) return;
                if (playing && !restart && !loop)
                    return;
                t.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
            }
            catch { }
        }

        void PlayOrdnanceAudio()
        {
            if (_ordAudioOn) return;
            if (!NearMe(500f)) return;
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var play = at.GetMethod("Play", Type.EmptyTypes);
            var loop = at.GetProperty("loop");
            var en = at.GetProperty("enabled");
            var clip = at.GetProperty("clip");
            foreach (var c in GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    en?.SetValue(c, true);
                    at.GetProperty("spatialBlend")?.SetValue(c, 1f);
                    at.GetProperty("dopplerLevel")?.SetValue(c, 0f);
                    at.GetProperty("minDistance")?.SetValue(c, 25f);
                    at.GetProperty("maxDistance")?.SetValue(c, 450f);
                    at.GetProperty("rolloffMode")?.SetValue(c, 0);
                    at.GetProperty("priority")?.SetValue(c, 200);
                    var cl = clip?.GetValue(c);
                    string cn = ((cl != null ? cl.ToString() : "") + " " + c.name).ToLowerInvariant();
                    bool launch = cn.IndexOf("launch") >= 0 || cn.IndexOf("fire") >= 0
                               || cn.IndexOf("shot") >= 0 || cn.IndexOf("eject") >= 0;
                    if (!launch)
                        loop?.SetValue(c, true);
                    play?.Invoke(c, null);
                }
                catch { }
            }
            _ordAudioOn = true;
        }

        void StopOrdnanceAudio()
        {
            if (!_ordAudioOn) return;
            if (_ordVoiceHeld && _missileVoices > 0) _missileVoices--;
            _ordVoiceHeld = false;
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at != null)
            {
                var stop = at.GetMethod("Stop", Type.EmptyTypes);
                foreach (var c in GetComponentsInChildren(at, true))
                {
                    if (c == null) continue;
                    try { stop?.Invoke(c, null); } catch { }
                }
            }
            _ordAudioOn = false;
        }

        static Type? ParticleType()
        {
            var t = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule");
            if (t != null) return t;
            return Plugin.FindGameType("ParticleSystem");
        }

        static bool HasParticles(GameObject go)
        {
            var pt = ParticleType();
            if (pt == null) return false;
            return go.GetComponentInChildren(pt, true) != null;
        }

        static void PlayParticles(GameObject go)
        {
            var pt = ParticleType();
            if (pt == null) return;
            var play = pt.GetMethod("Play", new[] { typeof(bool) });
            foreach (var c in go.GetComponentsInChildren(pt, true))
            {
                if (c == null) continue;
                try { play?.Invoke(c, new object[] { true }); } catch { }
            }
        }

        bool IsFlareTrack()
        {
            string t = Track?.Type ?? "";
            string n = Track?.Name ?? "";
            return t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal void ForceBoom()
        {
            if (!_ordnance || _boomed) return;
            _boomed = true;
            if (IsFlareTrack()) return;
            PlayWarheadBoom(transform.position);
        }

        internal void ForceWreck()
        {
            if (_ordnance || _boomed) return;
            if (Track == null || Track.RemovedAt < 0f) return;
            if (Plugin.Clock.Time + 0.05f < Track.RemovedAt) return;
            _boomed = true;
            PlayUnitWreckFx(transform.position, transform.rotation);
        }

        static void PlayDetachedShot(object? clip, Vector3 pos)
        {
            if (clip == null) return;
            if (!Plugin.AudioNearWorld(pos, 700f)) return;
            try
            {
                var go = new GameObject("NOReplay_Detonate");
                go.transform.position = pos;
                var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                         ?? Plugin.FindGameType("AudioSource");
                if (at == null) { UnityEngine.Object.Destroy(go); return; }
                var src = go.AddComponent(at);
                at.GetProperty("clip")?.SetValue(src, clip);
                at.GetProperty("loop")?.SetValue(src, false);
                at.GetProperty("spatialBlend")?.SetValue(src, 1f);
                at.GetProperty("dopplerLevel")?.SetValue(src, 0f);
                at.GetProperty("volume")?.SetValue(src, 1f);
                at.GetProperty("minDistance")?.SetValue(src, 20f);
                at.GetProperty("maxDistance")?.SetValue(src, 800f);
                at.GetProperty("rolloffMode")?.SetValue(src, 1);
                at.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
                UnityEngine.Object.Destroy(go, 8f);
            }
            catch { }
        }

        void PlayWarheadBoom(Vector3 pos)
        {
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (missile != null && mt != null)
            {
                try
                {
                    var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                    if (motors != null)
                    {
                        foreach (var motor in motors)
                        {
                            if (motor == null) continue;
                            try { motor.GetType().GetMethod("Burnout", flags)?.Invoke(motor, new object[] { true }); } catch { }
                        }
                    }
                }
                catch { }
                try
                {
                    var flight = mt.GetField("flightSound", flags)?.GetValue(missile);
                    var clip = mt.GetField("nearbyDetonationClip", flags)?.GetValue(missile);
                    if (flight != null && clip != null)
                    {
                        var ft = flight.GetType();
                        ft.GetMethod("Stop", Type.EmptyTypes)?.Invoke(flight, null);
                        ft.GetProperty("clip")?.SetValue(flight, clip);
                        ft.GetProperty("pitch")?.SetValue(flight, 1f);
                        ft.GetProperty("volume")?.SetValue(flight, 1f);
                        ft.GetProperty("loop")?.SetValue(flight, false);
                        ft.GetProperty("mute")?.SetValue(flight, false);
                        ft.GetMethod("Play", Type.EmptyTypes)?.Invoke(flight, null);
                    }
                }
                catch { }
            }
            GameObject? fx = null;
            if (missile != null && mt != null)
            {
                try
                {
                    var warhead = mt.GetField("warhead", flags)?.GetValue(missile);
                    if (warhead != null)
                    {
                        var wt = warhead.GetType();
                        bool terrain = false;
                        try
                        {
                            terrain = Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, 8f);
                        }
                        catch { }
                        string field = terrain ? "terrainEffect" : "airEffect";
                        fx = wt.GetField(field, flags)?.GetValue(warhead) as GameObject
                             ?? wt.GetField("airEffect", flags)?.GetValue(warhead) as GameObject
                             ?? wt.GetField("fizzleEffect", flags)?.GetValue(warhead) as GameObject;
                    }
                }
                catch { }
                try
                {
                    var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                    if (motors != null)
                    {
                        foreach (var motor in motors)
                        {
                            if (motor == null) continue;
                            try { motor.GetType().GetMethod("Burnout", flags)?.Invoke(motor, new object[] { true }); } catch { }
                            try { motor.GetType().GetMethod("Destruct", flags)?.Invoke(motor, new object[] { missile }); } catch { }
                        }
                    }
                }
                catch { }
                if (_detonateClip == null)
                {
                    try
                    {
                        _detonateClip = mt.GetField("nearbyDetonationClip", flags)?.GetValue(missile);
                        PlayDetachedShot(_detonateClip, pos);
                    }
                    catch { }
                }
            }
            if (fx != null)
            {
                var go = UnityEngine.Object.Instantiate(fx, pos, Quaternion.identity);
                go.name = "NOReplay_Boom";
                go.transform.SetParent(null, true);
                PlayParticles(go);
                foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    if (c.GetType().Name == "Shockwave") c.enabled = false;
                }
                UnityEngine.Object.Destroy(go, 12f);
                return;
            }
        }

        static void MuteBoomAudio(GameObject go)
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var c in go.GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    at.GetProperty("mute")?.SetValue(c, true);
                    stop?.Invoke(c, null);
                }
                catch { }
            }
        }

        static void PlaySafeBoomUnused(Vector3 pos)
        {
            if (!_boomTried)
            {
                _boomTried = true;
                string[] names = { "Explosion", "ExplosionFX", "MissileExplosion", "BombExplosion", "WarheadFX" };
                foreach (var o in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (o == null) continue;
                    string n = o.name ?? "";
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (n.IndexOf(names[i], StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (n.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (n.IndexOf("White", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (n.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (o.GetComponentInChildren<Camera>(true) != null) continue;
                        if (!HasParticles(o)) continue;
                        _boomPrefab = o;
                        break;
                    }
                    if (_boomPrefab != null) break;
                }
                if (_boomPrefab != null)
                    Plugin.Log.LogInfo("Boom-Prefab " + _boomPrefab.name);
                else
                    Plugin.Log.LogWarning("Kein Explosion-Prefab gefunden.");
            }
            if (_boomPrefab == null) return;
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(_boomPrefab, pos, Quaternion.identity); }
            catch { return; }
            go.name = "NOReplay_Boom";
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (tn.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                mb.enabled = false;
            }
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) cols[i].enabled = false;
            PlayParticles(go);
            UnityEngine.Object.Destroy(go, 4f);
        }

        static GameObject? _wreckPrefab;
        static bool _wreckTried;
        static MethodInfo? _mGetPrefabEffect;
        static MethodInfo? _mPlayPrefab;
        static object? _pem;

        void PlayUnitWreckFx(Vector3 pos, Quaternion rot)
        {
            Plugin.Log.LogInfo("Wreck-FX " + (Track != null ? Track.Name : "?")
                + " t=" + Plugin.Clock.Time.ToString("0.0"));
            if (!_wreckTried)
            {
                _wreckTried = true;
                try
                {
                    var gaT = Plugin.FindGameType("GameAssets");
                    var ga = gaT?.GetProperty("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                             ?? gaT?.GetField("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (ga != null)
                        _wreckPrefab = gaT?.GetField("vehicleWreckDestroyed")?.GetValue(ga) as GameObject;
                    var pemT = Plugin.FindGameType("ParticleEffectManager");
                    if (pemT != null)
                    {
                        _pem = pemT.GetProperty("i", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
                        if (_pem == null && pemT.BaseType != null)
                            _pem = pemT.BaseType.GetProperty("i", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
                        _mGetPrefabEffect = pemT.GetMethod("GetPrefabEffect", BindingFlags.Instance | BindingFlags.Public);
                    }
                    if (_wreckPrefab != null)
                        Plugin.Log.LogInfo("Wreck-Prefab " + _wreckPrefab.name);
                    else
                        Plugin.Log.LogWarning("GameAssets.vehicleWreckDestroyed nicht gefunden.");
                }
                catch (Exception ex) { Plugin.Log.LogWarning("Wreck-FX bind: " + ex.Message); }
            }
            if (_wreckPrefab == null) return;
            try
            {
                if (_pem != null && _mGetPrefabEffect != null)
                {
                    var fx = _mGetPrefabEffect.Invoke(_pem, new object[] { _wreckPrefab });
                    if (fx != null)
                    {
                        if (_mPlayPrefab == null)
                            _mPlayPrefab = fx.GetType().GetMethod("Play", new[] { typeof(Vector3), typeof(Quaternion) });
                        _mPlayPrefab?.Invoke(fx, new object[] { pos, rot });
                        return;
                    }
                }
            }
            catch { }
            try
            {
                var go = UnityEngine.Object.Instantiate(_wreckPrefab, pos, rot);
                go.name = "NOReplay_Wreck";
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    if (c != null) c.enabled = false;
                PlayParticles(go);
                UnityEngine.Object.Destroy(go, 10f);
            }
            catch { }
        }
    }
}