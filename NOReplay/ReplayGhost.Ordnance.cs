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


        object? _detonateClip;
        GameObject? _boomGo;
        float _boomClock = -1f;

        GameObject? _spawnedWreck;
        GameObject? _husk;
        bool _huskOn;
        readonly List<GameObject> _spawnedDamageFx = new();
        readonly List<ScorchMark> _scorches = new List<ScorchMark>();
        readonly List<float> _scorchStamps = new List<float>();

        struct ScorchMark
        {
            public GameObject Go;
            public float Time;
            public string Name;
        }

        static readonly List<ScorchMark> _ownedScorches = new List<ScorchMark>();

        static Type _decalSpawnerType;
        static FieldInfo _fDecalSize;
        static FieldInfo _fDecalMat;
        static FieldInfo _fDecalVertical;
        static bool _decalFieldsBound;
        static GameObject _scorchPrefab;
        static bool _scorchPrefabTried;
        static bool _scorchMissLogged;
        static Transform _datumOrigin;
        static bool _datumTried;

        internal void ResetFx()
        {
            _ordAudioOn = false;
            _ordVoiceHeld = false;
            if (_ordnance && IsTrackAlive())
                IgniteMissileFx();
            if (!_ordnance && IsTrackAlive() && !GroundBlastDue())
            {
                _boomed = false;
                ClearSpawnedWreck();
            }
            if (!_ordnance && !_ship && !_air && Plugin.Clock != null)
            {
                HoldRemovalWreck(Plugin.Clock.Time);
                AdvanceFires(Plugin.Clock.Time, Plugin.Clock.Speed >= 0f);
            }
        }

        bool GroundBlastDue()
        {
            if (_ordnance || _ship || _air || Track == null || Plugin.Clock == null) return false;
            return WreckMark.Due(Plugin.Clock.Time, UnitExplosionAt());
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
            // Decals are not children of the effect. Drop only the ones whose
            // blast is still in the future so an earlier hit keeps its mark.
            DropScorches(Plugin.Clock != null ? Plugin.Clock.Time : 0f, ScorchReason());
        }

        internal void ReleaseScorches(string reason)
        {
            DropScorches(float.NegativeInfinity, reason);
        }

        string ScorchUnit()
        {
            if (Track != null && !string.IsNullOrEmpty(Track.Name)) return Track.Name;
            return name;
        }

        static string ScorchReason()
        {
            if (Plugin.Clock != null && Plugin.Clock.Speed < -0.01f) return "reverse";
            return "seek";
        }

        void DropScorches(float now, string reason)
        {
            for (int i = _scorches.Count - 1; i >= 0; i--)
            {
                if (_scorches[i].Time <= now + 0.001f) continue;
                var go = _scorches[i].Go;
                float t = _scorches[i].Time;
                string who = string.IsNullOrEmpty(_scorches[i].Name) ? ScorchUnit() : _scorches[i].Name;
                _scorches.RemoveAt(i);
                if (go == null) continue;
                ForgetOwned(go);
                UnityEngine.Object.Destroy(go);
                Plugin.Log?.LogDebug("Scorch remove " + who + " t=" + t.ToString("0.00") + " reason=" + reason);
            }
        }

        void RememberScorch(GameObject go, float time, string who)
        {
            if (go == null) return;
            var mark = new ScorchMark { Go = go, Time = time, Name = who };
            _scorches.Add(mark);
            _ownedScorches.Add(mark);
        }

        static void RememberOwned(GameObject go, float time, string who)
        {
            if (go == null || IsOwned(go)) return;
            _ownedScorches.Add(new ScorchMark { Go = go, Time = time, Name = who });
        }

        static void ForgetOwned(GameObject go)
        {
            for (int i = _ownedScorches.Count - 1; i >= 0; i--)
                if (_ownedScorches[i].Go == go) _ownedScorches.RemoveAt(i);
        }

        static bool IsOwned(GameObject go)
        {
            for (int i = 0; i < _ownedScorches.Count; i++)
                if (_ownedScorches[i].Go == go) return true;
            return false;
        }

        static float _scorchSeen = -1f;

        internal static void SyncOwnedScorches(float now, string reason)
        {
            bool back = _scorchSeen >= 0f && now + 0.001f < _scorchSeen;
            _scorchSeen = now;
            if (!back) return;
            var ghosts = Plugin.GhostComps;
            for (int g = 0; g < ghosts.Count; g++)
                ghosts[g]?.DropScorches(now, reason);
            for (int i = _ownedScorches.Count - 1; i >= 0; i--)
            {
                if (_ownedScorches[i].Time <= now + 0.001f) continue;
                var go = _ownedScorches[i].Go;
                float t = _ownedScorches[i].Time;
                string who = _ownedScorches[i].Name ?? "";
                _ownedScorches.RemoveAt(i);
                if (go == null) continue;
                UnityEngine.Object.Destroy(go);
                Plugin.Log?.LogDebug("Scorch remove " + who + " t=" + t.ToString("0.00") + " reason=" + reason);
            }
        }

        internal static void ReleaseOwnedScorches(string reason)
        {
            _scorchSeen = float.MaxValue;
            SyncOwnedScorches(float.NegativeInfinity, reason);
            _scorchSeen = -1f;
        }

        static void BindDecalFields()
        {
            if (_decalFieldsBound) return;
            _decalFieldsBound = true;
            _decalSpawnerType = Plugin.FindGameType("DecalSpawner");
            if (_decalSpawnerType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fDecalSize = _decalSpawnerType.GetField("decalSize", flags);
            _fDecalMat = _decalSpawnerType.GetField("decalMaterial", flags);
            _fDecalVertical = _decalSpawnerType.GetField("verticalProjection", flags);
        }

        void ArmScorch(float time)
        {
            _scorchStamps.Clear();
            if (time >= 0f) _scorchStamps.Add(time);
        }

        void ArmScorchRange(int from, int to)
        {
            _scorchStamps.Clear();
            if (_blastTimes == null) return;
            for (int i = from; i < to && i < _blastTimes.Length; i++)
                _scorchStamps.Add(_blastTimes[i]);
        }

        void TakeScorches(GameObject fx, bool consume = true)
        {
            if (fx == null) return;
            BindDecalFields();
            if (_decalSpawnerType == null)
            {
                NoteUnownedProjectors(fx);
                return;
            }
            if (_scorchStamps.Count == 0)
                _scorchStamps.Add(Plugin.Clock != null ? Plugin.Clock.Time : 0f);
            var comps = fx.GetComponentsInChildren(_decalSpawnerType, true);
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is not Behaviour mb || mb == null) continue;
                // Start parents a DecalProjector to Datum.origin and then forgets it.
                mb.enabled = false;
                float size = 4f;
                try
                {
                    if (_fDecalSize != null) size = Convert.ToSingle(_fDecalSize.GetValue(mb));
                }
                catch { }
                Material mat = null;
                try { mat = _fDecalMat?.GetValue(mb) as Material; } catch { }
                bool vertical = true;
                try
                {
                    if (_fDecalVertical != null) vertical = Convert.ToBoolean(_fDecalVertical.GetValue(mb));
                }
                catch { }
                Vector3 at = mb.transform.position;
                bool owned = false;
                for (int s = 0; s < _scorchStamps.Count; s++)
                {
                    float stamp = _scorchStamps[s];
                    Vector3 place = at;
                    if (Track != null && Track.TrySample(stamp, out var sample))
                        place = Plugin.GlobalToLocal(sample.X, sample.Y, sample.Z);
                    var go = MakeScorch(place, size, mat, vertical);
                    if (go == null) continue;
                    RememberScorch(go, stamp, ScorchUnit());
                    owned = true;
                    Plugin.Log?.LogDebug("Scorch add " + ScorchUnit() + " t=" + stamp.ToString("0.00") + " kind=decal");
                }
                // A failed linecast must leave the spawner alive so its Start can
                // still place the game's decal. Start is what the player was seeing.
                if (owned && consume)
                    UnityEngine.Object.Destroy(mb);
                else
                    mb.enabled = true;
            }
            NoteUnownedProjectors(fx);
        }

        internal static bool CaptureDecalSpawner(Component spawner)
        {
            if (spawner == null) return false;
            BindDecalFields();
            var ghost = spawner.GetComponentInParent<ReplayGhost>();
            string who = ghost != null ? ghost.ScorchUnit() : spawner.gameObject.name;
            float stamp = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            float size = 4f;
            try
            {
                if (_fDecalSize != null) size = Convert.ToSingle(_fDecalSize.GetValue(spawner));
            }
            catch { }
            Material mat = null;
            try { mat = _fDecalMat?.GetValue(spawner) as Material; } catch { }
            bool vertical = true;
            try
            {
                if (_fDecalVertical != null) vertical = Convert.ToBoolean(_fDecalVertical.GetValue(spawner));
            }
            catch { }
            var go = MakeScorch(spawner.transform.position, size, mat, vertical);
            if (go == null) return false;
            if (ghost != null) ghost.RememberScorch(go, stamp, who);
            else RememberOwned(go, stamp, who);
            Plugin.Log?.LogDebug("Scorch add " + who + " t=" + stamp.ToString("0.00") + " kind=decal");
            return true;
        }

        internal static void ClaimGameScorch(Component spawner)
        {
            if (spawner == null) return;
            var ghost = spawner.GetComponentInParent<ReplayGhost>();
            string who = ghost != null ? ghost.ScorchUnit() : spawner.gameObject.name;
            float stamp = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            var datum = _datumOrigin != null ? _datumOrigin : Plugin.Datum;
            bool nearOwned = false;
            if (datum != null)
            {
                int n = datum.childCount;
                for (int i = 0; i < n; i++)
                {
                    var child = datum.GetChild(i);
                    if (child == null) continue;
                    if ((child.position - spawner.transform.position).sqrMagnitude > 2500f) continue;
                    bool projector = false;
                    var bits = child.GetComponents<Component>();
                    for (int c = 0; c < bits.Length; c++)
                    {
                        if (bits[c] != null && bits[c].GetType().Name == "DecalProjector")
                        {
                            projector = true;
                            break;
                        }
                    }
                    if (!projector) continue;
                    string cn = child.name ?? "";
                    if (cn.IndexOf("scorch", StringComparison.OrdinalIgnoreCase) < 0
                        && cn.IndexOf("NOReplay_Scorch", StringComparison.Ordinal) < 0)
                        continue;
                    if (IsOwned(child.gameObject))
                    {
                        nearOwned = true;
                        continue;
                    }
                    if (ghost != null) ghost.RememberScorch(child.gameObject, stamp, who);
                    else RememberOwned(child.gameObject, stamp, who);
                    nearOwned = true;
                    Plugin.Log?.LogDebug("Scorch add " + who + " t=" + stamp.ToString("0.00") + " kind=decal");
                }
            }
            if (!nearOwned)
                Plugin.Log?.LogDebug("Scorch unowned " + who + " t=" + stamp.ToString("0.00") + " kind=decal");
        }

        internal static void ClaimShockDecal(Component wave)
        {
            if (wave == null) return;
            GameObject go = null;
            try
            {
                go = wave.GetType().GetField("groundDecal",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(wave) as GameObject;
            }
            catch { }
            if (go == null || IsOwned(go)) return;
            if (go.transform.IsChildOf(wave.transform)) return;
            var ghost = wave.GetComponentInParent<ReplayGhost>();
            string who = ghost != null ? ghost.ScorchUnit() : wave.gameObject.name;
            float stamp = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            if (ghost != null) ghost.RememberScorch(go, stamp, who);
            else RememberOwned(go, stamp, who);
            Plugin.Log?.LogDebug("Scorch add " + who + " t=" + stamp.ToString("0.00") + " kind=shockwave");
        }

        void NoteUnownedProjectors(GameObject host)
        {
            if (host == null) return;
            float stamp = _scorchStamps.Count > 0 ? _scorchStamps[0]
                : (Plugin.Clock != null ? Plugin.Clock.Time : 0f);
            var datum = Plugin.Datum;
            var parts = host.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                var c = parts[i];
                if (c == null || c.GetType().Name != "DecalProjector") continue;
                if (IsOwned(c.gameObject)) continue;
                var parent = c.transform.parent;
                bool leaked = parent == null || (datum != null && parent == datum && c.gameObject != host);
                if (!leaked) continue;
                Plugin.Log?.LogDebug("Scorch unowned " + ScorchUnit() + " t=" + stamp.ToString("0.00") + " kind=projector");
            }
        }

        static GameObject MakeScorch(Vector3 pos, float size, Material mat, bool vertical)
        {
            if (!_scorchPrefabTried)
            {
                _scorchPrefabTried = true;
                try
                {
                    var gaT = Plugin.FindGameType("GameAssets");
                    var ga = gaT?.GetProperty("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                             ?? gaT?.GetField("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (ga != null)
                        _scorchPrefab = gaT?.GetField("scorchMarkDecal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ga) as GameObject;
                }
                catch { }
            }
            if (!_datumTried)
            {
                _datumTried = true;
                try
                {
                    var dT = Plugin.FindGameType("Datum");
                    _datumOrigin = dT?.GetProperty("origin", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Transform;
                }
                catch { }
            }
            if (_scorchPrefab == null || size <= 0.01f)
            {
                if (!_scorchMissLogged && _scorchPrefabTried && _scorchPrefab == null)
                {
                    _scorchMissLogged = true;
                    Plugin.Log?.LogWarning("Scorch prefab missing");
                }
                return null;
            }
            const int statics = 1 << 6;
            float half = Mathf.Max(0.05f, size * 0.5f);
            if (!Physics.Linecast(pos + Vector3.up * half, pos - Vector3.up * half, out var hit, statics))
                return null;
            var go = _datumOrigin != null
                ? UnityEngine.Object.Instantiate(_scorchPrefab, _datumOrigin)
                : UnityEngine.Object.Instantiate(_scorchPrefab);
            go.name = "NOReplay_Scorch";
            // LookRotation(Vector3.up) is what the game asks for; the axes are
            // parallel, so Unity keeps the identity rotation.
            go.transform.rotation = vertical ? Quaternion.identity : Quaternion.LookRotation(-hit.normal);
            go.transform.position = hit.point;
            Component proj = null;
            var parts = go.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].GetType().Name == "DecalProjector")
                {
                    proj = parts[i];
                    break;
                }
            }
            if (proj != null)
            {
                var pt = proj.GetType();
                try { pt.GetProperty("size")?.SetValue(proj, new Vector3(size, size, size * 0.2f)); } catch { }
                if (mat != null)
                {
                    try { pt.GetProperty("material")?.SetValue(proj, mat); } catch { }
                }
                try { pt.GetProperty("fadeFactor")?.SetValue(proj, 1f); } catch { }
            }
            return go;
        }

        void IgniteMissileFx()
        {
            if (_ordAudioOn) return;
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (missile != null && mt != null)
            {
                try
                {
                    var fxTr = mt.GetField("effectsTransform", flags)?.GetValue(missile) as Transform;
                    if (fxTr != null) fxTr.gameObject.SetActive(true);
                }
                catch { }
            }
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? Plugin.FindGameType("ParticleSystem");
            if (pt != null)
            {
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
            public FieldInfo? Speed, MaxPitch, PitchRange, BasePitch, Flight, Motors, Detonate, Booster;
        }
        struct MotorAudioFields
        {
            public FieldInfo? Startup, Sources, Delay, Burn, Fuel, Particles, Lights, Trails;
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
                Detonate = mt.GetField("nearbyDetonationClip", flags),
                Booster = mt.GetField("boosterIsAttached", flags),
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
                Delay = t.GetField("delayTimer", flags),
                Burn = t.GetField("burnTime", flags),
                Fuel = t.GetField("fuelMass", flags),
                Particles = t.GetField("particleSystems", flags),
                Lights = t.GetField("lights", flags),
                Trails = t.GetField("trailEmitters", flags),
            };
            _motorAudio[t] = bind;
            return bind;
        }


        void StopOrdnanceAudio()
        {
            _ordVoiceHeld = false;
            _ordAudioOn = false;
            StopMotorAndFlight();
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
            StopOrdnanceAudio();
            Plugin.ReleaseCameraIfOn(gameObject);
            PlayWarheadBoom(transform.position);
        }

        internal void ForceWreck()
        {
            if (_ordnance) return;
            if (_ship)
            {
                _boomed = true;
                return;
            }
            // A distant ground unit is switched off on the wreck frame, before
            // LateUpdate. Play any blast that is already due, including a second one.
            if (!_air)
            {
                float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
                AdvanceBlasts(now, Plugin.Clock == null || Plugin.Clock.Speed >= 0f);
                HoldRemovalWreck(now);
                AdvanceFires(now, Plugin.Clock == null || Plugin.Clock.Speed >= 0f);
                return;
            }
            if (_boomed) return;
            if (Track == null || Track.RemovedAt < 0f) return;
            if (Plugin.Clock.Time + 0.05f < Track.RemovedAt) return;
            _boomed = true;
            PlayUnitWreckFx(transform.position, transform.rotation);
        }

        void PlayFlightDetonation()
        {
            CacheFlightClip();
            if (_boomShotPlayed || _flightAudio == null || _boomClip == null) return;
            _boomShotPlayed = true;
            var src = _flightAudio;
            if (!src.gameObject.activeSelf) src.gameObject.SetActive(true);
            src.enabled = true;
            src.mute = false;
            src.Stop();
            src.clip = _boomClip;
            src.pitch = 1f;
            src.volume = 1f;
            src.dopplerLevel = 1f;
            src.loop = false;
            src.Play();
        }

        static string EffectField(char kind)
        {
            switch (kind)
            {
                case 'F': return "fizzleEffect";
                case 'U': return "underwaterEffect";
                case 'T': return "terrainEffect";
                case 'R': return "armorEffect";
                default: return "airEffect";
            }
        }

        void DisableMissileSim()
        {
            var mt = Plugin.FindGameType("Missile");
            if (mt == null) return;
            var raw = GetComponentsInChildren(mt, true);
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] is Behaviour b) b.enabled = false;
        }

        void PlayWarheadBoom(Vector3 pos)
        {
            CacheFlightClip();
            Plugin.Log?.LogDebug("Boom " + gameObject.name + " at " + pos);
            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (missile != null && mt != null)
            {
                // Do not call Motor.Burnout or Motor.Destruct. Burnout(true) stops every
                // motor source, and Destruct parents a fresh effect under the ghost.
                // The flight clip is left alone so a later seek can play the whoosh again.
                StopMotorAndFlight();
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
                        char kind = Track != null && Track.Det != '\0' ? Track.Det : 'A';
                        string field = EffectField(kind);
                        fx = wt.GetField(field, flags)?.GetValue(warhead) as GameObject;
                        if (fx == null && field != "airEffect")
                            fx = wt.GetField("airEffect", flags)?.GetValue(warhead) as GameObject;
                    }
                }
                catch { }
                PlayFlightDetonation();
            }
            if (fx != null)
            {
                Plugin.Log?.LogDebug("Boom-FX " + fx.name + " for " + gameObject.name);
                ClearBoom();
                float yieldKg = Track != null && Track.Yield > 0f ? Track.Yield : 0f;
                if (yieldKg <= 0f && mt != null)
                {
                    try
                    {
                        if (mt.GetMethod("GetYield", flags)?.Invoke(missile, null) is float fromPrefab && fromPrefab > 0f)
                            yieldKg = fromPrefab;
                    }
                    catch { }
                }
                bool wasActive = fx.activeSelf;
                fx.SetActive(false);
                GameObject go;
                try { go = UnityEngine.Object.Instantiate(fx, pos, Quaternion.identity); }
                finally { fx.SetActive(wasActive); }
                go.name = "NOReplay_Boom";
                go.SetActive(false);
                var datum = Plugin.Datum;
                if (datum != null) go.transform.SetParent(datum, true);
                if (yieldKg > 0f)
                    NukeTime.ApplyMissileYield(go, yieldKg);
                bool nuke = false;
                bool mushroom = false;
                foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn == "MushroomCloud") mushroom = true;
                    if (tn == "Shockwave" || tn == "MushroomCloud") nuke = true;
                }
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    if (col != null) col.enabled = false;
                float born = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
                if (nuke)
                {
                    var blast = go.AddComponent<ReplayBlast>();
                    blast.Born = born;
                    NukeTime.Watch(go, born);
                    if (mushroom) NukeTime.KeepPieces(go);
                }
                float detonation = Track != null && Track.RemovedAt >= 0f ? Track.RemovedAt : born;
                // terrainEffect (explosion_10kg and the rest) carries the scorch.
                // Start orphans it onto Datum after this object is activated.
                ArmScorch(detonation);
                TakeScorches(go);
                _scorchStamps.Clear();
                BoomAudio.PreferBorn = detonation;
                try { go.SetActive(true); }
                finally { BoomAudio.PreferBorn = -1f; }
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    if (col != null) col.enabled = false;
                _boomGo = go;
                PlayParticles(go);
                if (!Plugin.AudioNearWorld(pos, 700f))
                    MuteBoomAudio(go);
                if (!mushroom)
                {
                    float hold = BoomAudio.HoldSeconds(go);
                    UnityEngine.Object.Destroy(go, hold > 12f ? hold : 12f);
                }
                Plugin.Log?.LogDebug("Boom yield kg=" + yieldKg.ToString("0.##") + " nuke=" + nuke);
                return;
            }
            Plugin.Log?.LogWarning("Boom has no effect for " + gameObject.name);
        }

        void ClearBoom()
        {
            if (_boomGo == null) return;
            NukeTime.Forget(_boomGo);
            UnityEngine.Object.Destroy(_boomGo);
            _boomGo = null;
        }

        static void MuteBoomAudio(GameObject go)
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var c in go.GetComponentsInChildren(at, true))
            {
                if (c == null || BoomAudio.Allows(c)) continue;
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
                    Plugin.Log.LogWarning("No explosion prefab found.");
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
                var datum = Plugin.Datum;
                fxGo.transform.SetParent(datum != null ? datum : null, true);
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
                TakeScorches(fxGo);
                foreach (var c in fxGo.GetComponentsInChildren<Collider>(true))
                    if (c != null) c.enabled = false;

                PlayParticles(fxGo);
                PinSmokeToDatum(fxGo);
                _spawnedDamageFx.Add(fxGo);
                UnityEngine.Object.Destroy(fxGo, 25f);
            }
            catch { }
        }

        bool TrySpawnVehicleWreckAndFire(Vector3 pos, Quaternion rot, bool spawnModel = true)
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

            // 2. Bleibendes Wrack-Modell (GroundVehicle.wreckage). The blast path
            // skips this. The husk appears at Visible=0, not at the explosion.
            if (spawnModel)
            {
                var wreckGo = CreateWreckModel(pos, rot);
                if (wreckGo != null)
                {
                    _spawnedWreck = wreckGo;
                    spawnedAny = true;
                }
            }

            return spawnedAny;
        }

        bool RemovalBoomKept()
        {
            if (Track == null || Track.RemovedAt < 0f) return false;
            if (Track.FireTimes == null || Track.FireTimes.Count == 0) return false;
            var booms = Track.BoomTimes;
            if (booms == null) return false;
            for (int i = 0; i < booms.Count; i++)
                if (BlastSchedule.OnRemoval(booms[i], Track.RemovedAt)) return true;
            return false;
        }

        void HoldRemovalWreck(float now)
        {
            bool due = Track != null && BlastSchedule.RemovalDue(now, Track.RemovedAt);
            if (!due)
            {
                ClearHusk();
                if (Plugin.Clock != null)
                    DropScorches(now, ScorchReason());
                return;
            }
            if (_huskOn) return;
            _huskOn = true;
            var pos = transform.position;
            var rot = transform.rotation;
            string at = Track != null ? Track.RemovedAt.ToString("0.00") : now.ToString("0.00");
            string who = Track?.Name ?? name;
            ArmScorch(Track != null && Track.RemovedAt >= 0f ? Track.RemovedAt : now);
            var model = CreateWreckModel(pos, rot);
            _scorchStamps.Clear();
            if (model != null)
            {
                _husk = model;
                Plugin.Log?.LogInfo("Wreck " + who + " at=" + at + " prefab=" + _huskPrefabName);
                return;
            }
            // The killing explosion is already the Boom. A second destroyed-effect
            // on the same tick would be another blast.
            if (RemovalBoomKept())
                return;
            // Same stand-in the previous build logged. No part effects, so it is not a blast.
            Plugin.Log?.LogInfo("Wreck " + who + " at=" + at + " prefab=VehicleWreckDestroyed");
            ArmScorch(Track != null && Track.RemovedAt >= 0f ? Track.RemovedAt : now);
            PlayDestroyedWreck(pos, rot);
            _scorchStamps.Clear();
        }

        void ClearHusk()
        {
            _huskOn = false;
            if (_husk == null) return;
            UnityEngine.Object.Destroy(_husk);
            _husk = null;
        }

        string _huskPrefabName = "";

        GameObject? CreateWreckModel(Vector3 pos, Quaternion rot)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
                if (wreckPrefab == null) return null;
                _huskPrefabName = wreckPrefab.name;
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
                TakeScorches(wreckGo);
                return wreckGo;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Vehicle-Wreck: " + ex.Message);
                return null;
            }
        }

        void PlayUnitWreckFx(Vector3 pos, Quaternion rot, bool spawnModel = true, bool destroyedFallback = true)
        {
            ClearSpawnedWreck();
            TrySpawnVehicleWreckAndFire(pos, rot, spawnModel);
            // vehicleWreckDestroyed is the stand-in when the unit has no wreck prefab.
            // A recorded blast does not use it, and it is not played on top of the husk.
            if (_spawnedWreck != null || !destroyedFallback) return;
            PlayDestroyedWreck(pos, rot);
        }

        void PlayDestroyedWreck(Vector3 pos, Quaternion rot)
        {
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
                        Plugin.Log.LogWarning("GameAssets.vehicleWreckDestroyed was not found.");
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
                        var pooled = fx.GetType().GetField("gameObject")?.GetValue(fx) as GameObject;
                        if (pooled != null)
                            TakeScorches(pooled, consume: false);
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
                TakeScorches(go);
                UnityEngine.Object.Destroy(go, 10f);
            }
            catch { }
        }
    }
}