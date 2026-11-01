using System.Collections.Generic;

namespace NOReplay
{
    /// <summary>
    /// Left index for a time-sorted sample bracket. Same result as the binary
    /// search ReplayObject.TrySample used to run from scratch every call.
    /// A cursor makes the common case (playback moving forward one frame)
    /// a couple of comparisons.
    /// </summary>
    public static class SampleIndex
    {
        public interface ISampleTimes
        {
            int Count { get; }
            float TimeAt(int index);
        }

        public static int Bracket<T>(T series, float time, ref int cursor) where T : ISampleTimes
        {
            int n = series.Count;
            int c = cursor;
            if ((uint)c < (uint)(n - 1)
                && series.TimeAt(c) <= time
                && series.TimeAt(c + 1) > time)
                return c;

            if ((uint)c < (uint)(n - 1) && time > series.TimeAt(c))
            {
                int i = c + 1;
                int last = n - 1;
                int guard = 0;
                while (i < last && series.TimeAt(i) <= time && guard < 8)
                {
                    i++;
                    guard++;
                }
                if (i < n && series.TimeAt(i) > time && series.TimeAt(i - 1) <= time)
                {
                    cursor = i - 1;
                    return cursor;
                }
            }

            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (series.TimeAt(mid) <= time) lo = mid;
                else hi = mid;
            }
            cursor = lo;
            return lo;
        }

        readonly struct ListTimes : ISampleTimes
        {
            public readonly List<float> Times;
            public readonly int N;
            public ListTimes(List<float> times, int n) { Times = times; N = n; }
            public int Count => N;
            public float TimeAt(int index) => Times[index];
        }

        public static int Bracket(List<float> times, float time, ref int cursor)
        {
            return Bracket(new ListTimes(times, times.Count), time, ref cursor);
        }
    }
}
