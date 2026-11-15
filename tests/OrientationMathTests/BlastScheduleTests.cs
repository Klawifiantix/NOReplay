using System;

namespace NOReplay.OrientationTests
{
    static class BlastScheduleTests
    {
        public static int Failed;

        public static void Run()
        {
            Check(BlastSchedule.IsLater(10f, -1f), "the first blast is new");
            Check(!BlastSchedule.IsLater(10.02f, 10f), "the same hit is not a second blast");
            Check(BlastSchedule.IsLater(15f, 10f), "a later cook-off is a second blast");
            float[] times = { 40f, 45.2f };
            Check(BlastSchedule.Due(10f, times) == 0, "before the kill there is no blast");
            Check(BlastSchedule.Due(40f, times) == 1, "the killing hit plays the first blast");
            Check(BlastSchedule.Due(44f, times) == 1, "the wreck blast does not play early");
            Check(BlastSchedule.Due(45.2f, times) == 2, "the wreck blast plays at its own time");
            Check(BlastSchedule.Due(80f, times) == 2, "both blasts stay due after the wreck");
            Check(BlastSchedule.Due(39f, times) == 0, "rewinding before the kill clears both");
            Check(BlastSchedule.OnRemoval(13.992f, 13.992f), "a boom on the wreck line is the removal");
            Check(!BlastSchedule.OnRemoval(8.917f, 13.992f), "the kill is not the wreck line");
            Check(!BlastSchedule.RemovalDue(8.917f, 13.992f), "the wreck waits for Visible=0");
            Check(BlastSchedule.RemovalDue(13.992f, 13.992f), "the wreck is due on the removal line");
            Check(!BlastSchedule.RemovalDue(13.9f, 13.992f), "seeking back before removal clears the wreck");
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
