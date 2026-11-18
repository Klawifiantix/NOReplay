using System;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class SurfServoTests
    {
        public static int Failed;

        public static void Run()
        {
            float pitch = 0f;
            SurfServo.Axis(ref pitch, 1f, 20f, 20f, 0.1f, false);
            Expect(Near(pitch, 2f), "a 20 deg/s servo moves 2 degrees in a tenth, got " + pitch);
            SurfServo.Axis(ref pitch, 1f, 20f, 20f, 1f, true);
            Expect(Near(pitch, 20f), "a snap lands on stick times range");
            float limited = 0f;
            SurfServo.Axis(ref limited, 2f, 10f, 100f, 1f, true);
            Expect(Near(limited, 10f), "the axis stays inside its range");

            float flap = 0f;
            SurfServo.Flap(ref flap, 30f, true, 20f, 0.5f, false);
            Expect(Near(flap, -10f), "gear down drives the flap toward the stop");
            SurfServo.Flap(ref flap, 30f, true, 20f, 5f, true);
            Expect(Near(flap, -30f), "a snap puts the flap on the stop");
            SurfServo.Flap(ref flap, 30f, false, 20f, 5f, true);
            Expect(Near(flap, 0f), "gear up returns the flap to zero");

            float split = 0f;
            SurfServo.Split(ref split, 0.5f, true, 0f, 40f, 1f, 20f, 1f, true);
            Expect(Near(split, 0f), "an open throttle keeps the split closed");
            SurfServo.Split(ref split, 0f, true, 0f, 40f, 1f, 20f, 1f, true);
            Expect(Near(split, 40f), "a closed throttle opens the split");
            SurfServo.Split(ref split, 0f, false, 0f, 40f, 1f, 20f, 1f, true);
            Expect(Near(split, 0f), "a missing throttle does not open the split");
            if (Failed == 0) Console.WriteLine("surf servo ok");
        }

        static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        static void Expect(bool ok, string name)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + name);
        }
    }
}
