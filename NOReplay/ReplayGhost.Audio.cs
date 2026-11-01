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
            public MethodInfo? Stop;
            public Action<object, float>? WritePitch;
            public Action<object, float>? WriteVolume;
            public Action<object, float>? WriteSpatial;
            public Action<object, bool>? WriteLoop;
            public Action<object, bool>? WriteMute;
            public Action<object, bool>? WriteEnabled;
            public Action<object, bool>? WriteIgnorePause;
            public Action<object, int>? WritePriority;
            public Func<object, bool>? ReadPlaying;
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
                Stop = st.GetMethod("Stop", Type.EmptyTypes),
            };
            p.WritePitch = FieldFast.FloatPropSetter(p.Pitch);
            p.WriteVolume = FieldFast.FloatPropSetter(p.Volume);
            p.WriteSpatial = FieldFast.FloatPropSetter(p.SpatialBlend);
            p.WriteLoop = FieldFast.BoolPropSetter(p.Loop);
            p.WriteMute = FieldFast.BoolPropSetter(p.Mute);
            p.WriteEnabled = FieldFast.BoolPropSetter(p.Enabled);
            p.WriteIgnorePause = FieldFast.BoolPropSetter(p.IgnoreListenerPause);
            p.WritePriority = FieldFast.IntPropSetter(p.Priority);
            p.ReadPlaying = FieldFast.BoolPropGetter(p.IsPlaying);
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
                if (p.WriteVolume != null) p.WriteVolume(src, 0f);
                else p.Volume?.SetValue(src, 0f);
                p.Stop?.Invoke(src, null);
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
                SetBool(p.WriteLoop, p.Loop, src, true);
                SetBool(p.WriteMute, p.Mute, src, false);
                SetBool(p.WriteEnabled, p.Enabled, src, true);
                try { SetBool(p.WriteIgnorePause, p.IgnoreListenerPause, src, true); } catch { }
                SetFloat(p.WriteSpatial, p.SpatialBlend, src, 1f);
                SetInt(p.WritePriority, p.Priority, src, followed ? 0 : 64);
                SetFloat(p.WritePitch, p.Pitch, src, pitch);
                SetFloat(p.WriteVolume, p.Volume, src, vol);
                bool playing = p.ReadPlaying != null
                    ? p.ReadPlaying(src)
                    : p.IsPlaying?.GetValue(src) is bool on && on;
                if (!playing)
                    p.Play?.Invoke(src, null);
            }
            catch { }
        }

        static void SetFloat(Action<object, float>? fast, PropertyInfo? prop, object src, float value)
        {
            if (fast != null) fast(src, value);
            else prop?.SetValue(src, value);
        }

        static void SetBool(Action<object, bool>? fast, PropertyInfo? prop, object src, bool value)
        {
            if (fast != null) fast(src, value);
            else prop?.SetValue(src, value);
        }

        static void SetInt(Action<object, int>? fast, PropertyInfo? prop, object src, int value)
        {
            if (fast != null) fast(src, value);
            else prop?.SetValue(src, value);
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