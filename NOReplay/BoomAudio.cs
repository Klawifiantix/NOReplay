using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Plays ExplosionAudio the way the game does, on the replay clock.
    /// The game's Start registers the clip with ExplosionAudioManager, which
    /// advances on Time.deltaTime and is skipped for anything farther than
    /// 700 m. This keeps the clips and the 340 m/s front, and does not shake
    /// the camera or apply blast force.
    /// </summary>
    internal static class BoomAudio
    {
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly List<Voice> Voices = new();
        static readonly HashSet<int> Allowed = new();
        static readonly HashSet<int> Armed = new();
        internal static float PreferBorn = -1f;
        static float _prev;
        static int _seek = -1;
        static bool _havePrev;
        static bool _held;

        sealed class Voice
        {
            public AudioSource Source = null!;
            public AudioLowPassFilter? Filter;
            public float Yield;
            public float Born;
            public float Delay;
            public string Name = "";
            public bool Played;
            public bool SeekSkipped;
            public bool LoggedPlay;
            public bool LoggedSkip;
        }

        public static void Clear()
        {
            for (int i = 0; i < Voices.Count; i++)
                Stop(Voices[i]);
            Voices.Clear();
            Allowed.Clear();
            Armed.Clear();
            _havePrev = false;
            _held = false;
            _seek = -1;
            PreferBorn = -1f;
        }

        public static bool Allows(object? source)
        {
            return source is UnityEngine.Object obj && Allowed.Contains(obj.GetInstanceID());
        }

        public static float HoldSeconds(GameObject root)
        {
            if (root == null) return 0f;
            float speed = 1f;
            if (Plugin.Clock != null)
            {
                speed = Mathf.Abs(Plugin.Clock.Speed);
                if (speed < 0.25f) speed = 0.25f;
            }
            float hold = 0f;
            for (int i = 0; i < Voices.Count; i++)
            {
                var voice = Voices[i];
                if (voice.Source == null) continue;
                if (!On(voice.Source.transform, root.transform)) continue;
                float real = (voice.Delay + 12f) / speed;
                if (real > hold) hold = real;
            }
            return hold;
        }

        public static void Arm(Component? explosion, float born)
        {
            if (explosion == null || Plugin.CurrentDoc == null) return;
            if (!Armed.Add(explosion.GetInstanceID())) return;
            var field = explosion.GetType().GetField("explosionSounds", Flags);
            if (field?.GetValue(explosion) is not IList sounds) return;
            if (!Plugin.ListenPoint(out Vector3 ear)) ear = explosion.transform.position;
            AudioLowPassFilter? filter = null;
            try
            {
                filter = explosion.GetComponent<AudioLowPassFilter>();
                if (filter == null) filter = explosion.gameObject.AddComponent<AudioLowPassFilter>();
            }
            catch { }
            for (int i = 0; i < sounds.Count; i++)
            {
                object? sound = sounds[i];
                if (sound == null) continue;
                var kind = sound.GetType();
                if (kind.GetField("source", Flags)?.GetValue(sound) is not AudioSource source) continue;
                if (kind.GetField("clips", Flags)?.GetValue(sound) is not AudioClip[] clips || clips.Length == 0) continue;
                float yield = 0f;
                if (kind.GetField("yield", Flags)?.GetValue(sound) is float y) yield = y;
                float dist = Vector3.Distance(ear, source.transform.position);
                float max = source.maxDistance;
                string name = ClipName(source, clips);
                if (!BoomDelay.InRangeAtDetonation(dist, max))
                {
                    Plugin.Log?.LogInfo(Line(name, dist, max, 0f, "skipped range"));
                    continue;
                }
                int pick = clips.Length == 1 ? 0 : UnityEngine.Random.Range(0, clips.Length);
                source.clip = clips[pick];
                source.pitch += UnityEngine.Random.Range(-0.2f, 0.2f);
                source.dopplerLevel = 0f;
                source.spatialBlend = 1f;
                Allowed.Add(source.GetInstanceID());
                float delay = BoomDelay.Seconds(dist, yield);
                Voices.Add(new Voice
                {
                    Source = source,
                    Filter = filter,
                    Yield = yield,
                    Born = born,
                    Delay = delay,
                    Name = name
                });
                Plugin.Log?.LogInfo(Line(name, dist, max, delay, "armed"));
            }
        }

        public static void Tick(float time, bool paused, float speed, int seekSerial)
        {
            if (Plugin.CurrentDoc == null)
            {
                if (Voices.Count > 0) Clear();
                return;
            }
            if (!_havePrev)
            {
                _havePrev = true;
                _prev = time;
                _seek = seekSerial;
            }
            bool seek = seekSerial != _seek;
            if (seek) _seek = seekSerial;
            bool reverse = speed < 0f || time < _prev - 0.0001f;
            if (paused)
            {
                Hold(true);
                return;
            }
            if (_held) Hold(false);
            if (reverse)
            {
                Silence(time);
                _prev = time;
                return;
            }
            if (!Plugin.ListenPoint(out Vector3 ear))
            {
                _prev = time;
                return;
            }
            for (int i = Voices.Count - 1; i >= 0; i--)
            {
                var voice = Voices[i];
                if (voice.Source == null)
                {
                    Voices.RemoveAt(i);
                    continue;
                }
                float dist = Vector3.Distance(ear, voice.Source.transform.position);
                float previousDelay = voice.Delay;
                voice.Delay = BoomDelay.Seconds(dist, voice.Yield);
                float max = voice.Source.maxDistance;
                if (seek)
                {
                    Stop(voice);
                    voice.Played = false;
                    voice.LoggedPlay = false;
                    bool late = voice.Born + voice.Delay <= time;
                    voice.SeekSkipped = late;
                    if (late && !voice.LoggedSkip)
                    {
                        voice.LoggedSkip = true;
                        Plugin.Log?.LogInfo(Line(voice.Name, dist, max, voice.Delay, "skipped seek"));
                    }
                    else if (!late)
                        voice.LoggedSkip = false;
                    continue;
                }
                if (voice.Played || voice.SeekSkipped) continue;
                if (!BoomDelay.Reached(voice.Born, previousDelay, voice.Delay, _prev, time, false, false, false))
                    continue;
                Play(voice, voice.Delay);
                voice.Played = true;
                if (!voice.LoggedPlay)
                {
                    voice.LoggedPlay = true;
                    Plugin.Log?.LogInfo(Line(voice.Name, dist, max, voice.Delay, "played"));
                }
            }
            _prev = time;
        }

        static void Play(Voice voice, float travel)
        {
            var source = voice.Source;
            if (source == null) return;
            source.bypassListenerEffects = travel > 0.1f;
            if (voice.Filter != null)
            {
                float cutoff = travel <= 0.0001f ? 22000f : 22000f / travel;
                if (cutoff < 1000f) cutoff = 1000f;
                if (cutoff > 22000f) cutoff = 22000f;
                voice.Filter.cutoffFrequency = cutoff;
            }
            source.mute = false;
            source.Play();
        }

        static void Silence(float time)
        {
            for (int i = 0; i < Voices.Count; i++)
            {
                var voice = Voices[i];
                if (voice.Source == null) continue;
                Stop(voice);
                voice.Played = false;
                voice.LoggedPlay = false;
                voice.SeekSkipped = voice.Born + voice.Delay <= time;
            }
        }

        static void Hold(bool pause)
        {
            _held = pause;
            for (int i = 0; i < Voices.Count; i++)
            {
                var source = Voices[i].Source;
                if (source == null || !Voices[i].Played) continue;
                if (pause) source.Pause();
                else source.UnPause();
            }
        }

        static void Stop(Voice voice)
        {
            if (voice.Source == null) return;
            voice.Source.Stop();
        }

        static bool On(Transform t, Transform root)
        {
            return t == root || t.IsChildOf(root);
        }

        static string ClipName(AudioSource source, AudioClip[] clips)
        {
            if (source.clip != null && !string.IsNullOrEmpty(source.clip.name)) return source.clip.name;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && !string.IsNullOrEmpty(clips[i].name)) return clips[i].name;
            return source.gameObject.name;
        }

        static string Line(string name, float dist, float max, float delay, string why)
        {
            return "Boom-Sound " + name
                + " dist=" + dist.ToString("0")
                + " max=" + max.ToString("0")
                + " delay=" + delay.ToString("0.00")
                + " " + why;
        }
    }
}
