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
        bool _rollSpoke;
        bool _squealSpoke;
        bool _missSpoke;
        bool _audioSpoke;
        readonly float[] _surfT = new float[8];
        readonly float[] _surfY = new float[8];

        void DriveTires(ReplaySample s, bool gearDown, float agl, bool running)
        {
            if (!_air) return;
            if (_gears == null || _gears.Length == 0)
            {
                StopTireAudio();
                return;
            }
            BindTireFields();
            float age = SurfaceAge(Plugin.Clock.Time, s.Y);
            float dt = running ? Time.unscaledDeltaTime * Mathf.Abs(Plugin.Clock.Speed) : 0f;
            bool any = false;
            for (int i = 0; i < _gears.Length; i++)
            {
                var gear = _gears[i];
                if (gear == null) continue;
                if (Touching(gear, gearDown, out var hit, out bool tarmac, out float along, out float lateral))
                {
                    any = true;
                    // Rolling is the continuous wheel clip. Squeal and smoke need
                    // the wheel still catching up, which a carrier deck never shows
                    // as an altitude under 3 m.
                    bool fresh = TireMath.TouchdownSlip(along, age);
                    var fx = TireMath.At(true, tarmac, along, lateral, fresh, running, dt);
                    NoteTire(gear, fx, tarmac, along, age, hit);
                    DriveOneTire(gear, tarmac, fx, hit);
                }
                else
                    SilenceGear(gear);
            }
            if (!any)
            {
                if (_rollSpoke || _squealSpoke)
                {
                    _rollSpoke = false;
                    _squealSpoke = false;
                }
                if (!_missSpoke && gearDown && _spdMs > 25f)
                {
                    _missSpoke = true;
                    Plugin.Log?.LogInfo("Tire miss " + gameObject.name
                        + " agl=" + agl.ToString("0.0")
                        + " spd=" + _spdMs.ToString("0"));
                }
                StopTireAudio();
            }
        }

        float SurfaceAge(float now, float yNow)
        {
            int n = 0;
            if (Track != null)
            {
                for (int i = 1; i <= _surfT.Length; i++)
                {
                    float t = now - i * 0.2f;
                    if (t < 0f) break;
                    if (!Track.TrySample(t, out var old)) continue;
                    _surfT[n] = t;
                    _surfY[n] = old.Y;
                    n++;
                }
            }
            return TireMath.SecondsSinceDescent(now, yNow, _surfT, _surfY, n);
        }

        void NoteTire(Component gear, TireMath.Result fx, bool tarmac, float along, float age, RaycastHit hit)
        {
            if (!_audioSpoke)
            {
                _audioSpoke = true;
                bool noise = _fNoise?.GetValue(gear) != null;
                bool skid = _fSkid?.GetValue(gear) != null;
                if (!noise || !skid)
                    Plugin.Log?.LogInfo("Tire audio missing " + gameObject.name
                        + " roll=" + noise + " squeal=" + skid);
            }
            string hitName = hit.collider != null ? hit.collider.name : "-";
            if (fx.Rolling && !_rollSpoke)
            {
                _rollSpoke = true;
                Plugin.Log?.LogInfo("Tire roll " + gameObject.name
                    + " tarmac=" + (tarmac ? 1 : 0)
                    + " along=" + along.ToString("0")
                    + " hit=" + hitName);
            }
            if ((fx.Smoke || fx.Skid) && !_squealSpoke)
            {
                _squealSpoke = true;
                Plugin.Log?.LogInfo("Tire squeal " + gameObject.name
                    + " tarmac=" + (tarmac ? 1 : 0)
                    + " along=" + along.ToString("0")
                    + " age=" + age.ToString("0.00")
                    + " smoke=" + fx.SmokeCount
                    + " hit=" + hitName);
            }
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
            // The gear solver is not running, so the cast point can sit inside
            // the deck. The ray covers the strut above the cast as well as below it.
            Vector3 dir = -cast.up;
            Vector3 origin = cast.position - dir * travel;
            float reach = travel * 2f;
            var ray = new Ray(origin, dir);
            var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!AcceptHit(h, best)) continue;
                best = h.distance;
                hit = h;
                found = true;
            }
            if (!found)
                found = ClosestDeck(ray, reach, ref hit, ref best);
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

        bool AcceptHit(RaycastHit h, float best)
        {
            if (h.collider == null || h.distance >= best) return false;
            var tr = h.collider.transform;
            if (tr == transform || tr.IsChildOf(transform)) return false;
            return true;
        }

        static ReplayGhost[] _decks = Array.Empty<ReplayGhost>();
        static int _deckFrame = -100;

        bool ClosestDeck(Ray ray, float reach, ref RaycastHit hit, ref float best)
        {
            if (Time.frameCount - _deckFrame > 15)
            {
                _deckFrame = Time.frameCount;
                _decks = UnityEngine.Object.FindObjectsByType<ReplayGhost>(FindObjectsSortMode.None);
            }
            bool found = false;
            for (int g = 0; g < _decks.Length; g++)
            {
                var ship = _decks[g];
                if (ship == null || ship == this || !ship.HasDeck) continue;
                var cols = ship.DeckColliders();
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c == null || !c.enabled) continue;
                    if (!c.Raycast(ray, out var h, reach)) continue;
                    if (!AcceptHit(h, best)) continue;
                    best = h.distance;
                    hit = h;
                    found = true;
                }
            }
            return found;
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
