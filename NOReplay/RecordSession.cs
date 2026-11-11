using System;
using System.Reflection;
using HarmonyLib;

namespace NOReplay
{
    /// <summary>
    /// Closes the ACMI file when the mission restarts or the world unloads.
    /// Victory and defeat stay in the scene, so they do not stop the file.
    /// The record host is DontDestroyOnLoad, so a scene change does not stop it
    /// on its own.
    /// </summary>
    internal static class RecordSession
    {
        static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            int n = 0;
            n += Patch("MissionManager", "RestartMission", nameof(OnRestart), true);
            n += Patch("MissionManager", "StartMission", nameof(OnStarted), false);
            Plugin.Log?.LogInfo("Record-Session hooks=" + n);
        }

        static int Patch(string typeName, string method, string hook, bool prefix)
        {
            var type = Plugin.FindGameType(typeName);
            var found = type?.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (found == null)
            {
                Plugin.Log?.LogInfo("Record-Session missing " + typeName + "." + method);
                return 0;
            }
            try
            {
                var hm = new HarmonyMethod(typeof(RecordSession), hook);
                if (prefix)
                    Plugin.Harmony.Patch(found, prefix: hm);
                else
                    Plugin.Harmony.Patch(found, postfix: hm);
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Record-Session " + method + " " + ex.Message);
                return 0;
            }
        }

        static void OnRestart()
        {
            Record.Stop("restart");
        }

        static void OnStarted()
        {
            Record.MaybeAutoStart();
        }
    }
}
