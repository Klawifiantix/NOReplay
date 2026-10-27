using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        sealed class AudioSrcProps
        {
            public PropertyInfo? Clip;
            public PropertyInfo? Loop;
            public PropertyInfo? Mute;
            public PropertyInfo? Enabled;
            public PropertyInfo? IgnoreListenerPause;
            public PropertyInfo? SpatialBlend;
            public PropertyInfo? MinDistance;
            public PropertyInfo? MaxDistance;
            public PropertyInfo? DopplerLevel;
            public PropertyInfo? Priority;
            public PropertyInfo? Pitch;
            public PropertyInfo? Volume;
            public PropertyInfo? IsPlaying;
            public MethodInfo? Play;
        }

        static readonly Dictionary<Type, AudioSrcProps> _audioProps = new Dictionary<Type, AudioSrcProps>();
        static readonly Dictionary<(Type, string), FieldInfo?> _fieldCache = new Dictionary<(Type, string), FieldInfo?>();
        static readonly Dictionary<int, float> _origSourceVolume = new Dictionary<int, float>();
        static readonly Dictionary<int, float> _origSourcePitch = new Dictionary<int, float>();

        static AudioSrcProps PropsFor(Type st)
        {
            if (_audioProps.TryGetValue(st, out var p)) return p;
            p = new AudioSrcProps
            {
                Clip = st.GetProperty("clip"),
                Loop = st.GetProperty("loop"),
                Mute = st.GetProperty("mute"),
                Enabled = st.GetProperty("enabled"),
                IgnoreListenerPause = st.GetProperty("ignoreListenerPause"),
                SpatialBlend = st.GetProperty("spatialBlend"),
                MinDistance = st.GetProperty("minDistance"),
                MaxDistance = st.GetProperty("maxDistance"),
                DopplerLevel = st.GetProperty("dopplerLevel"),
                Priority = st.GetProperty("priority"),
                Pitch = st.GetProperty("pitch"),
                Volume = st.GetProperty("volume"),
                IsPlaying = st.GetProperty("isPlaying"),
                Play = st.GetMethod("Play", Type.EmptyTypes),
            };
            _audioProps[st] = p;
            return p;
        }

        static void RememberSource(object? src)
        {
            if (src is not UnityEngine.Object uo) return;
            int id = uo.GetInstanceID();
            if (_origSourceVolume.ContainsKey(id)) return;
            try
            {
                var p = PropsFor(src.GetType());
                if (p.Volume?.GetValue(src) is float vol) _origSourceVolume[id] = vol;
                if (p.Pitch?.GetValue(src) is float pitch) _origSourcePitch[id] = pitch;
            }
            catch { }
        }

        static float OrigVolume(object? src, float fallback)
        {
            if (src is UnityEngine.Object uo && _origSourceVolume.TryGetValue(uo.GetInstanceID(), out float v) && v > 0.001f)
                return v;
            return fallback;
        }

        static float OrigPitch(object? src, float fallback)
        {
            if (src is UnityEngine.Object uo && _origSourcePitch.TryGetValue(uo.GetInstanceID(), out float v) && v > 0.001f)
                return v;
            return fallback;
        }

        static void StopEngineSrc(object? src)
        {
            if (src == null) return;
            try
            {
                var p = PropsFor(src.GetType());
                p.Volume?.SetValue(src, 0f);
                src.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(src, null);
            }
            catch { }
        }

        static void DriveEngineSrc(Component host, object? src, object? clip, float vol, float pitch)
        {
            if (host is Behaviour bh && bh.enabled) bh.enabled = false;
            if (src == null) return;
            RememberSource(src);
            if (Plugin.Clock.Paused) vol = 0f;
            bool followed = host != null && Plugin.IsFollowed(host.gameObject);
            if (vol <= 0.01f || (host != null && !followed && !Plugin.AudioNear(host.gameObject, 550f)))
            {
                StopEngineSrc(src);
                return;
            }
            var p = PropsFor(src.GetType());
            try
            {
                if (clip != null) p.Clip?.SetValue(src, clip);
                p.Loop?.SetValue(src, true);
                p.Mute?.SetValue(src, false);
                p.Enabled?.SetValue(src, true);
                try { p.IgnoreListenerPause?.SetValue(src, true); } catch { }
                p.SpatialBlend?.SetValue(src, 1f);
                p.Priority?.SetValue(src, followed ? 0 : 64);
                p.Pitch?.SetValue(src, pitch);
                p.Volume?.SetValue(src, vol);
                object? playing = p.IsPlaying?.GetValue(src);
                if (!(playing is bool on && on))
                    p.Play?.Invoke(src, null);
            }
            catch { }
        }

        static object? FieldOn(Component c, string name)
        {
            var t = c.GetType();
            var key = (t, name);
            if (!_fieldCache.TryGetValue(key, out var f))
            {
                f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fieldCache[key] = f;
            }
            return f?.GetValue(c);
        }


        static void MuteOn(Component host)
        {
            if (host == null) return;
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var c in host.GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    RememberSource(c);
                    at.GetProperty("playOnAwake")?.SetValue(c, false);
                    at.GetProperty("volume")?.SetValue(c, 0f);
                    stop?.Invoke(c, null);
                }
                catch { }
            }
        }

        void SpatializeSources()
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            foreach (var c in GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    RememberSource(c);
                    at.GetProperty("playOnAwake")?.SetValue(c, false);
                    at.GetProperty("spatialBlend")?.SetValue(c, 1f);
                }
                catch { }
            }
        }

        void MuteAllSources()
        {
            SpatializeSources();
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var c in GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    RememberSource(c);
                    at.GetProperty("playOnAwake")?.SetValue(c, false);
                    at.GetProperty("volume")?.SetValue(c, 0f);
                    stop?.Invoke(c, null);
                }
                catch { }
            }
        }

        internal void SilenceSources()
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? Plugin.FindGameType("AudioSource");
            if (at == null) return;
            var stop = at.GetMethod("Stop", Type.EmptyTypes);
            foreach (var c in GetComponentsInChildren(at, true))
            {
                if (c == null) continue;
                try
                {
                    RememberSource(c);
                    at.GetProperty("volume")?.SetValue(c, 0f);
                    stop?.Invoke(c, null);
                }
                catch { }
            }
        }


    }
}