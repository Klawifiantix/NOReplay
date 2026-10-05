using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static void DriveEngineSrc(Component host, object? src, object? clip, float vol, float pitch)
        {
            if (host is Behaviour bh && bh.enabled) bh.enabled = false;
            if (src == null) return;
            if (Plugin.Clock.Paused) vol = 0f;
            bool followed = host != null && Plugin.IsFollowed(host.gameObject);
            if (!followed && host != null && vol > 0f && !Plugin.AudioNear(host.gameObject, 550f))
                vol = 0f;
            var st = src.GetType();
            try
            {
                if (clip != null) st.GetProperty("clip")?.SetValue(src, clip);
                st.GetProperty("loop")?.SetValue(src, true);
                st.GetProperty("mute")?.SetValue(src, false);
                st.GetProperty("enabled")?.SetValue(src, true);
                try { st.GetProperty("ignoreListenerPause")?.SetValue(src, true); } catch { }
                st.GetProperty("spatialBlend")?.SetValue(src, followed ? 0f : 1f);
                st.GetProperty("minDistance")?.SetValue(src, 12f);
                st.GetProperty("maxDistance")?.SetValue(src, 700f);
                st.GetProperty("dopplerLevel")?.SetValue(src, 0f);
                st.GetProperty("priority")?.SetValue(src, followed ? 0 : 64);
                st.GetProperty("pitch")?.SetValue(src, pitch);
                st.GetProperty("volume")?.SetValue(src, vol);
                object? playing = st.GetProperty("isPlaying")?.GetValue(src);
                if (!(playing is bool on && on) && vol > 0.01f)
                    st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
            }
            catch { }
        }

        static object? FieldOn(Component c, string name)
        {
            return c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c);
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
                    at.GetProperty("playOnAwake")?.SetValue(c, false);
                    at.GetProperty("spatialBlend")?.SetValue(c, 1f);
                    at.GetProperty("dopplerLevel")?.SetValue(c, 0f);
                    at.GetProperty("minDistance")?.SetValue(c, 15f);
                    at.GetProperty("maxDistance")?.SetValue(c, 700f);
                    at.GetProperty("rolloffMode")?.SetValue(c, 1);
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
                    at.GetProperty("playOnAwake")?.SetValue(c, false);
                    at.GetProperty("volume")?.SetValue(c, 0f);
                    stop?.Invoke(c, null);
                }
                catch { }
            }
        }


    }
}