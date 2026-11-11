using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Marks a spawned explosion so MushroomCloud and Shockwave can animate
    /// themselves from the replay clock. This component does not scale them.
    /// </summary>
    internal class ReplayBlast : MonoBehaviour
    {
        public float Born;
        bool _watched;
        bool _nuke;
        int _logs;
        float _nextLog;

        void Update()
        {
            if (Plugin.CurrentDoc == null || Plugin.Clock == null) return;
            if (!_watched)
            {
                _watched = true;
                NukeTime.Watch(gameObject, Born);
                foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (n == "MushroomCloud" || n == "Shockwave") { _nuke = true; break; }
                }
            }
            if (!_nuke) return;
            float age = Plugin.Clock.Time - Born;
            if (age < 0f) age = 0f;
            if (_logs >= 8 || age < _nextLog) return;
            _logs++;
            _nextLog = age + 2f;
            Plugin.Log?.LogInfo(NukeTime.Describe(gameObject, age));
        }
    }
}
