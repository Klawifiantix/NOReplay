using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        Component? _tiltWing;
        System.Array? _tiltJoints;
        bool _tiltBound;
        bool _tiltMiss;

        void ApplyNacelleTilt(ReplaySample s)
        {
            if (!s.HasTilt || s.TiltCount <= 0) return;
            if (!_tiltBound) BindTilt();
            if (_tiltJoints == null)
            {
                if (!_tiltMiss)
                {
                    _tiltMiss = true;
                    Plugin.Log?.LogInfo("Tilt is in the file but " + name + " has no TiltWingController");
                }
                return;
            }
            int n = s.TiltCount < _tiltJoints.Length ? s.TiltCount : _tiltJoints.Length;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (int i = 0; i < n; i++)
            {
                var item = _tiltJoints.GetValue(i);
                if (item == null) continue;
                var part = item.GetType().GetField("part", flags)?.GetValue(item) as Component;
                if (part == null) continue;
                var tr = part.transform;
                var e = tr.localEulerAngles;
                e.x = s.TiltAt(i);
                tr.localEulerAngles = e;
            }
        }

        void BindTilt()
        {
            _tiltBound = true;
            var t = Plugin.FindGameType("TiltWingController");
            if (t == null) return;
            _tiltWing = GetComponentInChildren(t, true) as Component;
            if (_tiltWing == null) return;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _tiltJoints = t.GetField("rotatingJoints", flags)?.GetValue(_tiltWing) as System.Array;
        }
    }
}
