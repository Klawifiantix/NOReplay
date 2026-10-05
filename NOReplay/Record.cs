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
            Plugin.Log.LogInfo("Aufnahme bereit. F8 startet und stoppt.");
        }

        public static void Toggle()
        {
            if (Plugin.CurrentDoc != null)
            {
                Plugin.Log.LogInfo("F8 ignoriert, Replay läuft.");
                return;
            }
            if (Active) Stop();
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
                host.AddComponent<RecordEngine>();
                Active = true;
                Plugin.Log.LogInfo("Aufnahme gestartet (F8).");
            }
            catch (Exception ex)
            {
                Active = false;
                Plugin.Log.LogError("Aufnahme Start: " + ex);
            }
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            var host = Host;
            Host = null;
            if (host != null) UnityEngine.Object.Destroy(host);
            var found = UnityEngine.Object.FindObjectsOfType<RecordObject>();
            for (int i = 0; i < found.Length; i++)
                if (found[i] != null) UnityEngine.Object.Destroy(found[i]);
            var flares = UnityEngine.Object.FindObjectsOfType<RecordFlare>();
            for (int i = 0; i < flares.Length; i++)
                if (flares[i] != null) UnityEngine.Object.Destroy(flares[i]);
            Plugin.Log.LogInfo("Aufnahme gestoppt (F8).");
        }

        sealed class RecLabel : MonoBehaviour
        {
            void Update()
            {
                if (Plugin.CurrentDoc != null && Active) Stop();
            }

            void OnGUI()
            {
                if (!Active) return;
                var style = new GUIStyle(GUI.skin.label) { fontSize = 22 };
                style.normal.textColor = Color.green;
                GUI.Label(new Rect(Screen.width - 78f, Screen.height - 42f, 60f, 28f), "Rec", style);
            }
        }
    }
}