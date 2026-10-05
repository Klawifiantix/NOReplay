using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        void CacheShipFx()
        {
            var tProp = Plugin.FindGameType("ShipPropulsion");
            var tShip = Plugin.FindGameType("Ship");
            if (tProp != null)
            {
                var raw = GetComponentsInChildren(tProp, true);
                var list = new System.Collections.Generic.List<Component>(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                    if (raw[i] is Component c) list.Add(c);
                _shipProps = list.ToArray();
            }
            if (tShip != null)
                _shipUnit = GetComponent(tShip) as Component ?? GetComponentInChildren(tShip) as Component;
        }

        static void PlayLoop(object? src, float volume, float pitch)
        {
            if (src == null) return;
            var st = src.GetType();
            st.GetProperty("loop")?.SetValue(src, true);
            st.GetProperty("mute")?.SetValue(src, false);
            st.GetProperty("enabled")?.SetValue(src, true);
            try { st.GetProperty("ignoreListenerPause")?.SetValue(src, true); } catch { }
            st.GetProperty("spatialBlend")?.SetValue(src, 1f);
            st.GetProperty("minDistance")?.SetValue(src, 20f);
            st.GetProperty("maxDistance")?.SetValue(src, 2500f);
            st.GetProperty("pitch")?.SetValue(src, pitch);
            st.GetProperty("volume")?.SetValue(src, volume);
            object? playing = st.GetProperty("isPlaying")?.GetValue(src);
            if (!(playing is bool on && on))
                st.GetMethod("Play", Type.EmptyTypes)?.Invoke(src, null);
        }

        void CacheVehicleAudio()
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] idleNames = { "engineIdleSound", "idleSound", "engineSound" };
            string[] driveNames = { "engineDriveSound", "driveSound", "moveSound", "trackSound", "rumbleSound" };
            foreach (var beh in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (beh == null) continue;
                string tn = beh.GetType().Name;
                if (tn != "GroundVehicle" && tn != "Vehicle" && tn != "Unit") continue;
                var t = beh.GetType();
                if (_vehIdle == null)
                {
                    for (int i = 0; i < idleNames.Length; i++)
                    {
                        var v = t.GetField(idleNames[i], flags)?.GetValue(beh);
                        if (v != null) { _vehIdle = v; break; }
                    }
                }
                if (_vehDrive == null)
                {
                    for (int i = 0; i < driveNames.Length; i++)
                    {
                        var v = t.GetField(driveNames[i], flags)?.GetValue(beh);
                        if (v != null) { _vehDrive = v; break; }
                    }
                }
            }
        }

        void DriveVehicleAudio(float spd)
        {
            float throttle = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd / 16f);
            if (throttle < 0.12f)
            {
                PlayLoop(_vehIdle, Plugin.Clock.Paused ? 0f : 0.35f, 0.9f);
                PlayLoop(_vehDrive, 0f, 1f);
            }
            else
            {
                PlayLoop(_vehIdle, 0.12f, 0.95f);
                PlayLoop(_vehDrive, 0.25f + throttle * 0.55f, 0.85f + throttle * 0.4f);
            }
        }

        void DriveShipAudio(float spd)
        {
            float throttle = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd / 18f);
            float pitch = Mathf.Min(0.55f + throttle * 0.7f, 1.4f);
            float vol = Plugin.Clock.Paused ? 0f : (0.15f + throttle * 0.55f);
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] propFields = { "engineSound", "thrustSound", "propSound", "turbineSound" };
            for (int i = 0; i < _shipProps.Length; i++)
            {
                var c = _shipProps[i];
                if (c == null) continue;
                var t = c.GetType();
                for (int k = 0; k < propFields.Length; k++)
                    PlayLoop(t.GetField(propFields[k], flags)?.GetValue(c), vol, pitch);
            }
            if (_shipUnit == null) return;
            float waterVol = Plugin.Clock.Paused ? 0f : Mathf.Clamp01(spd * spd * 0.0004f + throttle * 0.25f);
            float hullVol = Plugin.Clock.Paused ? 0f : (0.2f + Mathf.Clamp01(spd * 0.03f) * 0.4f);
            string[] arrNames = { "waterSounds", "hullSounds", "engineSounds", "wakeSounds" };
            var st = _shipUnit.GetType();
            for (int i = 0; i < arrNames.Length; i++)
                SetArrayVol(st.GetField(arrNames[i], flags)?.GetValue(_shipUnit), i < 2 ? (i == 0 ? waterVol : hullVol) : vol);
        }

        static void SetArrayVol(object? arr, float vol)
        {
            if (arr is not Array a) return;
            for (int i = 0; i < a.Length; i++)
                PlayLoop(a.GetValue(i), vol, 1f);
        }


    }
}