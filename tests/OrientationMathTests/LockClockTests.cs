using System;

namespace NOReplay.OrientationTests
{
    static class LockClockTests
    {
        public static int Failed;

        public static void Run()
        {
            var marks = new System.Collections.Generic.List<LockClock.Mark>
            {
                new LockClock.Mark(4f, "C8"),
                new LockClock.Mark(12f, ""),
                new LockClock.Mark(18f, "94"),
            };
            Check(LockClock.At(marks, 0f) == "", "before the first lock there is no target");
            Check(LockClock.At(marks, 4f) == "C8", "the lock applies on its own time");
            Check(LockClock.At(marks, 11f) == "C8", "the lock holds until it is cleared");
            Check(LockClock.At(marks, 12f) == "", "a clear removes the target");
            Check(LockClock.At(marks, 18f) == "94", "a later lock replaces the clear");
            Check(LockClock.At(marks, 40f) == "94", "the last lock holds to the end");
            Check(LockClock.At(marks, 11f) == "C8", "seeking back restores the earlier target");
            Check(LockClock.At(null, 5f) == "", "a unit with no marks has no target");
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
