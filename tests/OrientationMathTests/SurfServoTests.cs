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

            float nx = 0f, ny = 0f;
            SurfServo.Tvc(ref nx, ref ny, 1f, 0f, 0f, 1f, 4f, 15f, 15f, 10f, 50f, 200f, 100f, 1f, true);
            Expect(Near(nx, -15f), "full pitch stick points the nozzle down the pitch range");
            Expect(Near(ny, 0f), "zero yaw leaves the yaw nozzle at rest");
            nx = 0f;
            ny = 0f;
            SurfServo.Tvc(ref nx, ref ny, 0f, 0f, 1f, 1f, 4f, 15f, 15f, 10f, 50f, 200f, 1f, 1f, true);
            Expect(Near(ny, 0f), "yaw vectoring is off below 3 m radar altitude");
            nx = 4f;
            ny = 4f;
            SurfServo.Tvc(ref nx, ref ny, 0f, 0f, 1f, 1f, 4f, 15f, 15f, 10f, 250f, 200f, 100f, 1f, true);
            Expect(Near(nx, 0f) && Near(ny, 0f), "above the airspeed limit the nozzles shut");
            nx = 0f;
            SurfServo.Tvc(ref nx, ref ny, 1f, 0f, 0f, 1f, 4f, 15f, 15f, 10f, 10f, 200f, 100f, 0.1f, false);
            Expect(Near(nx, -7f), "the nozzle slews at 70 deg/s");

            float open = 0f;
            SurfServo.Airbrake(ref open, true, 2f, 0.1f, false);
            Expect(Near(open, 0.2f), "a closed throttle opens the airbrake at openSpeed");
            SurfServo.Airbrake(ref open, false, 2f, 1f, true);
            Expect(Near(open, 0f), "a snap with throttle open closes the airbrake");
            SurfServo.Airbrake(ref open, true, 2f, 1f, true);
            Expect(Near(open, 1f), "a snap with throttle closed deploys the airbrake");
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
