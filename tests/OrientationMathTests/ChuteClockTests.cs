using System.Collections.Generic;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class ChuteClockTests
    {
        public static int Failed;

        public static void Run()
        {
            ClosedAndOpen();
            AbsentStaysClosed();
            CanopyLerpsWhileOpen();
            OffCutsTheCanopy();
            RemovalCutsTheCanopy();
            PilotStateHolds();
            SeatDetachesAndReturns();
            FireOnlyOnAShortForwardStep();
            FrameStaysUntilTheLandingTimer();
            if (Failed == 0) System.Console.WriteLine("ChuteClock ok");
        }

        static void ClosedAndOpen()
        {
            var list = new List<ChuteClock.Event>();
            int n = ChuteClock.Parse("c0:0;c1:1|1|2|3|0|0|0|1|2|2|4|2|2|9|0.5|0.25;p:3;s:0;f:1;r:1", 8f, list);
            Expect(n == 6, "six chute segments");
            Expect(list[0].Kind == ChuteClock.Kind.Canopy && !list[0].On, "first canopy is closed");
            Expect(list[1].Kind == ChuteClock.Kind.Canopy && list[1].On && list[1].HasPose, "second canopy is open");
            Expect(Near(list[1].X, 1f) && Near(list[1].Sz, 4f) && Near(list[1].Lz, 9f), "canopy pose and line length");
            Expect(Near(list[1].Wrinkle, 0.5f) && Near(list[1].Strength, 0.25f), "wrinkle values");
            Expect(list[2].Kind == ChuteClock.Kind.Pilot && list[2].State == 3, "pilot state is parachuting");
            Expect(list[3].Kind == ChuteClock.Kind.Seat && !list[3].On, "seat still attached");
            Expect(list[4].Kind == ChuteClock.Kind.Fire && list[4].On, "seat fire edge");
            Expect(list[5].Kind == ChuteClock.Kind.Rig && list[5].On, "rig is up");
            Expect(list[0].Time == 8f, "segments share the line time");
        }

        static void AbsentStaysClosed()
        {
            var view = ChuteClock.At(null, 10f, -1f);
            Expect(!view.On && !view.HasPose, "missing channel is closed");
            var empty = new List<ChuteClock.Event>();
            view = ChuteClock.At(empty, 10f, -1f);
            Expect(!view.On, "empty channel is closed");
            empty.Add(Open(2f, 0f));
            view = ChuteClock.At(empty, 1f, -1f);
            Expect(!view.On, "before the first sample is closed");
        }

        static void CanopyLerpsWhileOpen()
        {
            var ev = new List<ChuteClock.Event>
            {
                Open(1f, 0f),
                Open(3f, 10f)
            };
            var mid = ChuteClock.At(ev, 2f, -1f);
            Expect(mid.On && mid.HasPose && Near(mid.X, 5f), "canopy position lerps while it stays open");
            Expect(Near(mid.Sx, 2f), "canopy scale lerps with the position");
            var held = ChuteClock.At(ev, 4f, -1f);
            Expect(held.On && Near(held.X, 10f), "after the last open sample the canopy holds");
        }

        static void OffCutsTheCanopy()
        {
            var ev = new List<ChuteClock.Event>
            {
                Open(1f, 4f),
                Closed(5f),
                Open(8f, 1f)
            };
            var gap = ChuteClock.At(ev, 6f, -1f);
            Expect(!gap.On && !gap.HasPose, "a closed sample puts the canopy away");
            var again = ChuteClock.At(ev, 8f, -1f);
            Expect(again.On && Near(again.X, 1f), "the next open starts from its own pose");
        }

        static void RemovalCutsTheCanopy()
        {
            var ev = new List<ChuteClock.Event> { Open(1f, 2f) };
            var live = ChuteClock.At(ev, 4f, 6f);
            Expect(live.On, "the canopy holds until the unit is removed");
            var dead = ChuteClock.At(ev, 6f, 6f);
            Expect(!dead.On, "removal puts the canopy away");
        }

        static void PilotStateHolds()
        {
            var ev = new List<ChuteClock.Event>
            {
                State(1f, 1),
                State(4f, 3),
                State(9f, 4)
            };
            var early = ChuteClock.At(ev, 2f, -1f);
            Expect(early.On && early.State == 1, "ejecting holds until the next state");
            var glide = ChuteClock.At(ev, 6f, -1f);
            Expect(glide.State == 3, "parachuting holds");
            var land = ChuteClock.At(ev, 9f, -1f);
            Expect(land.State == 4, "landing is the last state");
            var before = ChuteClock.At(ev, 0.5f, -1f);
            Expect(!before.On && before.State == 0, "before the first state nothing is set");
        }

        static void SeatDetachesAndReturns()
        {
            var ev = new List<ChuteClock.Event>
            {
                SeatOff(1f),
                SeatOn(3f, 0f),
                SeatOn(5f, 4f),
                SeatOff(8f)
            };
            var attached = ChuteClock.At(ev, 2f, -1f);
            Expect(!attached.On, "the seat stays on the pilot before detach");
            var mid = ChuteClock.At(ev, 4f, -1f);
            Expect(mid.On && mid.HasPose && Near(mid.X, 2f), "the detached seat lerps");
            var back = ChuteClock.At(ev, 8f, -1f);
            Expect(!back.On, "a later attached sample puts the seat back on the pilot");
        }

        static void FireOnlyOnAShortForwardStep()
        {
            var ev = new List<ChuteClock.Event> { Fire(2f) };
            Expect(ChuteClock.Crossed(ev, 1.9f, 2.05f, 0.15f, false), "a short forward step plays the seat");
            Expect(!ChuteClock.Crossed(ev, 1.9f, 2.05f, 0.15f, true), "pause does not replay the seat");
            Expect(!ChuteClock.Crossed(ev, 3f, 2.05f, -0.95f, false), "reverse does not replay the seat");
            Expect(!ChuteClock.Crossed(ev, 0f, 5f, 5f, false), "a seek does not replay the seat");
            Expect(!ChuteClock.Crossed(null, 1f, 3f, 0.1f, false), "no fire sample stays quiet");
        }

        static void FrameStaysUntilTheLandingTimer()
        {
            Expect(ChuteClock.HideFrame(false, 10f, -1f), "a rig-off sample removes the frame");
            Expect(!ChuteClock.HideFrame(true, 12f, 8f), "the frame stays during the five seconds after collapse");
            Expect(ChuteClock.HideFrame(true, 13f, 8f), "the frame goes once those five seconds have passed");
            Expect(!ChuteClock.HideFrame(true, 20f, -1f), "an open canopy with no collapse keeps the frame");
        }

        static ChuteClock.Event Open(float time, float x)
        {
            return new ChuteClock.Event
            {
                Time = time,
                Key = "c0",
                Kind = ChuteClock.Kind.Canopy,
                On = true,
                HasPose = true,
                X = x,
                Qw = 1f,
                Sx = 2f,
                Sy = 2f,
                Sz = 3f,
                Lx = 2f,
                Ly = 2f,
                Lz = 8f
            };
        }

        static ChuteClock.Event Closed(float time)
        {
            return new ChuteClock.Event { Time = time, Key = "c0", Kind = ChuteClock.Kind.Canopy };
        }

        static ChuteClock.Event State(float time, int state)
        {
            return new ChuteClock.Event
            {
                Time = time,
                Key = "p",
                Kind = ChuteClock.Kind.Pilot,
                On = true,
                State = state
            };
        }

        static ChuteClock.Event SeatOff(float time)
        {
            return new ChuteClock.Event { Time = time, Key = "s", Kind = ChuteClock.Kind.Seat };
        }

        static ChuteClock.Event SeatOn(float time, float x)
        {
            return new ChuteClock.Event
            {
                Time = time,
                Key = "s",
                Kind = ChuteClock.Kind.Seat,
                On = true,
                HasPose = true,
                X = x,
                Qw = 1f
            };
        }

        static ChuteClock.Event Fire(float time)
        {
            return new ChuteClock.Event { Time = time, Key = "f", Kind = ChuteClock.Kind.Fire, On = true };
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
