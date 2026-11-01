using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    [HarmonyPatch]
    static class CameraTVPatch
    {
        static System.Type? _camType;
        static FieldInfo? _follow;
        static bool _ready;
        static Object? _cam;
        static Component? _unit;
        static ReplayGhost? _ghost;

        static MethodBase TargetMethod()
        {
            var type = Plugin.FindGameType("CameraTVState");
            return type?.GetMethod("GetTVCamPoint", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        static void Prefix(ref Vector3 targetVel)
        {
            InjectVelocity(ref targetVel);
        }

        static void InjectVelocity(ref Vector3 targetVel)
        {
            if (!_ready)
            {
                _ready = true;
                _camType = Plugin.FindGameType("CameraStateManager");
                if (_camType != null)
                    _follow = _camType.GetField("followingUnit",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_camType == null || _follow == null) return;
            if (!ReadFollow(out var ghost) && !Rescan(out ghost)) return;
            int id = ghost.GetInstanceID();
            if (ReplayGhost.Velocities.TryGetValue(id, out var vel))
                targetVel = vel * 1.5f;
        }

        static bool ReadFollow(out ReplayGhost ghost)
        {
            ghost = null!;
            if (_cam == null || _follow == null) return false;
            Component? unit;
            try { unit = _follow.GetValue(_cam) as Component; }
            catch { return false; }
            if (unit == null) return false;
            if (!object.ReferenceEquals(unit, _unit))
            {
                _unit = unit;
                _ghost = unit.GetComponent<ReplayGhost>();
            }
            if (_ghost == null) return false;
            ghost = _ghost;
            return true;
        }

        static bool Rescan(out ReplayGhost ghost)
        {
            ghost = null!;
            _cam = null;
            _unit = null;
            _ghost = null;
            if (_camType == null || _follow == null) return false;
            foreach (var cam in Resources.FindObjectsOfTypeAll(_camType))
            {
                if (cam == null) continue;
                Component? unit;
                try { unit = _follow.GetValue(cam) as Component; }
                catch { continue; }
                if (unit == null) continue;
                var found = unit.GetComponent<ReplayGhost>();
                if (found == null) continue;
                _cam = cam;
                _unit = unit;
                _ghost = found;
                ghost = found;
                return true;
            }
            return false;
        }
    }
}
