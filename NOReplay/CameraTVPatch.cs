using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Fly-by reads Rigidbody.velocity in CameraTVState. A replay ghost is
    /// kinematic, so that value stays zero and the 3 second timer never fires.
    /// Substitute the recorded velocity for the reads in that state. The ghost
    /// update never calls into the camera.
    /// </summary>
    [HarmonyPatch]
    static class CameraTVPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var type = Plugin.FindGameType("CameraTVState");
            if (type == null) yield break;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var update = type.GetMethod("UpdateState", flags);
            var enter = type.GetMethod("EnterState", flags);
            if (update != null) yield return update;
            if (enter != null) yield return enter;
        }

        static void Prefix(object[] __args)
        {
            if (Plugin.CurrentDoc == null || __args == null || __args.Length == 0) return;
            if (__args[0] is not Component cam) return;
            var rb = cam.GetType().GetField("followingRB",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(cam) as Rigidbody;
            if (rb == null) return;
            var ghost = rb.GetComponent<ReplayGhost>() ?? rb.GetComponentInParent<ReplayGhost>();
            if (ghost == null) return;
            rb.velocity = ghost.CameraFeedVelocity();
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = typeof(Rigidbody).GetProperty(nameof(Rigidbody.velocity))?.GetGetMethod();
            var feed = typeof(CameraTVPatch).GetMethod(nameof(FeedVelocity), BindingFlags.Static | BindingFlags.NonPublic);
            if (getter == null || feed == null)
            {
                foreach (var same in instructions) yield return same;
                yield break;
            }
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                    yield return new CodeInstruction(OpCodes.Call, feed);
                else
                    yield return instruction;
            }
        }

        static Vector3 FeedVelocity(Rigidbody body)
        {
            if (Plugin.CurrentDoc != null && body != null)
            {
                var ghost = body.GetComponent<ReplayGhost>() ?? body.GetComponentInParent<ReplayGhost>();
                if (ghost != null) return ghost.CameraFeedVelocity();
            }
            return body != null ? body.velocity : Vector3.zero;
        }
    }
}
