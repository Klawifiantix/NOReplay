using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    [HarmonyPatch]
    static class CameraTVPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            var type = Plugin.FindGameType("CameraTVState");
            return type?.GetMethod("GetTVCamPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        static void Prefix(ref Vector3 targetVel)
        {
            InjectVelocity(ref targetVel);
        }

        static System.Reflection.MethodBase TargetMethod2()
        {
            var type = Plugin.FindGameType("CameraTVState");
            return type?.GetMethod("UpdateState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        }

        static void Prefix2(object __instance)
        {
            // Inject velocity into followingRB before the dot product check
            var camType = Plugin.FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var unit = camType.GetField("followingUnit", flags)?.GetValue(cam) as Component;
                if (unit == null) continue;
                var ghost = unit.GetComponent<ReplayGhost>();
                if (ghost == null) continue;
                int id = ghost.GetInstanceID();
                if (ReplayGhost.Velocities.TryGetValue(id, out var vel))
                {
                    var rb = camType.GetField("followingRB", flags)?.GetValue(cam) as Rigidbody;
                    if (rb != null) rb.velocity = vel;
                }
                break;
            }
        }

        static void InjectVelocity(ref Vector3 targetVel)
        {
            var camType = Plugin.FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var unit = camType.GetField("followingUnit", flags)?.GetValue(cam) as Component;
                if (unit == null) continue;
                var ghost = unit.GetComponent<ReplayGhost>();
                if (ghost == null) continue;
                int id = ghost.GetInstanceID();
                if (ReplayGhost.Velocities.TryGetValue(id, out var vel))
                    targetVel = vel * 1.5f;
                break;
            }
        }
    }
}