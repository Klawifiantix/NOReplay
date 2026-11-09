using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct LaserEmitter
        {
            public Component Laser;
            public string Key;
            public Renderer Beam;
            public Transform BeamTr;
            public float BeamScale;
            public Color Tint;
            public bool ModifyPitch;
            public float Pitch;
            public float Volume;
            public float StartMul;
            public float FallRate;
            public AudioClip StartClip;
            public AudioClip LoopClip;
            public AudioClip EndClip;
            public AudioSource StartSrc;
            public AudioSource LoopSrc;
            public AudioSource EndSrc;
            public Component[] Muzzles;
            public GameObject Hit;
            public Component[] HitParts;
            public bool HavePrev;
            public bool FireLogged;
            public float PrevTime;
            public LaserClock.View Prev;
        }

        LaserEmitter[] _lasers = Array.Empty<LaserEmitter>();
        bool _lasersBound;
        int _laserMiss;
        bool _laserGapLogged;
        bool _laserPlayLogged;
        static FieldInfo? _lBeam, _lColor, _lMuzzle, _lHitPrefab, _lStart, _lLoop, _lEnd, _lPitch, _lModify, _lFall, _lMul, _lVolume;
        static bool _lFields;
        static Type? _particleType;

        void DriveLasers()
        {
            if (!_lasersBound || (_lasers.Length == 0 && _laserMiss < 8))
                BindLasers();
            if (_lasers.Length == 0) return;
            KeepLaserMounts();
            if (!_laserPlayLogged)
            {
                _laserPlayLogged = true;
                string unitName = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
                for (int i = 0; i < _lasers.Length; i++)
                {
                    var em = _lasers[i];
                    int samples = Track != null ? Track.LaserCount(em.Key) : 0;
                    Plugin.Log?.LogInfo("Laser play " + unitName
                        + " key=" + em.Key
                        + " beam=" + (em.Beam != null ? em.Beam.name : "none")
                        + " samples=" + samples);
                }
            }
            bool alive = Track != null && IsTrackAlive() && !(_ship && ShipIsDead(Plugin.Clock.Time)) && !VehicleIsDying(Plugin.Clock.Time);
            bool paused = Plugin.Clock.Paused;
            float time = Plugin.Clock.Time;
            float dt = _laserPrevClock >= 0f ? time - _laserPrevClock : 0f;
            bool hadClock = _laserPrevClock >= 0f;
            _laserPrevClock = time;
            for (int i = 0; i < _lasers.Length; i++)
                ApplyLaser(ref _lasers[i], alive, paused, time, dt, hadClock);
        }

        float _laserPrevClock = -1f;

        void QuietLasers()
        {
            if (_lasers.Length == 0) return;
            for (int i = 0; i < _lasers.Length; i++)
                SilenceLaser(ref _lasers[i], hideBeam: true);
        }

        void ReleaseLasers()
        {
            for (int i = 0; i < _lasers.Length; i++)
            {
                SilenceLaser(ref _lasers[i], hideBeam: true);
                if (_lasers[i].Hit != null)
                    UnityEngine.Object.Destroy(_lasers[i].Hit);
            }
            _lasers = Array.Empty<LaserEmitter>();
            _lasersBound = false;
        }

        void BindLasers()
        {
            _lasersBound = true;
            var t = Plugin.FindGameType("Laser");
            if (t == null) return;
            BindLaserFields(t);
            var found = GetComponentsInChildren(t, true);
            var list = new List<LaserEmitter>(found.Length);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < found.Length; i++)
            {
                var c = found[i] as Component;
                if (c == null) continue;
                if (c is Behaviour b) b.enabled = false;
                string path = TurretKeys.Sanitize(TurretKeys.Relative(transform, c.transform));
                if (path.Length == 0) path = "laser";
                counts.TryGetValue(path, out int n);
                n++;
                counts[path] = n;
                string key = n == 1 ? path : path + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var beam = _lBeam?.GetValue(c) as Renderer;
                if (beam != null)
                {
                    beam.enabled = false;
                    _disabledInPrefab.Remove(beam);
                }
                var em = new LaserEmitter
                {
                    Laser = c,
                    Key = key,
                    Beam = beam,
                    BeamTr = beam != null ? beam.transform : null,
                    BeamScale = beam != null ? beam.transform.localScale.x : 1f,
                    Tint = _lColor?.GetValue(c) is Color col ? col : Color.white,
                    ModifyPitch = _lModify?.GetValue(c) is bool mod && mod,
                    Pitch = _lPitch?.GetValue(c) is float p && p > 0f ? p : 1f,
                    Volume = _lVolume?.GetValue(c) is float vol && vol > 0f ? vol : 1f,
                    StartMul = _lMul?.GetValue(c) is float sm && sm > 0f ? sm : 1f,
                    FallRate = _lFall?.GetValue(c) is float fr && fr > 0f ? fr : 1f,
                    StartClip = _lStart?.GetValue(c) as AudioClip,
                    LoopClip = _lLoop?.GetValue(c) as AudioClip,
                    EndClip = _lEnd?.GetValue(c) as AudioClip
                };
                var muz = _lMuzzle?.GetValue(c) as Array;
                if (muz != null)
                {
                    var parts = new List<Component>();
                    for (int m = 0; m < muz.Length; m++)
                        if (muz.GetValue(m) is Component ps) parts.Add(ps);
                    em.Muzzles = parts.ToArray();
                }
                else em.Muzzles = Array.Empty<Component>();
                BuildLaserAudio(c, ref em);
                BuildHit(c, ref em);
                ShowLaserMount(c, beam);
                list.Add(em);
            }
            _lasers = list.ToArray();
            if (_lasers.Length == 0) _laserMiss++;
            else _laserMiss = 8;
            string unitName = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
            if (_lasers.Length > 0)
                Plugin.Log?.LogInfo("Laser playback " + unitName + " n=" + _lasers.Length);
            else if (!_laserGapLogged && NamedLaserHull(unitName) && _laserMiss >= 8)
            {
                _laserGapLogged = true;
                Plugin.Log?.LogInfo("Laser playback " + unitName + " has no Laser component");
            }
        }

        static bool NamedLaserHull(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return false;
            return unitName.IndexOf("Spearhead", StringComparison.OrdinalIgnoreCase) >= 0
                || unitName.IndexOf("LFD", StringComparison.OrdinalIgnoreCase) >= 0
                || unitName.IndexOf("HLT-HEL", StringComparison.OrdinalIgnoreCase) >= 0
                || unitName.IndexOf("LADS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ApplyLaser(ref LaserEmitter em, bool alive, bool paused, float time, float dt, bool hadClock)
        {
            if (em.Laser is Behaviour b && b.enabled) b.enabled = false;
            LaserClock.View view = default;
            bool known = alive && Track != null && Track.TryLaser(em.Key, time, out view);
            if (!known || !view.On)
            {
                var edge = LaserClock.Audio(default, em.HavePrev && hadClock, em.Prev, dt, paused);
                if (edge.PlayEnd) PlayLaserOne(em.EndSrc, em.EndClip, em.Volume);
                SilenceLaser(ref em, hideBeam: true);
                em.Prev = default;
                em.HavePrev = true;
                em.PrevTime = time;
                return;
            }
            var audio = LaserClock.Audio(view, em.HavePrev && hadClock, em.Prev, dt, paused);
            if (!em.FireLogged)
            {
                em.FireLogged = true;
                string unitName = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
                Plugin.Log?.LogInfo("Laser on " + unitName + " key=" + em.Key
                    + " point=" + (view.HasPoint ? 1 : 0)
                    + " t=" + time.ToString("0.00"));
            }
            PlaceBeam(ref em, view);
            if (paused)
            {
                PauseLaserAudio(ref em);
                PauseParticleSystems(em.Muzzles);
                PauseParticleSystems(em.HitParts);
            }
            else
            {
                if (audio.PlayStart) PlayLaserOne(em.StartSrc, em.StartClip, em.Volume);
                DriveLaserLoop(ref em, audio.Loop, view.Age);
                for (int i = 0; i < em.Muzzles.Length; i++)
                    PlayParticleSystem(em.Muzzles[i]);
                if (view.HasPoint && em.Hit != null)
                {
                    var at = Plugin.GlobalToLocal(view.X, view.Y, view.Z);
                    em.Hit.transform.position = at;
                    if (!em.Hit.activeSelf) em.Hit.SetActive(true);
                    for (int i = 0; i < em.HitParts.Length; i++)
                        PlayParticleSystem(em.HitParts[i]);
                }
            }
            em.Prev = view;
            em.HavePrev = true;
            em.PrevTime = time;
        }

        void ShowLaserMount(Component laser, Renderer? beam)
        {
            var turret = laser.GetComponentInParent<Turret>();
            var root = turret != null ? turret.transform : laser.transform;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || r == beam) continue;
                _disabledInPrefab.Remove(r);
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                if (_visible) r.enabled = true;
                for (int p = 0; p < _parts.Length; p++)
                {
                    if (_parts[p] == r.transform)
                        _partSkip[p] = true;
                }
            }
            if (beam != null)
            {
                _disabledInPrefab.Remove(beam);
                for (int p = 0; p < _parts.Length; p++)
                {
                    if (_parts[p] == beam.transform)
                        _partSkip[p] = true;
                }
            }
        }

        void KeepLaserMounts()
        {
            for (int i = 0; i < _lasers.Length; i++)
            {
                if (_lasers[i].Laser != null)
                    ShowLaserMount(_lasers[i].Laser, _lasers[i].Beam);
            }
        }

        void PlaceBeam(ref LaserEmitter em, LaserClock.View view)
        {
            if (em.Beam == null) return;
            bool show = view.HasPoint;
            em.Beam.enabled = show;
            if (em.BeamTr != null && show && !em.BeamTr.gameObject.activeSelf)
                em.BeamTr.gameObject.SetActive(true);
            if (!show || em.BeamTr == null) return;
            var at = Plugin.GlobalToLocal(view.X, view.Y, view.Z);
            em.BeamTr.LookAt(at);
            float dist = Vector3.Distance(em.BeamTr.position, at);
            em.BeamTr.localScale = new Vector3(em.BeamScale, em.BeamScale, dist);
            try
            {
                var mat = em.Beam.material;
                if (mat != null)
                {
                    mat.SetColor("_Color", em.Tint * view.Power);
                    SetBeamDatum(mat);
                }
            }
            catch { }
        }

        static void SetBeamDatum(Material mat)
        {
            var datum = Plugin.FindGameType("Datum");
            if (datum == null) return;
            var origin = datum.GetProperty("originPosition", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            if (origin is Vector3 v)
                mat.SetVector("_WorldOffset", new Vector4(v.x, v.y, v.z, 0f));
        }

        void SilenceLaser(ref LaserEmitter em, bool hideBeam)
        {
            if (hideBeam && em.Beam != null) em.Beam.enabled = false;
            StopLaserSource(em.LoopSrc);
            StopLaserSource(em.StartSrc);
            if (em.Muzzles != null)
            {
                for (int i = 0; i < em.Muzzles.Length; i++)
                    StopParticleSystem(em.Muzzles[i], false);
            }
            if (em.Hit != null && em.Hit.activeSelf) em.Hit.SetActive(false);
        }

        static void PauseLaserAudio(ref LaserEmitter em)
        {
            StopLaserSource(em.LoopSrc);
            StopLaserSource(em.StartSrc);
        }

        void DriveLaserLoop(ref LaserEmitter em, bool loop, float age)
        {
            if (em.LoopSrc == null || em.LoopClip == null || !loop)
            {
                StopLaserSource(em.LoopSrc);
                return;
            }
            em.LoopSrc.volume = em.Volume;
            em.LoopSrc.pitch = LaserClock.SustainPitch(em.ModifyPitch, em.Pitch, em.StartMul, em.FallRate, age);
            if (!em.LoopSrc.isPlaying)
            {
                em.LoopSrc.clip = em.LoopClip;
                em.LoopSrc.loop = true;
                em.LoopSrc.Play();
            }
        }

        static void PlayLaserOne(AudioSource src, AudioClip clip, float volume)
        {
            if (src == null || clip == null) return;
            src.volume = volume;
            src.pitch = 1f;
            src.PlayOneShot(clip);
        }

        static void StopLaserSource(AudioSource src)
        {
            if (src == null) return;
            if (src.isPlaying) src.Stop();
            src.volume = 0f;
        }

        void BuildLaserAudio(Component laser, ref LaserEmitter em)
        {
            var host = new GameObject("NOReplay_LaserSnd");
            host.transform.SetParent(laser.transform, false);
            em.StartSrc = host.AddComponent<AudioSource>();
            em.LoopSrc = host.AddComponent<AudioSource>();
            em.EndSrc = host.AddComponent<AudioSource>();
            DressLaserSource(em.StartSrc);
            DressLaserSource(em.LoopSrc);
            DressLaserSource(em.EndSrc);
            em.LoopSrc.loop = true;
            em.LoopSrc.clip = em.LoopClip;
        }

        static void DressLaserSource(AudioSource src)
        {
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.spread = 20f;
            src.dopplerLevel = 0f;
            src.minDistance = 10f;
            src.maxDistance = 50f;
            src.volume = 0f;
            try
            {
                var sm = Plugin.FindGameType("SoundManager");
                if (sm == null) return;
                var inst = sm.GetProperty("i", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                var mixer = inst?.GetType().GetField("EffectsMixer")?.GetValue(inst);
                if (mixer != null)
                    src.outputAudioMixerGroup = mixer as UnityEngine.Audio.AudioMixerGroup;
            }
            catch { }
        }

        void BuildHit(Component laser, ref LaserEmitter em)
        {
            em.HitParts = Array.Empty<Component>();
            var prefab = _lHitPrefab?.GetValue(laser) as GameObject;
            if (prefab == null) return;
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, transform, false); }
            catch { return; }
            go.name = "NOReplay_LaserHit";
            go.SetActive(false);
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null) behaviours[i].enabled = false;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) cols[i].enabled = false;
            if (_particleType == null)
                _particleType = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                    ?? Plugin.FindGameType("ParticleSystem");
            if (_particleType != null)
            {
                var raw = go.GetComponentsInChildren(_particleType, true);
                var parts = new List<Component>(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                    if (raw[i] is Component c) parts.Add(c);
                em.HitParts = parts.ToArray();
            }
            em.Hit = go;
        }

        static void BindLaserFields(Type t)
        {
            if (_lFields) return;
            _lFields = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _lBeam = t.GetField("beamRenderer", flags);
            _lColor = t.GetField("color", flags);
            _lMuzzle = t.GetField("muzzleParticles", flags);
            _lHitPrefab = t.GetField("hitEffectPrefab", flags);
            _lStart = t.GetField("fireStart", flags);
            _lLoop = t.GetField("fireSustained", flags);
            _lEnd = t.GetField("fireEnd", flags);
            _lPitch = t.GetField("pitch", flags);
            _lModify = t.GetField("modifyPitch", flags);
            _lFall = t.GetField("pitchFallRate", flags);
            _lMul = t.GetField("startPitchMultiplier", flags);
            _lVolume = t.GetField("volume", flags);
        }
    }
}
