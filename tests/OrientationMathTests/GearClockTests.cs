using System;

namespace NOReplay.OrientationTests
{
    static class GearClockTests
    {
        public static int Failed;

        public static void Run()
        {
            SpawnFollowsTheSurface();
            KeysStepAndDoNotLookAhead();
            Check(GearClock.OnSurface(GearClock.NoSurface, 2f) == false, "no ground is an air spawn");
        }

        static void SpawnFollowsTheSurface()
        {
            // radarAlt = hit - offset. Airborne when radarAlt > offset + 1.
            Check(GearClock.OnSurface(2f, 1.5f), "sitting on the deck is a surface spawn");
            Check(!GearClock.OnSurface(400f, 2f), "hundreds of metres up is an air spawn");
            Check(GearClock.OnSurface(3f, 1f), "the boundary radarAlt == offset + 1 stays down");
            Check(!GearClock.OnSurface(3.1f, 1f), "just above that boundary is airborne");
            Check(!GearClock.Extended(false, 100f, Array.Empty<float>(), Array.Empty<float>()),
                "an air spawn stays retracted with no keys");
            Check(GearClock.Extended(true, 100f, Array.Empty<float>(), Array.Empty<float>()),
                "a ground or carrier spawn stays extended with no keys");
        }

        static void KeysStepAndDoNotLookAhead()
        {
            float[] times = { 50f, 223.784f };
            float[] gear = { 0f, 1f };
            Check(!GearClock.Extended(false, 49.9f, times, gear), "before the first key an air spawn is retracted");
            Check(!GearClock.Extended(false, 50f, times, gear), "LandingGear=0 holds the gear up");
            Check(!GearClock.Extended(false, 200f, times, gear), "a later key does not reach backward");
            Check(GearClock.Extended(false, 223.784f, times, gear), "the gear extends at its own key");
            Check(GearClock.Extended(false, 230f, times, gear), "the key holds after its time");
            Check(!GearClock.Extended(false, 10f, times, gear), "rewinding before the key retracts an air spawn");
            Check(GearClock.Extended(true, 10f, times, gear), "rewinding a ground spawn before any key stays extended");
            Check(!GearClock.Extended(true, 80f, times, gear), "a retract key overrides a ground spawn");
            Check(GearClock.ActiveKey(223.7f, times) == 0, "just before the second key the first key is active");
            Check(GearClock.ActiveKey(223.784f, times) == 1, "the second key is active on its timestamp");
            Check(Near(GearClock.Fold(false, 10f, times, gear), 1f), "before any key an air spawn is folded");
            Check(Near(GearClock.Fold(true, 10f, times, gear), 0f), "before any key a ground spawn is down");
            Check(Near(GearClock.Fold(true, 50f, times, gear), 0f), "a retract key on a ground spawn starts the fold");
            float mid = 50f + 0.5f / GearClock.FoldPerSecond;
            float half = GearClock.Fold(true, mid, times, gear);
            Check(half > 0.45f && half < 0.55f, "halfway through the retract the strut is half folded, got " + half.ToString("0.00"));
            Check(Near(GearClock.Fold(true, 50f + 5f, times, gear), 1f), "after the fold time the gear is fully up");
            Check(Near(GearClock.Fold(false, 223.784f + 0.5f / GearClock.FoldPerSecond, times, gear), 0.5f),
                "an extend key animates back down from the previous key");
            Check(Near(GearClock.Fold(false, 10f, times, gear), 1f), "rewinding before the keys returns the air spawn");
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.02f;

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
