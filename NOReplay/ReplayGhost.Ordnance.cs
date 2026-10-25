using System;
using System.Collections;
using System.Collections.Generic;
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
        static int _nextVoiceSeq;
        static float _nextStartupAt;
        static readonly System.Collections.Generic.List<ReplayGhost> _voiceHolders = new();
        int _voiceSeq;
        float _nextVoiceTry;
        object? _flightSrc;
        object? _detonateClip;

        GameObject? _spawnedWreck;
        readonly List<GameObject> _spawnedDamageFx = new();

        internal void ResetFx()
        {
            _ordAudioOn = false;
            _ordVoiceHeld = false;
            if (_ordnance && IsTrackAlive())
            {
                _bornAt = Time.unscaledTime;
                IgniteMissileFx();
            }
            if (!_ordnance && IsTrackAlive())
            {
                _boomed = false;
                ClearSpawnedWreck();
            }
        }

        void ClearSpawnedWreck()
        {
            if (_spawnedWreck != null)
            {
                UnityEngine.Object.Destroy(_spawnedWreck);
                _spawnedWreck = null;
            }
            for (int i = 0; i < _spawnedDamageFx.Count; i++)
            {
                if (_spawnedDamageFx[i] != null)
                    UnityEngine.Object.Destroy(_spawnedDamageFx[i]);
            }
            _spawnedDamageFx.Clear();
        }

        void IgniteMissileFx()
        {
            if (_ordAudioOn) return;
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
                    try { play?.Invoke(ps, new object[] { true }); } catch { }
                }
            }
            _ordAudioOn = true;
        }

        struct MissileAudioFields
        {
            public FieldInfo? Speed, MaxPitch, PitchRange, BasePitch, Flight, Motors;
        }
        struct MotorAudioFields
        {
            public FieldInfo? Startup, Sources;
        }
        static readonly Dictionary<Type, MissileAudioFields> _missileAudio = new();
        static readonly Dictionary<Type, MotorAudioFields> _motorAudio = new();
        float _ordPrevX, _ordPrevY, _ordPrevZ, _ordPrevT = -1f;

        static MissileAudioFields MissileAudioOf(Type mt)
        {
            if (_missileAudio.TryGetValue(mt, out var bind)) return bind;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bind = new MissileAudioFields
            {
                Speed = mt.GetField("speed", flags),
                MaxPitch = mt.GetField("maxPitchSpeed", flags),
                PitchRange = mt.GetField("pitchRange", flags),
                BasePitch = mt.GetField("basePitch", flags),
                Flight = mt.GetField("flightSound", flags),
                Motors = mt.GetField("motors", flags),
            };
            _missileAudio[mt] = bind;
            return bind;
        }

        static MotorAudioFields MotorAudioOf(Type t)
        {
            if (_motorAudio.TryGetValue(t, out var bind)) return bind;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bind = new MotorAudioFields
            {
                Startup = t.GetField("startupSource", flags),
                Sources = t.GetField("audioSources", flags),
            };
            _motorAudio[t] = bind;
            return bind;
        }

        void LateOrdnanceAudio(ReplaySample s)
        {
            if (!_ordnance || _boomed) return;
            if (!NearMe(500f)) return;
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            if (missile == null || mt == null) return;
            if (!_ordVoiceHeld)
                ApplyMissileAudio(missile, mt);
            DriveMissileAudio(missile, mt, s);
        }

        void DriveMissileAudio(object missile, Type mt, ReplaySample s)
        {
            var bind = MissileAudioOf(mt);
            float spd = 0f;
            try
            {
                if (bind.Speed?.GetValue(missile) is float sf) spd = sf;
            }
            catch { }
            float fromTrack = 0f;
            if (s.HasTas && s.Tas > 1f)
                fromTrack = s.Tas > 40f ? s.Tas / 1.94384f : s.Tas;
            if (fromTrack < 1f && _ordPrevT >= 0f)
            {
                float dt = Plugin.Clock.Time - _ordPrevT;
                if (dt > 0.01f)
                {
                    float dx = s.X - _ordPrevX;
                    float dy = s.Y - _ordPrevY;
                    float dz = s.Z - _ordPrevZ;
                    fromTrack = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / dt;
                }
            }
            _ordPrevX = s.X;
            _ordPrevY = s.Y;
            _ordPrevZ = s.Z;
            _ordPrevT = Plugin.Clock.Time;
            if (fromTrack > spd) spd = fromTrack;
            try { bind.Speed?.SetValue(missile, spd); } catch { }
            float maxPitch = 340f;
            float pitchRange = 1f;
            float basePitch = 0.5f;
            try
            {
                if (bind.MaxPitch?.GetValue(missile) is float m) maxPitch = m;
                if (bind.PitchRange?.GetValue(missile) is float p) pitchRange = p;
                if (bind.BasePitch?.GetValue(missile) is float b) basePitch = b;
            }
            catch { }
            float num = Mathf.Clamp01((-50f + spd) / Mathf.Max(maxPitch, 1f));
            object? flight = null;
            try { flight = bind.Flight?.GetValue(missile); } catch { }
            if (flight != null)
            {
                var audio = PropsFor(flight.GetType());
                audio.Mute?.SetValue(flight, false);
                float vol = 0f;
                try { if (audio.Volume?.GetValue(flight) is float v) vol = v; } catch { }
                audio.Volume?.SetValue(flight, vol < num ? Mathf.Min(num, vol + Time.deltaTime) : num);
                audio.Pitch?.SetValue(flight, pitchRange * num + basePitch);
                bool playing = false;
                try { playing = audio.IsPlaying?.GetValue(flight) is bool on && on; } catch { }
                if (!playing) audio.Play?.Invoke(flight, null);
            }
            try
            {
                var motors = bind.Motors?.GetValue(missile) as Array;
                if (motors == null) return;
                foreach (var motor in motors)
                {
                    if (motor == null) continue;
                    var motorFields = MotorAudioOf(motor.GetType());
                    var start = motorFields.Startup?.GetValue(motor);
                    if (start != null && !_startupPlayed)
                    {
                        var st = PropsFor(start.GetType());
                        st.Mute?.SetValue(start, false);
                        bool sp = false;
                        try { sp = st.IsPlaying?.GetValue(start) is bool b && b; } catch { }
                        if (!sp) st.Play?.Invoke(start, null);
                        _startupPlayed = true;
                    }
                    if (!_startupPlayed) continue;
                    var srcs = motorFields.Sources?.GetValue(motor) as Array;
                    if (srcs == null) continue;
                    foreach (var src in srcs)
                    {
                        if (src == null || ReferenceEquals(src, start)) continue;
                        var st = PropsFor(src.GetType());
                        st.Mute?.SetValue(src, false);
                        bool playing = false;
                        try { playing = st.IsPlaying?.GetValue(src) is bool pb && pb; } catch { }
                        if (!playing) st.Play?.Invoke(src, null);
                    }
                }
            }
            catch { }
        }

        bool _startupPlayed;
        float _bornAt;

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

        static int _ownerFrame = -1;
        static int _ownerId;

        bool OwnerFollowed()
        {
            string? parent = Track?.ParentId;
            if (string.IsNullOrEmpty(parent)) return false;
            if (_ownerFrame != Time.frameCount)
            {
                _ownerFrame = Time.frameCount;
                _ownerId = 0;
                var follow = Plugin.FollowedObject();
                var fg = follow != null ? follow.GetComponentInParent<ReplayGhost>() : null;
                if (fg != null && fg.Track != null && int.TryParse(fg.Track.Id, System.Globalization.NumberStyles.HexNumber, null, out var id))
                    _ownerId = id;
            }
            return int.TryParse(parent, System.Globalization.NumberStyles.HexNumber, null, out var mine) && mine == _ownerId && _ownerId != 0;
        }

        bool TryClaimMissileVoice()
        {
            if (_ordVoiceHeld) return true;
            _ordVoiceHeld = true;
            return true;
        }

        void ApplyMissileAudio(object missile, Type mt)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            bool near = Plugin.IsFollowed(gameObject) || NearMe(900f) || OwnerFollowed();
            if (!near)
            {
                return;
            }
            if (_ordVoiceHeld) return;
            if (!TryClaimMissileVoice()) return;
            try { _flightSrc = mt.GetField("flightSound", flags)?.GetValue(missile); } catch { }
            try { _detonateClip = mt.GetField("nearbyDetonationClip", flags)?.GetValue(missile); } catch { }
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
                        // Launch sound only at actual launch, not when flying past later.
                        float age = Time.unscaledTime - _bornAt;
                        if (start != null && !_startupPlayed && age < 1.5f)
                        {
                            start.GetType().GetProperty("mute")?.SetValue(start, false);
                            start.GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(start, null);
                            _startupPlayed = true;
                        }
                        var srcs = tt.GetField("audioSources", flags)?.GetValue(motor) as Array;
                        if (srcs == null) continue;
                        foreach (var s in srcs)
                        {
                            if (s == null) continue;
                            // Prefab decides loop/volume/distance. Just ensure it plays.
                            var st = s.GetType();
                            st.GetProperty("mute")?.SetValue(s, false);
                            bool playing = false;
                            try { playing = st.GetProperty("isPlaying")?.GetValue(s) is bool b && b; } catch { }
                            if (!playing) st.GetMethod("Play", Type.EmptyTypes)?.Invoke(s, null);
                        }
                    }
                }
            }
            catch { }
            if (_flightSrc != null)
            {
                var ft = _flightSrc.GetType();
                ft.GetProperty("mute")?.SetValue(_flightSrc, false);
                bool fp = false;
                try { fp = ft.GetProperty("isPlaying")?.GetValue(_flightSrc) is bool b && b; } catch { }
                if (!fp) ft.GetMethod("Play", Type.EmptyTypes)?.Invoke(_flightSrc, null);
            }
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
            if (!_ordAudioOn && !_ordVoiceHeld) return;
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
            Plugin.Log?.LogInfo("Boom " + gameObject.name + " at " + pos);
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
                Plugin.Log?.LogInfo("Boom-FX " + fx.name + " for " + gameObject.name);
                var go = UnityEngine.Object.Instantiate(fx, pos, Quaternion.identity);
                go.name = "NOReplay_Boom";
                go.transform.SetParent(null, true);
                go.SetActive(true);
                PlayParticles(go);
                foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    if (c.GetType().Name == "Shockwave") c.enabled = false;
                }
                UnityEngine.Object.Destroy(go, 12f);
                return;
            }
            Plugin.Log?.LogWarning("Boom kein Effekt für " + gameObject.name);
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

        static Type? _tUnitPart;
        static Type? _tGroundVehicle;
        static Type? _tDamageParticles;
        static FieldInfo? _fPartDamageEffects;
        static FieldInfo? _fPartDisintegrationEffects;
        static FieldInfo? _fDamageEffectPrefab;
        static FieldInfo? _fStopWhenCulled;

        void SpawnDetachedFxPrefab(GameObject fxPrefab, Vector3 pos, Quaternion rot)
        {
            if (fxPrefab == null) return;
            try
            {
                var fxGo = UnityEngine.Object.Instantiate(fxPrefab, pos, rot);
                fxGo.name = "NOReplay_VehicleDamageFx";
                fxGo.transform.SetParent(null, true);
                fxGo.SetActive(true);

                _tDamageParticles ??= Plugin.FindGameType("DamageParticles");
                if (_tDamageParticles != null)
                {
                    _fStopWhenCulled ??= _tDamageParticles.GetField("stopWhenParentCulled",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var dp in fxGo.GetComponentsInChildren(_tDamageParticles, true))
                    {
                        if (dp == null) continue;
                        try { _fStopWhenCulled?.SetValue(dp, false); } catch { }
                        UnityEngine.Object.Destroy(dp);
                    }
                }

                foreach (var mb in fxGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (mb.GetType().Name == "Shockwave") mb.enabled = false;
                }
                foreach (var c in fxGo.GetComponentsInChildren<Collider>(true))
                    if (c != null) c.enabled = false;

                PlayParticles(fxGo);
                _spawnedDamageFx.Add(fxGo);
                UnityEngine.Object.Destroy(fxGo, 25f);
            }
            catch { }
        }

        bool TrySpawnVehicleWreckAndFire(Vector3 pos, Quaternion rot)
        {
            bool spawnedAny = false;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1. Echte Explosions-, Feuer- und Rauch-Prefabs aus den UnitParts spawnen
            try
            {
                _tUnitPart ??= Plugin.FindGameType("UnitPart");
                if (_tUnitPart != null)
                {
                    _fPartDamageEffects ??= _tUnitPart.GetField("damageEffects", flags);
                    _fPartDisintegrationEffects ??= _tUnitPart.GetField("disintegrationEffects", flags);

                    var seenPrefabs = new HashSet<int>();
                    foreach (var part in GetComponentsInChildren(_tUnitPart, true))
                    {
                        if (part == null) continue;
                        var partComp = part as Component;
                        Vector3 pPos = partComp != null ? partComp.transform.position : pos;
                        Quaternion pRot = partComp != null ? partComp.transform.rotation : rot;

                        if (_fPartDamageEffects?.GetValue(part) is IList effects)
                        {
                            for (int i = 0; i < effects.Count; i++)
                            {
                                var eff = effects[i];
                                if (eff == null) continue;
                                _fDamageEffectPrefab ??= eff.GetType().GetField("prefab", flags);
                                if (_fDamageEffectPrefab?.GetValue(eff) is GameObject fxPrefab && fxPrefab != null)
                                {
                                    if (!seenPrefabs.Add(fxPrefab.GetInstanceID())) continue;
                                    SpawnDetachedFxPrefab(fxPrefab, pPos, pRot);
                                    spawnedAny = true;
                                }
                            }
                        }

                        if (_fPartDisintegrationEffects?.GetValue(part) is GameObject[] disFx)
                        {
                            for (int i = 0; i < disFx.Length; i++)
                            {
                                var fxPrefab = disFx[i];
                                if (fxPrefab == null) continue;
                                if (!seenPrefabs.Add(fxPrefab.GetInstanceID())) continue;
                                SpawnDetachedFxPrefab(fxPrefab, pPos, pRot);
                                spawnedAny = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Vehicle-DamageFX: " + ex.Message); }

            // 2. Bleibendes Wrack-Modell (GroundVehicle.wreckage) auf den Boden stellen
            try
            {
                _tGroundVehicle ??= Plugin.FindGameType("GroundVehicle");
                Component? gv = null;
                if (_tGroundVehicle != null)
                    gv = (GetComponent(_tGroundVehicle) ?? GetComponentInChildren(_tGroundVehicle, true)) as Component;

                GameObject? wreckPrefab = null;
                if (gv != null)
                {
                    var fWreck = gv.GetType().GetField("wreckage", flags);
                    wreckPrefab = fWreck?.GetValue(gv) as GameObject;
                }
                if (wreckPrefab == null)
                {
                    foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        var fWreck = mb.GetType().GetField("wreckage", flags);
                        if (fWreck?.GetValue(mb) is GameObject wp && wp != null)
                        {
                            wreckPrefab = wp;
                            break;
                        }
                    }
                }

                if (wreckPrefab != null)
                {
                    var wreckGo = UnityEngine.Object.Instantiate(wreckPrefab, pos, rot);
                    wreckGo.name = "NOReplay_VehicleWreck";
                    wreckGo.transform.SetParent(null, true);
                    wreckGo.SetActive(true);

                    foreach (var mb in wreckGo.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        if (mb.GetType().Name == "Wreckage")
                            UnityEngine.Object.Destroy(mb);
                    }
                    foreach (var rb in wreckGo.GetComponentsInChildren<Rigidbody>(true))
                    {
                        if (rb == null) continue;
                        rb.isKinematic = true;
                        rb.detectCollisions = false;
                    }
                    foreach (var c in wreckGo.GetComponentsInChildren<Collider>(true))
                        if (c != null) c.enabled = false;

                    PlayParticles(wreckGo);
                    _spawnedWreck = wreckGo;
                    spawnedAny = true;
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("Vehicle-Wreck: " + ex.Message); }

            return spawnedAny;
        }

        void PlayUnitWreckFx(Vector3 pos, Quaternion rot)
        {
            ClearSpawnedWreck();
            TrySpawnVehicleWreckAndFire(pos, rot);

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