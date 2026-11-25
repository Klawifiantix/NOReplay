using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using NuclearOption.Networking;
using UnityEngine;

namespace NOReplay
{
    public static class Record
    {
        public static bool Active;
        public static bool Manual;
        public static ManualLogSource? Log;
        public static GameObject? Host;
        public static BasePlayer? Player;
        static RecLabel? Hud;
        public static readonly Dictionary<string, Dictionary<string, string[]>> UnitInfo = new()
        {
            { "aircraft", new Dictionary<string, string[]>() },
            { "missiles", new Dictionary<string, string[]>() },
            { "ships", new Dictionary<string, string[]>() },
            { "vehicles", new Dictionary<string, string[]>() }
        };

        public static void Init(ConfigFile config)
        {
            Log = BepInEx.Logging.Logger.CreateLogSource("NOReplay.Record");
            try { RecordConfig.InitSettings(config); }
            catch (Exception ex) { Plugin.Log.LogError("Record-Config: " + ex); }
            if (Hud == null)
            {
                var go = new GameObject("NOReplay_Rec");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                Hud = go.AddComponent<RecLabel>();
            }
            try { RecordListener.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordListener.Install: " + ex); }
            try { RecordParts.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordParts.Install: " + ex); }
            try { RecordWreck.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordWreck.Install: " + ex); }
            try { RecordTrap.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordTrap.Install: " + ex); }
            try { RecordLasers.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordLasers.Install: " + ex); }
            try { RecordChutes.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordChutes.Install: " + ex); }
            try { RecordBay.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordBay.Install: " + ex); }
            try { RecordBuilding.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordBuilding.Install: " + ex); }
            try { RecordShock.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordShock.Install: " + ex); }
            try { RecordFeed.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordFeed.Install: " + ex); }
            try { NukeTime.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("NukeTime.Install: " + ex); }
            try { RecordSession.Install(); }
            catch (Exception ex) { Plugin.Log.LogError("RecordSession.Install: " + ex); }
            Plugin.Log.LogInfo("Recording ready. " + RecordKeyLabel() + " starts and stops.");
        }

        public static void Toggle()
        {
            if (Plugin.CurrentDoc != null)
            {
                Plugin.Log.LogInfo(RecordKeyLabel() + " ignored, a replay is running.");
                return;
            }
            if (Active) Stop("manual");
            else Start();
        }

        public static void Start()
        {
            if (Active || Plugin.CurrentDoc != null) return;
            try
            {
                var host = new GameObject("NOReplay_Record");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;
                Host = host;
                var engine = host.AddComponent<RecordEngine>();
                // Capture writes the t=0 turret baseline. Flush drops that sample
                // while Active is still false, so arm recording first.
                RecordParts.Reset();
                Active = true;
                engine.CaptureExistingUnits();
                engine.ArmOpeningCapture();
                Plugin.Log.LogInfo("Recording started (" + RecordKeyLabel() + ").");
            }
            catch (Exception ex)
            {
                Active = false;
                Plugin.Log.LogError("Recording start: " + ex);
            }
        }

        public static void MaybeAutoStart()
        {
            if (Active || Plugin.CurrentDoc != null) return;
            if (RecordConfig.AutoStartRecording == null || !RecordConfig.AutoStartRecording.Value) return;
            try
            {
                var state = GameManager.gameState;
                if (state != GameState.SinglePlayer && state != GameState.Multiplayer) return;
            }
            catch { return; }
            Plugin.Log.LogInfo("Recording started: mission load");
            Start();
        }

        public static void Stop(string reason = "manual")
        {
            if (!Active) return;
            try { RecordLasers.Flush(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Laser-Record flush: " + ex.Message); }
            try { RecordChutes.Flush(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Chute-Record flush: " + ex.Message); }
            try { RecordBay.Flush(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Bay-Record flush: " + ex.Message); }
            Active = false;
            RecordParts.Reset();
            var host = Host;
            Host = null;
            if (host != null) UnityEngine.Object.Destroy(host);
            var found = UnityEngine.Object.FindObjectsOfType<RecordObject>();
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null) UnityEngine.Object.Destroy(found[i]);
            var flares = UnityEngine.Object.FindObjectsOfType<RecordFlare>();
            for (int i = 0; i < flares.Length; i++)
                if (flares[i] != null) UnityEngine.Object.Destroy(flares[i]);
            var turrets = UnityEngine.Object.FindObjectsOfType<RecordTurrets>();
            for (int i = 0; i < turrets.Length; i++)
                if (turrets[i] != null) UnityEngine.Object.Destroy(turrets[i]);
            Plugin.Log.LogInfo("Recording stopped: " + reason);
            Plugin.Log.LogInfo("Recording stopped (" + RecordKeyLabel() + ").");
        }

        static string RecordKeyLabel()
        {
            try
            {
                var entry = RecordConfig.StartStopRecordingKey;
                if (entry != null) return entry.Value.ToString();
            }
            catch { }
            return "F8";
        }

        sealed class RecLabel : MonoBehaviour
        {
            const float HoldSeconds = 3f;
            const float FadeSeconds = 1f;
            const float FontSize = 22f;
            GUIStyle? _style;
            bool _recording;
            bool _stop;
            bool _draw;
            float _phaseAt;

            void Awake()
            {
                useGUILayout = false;
            }

            void Update()
            {
                if (Plugin.CurrentDoc != null && Active) Stop("replay");
                bool recording = Active;
                if (recording != _recording)
                {
                    _recording = recording;
                    _phaseAt = Time.unscaledTime;
                    _stop = !recording && WantFade() && WantIndicator();
                }
                else if (_stop && Time.unscaledTime - _phaseAt >= HoldSeconds + FadeSeconds)
                    _stop = false;
                _draw = ShouldDraw();
            }

            void OnGUI()
            {
                if (!_draw) return;
                float alpha = LabelAlpha();
                if (alpha <= 0f) return;
                if (_style == null)
                    _style = new GUIStyle(GUI.skin.label) { fontSize = (int)FontSize };
                Color color = _stop ? new Color(1f, 0f, 0f, alpha) : RecColor(alpha);
                _style.normal.textColor = color;
                float x = Unit(RecordConfig.RecordingIndicatorX, RecordConfig.DefaultRecordingIndicatorX) * Screen.width;
                float y = Unit(RecordConfig.RecordingIndicatorY, RecordConfig.DefaultRecordingIndicatorY) * Screen.height;
                GUI.Label(new Rect(x, y, 90f, 28f), _stop ? "STOP" : "REC", _style);
            }

            bool ShouldDraw()
            {
                if (!WantIndicator()) return false;
                if (_recording) return !WantFade() || Time.unscaledTime - _phaseAt < HoldSeconds + FadeSeconds;
                return _stop && WantFade() && Time.unscaledTime - _phaseAt < HoldSeconds + FadeSeconds;
            }

            float LabelAlpha()
            {
                float opacity = Unit(RecordConfig.TextColorA, RecordConfig.DefaultTextColorA);
                if (!WantFade()) return opacity;
                float age = Time.unscaledTime - _phaseAt;
                if (age <= HoldSeconds) return opacity;
                if (age >= HoldSeconds + FadeSeconds) return 0f;
                return opacity * (1f - (age - HoldSeconds) / FadeSeconds);
            }

            static Color RecColor(float alpha)
            {
                return new Color(
                    Unit(RecordConfig.TextColorR, RecordConfig.DefaultTextColorR),
                    Unit(RecordConfig.TextColorG, RecordConfig.DefaultTextColorG),
                    Unit(RecordConfig.TextColorB, RecordConfig.DefaultTextColorB),
                    alpha);
            }

            static bool WantIndicator()
            {
                var entry = RecordConfig.EnableRecordingIndicator;
                if (entry == null) return RecordConfig.DefaultEnableRecordingIndicator;
                try { return entry.Value; }
                catch { return RecordConfig.DefaultEnableRecordingIndicator; }
            }

            static bool WantFade()
            {
                var entry = RecordConfig.Fade;
                if (entry == null) return RecordConfig.DefaultFade;
                try { return entry.Value; }
                catch { return RecordConfig.DefaultFade; }
            }

            static float Unit(ConfigEntry<float>? entry, float fallback)
            {
                if (entry == null) return fallback;
                try
                {
                    float value = entry.Value;
                    if (value < 0f || value > 1f) return fallback;
                    return value;
                }
                catch { return fallback; }
            }
        }
    }
}