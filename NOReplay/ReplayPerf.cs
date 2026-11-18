using System;
using System.Diagnostics;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Adds up replay work for five seconds, then writes the five heaviest parts.
    /// </summary>
    internal static class ReplayPerf
    {
        const int Max = 48;
        static readonly string[] _names = new string[Max];
        static readonly long[] _ticks = new long[Max];
        static readonly int[] _order = new int[Max];
        static int _used;
        static int _frames;
        static float _logAt;
        static readonly long[] _jump = new long[Max];
        static bool _jumping;
        static long _jumpWall;
        static string _jumpKind = "";
        static float _jumpFrom;
        static float _jumpTo;

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
            if (_jumping) _jump[i] += dt;
        }

        /// <summary>
        /// One timeline jump. The line is written on the next update, after
        /// the ghosts have run their LateUpdate for this jump.
        /// </summary>
        public static void BeginJump(string kind, float from, float to)
        {
            EndJump();
            _jumping = true;
            _jumpKind = kind;
            _jumpFrom = from;
            _jumpTo = to;
            _jumpWall = Stamp();
            for (int i = 0; i < Max; i++) _jump[i] = 0;
        }

        public static void EndJump()
        {
            if (!_jumping) return;
            _jumping = false;
            double freq = Stopwatch.Frequency;
            double ms = (Stamp() - _jumpWall) * 1000.0 / freq;
            var line = _jumpKind == "seek"
                ? "Seek " + _jumpFrom.ToString("0.0") + "->" + _jumpTo.ToString("0.0")
                : "Reverse start";
            line += " ms=" + ms.ToString("0");
            line += " parts=";
            bool any = false;
            for (int i = 0; i < _used; i++)
            {
                if (_jump[i] <= 0 || _names[i] == null) continue;
                if (any) line += " ";
                any = true;
                line += _names[i] + "=" + (_jump[i] * 1000.0 / freq).ToString("0");
            }
            if (!any) line += "none";
            Plugin.Log?.LogInfo(line);
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
            var late = "Perf late n=" + _frames;
            bool anyLate = false;
            for (int i = 0; i < n; i++)
            {
                if (_names[i] == null || !_names[i].StartsWith("late-", StringComparison.Ordinal)) continue;
                anyLate = true;
                double ms = _frames > 0 ? _ticks[i] * 1000.0 / freq / _frames : 0;
                late += " " + _names[i] + "=" + ms.ToString("0.00");
            }
            if (!anyLate) late += " none";
            Plugin.Log?.LogInfo(late);
            for (int i = 0; i < n; i++) _ticks[i] = 0;
            _frames = 0;
            _logAt = Time.unscaledTime + 5f;
        }
    }
}
