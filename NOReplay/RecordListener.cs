using System;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Listens for newly registered units via Harmony and forwards them to the active RecordEngine.
    /// Replaces the periodic FindObjectsByType discovery loop.
    /// </summary>
    internal static class RecordListener
    {
        private static bool _patched;

        public static void Install()
        {
            if (_patched) return;
            _patched = true;

            try
            {
                var registryType = AccessTools.TypeByName("UnitRegistry");
                if (registryType == null)
                {
                    Plugin.Log?.LogWarning("RecordListener: UnitRegistry type not found.");
                    return;
                }

                var method = AccessTools.Method(registryType, "RegisterUnit");
                if (method == null)
                {
                    Plugin.Log?.LogWarning("RecordListener: UnitRegistry.RegisterUnit not found.");
                    return;
                }

                var harmony = new Harmony("noreplay.listener");
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(RecordListener), nameof(OnUnitRegistered)));
                Plugin.Log?.LogInfo("RecordListener: Patched UnitRegistry.RegisterUnit");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("RecordListener install failed: " + ex);
            }
        }

        static void OnUnitRegistered(Unit unit, PersistentID id)
        {
            if (unit == null) return;
            if (!Record.Active) return;

            var engine = Record.Host?.GetComponent<RecordEngine>();
            if (engine == null) return;

            try
            {
                engine.RegisterUnitFromListener(unit);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("RecordListener.OnUnitRegistered: " + ex.Message);
            }
        }
    }
}