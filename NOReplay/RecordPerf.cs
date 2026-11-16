using System;
using System.Diagnostics;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Five-second average of recording work on the main thread.
    /// gc0 is how many generation-0 collections happened in the window.
    /// A climb there is the frame-time wobble. allocKB/s is the change in
    /// GC.GetTotalMemory over the same window, so a collection can make it
    /// negative.
    /// </summary>
    internal static class RecordPerf
    {
        const int Max = 16;
        static readonly string[] _names = new string[Max];
        static readonly long[] _ticks = new long[Max];
        static readonly int[] _order = new int[Max];
        static int _used;
        static int _frames;
        static int _sampled;
        static float _logAt;
        static bool _on;
        static int _gc0;
        static long _mem;

        public static long Stamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static void Add(string name, long dt)
        {
            if (!_on || dt <= 0 || string.IsNullOrEmpty(name)) return;
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

        public static void Sampled()
        {
            if (_on) _sampled++;
        }

        public static void Frame(int units)
        {
            bool active = Record.Active;
            if (!active)
            {
                if (_on) Reset();
                return;
            }
            if (!_on)
            {
                _on = true;
                _gc0 = GC.CollectionCount(0);
                _mem = GC.GetTotalMemory(false);
                _logAt = Time.unscaledTime + 5f;
            }
            _frames++;
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
            double ms = 0;
            for (int i = 0; i < n; i++)
                ms += _frames > 0 ? _ticks[i] * 1000.0 / freq / _frames : 0;
            int show = n < 4 ? n : 4;
            var line = "Rec perf n=" + _frames
                + " ms=" + ms.ToString("0.00")
                + " units=" + units
                + " sampled=" + _sampled;
            for (int i = 0; i < show; i++)
            {
                int slot = _order[i];
                double part = _frames > 0 ? _ticks[slot] * 1000.0 / freq / _frames : 0;
                line += " " + _names[slot] + "=" + part.ToString("0.00");
            }
            int gc = GC.CollectionCount(0) - _gc0;
            long mem = GC.GetTotalMemory(false);
            double kb = (mem - _mem) / 1024.0 / 5.0;
            line += " gc0=" + gc.ToString()
                + " allocKB/s=" + kb.ToString("0");
            Plugin.Log?.LogInfo(line);
            for (int i = 0; i < n; i++) _ticks[i] = 0;
            _frames = 0;
            _sampled = 0;
            _gc0 = GC.CollectionCount(0);
            _mem = mem;
            _logAt = Time.unscaledTime + 5f;
        }

        static void Reset()
        {
            _on = false;
            _used = 0;
            _frames = 0;
            _sampled = 0;
            for (int i = 0; i < Max; i++)
            {
                _names[i] = null;
                _ticks[i] = 0;
            }
        }
    }
}
