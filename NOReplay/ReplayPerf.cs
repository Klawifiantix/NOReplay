using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    internal enum LifeKind
    {
        Unit = 0,
        Missile = 1,
        Wreck = 2,
        Explosion = 3,
        Flare = 4,
        Bullet = 5
    }

    /// <summary>
    /// Adds up replay work for five seconds, then writes the five heaviest parts.
    /// With PerfLog on, a frame slower than 30 ms also writes the buckets that grew,
    /// and the five-second line includes instantiate and garbage-collection counts.
    /// </summary>
    internal static class ReplayPerf
    {
        const int Max = 48;
        const int Kinds = 6;
        const float SpikeSeconds = 0.030f;
        static readonly string[] _names = new string[Max];
        static readonly long[] _ticks = new long[Max];
        static readonly long[] _frameBase = new long[Max];
        static readonly int[] _order = new int[Max];
        static readonly int[] _born = new int[Kinds];
        static readonly int[] _died = new int[Kinds];
        static readonly string[] _kindName = { "unit", "missile", "wreck", "explosion", "flare", "bullet" };
        static int _used;
        static int _frames;
        static float _logAt;
        static readonly long[] _jump = new long[Max];
        static bool _jumping;
        static long _jumpWall;
        static string _jumpKind = "";
        static float _jumpFrom;
        static float _jumpTo;
        static int _gc0 = -1;
        static int _gc1;
        static int _gc2;

        public static long Stamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static void Add(string name, long dt)
        {
            if (dt <= 0 || string.IsNullOrEmpty(name)) return;
            if (!Plugin.PerfLog && !_jumping) return;
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
                _frameBase[i] = 0;
            }
            if (Plugin.PerfLog) _ticks[i] += dt;
            if (_jumping) _jump[i] += dt;
        }

        public static void ResetLife()
        {
            for (int i = 0; i < Kinds; i++)
            {
                _born[i] = 0;
                _died[i] = 0;
            }
            _gc0 = -1;
        }

        public static void Born(LifeKind kind)
        {
            if (!Plugin.PerfLog || Plugin.CurrentDoc == null) return;
            _born[(int)kind]++;
        }

        public static void Died(LifeKind kind)
        {
            if (!Plugin.PerfLog || Plugin.CurrentDoc == null) return;
            _died[(int)kind]++;
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

        /// <summary>
        /// Called once per replay frame, after the ghosts have updated.
        /// Does nothing unless PerfLog is on and a replay is loaded.
        /// </summary>
        public static void EndFrame(float replayTime, float frameSeconds)
        {
            if (!Plugin.PerfLog || Plugin.CurrentDoc == null) return;
            if (_gc0 < 0)
            {
                _gc0 = GC.CollectionCount(0);
                _gc1 = GC.CollectionCount(1);
                _gc2 = GC.CollectionCount(2);
            }
            if (frameSeconds > SpikeSeconds)
                WriteSpike(replayTime, frameSeconds * 1000.0);
            Frame();
            CopyBase();
        }

        public static void Frame()
        {
            if (!Plugin.PerfLog || Plugin.CurrentDoc == null) return;
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
            WriteLife();
            for (int i = 0; i < n; i++) _ticks[i] = 0;
            _frames = 0;
            _logAt = Time.unscaledTime + 5f;
        }

        static void WriteSpike(float replayTime, double frameMs)
        {
            double freq = Stopwatch.Frequency;
            var sb = new StringBuilder(160);
            sb.Append("Perf spike t=");
            sb.Append(replayTime.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" frame=");
            sb.Append(frameMs.ToString("0", CultureInfo.InvariantCulture));
            sb.Append("ms");
            bool any = false;
            for (int i = 0; i < _used; i++)
            {
                if (_names[i] == null) continue;
                long delta = _ticks[i] - _frameBase[i];
                if (delta <= 0) continue;
                double ms = delta * 1000.0 / freq;
                if (ms < 0.05) continue;
                if (!any) sb.Append(" buckets");
                any = true;
                sb.Append(' ');
                sb.Append(_names[i]);
                sb.Append('=');
                sb.Append(ms.ToString("0.0", CultureInfo.InvariantCulture));
            }
            if (!any) sb.Append(" buckets=none");
            Plugin.Log?.LogInfo(sb.ToString());
        }

        static void WriteLife()
        {
            int c0 = GC.CollectionCount(0);
            int c1 = GC.CollectionCount(1);
            int c2 = GC.CollectionCount(2);
            int d0 = _gc0 < 0 ? 0 : c0 - _gc0;
            int d1 = _gc1 < 0 ? 0 : c1 - _gc1;
            int d2 = _gc2 < 0 ? 0 : c2 - _gc2;
            _gc0 = c0;
            _gc1 = c1;
            _gc2 = c2;
            var sb = new StringBuilder(180);
            sb.Append("Perf life");
            for (int i = 0; i < Kinds; i++)
            {
                sb.Append(' ');
                sb.Append(_kindName[i]);
                sb.Append("=+");
                sb.Append(_born[i].ToString(CultureInfo.InvariantCulture));
                sb.Append("/-");
                sb.Append(_died[i].ToString(CultureInfo.InvariantCulture));
                _born[i] = 0;
                _died[i] = 0;
            }
            sb.Append(" gc0=");
            sb.Append(d0.ToString(CultureInfo.InvariantCulture));
            sb.Append(" gc1=");
            sb.Append(d1.ToString(CultureInfo.InvariantCulture));
            sb.Append(" gc2=");
            sb.Append(d2.ToString(CultureInfo.InvariantCulture));
            Plugin.Log?.LogInfo(sb.ToString());
        }

        static void CopyBase()
        {
            for (int i = 0; i < _used; i++)
                _frameBase[i] = _ticks[i];
        }
    }
}
