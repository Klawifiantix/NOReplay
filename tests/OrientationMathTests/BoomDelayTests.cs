using NOReplay;

namespace NOReplay.OrientationTests
{
    static class BoomDelayTests
    {
        public static int Failed;

        public static void Run()
        {
            FrontGrowsWithYield();
            InsideTheFrontIsImmediate();
            FartherTakesLonger();
            RangeIsTheDetonationDistance();
            PlayOnlyOnAForwardCross();
            if (Failed == 0) System.Console.WriteLine("BoomDelay ok");
        }

        static void FrontGrowsWithYield()
        {
            Expect(BoomDelay.Front(0f) == 0f, "no yield has no head start");
            Expect(BoomDelay.Front(1000f) > BoomDelay.Front(1f), "a larger layer starts farther out");
        }

        static void InsideTheFrontIsImmediate()
        {
            float front = BoomDelay.Front(1000f);
            Expect(BoomDelay.Seconds(front * 0.5f, 1000f) == 0f, "inside the front the sound is already here");
        }

        static void FartherTakesLonger()
        {
            float near = BoomDelay.Seconds(340f, 0f);
            float far = BoomDelay.Seconds(680f, 0f);
            Expect(Near(near, 1f), "340 m is one second");
            Expect(Near(far, 2f), "680 m is two seconds");
            Expect(far > near, "distance adds delay");
        }

        static void RangeIsTheDetonationDistance()
        {
            Expect(BoomDelay.InRangeAtDetonation(699f, 700f), "inside maxDistance is registered");
            Expect(!BoomDelay.InRangeAtDetonation(700f, 700f), "the game uses a strict less-than");
            Expect(!BoomDelay.InRangeAtDetonation(10f, 0f), "a zero maxDistance registers nothing");
        }

        static void PlayOnlyOnAForwardCross()
        {
            Expect(BoomDelay.Crosses(10f, 2f, 11.9f, 12.1f, false, false, false), "forward playback crosses the arrival");
            Expect(!BoomDelay.Crosses(10f, 2f, 11.9f, 12.1f, true, false, false), "pause does not play");
            Expect(!BoomDelay.Crosses(10f, 2f, 11.9f, 12.1f, false, true, false), "reverse does not play");
            Expect(!BoomDelay.Crosses(10f, 2f, 11.9f, 12.1f, false, false, true), "a seek does not play");
            Expect(!BoomDelay.Crosses(10f, 2f, 13f, 14f, false, false, false), "already past the arrival stays silent");
            Expect(!BoomDelay.Crosses(10f, 2f, 12.2f, 11f, false, false, false), "moving backward does not play");
            Expect(BoomDelay.Reached(10f, 8f, 1f, 12f, 12.2f, false, false, false), "flying toward the blast can pull the arrival forward");
            Expect(!BoomDelay.Reached(10f, 1f, 1f, 12f, 12.2f, false, false, false), "an arrival already behind the previous frame stays silent");
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.02f;

        static void Expect(bool ok, string name)
        {
            if (ok) System.Console.WriteLine("ok  " + name);
            else
            {
                Failed++;
                System.Console.WriteLine("FAIL " + name);
            }
        }
    }
}
