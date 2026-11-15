using System.Collections.Generic;

namespace NOReplay
{
    /// <summary>
    /// A unit's locked target on the replay clock. Marks are in time order.
    /// An empty id means the lock was cleared. Seeking back returns the mark
    /// that was current at that time, not the latest one in the file.
    /// </summary>
    public static class LockClock
    {
        public readonly struct Mark
        {
            public readonly float Time;
            public readonly string Id;

            public Mark(float time, string id)
            {
                Time = time;
                Id = id ?? "";
            }
        }

        public static string At(List<Mark>? marks, float time)
        {
            if (marks == null || marks.Count == 0) return "";
            string id = "";
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].Time > time + 0.0001f) break;
                id = marks[i].Id;
            }
            return id;
        }
    }
}
