using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static FieldInfo? _fCast, _fTravel, _fFoldAmt, _fDust, _fNoise, _fSkid;
        static bool _tireFields;
        static int _tireSmokeId = -1;
        static bool _tireSmokeTried;
        bool _tireTarmac = true;
        bool _tireClips;

        void DriveTires(ReplaySample s, bool gearDown, float agl, bool running)
        {
            if (!_air) return;
            if (_gears == null || _gears.Length == 0)
            {
                StopTireAudio();
                return;
            }
            BindTireFields();
            bool recent = false;
            if (Track != null && Track.TrySample(Plugin.Clock.Time - TireMath.Lookback, out var ago))
                recent = TireMath.RecentAir(agl, true, ago.HasAgl ? ago.Agl : agl, ago.HasAgl);
            float dt = running ? Time.unscaledDeltaTime * Mathf.Abs(Plugin.Clock.Speed) : 0f;
            bool any = false;
            for (int i = 0; i < _gears.Length; i++)
            {
                var gear = _gears[i];
                if (gear == null) continue;
                if (Touching(gear, gearDown, out var hit, out bool tarmac, out float along, out float lateral))
                {
                    any = true;
                    var fx = TireMath.At(true, tarmac, along, lateral, recent, running, dt);
                    DriveOneTire(gear, tarmac, fx, hit);
                }
                else
                    SilenceGear(gear);
            }
            if (!any) StopTireAudio();
        }

        bool Touching(Component gear, bool gearDown, out RaycastHit hit, out bool tarmac, out float along, out float lateral)
        {
            hit = default;
            tarmac = true;
            along = 0f;
            lateral = 0f;
            if (!gearDown) return false;
            float fold = _fFoldAmt?.GetValue(gear) is float f ? f : 0f;
            if (fold >= 0.2f) return false;
            var cast = _fCast?.GetValue(gear) as Transform;
            if (cast == null) cast = gear.transform;
            float travel = _fTravel?.GetValue(gear) is float tv && tv > 0.05f ? tv : 1.2f;
            var hits = Physics.RaycastAll(cast.position, -cast.up, travel, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    hit = h;
                    found = true;
                }
            }
            if (!found) return false;
            tarmac = true;
            try
            {
                var terrain = GameAssets.i != null ? GameAssets.i.terrainMaterial : null;
                if (terrain != null && hit.collider.sharedMaterial == terrain)
                    tarmac = false;
            }
            catch { }
            Vector3 flat = Vector3.ProjectOnPlane(_velMs, Vector3.up);
            along = Vector3.Dot(flat, cast.forward);
            lateral = Vector3.Dot(flat, cast.right);
            return true;
        }

        void DriveOneTire(Component gear, bool tarmac, TireMath.Result fx, RaycastHit hit)
        {
            var dust = _fDust?.GetValue(gear) as Component;
            if (fx.Dust) PlayParticleSystem(dust);
            else StopParticleSystem(dust, false);
            var noise = _fNoise?.GetValue(gear);
            var skid = _fSkid?.GetValue(gear);
            if (_tireTarmac != tarmac || !_tireClips)
            {
                _tireTarmac = tarmac;
                _tireClips = true;
                AssignClips(noise, skid, tarmac);
            }
            SetTireSource(noise, fx.Rolling, fx.NoiseVolume, fx.NoisePitch);
            SetTireSource(skid, fx.Skid, fx.SkidVolume, fx.SkidPitch);
            if (fx.SmokeCount > 0)
                EmitTireSmoke(hit, fx.SmokeCount);
        }

        void SilenceGear(Component gear)
        {
            StopParticleSystem(_fDust?.GetValue(gear) as Component, false);
            SetTireSource(_fNoise?.GetValue(gear), false, 0f, 1f);
            SetTireSource(_fSkid?.GetValue(gear), false, 0f, 1f);
        }

        void StopTireAudio()
        {
            if (_gears == null) return;
            for (int i = 0; i < _gears.Length; i++)
                if (_gears[i] != null) SilenceGear(_gears[i]);
        }

        static void AssignClips(object? noise, object? skid, bool tarmac)
        {
            AudioClip? roll = null;
            AudioClip? slide = null;
            try
            {
                if (GameAssets.i != null)
                {
                    roll = tarmac ? GameAssets.i.wheelRollingRoad : GameAssets.i.wheelRollingDirt;
                    slide = tarmac ? GameAssets.i.wheelSlidingRoad : GameAssets.i.wheelSlidingDirt;
                }
            }
            catch { }
            SetClip(noise, roll);
            SetClip(skid, slide);
        }

        static void SetClip(object? src, AudioClip? clip)
        {
            if (src == null || clip == null) return;
            try
            {
                var t = src.GetType();
                if (t.GetProperty("clip")?.GetValue(src) != clip)
                {
                    t.GetMethod("Stop", Type.EmptyTypes)?.Invoke(src, null);
                    t.GetProperty("clip")?.SetValue(src, clip);
                }
            }
            catch { }
        }

        static void SetTireSource(object? src, bool on, float volume, float pitch)
        {
            if (src == null) return;
            try
            {
                var t = src.GetType();
                t.GetProperty("volume")?.SetValue(src, on ? volume : 0f);
                t.GetProperty("pitch")?.SetValue(src, pitch);
                bool playing = t.GetProperty("isPlaying")?.GetValue(src) is bool p && p;
                if (on && !playing)
                    t.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
                else if (!on && playing)
                    t.GetMethod("Stop", Type.EmptyTypes)?.Invoke(src, null);
            }
            catch { }
        }

        void EmitTireSmoke(RaycastHit hit, int count)
        {
            try
            {
                var pem = SceneSingleton<ParticleEffectManager>.i;
                if (pem == null) return;
                if (!_tireSmokeTried)
                {
                    _tireSmokeTried = true;
                    _tireSmokeId = pem.GetSystemID("TireSmoke");
                }
                if (_tireSmokeId < 0) return;
                var at = (hit.point + hit.normal * 0.4f).ToGlobalPosition();
                pem.EmitParticles(_tireSmokeId, count, at, Vector3.ProjectOnPlane(_velMs, Vector3.up),
                    0f, 1.2f, 0.3f, 1.5f, 0.3f, 0.3f, 0.6f, 0.3f);
            }
            catch { }
        }

        static void BindTireFields()
        {
            if (_tireFields) return;
            _tireFields = true;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var t = typeof(LandingGear);
            _fCast = t.GetField("castPoint", flags);
            _fTravel = t.GetField("suspensionTravel", flags);
            _fFoldAmt = t.GetField("foldAmount", flags);
            _fDust = t.GetField("dust", flags);
            _fNoise = t.GetField("tireNoiseSound", flags);
            _fSkid = t.GetField("tireSkidSound", flags);
        }
    }
}
