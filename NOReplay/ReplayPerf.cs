using System.Diagnostics;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Adds up replay work for five seconds, then writes the five heaviest parts.
    /// </summary>
    internal static class ReplayPerf
    {
        const int Max = 12;
        static readonly string[] _names = new string[Max];
        static readonly long[] _ticks = new long[Max];
        static readonly int[] _order = new int[Max];
        static int _used;
        static int _frames;
        static float _logAt;

        public static long Stamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static void Add(string name, long dt)
        {
            if (dt <= 0 || string.IsNullOrEmpty(name)) return;
            int i = -1;
            for (int n = 0; n < _used; n++)
            {
                if (_names[n] == name) { i = n; break; }
            }
            if (i < 0)
            {
                if (_used >= Max) return;
                i = _used++;
                _names[i] = name;
            }
            _ticks[i] += dt;
        }

        public static void Frame()
        {
            if (Plugin.CurrentDoc == null) return;
            _frames++;
            if (_logAt <= 0f) _logAt = Time.unscaledTime + 5f;
            if (Time.unscaledTime < _logAt) return;
            int n = _used;
            for (int i = 0; i < n; i++) _order[i] = i;
            for (int i = 0; i < n; i++)
            {
                int best = i;
                for (int j = i + 1; j < n; j++)
                    if (_ticks[_order[j]] > _ticks[_order[best]]) best = j;
                int swap = _order[i];
                _order[i] = _order[best];
                _order[best] = swap;
            }
            double freq = Stopwatch.Frequency;
            int show = n < 5 ? n : 5;
            var line = "Perf top n=" + _frames;
            for (int i = 0; i < show; i++)
            {
                int slot = _order[i];
                double ms = _frames > 0 ? _ticks[slot] * 1000.0 / freq / _frames : 0;
                line += " " + _names[slot] + "=" + ms.ToString("0.00");
            }
            Plugin.Log?.LogInfo(line);
            for (int i = 0; i < n; i++) _ticks[i] = 0;
            _frames = 0;
            _logAt = Time.unscaledTime + 5f;
        }
    }
}
