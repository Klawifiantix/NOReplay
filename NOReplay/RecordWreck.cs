using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace NOReplay
{
    /// <summary>
    /// GroundVehicle.WreckAndRemove waits, then SpawnWreckage hides the mesh.
    /// The postfix only records that moment. It does not change the game.
    /// </summary>
    internal static class RecordWreck
    {
        static bool _patched;
        static readonly Dictionary<long, RecordVehicle> _live = new Dictionary<long, RecordVehicle>();

        public static void Install()
        {
            if (_patched) return;
            _patched = true;
            var spawn = typeof(GroundVehicle).GetMethod("SpawnWreckage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (spawn == null)
            {
                Plugin.Log?.LogWarning("Wreck-Record: SpawnWreckage fehlt");
                return;
            }
            new Harmony("noreplay.wreck").Patch(spawn,
                postfix: new HarmonyMethod(typeof(RecordWreck), nameof(AfterSpawn)));
            Plugin.Log?.LogInfo("Wreck-Record: SpawnWreckage");
        }

        public static void Register(RecordVehicle rec, long unitId)
        {
            if (rec == null) return;
            _live[unitId] = rec;
        }

        public static void Forget(long unitId)
        {
            _live.Remove(unitId);
        }

        static void AfterSpawn(GroundVehicle __instance)
        {
            if (!Record.Active || Plugin.CurrentDoc != null || __instance == null) return;
            try
            {
                long id = __instance.persistentID.Id;
                if (_live.TryGetValue(id, out var rec) && rec != null)
                    rec.OnWreckSpawned();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Wreck-Record: " + ex.Message);
            }
        }
    }
}
