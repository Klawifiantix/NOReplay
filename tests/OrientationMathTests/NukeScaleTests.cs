using NOReplay;

namespace NOReplay.OrientationTests
{
    static class NukeScaleTests
    {
        public static int Failed;

        public static void Run()
        {
            Kilotons();
            LargerYieldIsLarger();
            StepLandsOnTheAge();
            DustHoldsThenFades();
            if (Failed == 0) System.Console.WriteLine("NukeScale ok");
        }

        static void Kilotons()
        {
            Expect(Near(NukeScale.Kilotons(20000000f), 20f), "20 million kg is 20 kt");
            Expect(Near(NukeScale.Kilotons(1000f), 0.001f), "1000 kg is 0.001 kt");
        }

        static void LargerYieldIsLarger()
        {
            float tactical = NukeScale.BlastRadius(1f);
            float strategic = NukeScale.BlastRadius(20f);
            Expect(strategic > tactical * 2f, "20 kt radius is more than twice 1 kt");
            Expect(NukeScale.Propagation(20f, 0f) < NukeScale.Propagation(20f, 1f), "the wave grows with age");
        }

        static void StepLandsOnTheAge()
        {
            float age = 4.5f;
            float step = 0.016f;
            float stored = NukeScale.BeforeStep(NukeScale.Propagation(5f, age), 340f * step);
            Expect(Near(stored + 340f * step, NukeScale.Propagation(5f, age)), "propagation after the frame is the replay age");
            Expect(Near(NukeScale.BeforeStep(age, step) + step, age), "blast time after the frame is the replay age");
            Expect(Near(NukeScale.BeforeStep(age, 0f), age), "a paused frame stays on the replay age");
        }

        static void DustHoldsThenFades()
        {
            float until = NukeScale.RadiusAge(1f);
            Expect(NukeScale.DustOpacity(1f, until - 0.1f) == 1f, "dust stays opaque until the wave passes");
            Expect(NukeScale.DustOpacity(1f, until + 1f) < 1f, "dust fades after the wave passes");
            Expect(NukeScale.DustOpacity(1f, until + 100f) == 0f, "dust reaches zero");
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.001f;

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
