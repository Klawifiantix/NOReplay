using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static readonly BindingFlags GunFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo? _fMuzzles, _fSpin, _fRecoil, _fSpinRate, _fRecoilTravel, _fRecoilRate, _fRecoilReturn;
        static FieldInfo? _fParticles, _fFireSounds, _fFireStart, _fFireSustained, _fFireEnd, _fSources, _fRecoilSound;
        static FieldInfo? _fVolume, _fPitch, _fPitchVar, _fModifyPitch, _fStartPitch, _fPitchClimb;
        static FieldInfo? _fSpread, _fMuzzleVel, _fSelfDestruct, _fTracerRatio, _fTracerSize, _fTracerColor;
        static FieldInfo? _fEjectPrefab, _fEjectXf, _fEjectDelay, _fEjectLife, _fEjectLin, _fEjectAng;
        static FieldInfo? _fImpactGround, _fImpactArmor, _fImpactWater, _fImpactSelf, _fVisualOnly;
        static bool _gunFields;

        GunRuntime[] _gunRt = Array.Empty<GunRuntime>();
        Transform[] _turrets = Array.Empty<Transform>();
        Transform[] _elev = Array.Empty<Transform>();
        readonly HashSet<Transform> _gunFree = new HashSet<Transform>();
        readonly HashSet<int> _muzzleIds = new HashSet<int>();
        readonly List<PendingEject> _ejects = new List<PendingEject>();
        string[] _turretKeys = Array.Empty<string>();
        bool _gunsBound;
        bool _gunStampValid;
        int _gunStamp;
        float _nextGunScan;
        float _gunWatchStart = -1f;
        float _gunAimUntil;
        static bool _simLogged;
        static bool _simFailLogged;

        internal bool HasTurretTrack => Track != null && Track.HasTurrets;

        void AimGuns()
        {
            if (_ordnance) return;
            EnsureGunsCurrent(false);
            if (HasTurretTrack)
            {
                ApplyTurretAngles(Plugin.Clock.Time);
                return;
            }
            if (_neutral) return;
            if (_gunAimUntil > Time.unscaledTime) return;
            for (int i = 0; i < _turrets.Length; i++)
            {
                if (_turrets[i] != null) _turrets[i].localRotation = Quaternion.identity;
                if (_elev[i] != null) _elev[i].localRotation = Quaternion.identity;
            }
        }

        internal void ApplyTurretAngles(float time)
        {
            EnsureGunsCurrent(false);
            if (Track == null) return;
            for (int i = 0; i < _turrets.Length; i++)
            {
                string key = i < _turretKeys.Length ? _turretKeys[i] : "";
                if (!Track.TryTurret(key, time, out float trav, out float elev)) continue;
                var elevTr = _elev[i];
                if (elevTr != null)
                    elevTr.localEulerAngles = new Vector3(elev, 0f, 0f);
                var tr = _turrets[i];
                if (tr != null)
                    tr.localEulerAngles = new Vector3(0f, trav, 0f);
            }
        }

        internal void AimGun(Vector3 worldDir)
        {
            if (HasTurretTrack) return;
            EnsureGunsCurrent(false);
            if (worldDir.sqrMagnitude < 0.01f || Drive == null) return;
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

        internal void FireRecordedGun(ReplayObject burst, float sustainUntil)
        {
            var rt = ResolveGun(burst);
            if (rt == null) return;
            if (HasTurretTrack)
                ApplyTurretAngles(Plugin.Clock.Time);
            KickRecoil(rt);
            KickSpin(rt);
            PlayMuzzles(rt);
            PlayShotAudio(rt, sustainUntil);
            Eject(rt);
            SpawnVisualBullets(rt, burst);
        }

        internal void HaltGuns(bool clearBullets)
        {
            _gunAimUntil = 0f;
            _ejects.Clear();
            if (_gunRt == null) return;
            for (int i = 0; i < _gunRt.Length; i++)
            {
                var rt = _gunRt[i];
                if (rt == null) continue;
                StopLoop(rt, false);
                rt.SustainUntil = -1f;
                if (rt.Sim == null) continue;
                if (clearBullets)
                {
                    ClearBullets(rt.Sim);
                    UnityEngine.Object.Destroy(rt.Sim.gameObject);
                    rt.Sim = null;
                }
                else
                    rt.Sim.enabled = false;
            }
        }

        internal void ResumeGuns()
        {
            if (_gunRt == null) return;
            for (int i = 0; i < _gunRt.Length; i++)
            {
                var rt = _gunRt[i];
                if (rt != null && rt.Sim != null) rt.Sim.enabled = true;
            }
        }

        internal void TickGunVisuals()
        {
            if (_gunRt == null || _gunRt.Length == 0) return;
            bool paused = Plugin.Clock != null && Plugin.Clock.Paused;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            float dt = paused ? 0f : Time.unscaledDeltaTime;
            for (int i = 0; i < _gunRt.Length; i++)
            {
                var rt = _gunRt[i];
                if (rt == null) continue;
                if (!paused && rt.Spin != null && rt.SpinNow > 0f && rt.SpinRate != 0f && CamNear(rt.Spin.position, 180f))
                {
                    rt.Spin.Rotate(rt.SpinNow * rt.SpinRate * dt * Vector3.forward, Space.Self);
                    rt.SpinNow = Mathf.Max(0f, rt.SpinNow - dt);
                }
                if (!paused && rt.Recoil != null && rt.RecoilTravel > 0f)
                {
                    float step = rt.RecoilEnergy > 0f ? rt.RecoilRate * dt : -rt.RecoilReturn * dt;
                    rt.RecoilPos = Mathf.Clamp01(rt.RecoilPos + step);
                    if (rt.RecoilPos >= 1f) rt.RecoilEnergy = 0f;
                    rt.Recoil.localPosition = new Vector3(0f, 0f, -rt.RecoilPos * rt.RecoilTravel);
                }
                if (rt.Sources.Length > 1 && rt.FireSustained != null)
                {
                    bool hear = Plugin.AudioNear(gameObject, 700f);
                    bool want = !paused && rt.SustainUntil >= now;
                    bool playing = rt.Sources.Length > 1 && IsPlaying(rt.Sources[1]);
                    if (want && hear && (!rt.Looping || !playing)) StartLoop(rt);
                    else if (rt.Looping && (!want || !hear)) StopLoop(rt, !want && !paused && hear);
                }
                else if (!paused && rt.Looping && now > rt.SustainUntil)
                    StopLoop(rt, true);
                if (!paused && rt.Looping && rt.ModifyPitch)
                    ClimbPitch(rt, dt);
            }
            if (!paused) TickEjects();
        }

        internal void InvalidateGuns()
        {
            ReleaseGunVisuals();
            _gunsBound = false;
            _gunRt = Array.Empty<GunRuntime>();
            _gunFree.Clear();
            _muzzleIds.Clear();
            _turrets = Array.Empty<Transform>();
            _elev = Array.Empty<Transform>();
            _turretKeys = Array.Empty<string>();
            _gunStampValid = false;
            _nextGunScan = 0f;
            _killPsBound = false;
        }

        internal void ReleaseGunVisuals()
        {
            HaltGuns(true);
        }

        internal bool IsMuzzleParticle(Component c)
        {
            return c != null && _muzzleIds.Contains(c.GetInstanceID());
        }

        void ProtectGunParts()
        {
            if (_ordnance) return;
            BindGuns();
            if (_parts.Length == 0 || _gunFree.Count == 0) return;
            for (int i = 0; i < _parts.Length; i++)
            {
                var tr = _parts[i];
                if (tr != null && _gunFree.Contains(tr))
                    _partSkip[i] = true;
            }
        }

        float NextGunInterval()
        {
            if (_gunWatchStart < 0f) _gunWatchStart = Time.unscaledTime;
            bool waiting = _air && _turrets.Length == 0 && Time.unscaledTime - _gunWatchStart < 20f;
            return waiting ? 0.2f : 1.25f;
        }

        int ComputeGunStamp()
        {
            int h = 17;
            var turrets = GetComponentsInChildren<Turret>(true);
            h = h * 31 + turrets.Length;
            for (int i = 0; i < turrets.Length; i++)
            {
                if (turrets[i] != null) h = h * 31 + turrets[i].GetInstanceID();
            }
            var guns = GetComponentsInChildren<Gun>(true);
            h = h * 31 + guns.Length;
            for (int i = 0; i < guns.Length; i++)
            {
                if (guns[i] != null) h = h * 31 + guns[i].GetInstanceID();
            }
            return h;
        }

        /// <summary>
        /// Hardpoint turrets (Chicane chin gun, gunpods) spawn after the first bind.
        /// Rebind when the turret or gun set changes so aim, muzzle flash and
        /// HoldLocals exclusion follow the new mounts.
        /// </summary>
        void EnsureGunsCurrent(bool force)
        {
            if (_ordnance) return;
            if (!force && _gunsBound && Time.unscaledTime < _nextGunScan) return;
            int stamp = ComputeGunStamp();
            if (_gunsBound && _gunStampValid && stamp == _gunStamp)
            {
                _nextGunScan = Time.unscaledTime + NextGunInterval();
                return;
            }
            if (_gunsBound) InvalidateGuns();
            BindGuns();
            _nextGunScan = Time.unscaledTime + NextGunInterval();
            ProtectGunParts();
        }

        void BindGuns()
        {
            if (_gunsBound) return;
            _gunsBound = true;
            BindGunFields();
            var guns = GetComponentsInChildren<Gun>(true);
            _gunRt = new GunRuntime[guns.Length];
            for (int i = 0; i < guns.Length; i++)
            {
                var gun = guns[i];
                if (gun == null) continue;
                if (gun is Behaviour b) b.enabled = false;
                var rt = ReadGun(gun, i);
                _gunRt[i] = rt;
                _gunFree.Add(gun.transform);
                if (rt.Spin != null) _gunFree.Add(rt.Spin);
                if (rt.Recoil != null) _gunFree.Add(rt.Recoil);
                for (int p = 0; p < rt.Particles.Length; p++)
                {
                    if (rt.Particles[p] is Component pc)
                    {
                        _muzzleIds.Add(pc.GetInstanceID());
                        ArmParticle(rt.Particles[p]);
                    }
                }
            }
            var turrets = GetComponentsInChildren<Turret>(true);
            _turrets = new Transform[turrets.Length];
            _elev = new Transform[turrets.Length];
            _turretKeys = TurretKeys.Build(transform, turrets);
            var elevField = typeof(Turret).GetField("elevationTransform", GunFlags);
            for (int i = 0; i < turrets.Length; i++)
            {
                var t = turrets[i];
                if (t == null) continue;
                if (t is Behaviour tb) tb.enabled = false;
                _turrets[i] = t.transform;
                _elev[i] = elevField?.GetValue(t) as Transform;
                _gunFree.Add(t.transform);
                if (_elev[i] != null) _gunFree.Add(_elev[i]);
            }
            _gunStamp = ComputeGunStamp();
            _gunStampValid = true;
        }

        GunRuntime ReadGun(Gun gun, int index)
        {
            var rt = new GunRuntime
            {
                Gun = gun,
                Index = index,
                Path = GunPath(transform, gun.transform),
                Muzzles = ReadTransforms(_fMuzzles, gun),
                Spin = _fSpin?.GetValue(gun) as Transform,
                Recoil = _fRecoil?.GetValue(gun) as Transform,
                SpinRate = Flt(_fSpinRate, gun),
                RecoilTravel = Flt(_fRecoilTravel, gun),
                RecoilRate = Flt(_fRecoilRate, gun, 8f),
                RecoilReturn = Flt(_fRecoilReturn, gun, 3f),
                Particles = ReadObjects(_fParticles, gun),
                FireSounds = ReadObjects(_fFireSounds, gun),
                FireStart = _fFireStart?.GetValue(gun),
                FireSustained = _fFireSustained?.GetValue(gun),
                FireEnd = _fFireEnd?.GetValue(gun),
                Sources = ReadObjects(_fSources, gun),
                RecoilSound = _fRecoilSound?.GetValue(gun),
                Volume = Flt(_fVolume, gun, 1f),
                Pitch = Flt(_fPitch, gun, 1f),
                PitchVar = Flt(_fPitchVar, gun),
                ModifyPitch = _fModifyPitch?.GetValue(gun) is bool mod && mod,
                StartPitchMul = Flt(_fStartPitch, gun, 1f),
                PitchClimb = Flt(_fPitchClimb, gun, 1f),
                Spread = Flt(_fSpread, gun, 0.1f),
                MuzzleVelocity = Flt(_fMuzzleVel, gun),
                SelfDestruct = Flt(_fSelfDestruct, gun, 5f),
                TracerRatio = _fTracerRatio?.GetValue(gun) is int ratio && ratio > 0 ? ratio : 6,
                TracerSize = Flt(_fTracerSize, gun, 1f),
                TracerColor = _fTracerColor?.GetValue(gun) is Color col ? col : Color.red,
                EjectPrefab = _fEjectPrefab?.GetValue(gun) as GameObject,
                EjectPoint = _fEjectXf?.GetValue(gun) as Transform,
                EjectDelay = Flt(_fEjectDelay, gun, 0.2f),
                EjectLife = Flt(_fEjectLife, gun, 4f),
                EjectLin = _fEjectLin?.GetValue(gun) is Vector3 lin ? lin : Vector3.zero,
                EjectAng = _fEjectAng?.GetValue(gun) is Vector3 ang ? ang : Vector3.zero,
                SustainUntil = -1f
            };
            if (rt.MuzzleVelocity < 1f && gun.info != null)
                rt.MuzzleVelocity = gun.info.muzzleVelocity;
            if (rt.Volume <= 0f) rt.Volume = 1f;
            if (rt.Pitch <= 0f) rt.Pitch = 1f;
            if (rt.SelfDestruct <= 0f) rt.SelfDestruct = 5f;
            if (rt.EjectLife <= 0f) rt.EjectLife = 4f;
            if (rt.Sources.Length == 0)
                rt.Sources = SourcesOn(gun.gameObject);
            return rt;
        }

        GunRuntime? ResolveGun(ReplayObject burst)
        {
            EnsureGunsCurrent(false);
            if (_gunRt.Length == 0) return null;
            if (burst.GunIndex >= 0 && burst.GunIndex < _gunRt.Length)
            {
                var byIndex = _gunRt[burst.GunIndex];
                if (byIndex != null && (string.IsNullOrEmpty(burst.Weapon) || PathEquals(byIndex.Path, burst.Weapon)))
                    return byIndex;
            }
            if (!string.IsNullOrEmpty(burst.Weapon))
            {
                for (int i = 0; i < _gunRt.Length; i++)
                {
                    var rt = _gunRt[i];
                    if (rt != null && PathEquals(rt.Path, burst.Weapon)) return rt;
                }
                string leaf = burst.Weapon;
                int slash = burst.Weapon.LastIndexOf('/');
                if (slash >= 0 && slash < burst.Weapon.Length - 1) leaf = burst.Weapon.Substring(slash + 1);
                for (int i = 0; i < _gunRt.Length; i++)
                {
                    var rt = _gunRt[i];
                    if (rt != null && rt.Gun != null && rt.Gun.name.Equals(leaf, StringComparison.OrdinalIgnoreCase))
                        return rt;
                }
            }
            if (burst.GunIndex >= 0 && burst.GunIndex < _gunRt.Length && _gunRt[burst.GunIndex] != null)
                return _gunRt[burst.GunIndex];
            return _gunRt[0];
        }

        static bool PathEquals(string have, string want)
        {
            have = TurretKeys.Sanitize(have);
            want = TurretKeys.Sanitize(want);
            if (have.Equals(want, StringComparison.OrdinalIgnoreCase)) return true;
            return have.EndsWith("/" + want, StringComparison.OrdinalIgnoreCase)
                || want.EndsWith("/" + have, StringComparison.OrdinalIgnoreCase);
        }

        void SpawnVisualBullets(GunRuntime rt, ReplayObject burst)
        {
            if (rt.Muzzles.Length == 0) return;
            var sim = EnsureSim(rt);
            if (sim == null) return;
            float speed = rt.MuzzleVelocity;
            if (burst.Muzzle > 50f) speed = burst.Muzzle;
            if (speed < 1f) speed = 900f;
            float spread = rt.Spread > 0f ? rt.Spread : (burst.Spread > 0f ? burst.Spread : 0.1f);
            Vector3 inherit = Vector3.zero;
            if (Velocities.TryGetValue(GetInstanceID(), out var vel)) inherit = vel;
            for (int i = 0; i < rt.Muzzles.Length; i++)
            {
                var muzzle = rt.Muzzles[i];
                if (muzzle == null) continue;
                rt.TracerSeed++;
                bool tracer = false;
                if (rt.TracerSeed > rt.TracerRatio)
                {
                    rt.TracerSeed -= rt.TracerRatio;
                    tracer = true;
                }
                Vector3 shot = inherit + muzzle.forward * speed;
                try
                {
                    _fVisualOnly?.SetValue(sim, true);
                    sim.AddBullet(muzzle, shot, spread, rt.SelfDestruct, tracer, rt.TracerSize, rt.TracerColor, 0f, null);
                }
                catch (Exception ex)
                {
                    rt.SimFailed = true;
                    if (rt.Sim != null)
                    {
                        ClearBullets(rt.Sim);
                        UnityEngine.Object.Destroy(rt.Sim.gameObject);
                        rt.Sim = null;
                    }
                    LogSimOnce("AddBullet " + ex.Message);
                    return;
                }
            }
        }

        BulletSim? EnsureSim(GunRuntime rt)
        {
            if (rt.Sim != null) return rt.Sim;
            if (rt.SimFailed) return null;
            var gun = rt.Gun as Gun;
            if (gun == null || gun.info == null)
            {
                rt.SimFailed = true;
                LogSimOnce("Gun ohne WeaponInfo " + (rt.Gun != null ? rt.Gun.name : "?"));
                return null;
            }
            Unit? owner = gun.attachedUnit;
            if (owner == null) owner = GetComponent<Unit>() ?? GetComponentInParent<Unit>();
            if (owner == null)
            {
                rt.SimFailed = true;
                LogSimOnce("Gun ohne Unit " + gun.name);
                return null;
            }
            if (gun.attachedUnit == null) gun.attachedUnit = owner;
            EnsureImpactPrefabs(gun);
            try
            {
                var sim = BulletSim.Create(owner, gun, null);
                if (sim == null)
                {
                    rt.SimFailed = true;
                    return null;
                }
                if (_fVisualOnly == null || !(_fVisualOnly.GetValue(sim) is bool))
                {
                    rt.SimFailed = true;
                    LogSimOnce("BulletSim.visualOnly fehlt");
                    UnityEngine.Object.Destroy(sim.gameObject);
                    return null;
                }
                _fVisualOnly.SetValue(sim, true);
                if (_fVisualOnly.GetValue(sim) is bool on && !on)
                {
                    rt.SimFailed = true;
                    LogSimOnce("visualOnly ließ sich nicht setzen");
                    UnityEngine.Object.Destroy(sim.gameObject);
                    return null;
                }
                sim.gameObject.name = "NOReplay_BulletSim";
                sim.enabled = true;
                rt.Sim = sim;
                if (!_simLogged)
                {
                    _simLogged = true;
                    Plugin.Log.LogInfo("Kanonen über BulletSim (visualOnly) Läufe=" + rt.Muzzles.Length + " " + rt.Path);
                }
                return sim;
            }
            catch (Exception ex)
            {
                rt.SimFailed = true;
                LogSimOnce(ex.Message);
                return null;
            }
        }

        static void EnsureImpactPrefabs(Gun gun)
        {
            var prop = typeof(Gun).GetProperty("ImpactEffectsPrefabs", GunFlags);
            if (prop?.GetValue(gun) is GameObject[] have && have.Length >= 4) return;
            var built = new GameObject[4];
            built[0] = _fImpactGround?.GetValue(gun) as GameObject;
            built[1] = _fImpactArmor?.GetValue(gun) as GameObject;
            built[2] = _fImpactWater?.GetValue(gun) as GameObject;
            built[3] = _fImpactSelf?.GetValue(gun) as GameObject;
            try { prop?.SetValue(gun, built); } catch { }
        }

        static void ClearBullets(BulletSim sim)
        {
            if (sim == null) return;
            try
            {
                var field = typeof(BulletSim).GetField("bullets", GunFlags);
                if (field?.GetValue(sim) is not IList list) return;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is BulletSim.Bullet bullet) bullet.Remove();
                }
                list.Clear();
            }
            catch { }
        }

        static void LogSimOnce(string msg)
        {
            if (_simFailLogged) return;
            _simFailLogged = true;
            Plugin.Log.LogWarning("BulletSim: " + msg);
        }

        void PlayMuzzles(GunRuntime rt)
        {
            for (int i = 0; i < rt.Particles.Length; i++)
                PlayMuzzle(rt.Particles[i]);
        }

        static void PlayMuzzle(object? p)
        {
            if (p == null) return;
            try
            {
                ArmParticle(p);
                var t = p.GetType();
                var play = t.GetMethod("Play", new[] { typeof(bool) }) ?? t.GetMethod("Play", Type.EmptyTypes);
                if (play == null) return;
                if (play.GetParameters().Length == 1) play.Invoke(p, new object[] { true });
                else play.Invoke(p, null);
            }
            catch { }
        }

        static void ArmParticle(object p)
        {
            try
            {
                var t = p.GetType();
                if (p is Behaviour b) b.enabled = true;
                t.GetProperty("enableEmission")?.SetValue(p, true);
                var emission = t.GetProperty("emission")?.GetValue(p);
                emission?.GetType().GetProperty("enabled")?.SetValue(emission, true);
            }
            catch { }
        }

        void PlayShotAudio(GunRuntime rt, float sustainUntil)
        {
            if (sustainUntil > rt.SustainUntil) rt.SustainUntil = sustainUntil;
            if (!Plugin.AudioNear(gameObject, 800f)) return;
            if (rt.Sources.Length == 0) return;
            float pitch = rt.Pitch;
            if (rt.PitchVar > 0f)
                pitch += UnityEngine.Random.value * rt.PitchVar - UnityEngine.Random.value * rt.PitchVar;
            var src = rt.Sources[0];
            ArmSource(src, rt.Volume, pitch);
            if (rt.Sources.Length < 2)
            {
                if (rt.FireSounds.Length > 0)
                {
                    int pick = rt.FireSounds.Length == 1 ? 0 : UnityEngine.Random.Range(0, rt.FireSounds.Length);
                    PlayOne(src, rt.FireSounds[pick]);
                }
            }
            else if (rt.FireStart != null && !IsPlaying(rt.Sources[1]))
                PlayOne(src, rt.FireStart);
            if (rt.RecoilSound != null)
            {
                ArmSource(rt.RecoilSound, rt.Volume, UnityEngine.Random.Range(0.95f, 1.05f));
                var st = rt.RecoilSound.GetType();
                st.GetMethod("Play", Type.EmptyTypes)?.Invoke(rt.RecoilSound, null);
            }
            if (rt.Sources.Length > 1 && rt.FireSustained != null && !rt.Looping)
                StartLoop(rt);
        }

        void StartLoop(GunRuntime rt)
        {
            if (rt.Sources.Length < 2 || rt.FireSustained == null) return;
            var src = rt.Sources[1];
            float startPitch = rt.ModifyPitch ? rt.Pitch * rt.StartPitchMul : rt.Pitch;
            ArmSource(src, rt.Volume, startPitch);
            var st = src.GetType();
            st.GetProperty("loop")?.SetValue(src, true);
            st.GetProperty("clip")?.SetValue(src, rt.FireSustained);
            if (IsPlaying(src)) { rt.Looping = true; return; }
            st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
            float len = 0f;
            var lp = rt.FireSustained.GetType().GetProperty("length")?.GetValue(rt.FireSustained);
            if (lp is float f) len = f;
            if (len > 0.05f)
                st.GetProperty("time")?.SetValue(src, UnityEngine.Random.Range(0f, len));
            rt.Looping = true;
        }

        void StopLoop(GunRuntime rt, bool playEnd)
        {
            if (rt.Sources.Length > 1)
            {
                var loop = rt.Sources[1];
                bool was = rt.Looping || IsPlaying(loop);
                var st = loop.GetType();
                st.GetMethod("Stop", Type.EmptyTypes)?.Invoke(loop, null);
                st.GetProperty("volume")?.SetValue(loop, 0f);
                if (playEnd && was && rt.FireEnd != null && rt.Sources.Length > 0 && Plugin.AudioNear(gameObject, 800f))
                {
                    ArmSource(rt.Sources[0], rt.Volume, rt.Pitch);
                    PlayOne(rt.Sources[0], rt.FireEnd);
                }
            }
            rt.Looping = false;
        }

        static void ClimbPitch(GunRuntime rt, float dt)
        {
            if (rt.Sources.Length < 2) return;
            var src = rt.Sources[1];
            var st = src.GetType();
            float cur = st.GetProperty("pitch")?.GetValue(src) is float p ? p : rt.Pitch;
            if (cur < rt.Pitch)
                st.GetProperty("pitch")?.SetValue(src, Mathf.Min(rt.Pitch, cur + dt * Mathf.Max(0.1f, rt.PitchClimb)));
        }

        static void ArmSource(object src, float volume, float pitch)
        {
            var st = src.GetType();
            st.GetProperty("mute")?.SetValue(src, false);
            st.GetProperty("volume")?.SetValue(src, volume);
            st.GetProperty("pitch")?.SetValue(src, pitch);
            st.GetProperty("spatialBlend")?.SetValue(src, 1f);
        }

        static bool IsPlaying(object src)
        {
            return src.GetType().GetProperty("isPlaying")?.GetValue(src) is bool p && p;
        }

        static void PlayOne(object src, object? clip)
        {
            if (clip == null) return;
            var st = src.GetType();
            var play = st.GetMethod("PlayOneShot", new[] { clip.GetType() });
            if (play != null) { play.Invoke(src, new[] { clip }); return; }
            play = st.GetMethod("PlayOneShot", new[] { clip.GetType(), typeof(float) });
            play?.Invoke(src, new object[] { clip, 1f });
        }

        void KickRecoil(GunRuntime rt)
        {
            if (rt.Recoil == null || rt.RecoilTravel <= 0f) return;
            rt.RecoilEnergy = 1f;
        }

        void KickSpin(GunRuntime rt)
        {
            if (rt.Spin == null || Mathf.Abs(rt.SpinRate) < 0.01f) return;
            if (!CamNear(rt.Spin.position, 180f)) return;
            rt.SpinNow = 1f;
        }

        void Eject(GunRuntime rt)
        {
            if (rt.EjectPrefab == null || rt.EjectPoint == null) return;
            if (!CamNear(rt.EjectPoint.position, 220f)) return;
            _ejects.Add(new PendingEject { At = Time.unscaledTime + Mathf.Max(0f, rt.EjectDelay), Rt = rt });
        }

        void TickEjects()
        {
            if (_ejects.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _ejects.Count - 1; i >= 0; i--)
            {
                if (now < _ejects[i].At) continue;
                SpawnEject(_ejects[i].Rt);
                _ejects.RemoveAt(i);
            }
        }

        void SpawnEject(GunRuntime rt)
        {
            if (rt.EjectPrefab == null || rt.EjectPoint == null) return;
            try
            {
                var obj = UnityEngine.Object.Instantiate(rt.EjectPrefab, rt.EjectPoint.position, rt.EjectPoint.rotation);
                var body = obj.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.solverIterations = 1;
                    Vector3 inherit = Vector3.zero;
                    if (Velocities.TryGetValue(GetInstanceID(), out var vel)) inherit = vel;
                    body.velocity = inherit;
                    body.AddRelativeForce(rt.EjectLin, ForceMode.VelocityChange);
                    body.AddRelativeTorque(rt.EjectAng, ForceMode.VelocityChange);
                }
                UnityEngine.Object.Destroy(obj, rt.EjectLife);
            }
            catch { }
        }

        internal void PulseAt(Vector3 pos)
        {
            EnsureGunsCurrent(false);
            if (_gunRt.Length == 0) return;
            GunRuntime? best = null;
            float bestD = 80f * 80f;
            for (int i = 0; i < _gunRt.Length; i++)
            {
                var rt = _gunRt[i];
                if (rt == null) continue;
                var origin = rt.Muzzles.Length > 0 && rt.Muzzles[0] != null ? rt.Muzzles[0] : rt.Gun.transform;
                float d = (origin.position - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = rt;
                }
            }
            if (best == null || bestD > 80f * 80f) return;
            PlayMuzzles(best);
            if (Plugin.AudioNear(gameObject, 800f))
                PlayOneShotOnly(best);
        }

        void PlayOneShotOnly(GunRuntime rt)
        {
            if (rt.Sources.Length == 0) return;
            float pitch = rt.Pitch;
            if (rt.PitchVar > 0f)
                pitch += UnityEngine.Random.value * rt.PitchVar - UnityEngine.Random.value * rt.PitchVar;
            ArmSource(rt.Sources[0], rt.Volume, pitch);
            if (rt.FireSounds.Length > 0)
            {
                int pick = rt.FireSounds.Length == 1 ? 0 : UnityEngine.Random.Range(0, rt.FireSounds.Length);
                PlayOne(rt.Sources[0], rt.FireSounds[pick]);
            }
            else if (rt.FireStart != null)
                PlayOne(rt.Sources[0], rt.FireStart);
        }

        internal Vector3 MuzzlePos()
        {
            EnsureGunsCurrent(false);
            for (int i = 0; i < _gunRt.Length; i++)
            {
                var rt = _gunRt[i];
                if (rt == null || rt.Muzzles.Length == 0 || rt.Muzzles[0] == null) continue;
                return rt.Muzzles[0].position;
            }
            return transform.position;
        }

        static bool CamNear(Vector3 world, float metres)
        {
            var cam = Camera.main;
            if (cam == null) return true;
            return (cam.transform.position - world).sqrMagnitude <= metres * metres;
        }

        static string GunPath(Transform root, Transform gun)
        {
            return TurretKeys.Sanitize(TurretKeys.Relative(root, gun));
        }

        static object[] SourcesOn(GameObject go)
        {
            var srcType = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule");
            if (srcType == null) return Array.Empty<object>();
            var found = go.GetComponents(srcType);
            var list = new List<object>(found.Length);
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null) list.Add(found[i]);
            return list.ToArray();
        }

        static Transform[] ReadTransforms(FieldInfo? field, object obj)
        {
            if (field?.GetValue(obj) is not Transform[] arr || arr.Length == 0)
                return Array.Empty<Transform>();
            return arr;
        }

        static object[] ReadObjects(FieldInfo? field, object obj)
        {
            if (field?.GetValue(obj) is not Array arr || arr.Length == 0)
                return Array.Empty<object>();
            var list = new List<object>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                if (item != null) list.Add(item);
            }
            return list.ToArray();
        }

        static float Flt(FieldInfo? field, object obj, float fallback = 0f)
        {
            if (field == null) return fallback;
            try
            {
                if (field.GetValue(obj) is float v) return v;
            }
            catch { }
            return fallback;
        }

        static void BindGunFields()
        {
            if (_gunFields) return;
            _gunFields = true;
            var g = typeof(Gun);
            _fMuzzles = g.GetField("muzzles", GunFlags);
            _fSpin = g.GetField("spinTransform", GunFlags);
            _fRecoil = g.GetField("recoilTransform", GunFlags);
            _fSpinRate = g.GetField("spinRate", GunFlags);
            _fRecoilTravel = g.GetField("recoilTravel", GunFlags);
            _fRecoilRate = g.GetField("recoilRate", GunFlags);
            _fRecoilReturn = g.GetField("recoilReturnRate", GunFlags);
            _fParticles = g.GetField("muzzleParticles", GunFlags);
            _fFireSounds = g.GetField("fireSounds", GunFlags);
            _fFireStart = g.GetField("fireStart", GunFlags);
            _fFireSustained = g.GetField("fireSustained", GunFlags);
            _fFireEnd = g.GetField("fireEnd", GunFlags);
            _fSources = g.GetField("sources", GunFlags);
            _fRecoilSound = g.GetField("recoilSound", GunFlags);
            _fVolume = g.GetField("volume", GunFlags);
            _fPitch = g.GetField("pitch", GunFlags);
            _fPitchVar = g.GetField("pitchVariation", GunFlags);
            _fModifyPitch = g.GetField("modifyPitch", GunFlags);
            _fStartPitch = g.GetField("startPitchMultiplier", GunFlags);
            _fPitchClimb = g.GetField("pitchClimbRate", GunFlags);
            _fSpread = g.GetField("bulletSpread", GunFlags);
            _fMuzzleVel = g.GetField("muzzleVelocity", GunFlags);
            _fSelfDestruct = g.GetField("bulletSelfDestruct", GunFlags);
            _fTracerRatio = g.GetField("tracerRatio", GunFlags);
            _fTracerSize = g.GetField("tracerSize", GunFlags);
            _fTracerColor = g.GetField("tracerColor", GunFlags);
            _fEjectPrefab = g.GetField("ejectionPrefab", GunFlags);
            _fEjectXf = g.GetField("ejectionTransform", GunFlags);
            _fEjectDelay = g.GetField("ejectionDelay", GunFlags);
            _fEjectLife = g.GetField("ejectionLifetime", GunFlags);
            _fEjectLin = g.GetField("ejectionLinearVelocity", GunFlags);
            _fEjectAng = g.GetField("ejectionRotationalVelocity", GunFlags);
            _fImpactGround = g.GetField("impactEffectGround", GunFlags);
            _fImpactArmor = g.GetField("impactEffectArmor", GunFlags);
            _fImpactWater = g.GetField("impactEffectWater", GunFlags);
            _fImpactSelf = g.GetField("selfDestructEffect", GunFlags);
            _fVisualOnly = typeof(BulletSim).GetField("visualOnly", GunFlags);
        }

        sealed class GunRuntime
        {
            public Component Gun = null!;
            public int Index;
            public string Path = "";
            public Transform[] Muzzles = Array.Empty<Transform>();
            public Transform? Spin;
            public Transform? Recoil;
            public float SpinRate, RecoilTravel, RecoilRate, RecoilReturn;
            public float RecoilPos, RecoilEnergy, SpinNow;
            public object[] Particles = Array.Empty<object>();
            public object[] FireSounds = Array.Empty<object>();
            public object? FireStart, FireSustained, FireEnd, RecoilSound;
            public object[] Sources = Array.Empty<object>();
            public float Volume = 1f, Pitch = 1f, PitchVar, StartPitchMul = 1f, PitchClimb = 1f;
            public bool ModifyPitch, Looping;
            public float Spread, MuzzleVelocity, SelfDestruct = 5f, TracerSize = 1f;
            public int TracerRatio = 6, TracerSeed;
            public Color TracerColor = Color.red;
            public GameObject? EjectPrefab;
            public Transform? EjectPoint;
            public float EjectDelay, EjectLife;
            public Vector3 EjectLin, EjectAng;
            public float SustainUntil = -1f;
            public bool SimFailed;
            public BulletSim? Sim;
        }

        struct PendingEject
        {
            public float At;
            public GunRuntime Rt;
        }
    }
}
