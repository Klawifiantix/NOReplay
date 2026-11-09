using System.Collections.Generic;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class DoorClockTests
    {
        public static int Failed;

        public static void Run()
        {
            ParsesRampAndCargo();
            RampStartsClosed();
            RampLerps();
            CargoHidesAndComesBack();
            RemovalHidesTheLoad();
            if (Failed == 0) System.Console.WriteLine("DoorClock ok");
        }

        static void ParsesRampAndCargo()
        {
            var list = new List<DoorClock.Event>();
            int n = DoorClock.Parse("r/ramp:12.5;m/load:1|0|1|2;m/load:0", 3f, list);
            Expect(n == 3, "ramp, shown cargo, hidden cargo");
            Expect(!list[0].Mount && Near(list[0].Angle, 12.5f), "ramp angle");
            Expect(list[1].Mount && list[1].On && list[1].HasPose && Near(list[1].Y, 1f), "cargo still on the rail");
            Expect(list[2].Mount && !list[2].On, "cargo hidden after release");
        }

        static void RampStartsClosed()
        {
            var ev = new List<DoorClock.Event>
            {
                new DoorClock.Event { Time = 4f, Key = "r/ramp", On = true, HasPose = true, Angle = 40f }
            };
            var early = DoorClock.At(ev, 1f, -1f);
            Expect(early.Present && Near(early.Angle, 0f), "before the first sample the ramp is closed");
            var missing = DoorClock.At(null, 1f, -1f);
            Expect(!missing.Present, "an old file leaves the ramp alone");
        }

        static void RampLerps()
        {
            var ev = new List<DoorClock.Event>
            {
                new DoorClock.Event { Time = 1f, On = true, HasPose = true, Angle = 0f },
                new DoorClock.Event { Time = 3f, On = true, HasPose = true, Angle = 80f }
            };
            var mid = DoorClock.At(ev, 2f, -1f);
            Expect(Near(mid.Angle, 40f), "the hinge angle lerps");
            var held = DoorClock.At(ev, 9f, -1f);
            Expect(Near(held.Angle, 80f), "the open ramp holds");
        }

        static void CargoHidesAndComesBack()
        {
            var ev = new List<DoorClock.Event>
            {
                new DoorClock.Event { Time = 1f, Mount = true, On = true, HasPose = true, Z = 0f },
                new DoorClock.Event { Time = 3f, Mount = true, On = true, HasPose = true, Z = -4f },
                new DoorClock.Event { Time = 5f, Mount = true, On = false }
            };
            var before = DoorClock.At(ev, 0.2f, -1f);
            Expect(before.On && !before.HasPose, "before the first sample the load stays where the prefab put it");
            var slide = DoorClock.At(ev, 2f, -1f);
            Expect(slide.On && slide.HasPose && Near(slide.Z, -2f), "the load slides along the rail");
            var gone = DoorClock.At(ev, 6f, -1f);
            Expect(!gone.On, "after release the mounted mesh is hidden");
            var back = DoorClock.At(ev, 2f, -1f);
            Expect(back.On && Near(back.Z, -2f), "seeking back shows the load on the rail again");
        }

        static void RemovalHidesTheLoad()
        {
            var ev = new List<DoorClock.Event>
            {
                new DoorClock.Event { Time = 1f, Mount = true, On = true, HasPose = true, Z = 1f }
            };
            var dead = DoorClock.At(ev, 8f, 4f);
            Expect(!dead.On, "removing the aircraft hides the load");
        }

        static void Expect(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            System.Console.WriteLine("FAIL " + message);
        }

        static bool Near(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < 0.001f;
        }
    }
}
