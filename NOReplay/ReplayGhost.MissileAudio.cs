using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        sealed class MotorSlot
        {
            public object? Body;
            public AudioSource? Startup;
            public AudioSource[] Sources = Array.Empty<AudioSource>();
        }

        MotorSlot[]? _slots;
        MissileMotorAudio.Stage[] _motorStages = Array.Empty<MissileMotorAudio.Stage>();
        object? _slotsMissile;
        int _ignitionStage = -1;
        int _headStage = -1;
        int _visualStage = int.MinValue;
        float _ordAudioClock = -1f;
        bool _ordHeard;
        bool _ordPaused;
        bool _resumeAudio;
        AudioSource? _flightAudio;
        AudioClip? _flightClip;
        bool _flightClipSaved;
        AudioClip? _boomClip;

        float OrdnanceBorn()
        {
            if (Track == null) return Plugin.Clock.Time;
            float born = 0f;
            if (Track.Samples != null && Track.Samples.Count > 0)
                born = Track.Samples[0].Time;
            if (Track.SpawnAt > born) born = Track.SpawnAt;
            return born;
        }

        void TickOrdnanceAudio(ReplaySample s)
        {
            if (!_ordnance || _boomed || IsFlareTrack()) return;
            float now = Plugin.Clock.Time;
            float launch = OrdnanceBorn();
            float age = now - launch;
            float prevClock = _ordAudioClock;
            _ordAudioClock = now;

            var mt = Plugin.FindGameType("Missile");
            object? missile = mt != null ? (GetComponent(mt) ?? GetComponentInChildren(mt, true)) : null;
            if (missile == null || mt == null) return;
            EnsureMotorSlots(missile, mt);

            bool backward = prevClock >= 0f && now + 0.05f < prevClock;
            bool jumped = prevClock >= 0f && Mathf.Abs(now - prevClock) > 1f;
            bool intoIgnition = false;
            if (backward)
            {
                float prevAge = prevClock - launch;
                var was = MissileMotorAudio.At(prevAge, _motorStages, -1, -1);
                var nowCue = MissileMotorAudio.At(age, _motorStages, -1, -1);
                intoIgnition = nowCue.Ignition && !was.Ignition;
            }
            if (jumped || intoIgnition)
            {
                MissileMotorAudio.OnSeek(age, _motorStages, ref _ignitionStage, ref _headStage);
                StopMotorAndFlight();
                _ordPrevT = -1f;
            }

            var cue = MissileMotorAudio.At(age, _motorStages, _ignitionStage, _headStage);
            SyncMotorPresentation(cue);

            bool hear = NearMe(Plugin.OrdMaxAudioDist);
            if (!hear)
            {
                if (_ordHeard || _ordPaused)
                    StopMotorAndFlight();
                return;
            }
            _resumeAudio = false;
            if (Plugin.Clock.Paused)
            {
                if (_ordHeard && !_ordPaused)
                    SetMotorPause(true);
                return;
            }
            if (_ordPaused)
            {
                SetMotorPause(false);
                _resumeAudio = true;
            }

            DriveFlight(missile, mt, s, cue);
            DriveMotorSources(cue);
            _ordHeard = true;
        }

        void EnsureMotorSlots(object missile, Type mt)
        {
            if (ReferenceEquals(_slotsMissile, missile) && _slots != null) return;
            _slotsMissile = missile;
            _visualStage = int.MinValue;
            _ignitionStage = -1;
            _headStage = -1;
            var bind = MissileAudioOf(mt);
            var list = new List<MotorSlot>(4);
            var stages = new List<MissileMotorAudio.Stage>(4);

            var boostType = Plugin.FindGameType("VLSBooster");
            bool attached = false;
            try { attached = bind.Booster?.GetValue(missile) is bool on && on; } catch { }
            if (boostType != null)
            {
                var raw = GetComponentsInChildren(boostType, true);
                for (int i = 0; i < raw.Length; i++)
                {
                    if (raw[i] is Behaviour b) b.enabled = false;
                    if (!attached || raw[i] == null) continue;
                    if (list.Count > 0) continue;
                    AddMotorSlot(raw[i], list, stages);
                }
            }

            try
            {
                var motors = bind.Motors?.GetValue(missile) as Array;
                if (motors != null)
                {
                    foreach (var motor in motors)
                    {
                        if (motor == null) continue;
                        AddMotorSlot(motor, list, stages);
                    }
                }
            }
            catch { }

            _slots = list.ToArray();
            _motorStages = stages.ToArray();
            try { _flightAudio = bind.Flight?.GetValue(missile) as AudioSource; } catch { }
            try
            {
                var clip = bind.Detonate?.GetValue(missile);
                _detonateClip = clip;
                _boomClip = clip as AudioClip;
            }
            catch { }
            RememberFlightClip();
        }

        void AddMotorSlot(object body, List<MotorSlot> list, List<MissileMotorAudio.Stage> stages)
        {
            var fields = MotorAudioOf(body.GetType());
            float delay = FieldFloat(body, fields.Delay, 0f);
            float burn = FieldFloat(body, fields.Burn, 0f);
            float fuel = FieldFloat(body, fields.Fuel, 1f);
            var stage = MissileMotorAudio.FromFuel(delay, burn, fuel);
            if (stage.Delay <= 0f && stage.Burn <= 0f) return;
            var slot = new MotorSlot { Body = body };
            try { slot.Startup = fields.Startup?.GetValue(body) as AudioSource; } catch { }
            try
            {
                object? raw = fields.Sources?.GetValue(body);
                if (raw is AudioSource[] many)
                    slot.Sources = many;
                else if (raw is Array arr)
                {
                    var built = new List<AudioSource>(arr.Length);
                    foreach (var item in arr)
                        if (item is AudioSource src) built.Add(src);
                    slot.Sources = built.ToArray();
                }
            }
            catch { }
            list.Add(slot);
            stages.Add(stage);
        }

        static float FieldFloat(object body, FieldInfo? field, float missing)
        {
            if (field == null) return missing;
            try
            {
                if (field.GetValue(body) is float v) return v;
            }
            catch { }
            return missing;
        }

        void RememberFlightClip()
        {
            if (_flightClipSaved || _flightAudio == null) return;
            var clip = _flightAudio.clip;
            if (clip == null || clip == _boomClip) return;
            _flightClip = clip;
            _flightClipSaved = true;
        }

        bool IsBoomClip(AudioSource src)
        {
            return _boomed && _boomClip != null && src.clip == _boomClip;
        }

        void StopMotorAndFlight()
        {
            var sources = GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (src == null || IsBoomClip(src)) continue;
                src.Stop();
            }
            _ordHeard = false;
            _ordPaused = false;
        }

        void SetMotorPause(bool paused)
        {
            var sources = GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (src == null || IsBoomClip(src)) continue;
                if (paused) src.Pause();
                else src.UnPause();
            }
            _ordPaused = paused;
        }

        void StartAt(AudioSource src, float time)
        {
            src.enabled = true;
            src.mute = false;
            // Pause() makes isPlaying false. UnPause already continued the clip;
            // Play() would start it over, including the ignition at time 0.
            if (src.isPlaying || _resumeAudio) return;
            src.Play();
            if (time > 0.02f && src.clip != null && src.clip.length > time + 0.02f)
                src.time = time;
        }

        void DriveFlight(object missile, Type mt, ReplaySample s, MissileMotorAudio.Cue cue)
        {
            var flight = _flightAudio;
            if (flight == null) return;
            RememberFlightClip();
            if (_flightClipSaved && _flightClip != null && flight.clip == _boomClip)
            {
                flight.Stop();
                flight.clip = _flightClip;
                flight.loop = true;
            }
            if (flight.clip == _boomClip) return;

            var bind = MissileAudioOf(mt);
            float spd = 0f;
            try { if (bind.Speed?.GetValue(missile) is float sf) spd = sf; } catch { }
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
            flight.mute = false;
            flight.enabled = true;
            flight.loop = true;
            float vol = flight.volume;
            if (cue.SnapFlight || vol >= num)
                vol = num;
            else
                vol = Mathf.Min(num, vol + Time.deltaTime);
            flight.volume = vol;
            flight.pitch = pitchRange * num + basePitch;
            float at = 0f;
            if (cue.SnapFlight && flight.clip != null)
                at = MissileMotorAudio.LoopOffset(flight.clip.length, GetInstanceID() + 41);
            StartAt(flight, at);
        }

        void DriveMotorSources(MissileMotorAudio.Cue cue)
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                bool current = i == cue.Stage;
                var startup = slot.Startup;
                if (startup != null)
                {
                    if (current && cue.Ignition)
                    {
                        StartAt(startup, 0f);
                        if (startup.isPlaying || startup.clip == null)
                            _ignitionStage = i;
                    }
                    else if (startup.isPlaying && !(current && _ignitionStage == i))
                        startup.Stop();
                }

                bool sustain = current && cue.Motor != MissileMotorAudio.MotorStart.Silent;
                var sources = slot.Sources;
                for (int s = 0; s < sources.Length; s++)
                {
                    var src = sources[s];
                    if (src == null || ReferenceEquals(src, startup)) continue;
                    if (!sustain)
                    {
                        if (src.loop && src.isPlaying) src.Stop();
                        continue;
                    }
                    if (cue.Motor == MissileMotorAudio.MotorStart.FromHead && _headStage != i)
                    {
                        StartAt(src, 0f);
                        continue;
                    }
                    if (src.isPlaying) continue;
                    float len = src.clip != null ? src.clip.length : 0f;
                    if (!src.loop)
                    {
                        if (len < MissileMotorAudio.SustainedOneShot) continue;
                        float shot = MissileMotorAudio.OneShotTime(len, cue.SinceActivate);
                        if (shot < 0f) continue;
                        float skip = MissileMotorAudio.LoopHeadSkip;
                        if (shot < skip) shot = skip < len ? skip : 0f;
                        StartAt(src, shot);
                        continue;
                    }
                    StartAt(src, MissileMotorAudio.LoopOffset(len, GetInstanceID() + i * 17 + s * 131));
                }
                if (sustain)
                    _headStage = i;
            }
        }

        void SyncMotorPresentation(MissileMotorAudio.Cue cue)
        {
            int lit = cue.Motor == MissileMotorAudio.MotorStart.Silent ? -1 : cue.Stage;
            if (lit == _visualStage) return;
            _visualStage = lit;
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
                SetMotorLit(_slots[i].Body, i == lit);
        }

        void SetMotorLit(object? body, bool on)
        {
            if (body == null) return;
            var fields = MotorAudioOf(body.GetType());
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var parts = fields.Particles?.GetValue(body) as Array;
                if (parts != null)
                {
                    foreach (var p in parts)
                    {
                        if (on) PlayMotorPs(p);
                        else StopMotorPs(p);
                    }
                }
            }
            catch { }
            try
            {
                var lights = fields.Lights?.GetValue(body) as Array;
                if (lights != null)
                {
                    foreach (var l in lights)
                    {
                        if (l is Behaviour lb) lb.enabled = on;
                        else
                        {
                            try { l?.GetType().GetProperty("enabled")?.SetValue(l, on); } catch { }
                        }
                    }
                }
            }
            catch { }
            if (!on) return;
            try
            {
                var trails = fields.Trails?.GetValue(body) as Array;
                if (trails == null) return;
                foreach (var tr in trails)
                {
                    if (tr == null) continue;
                    if (tr is Behaviour tb) tb.enabled = true;
                    tr.GetType().GetMethod("StartTrail", flags)?.Invoke(tr, null);
                }
            }
            catch { }
        }

        static void PlayMotorPs(object? ps)
        {
            if (ps == null) return;
            try
            {
                var t = ps.GetType();
                if (ps is Behaviour b) b.enabled = true;
                var play = t.GetMethod("Play", new[] { typeof(bool) })
                           ?? t.GetMethod("Play", Type.EmptyTypes);
                if (play == null) return;
                play.Invoke(ps, play.GetParameters().Length == 1 ? new object[] { true } : null);
            }
            catch { }
        }

        static void StopMotorPs(object? ps)
        {
            if (ps == null) return;
            try
            {
                var t = ps.GetType();
                var stop = t.GetMethod("Stop", new[] { typeof(bool) })
                           ?? t.GetMethod("Stop", Type.EmptyTypes);
                if (stop == null) return;
                if (stop.GetParameters().Length == 1) stop.Invoke(ps, new object[] { true });
                else stop.Invoke(ps, null);
            }
            catch { }
        }
    }
}
